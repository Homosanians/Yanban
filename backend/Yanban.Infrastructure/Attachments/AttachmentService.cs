using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Attachments;
using Yanban.Application.Common;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Attachments;

public class AttachmentService : IAttachmentService
{
    private readonly YanbanDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityRecorder _activity;
    private readonly IBoardQuotaPolicy _quota;
    private readonly S3Options _options;

    public AttachmentService(
        YanbanDbContext db, IObjectStorage storage, ICurrentUser currentUser,
        IActivityRecorder activity, IBoardQuotaPolicy quota, IOptions<S3Options> options)
    {
        _db = db;
        _storage = storage;
        _currentUser = currentUser;
        _activity = activity;
        _quota = quota;
        _options = options.Value;
    }

    private TimeSpan Expiry => TimeSpan.FromMinutes(_options.PresignExpiryMinutes);

    /// <summary>
    /// Mints a presigned PUT — and is the <b>one</b> place the quota is enforced.
    ///
    /// <para>Serialized on the board row (ADR-14, the same idiom as the move lock). The check is a
    /// read followed by a write, and two callers who each read "49 GB used" would each conclude
    /// they had room for another gigabyte. With the lock they queue, and the second one reads what
    /// the first actually committed.</para>
    ///
    /// <para>Pending rows count against the board. <b>A ticket is a reservation</b>: without that,
    /// two concurrent 2 GB uploads both pass a check that neither could pass second, and the board
    /// sails past its limit while both are still in flight. The cost is that an abandoned ticket
    /// holds space until the worker's reaper sweeps it (M15) — a bounded, self-healing leak, which
    /// is the better failure.</para>
    /// </summary>
    public async Task<UploadTicketDto> RequestUploadAsync(Guid boardId, Guid cardId, CreateAttachmentRequest request, CancellationToken ct)
    {
        await EnsureCardOnBoardAsync(boardId, cardId, ct);

        var quota = await _quota.GetAsync(boardId, ct);

        // Cheap and unconditional: a 3 GB file is refused before we bother locking anything.
        if (request.SizeBytes > quota.MaxFileBytes)
            throw new QuotaExceededAppException(
                $"That file is {Format(request.SizeBytes)}. The limit is {Format(quota.MaxFileBytes)} per file.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // `boards` has no concurrency token, so SELECT * is safe for FromSql, and ToListAsync runs
        // the statement verbatim so FOR UPDATE stays at the top level.
        await _db.Boards.FromSql($"SELECT * FROM boards WHERE id = {boardId} FOR UPDATE").ToListAsync(ct);

        // Read *after* the lock: a caller that queued behind another upload wakes and sums what
        // actually committed, not the snapshot it arrived with.
        var used = await UsedBytesAsync(boardId, ct);

        if (used + request.SizeBytes > quota.MaxBoardBytes)
            throw new QuotaExceededAppException(
                $"This board has {Format(quota.MaxBoardBytes - used)} left of its {Format(quota.MaxBoardBytes)}. " +
                $"That file is {Format(request.SizeBytes)}.");

        var id = Guid.NewGuid();
        var attachment = new Attachment
        {
            Id = id,
            CardId = cardId,
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            StorageKey = $"attachments/{id}",
            Status = AttachmentStatus.Pending,
            UploadedById = _currentUser.UserId!.Value,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // No activity yet: a pending upload may never complete. It is logged on completion.
        var (url, expiresAt) = _storage.CreateUploadUrl(attachment.StorageKey, attachment.ContentType, Expiry);
        return new UploadTicketDto(id, "PUT", url, attachment.ContentType, expiresAt);
    }

    public async Task<BoardUsageDto> GetUsageAsync(Guid boardId, CancellationToken ct)
    {
        var quota = await _quota.GetAsync(boardId, ct);

        // The bar shows what is *there*, not what is merely spoken for: counting a stranger's
        // half-finished upload as used space would be baffling to look at.
        var ready = await _db.Attachments
            .Where(a => a.Card.List.BoardId == boardId && a.Status == AttachmentStatus.Ready)
            .ToListAsync(ct);

        return new BoardUsageDto(
            ready.Sum(a => a.SizeBytes),
            quota.MaxBoardBytes,
            quota.MaxFileBytes,
            ready.Count);
    }

    /// <summary>
    /// What the board is holding *and* what it has promised to hold. Pending rows are reservations —
    /// see <see cref="RequestUploadAsync"/>.
    /// </summary>
    private Task<long> UsedBytesAsync(Guid boardId, CancellationToken ct) =>
        _db.Attachments
            .Where(a => a.Card.List.BoardId == boardId
                        && (a.Status == AttachmentStatus.Ready || a.Status == AttachmentStatus.Pending))
            .SumAsync(a => a.SizeBytes, ct);

    /// <summary>Bytes as a person would say them — this text goes straight into a toast.</summary>
    private static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    /// <summary>
    /// Confirms an upload landed and flips the attachment to Ready. Idempotent: a client whose
    /// response was dropped will retry, and retrying must not be punished.
    ///
    /// <para>Serialized on the attachment row (ADR-14). "Already Ready? return early" is a
    /// check-then-write, and <c>attachments</c> carries no <c>xmin</c> token, so nothing made a
    /// second concurrent caller lose: both read Pending, both passed the size check, and both
    /// wrote an audit row. One upload produced several "Attached …" events — which the outbox
    /// tailer then fanned out to every client watching the board. Measured, not theorized.</para>
    ///
    /// <para>Idempotent has to mean idempotent in its <i>effects</i>, not just in its status code.</para>
    /// </summary>
    public async Task<AttachmentDto> CompleteAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct)
    {
        // Scope + 404 check, deliberately *not* materializing a tracked entity: the tracking load
        // happens under the lock below. Using FindAsync here would defeat the whole fix — EF hands
        // an already-tracked instance straight back from the change tracker rather than overwriting
        // it with fresh column values, so the post-lock re-read would silently return the stale
        // Pending status and every caller would still write an audit row. (Measured: it did.)
        var exists = await _db.Attachments.AnyAsync(
            a => a.Id == attachmentId && a.CardId == cardId && a.Card.List.BoardId == boardId, ct);
        if (!exists)
            throw new NotFoundAppException("Attachment not found.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // `attachments` has no concurrency token, so SELECT * is safe for FromSql, and ToListAsync
        // runs the statement verbatim so FOR UPDATE stays at the top level (the idiom already used
        // by CardService.MoveAsync and AuthService.RefreshAsync).
        var locked = await _db.Attachments
            .FromSql($"SELECT * FROM attachments WHERE id = {attachmentId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
            throw new NotFoundAppException("Attachment not found.");

        var attachment = locked[0];

        // Re-read the status *after* the lock: the caller that queued behind the winner wakes up
        // here, sees Ready, and takes the early return — one row, one event.
        if (attachment.Status == AttachmentStatus.Ready)
        {
            await tx.CommitAsync(ct);
            return ToDto(attachment);
        }

        // The API never saw the bytes, so trust nothing: the object must actually exist
        // and its real size must match what the client declared up front.
        var size = await _storage.TryGetObjectSizeAsync(attachment.StorageKey, ct);
        if (size is null)
            throw new ValidationAppException("No uploaded file found. PUT the file to the upload URL before completing.");
        if (size != attachment.SizeBytes)
            throw new ValidationAppException("The uploaded file size does not match the declared size.");

        attachment.Status = AttachmentStatus.Ready;
        _activity.Record(boardId, ActivityAction.Created, ActivityEntityTypes.Attachment, attachmentId, $"Attached \"{attachment.FileName}\"");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDto(attachment);
    }

    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        await EnsureCardOnBoardAsync(boardId, cardId, ct);

        return await _db.Attachments
            .Where(a => a.CardId == cardId && a.Status == AttachmentStatus.Ready)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AttachmentDto(a.Id, a.CardId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedById, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<DownloadUrlDto> GetDownloadUrlAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await FindAsync(boardId, cardId, attachmentId, ct);
        if (attachment.Status != AttachmentStatus.Ready)
            throw new NotFoundAppException("Attachment not found.");

        var (url, expiresAt) = _storage.CreateDownloadUrl(attachment.StorageKey, attachment.FileName, attachment.ContentType, Expiry);
        return new DownloadUrlDto(url, expiresAt);
    }

    public async Task DeleteAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await FindAsync(boardId, cardId, attachmentId, ct);

        // Remove the object first; if storage is unreachable the row survives and the
        // delete can be retried (rather than leaving a row that points at live bytes).
        await _storage.DeleteAsync(attachment.StorageKey, ct);

        _db.Attachments.Remove(attachment);
        if (attachment.Status == AttachmentStatus.Ready)
            _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.Attachment, attachmentId, $"Removed \"{attachment.FileName}\"");
        await _db.SaveChangesAsync(ct);
    }

    private static AttachmentDto ToDto(Attachment a) =>
        new(a.Id, a.CardId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedById, a.CreatedAt);

    private async Task EnsureCardOnBoardAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        if (!await _db.Cards.AnyAsync(c => c.Id == cardId && c.List.BoardId == boardId, ct))
            throw new NotFoundAppException("Card not found.");
    }

    private async Task<Attachment> FindAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct) =>
        await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CardId == cardId && a.Card.List.BoardId == boardId, ct)
        ?? throw new NotFoundAppException("Attachment not found.");
}
