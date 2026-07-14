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
    private readonly S3Options _options;

    public AttachmentService(
        YanbanDbContext db, IObjectStorage storage, ICurrentUser currentUser,
        IActivityRecorder activity, IOptions<S3Options> options)
    {
        _db = db;
        _storage = storage;
        _currentUser = currentUser;
        _activity = activity;
        _options = options.Value;
    }

    private TimeSpan Expiry => TimeSpan.FromMinutes(_options.PresignExpiryMinutes);

    public async Task<UploadTicketDto> RequestUploadAsync(Guid boardId, Guid cardId, CreateAttachmentRequest request, CancellationToken ct)
    {
        await EnsureCardOnBoardAsync(boardId, cardId, ct);

        if (request.SizeBytes > _options.MaxUploadBytes)
            throw new ValidationAppException($"Attachment exceeds the maximum size of {_options.MaxUploadBytes} bytes.");

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

        // No activity yet: a pending upload may never complete. It is logged on completion.
        var (url, expiresAt) = _storage.CreateUploadUrl(attachment.StorageKey, attachment.ContentType, Expiry);
        return new UploadTicketDto(id, "PUT", url, attachment.ContentType, expiresAt);
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
