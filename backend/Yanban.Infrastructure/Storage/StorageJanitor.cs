using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Storage;

/// <summary>
/// Keeps object storage honest. Two jobs, both draining a queue the database fills:
///
/// <list type="number">
/// <item>Deletes orphaned objects. An <c>AFTER DELETE ON attachments</c> trigger enqueues the
/// storage key of every attachment row that dies (app delete, cascade, reaper). This drains that
/// queue to S3, so a deleted board's files do not live in the bucket forever.</item>
/// <item>Reaps abandoned uploads. A ticket that was minted but never completed leaves a
/// <c>Pending</c> row holding quota (a reservation; see AttachmentService). Once it is older than
/// any presign URL could still be valid, it is dead weight: deleting the row fires the same
/// trigger, so its object (if the bytes ever landed) is cleaned up too.</item>
/// </list>
///
/// <para>Claims with the same <c>FOR UPDATE SKIP LOCKED</c> as the notification outbox, and is
/// at-least-once for the same reason: a crash after the S3 delete but before the row is marked
/// re-deletes, and deleting an object that is already gone is a no-op. The alternative would leave
/// an orphan that is never collected.</para>
/// </summary>
public class StorageJanitor
{
    private readonly YanbanDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly EmailOptions _options;
    private readonly ILogger<StorageJanitor> _logger;

    // A presign URL is valid for S3Options.PresignExpiryMinutes (15 by default). A Pending row
    // older than this generous window can never be completed against a live URL, so it is safe to
    // reap. Padded well past the presign lifetime so an upload in flight is never swept.
    private static readonly TimeSpan PendingTtl = TimeSpan.FromHours(1);

    public StorageJanitor(
        YanbanDbContext db,
        IObjectStorage storage,
        IOptions<EmailOptions> options,
        ILogger<StorageJanitor> logger)
    {
        _db = db;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Deletes claimed orphaned objects from storage. Returns how many were claimed.</summary>
    public async Task<int> DrainDeletionsAsync(CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var batch = await _db.ObjectDeletions
            .FromSql($"""
                      SELECT * FROM object_deletions
                      WHERE deleted_at IS NULL
                      ORDER BY enqueued_at
                      LIMIT {_options.BatchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        foreach (var deletion in batch)
        {
            try
            {
                // Idempotent by nature: deleting an object that is already gone is a no-op in S3.
                await _storage.DeleteAsync(deletion.StorageKey, ct);
                deletion.DeletedAt = DateTimeOffset.UtcNow;
                deletion.Attempts += 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Left pending; the next pass picks it up again. Storage being briefly unreachable
                // must not lose the intent to delete.
                deletion.Attempts += 1;
                deletion.LastError = Truncate(ex.Message, 1000);
                _logger.LogWarning(ex, "Failed to delete object {Key} (attempt {Attempts}); will retry.",
                    deletion.StorageKey, deletion.Attempts);
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    /// <summary>
    /// Deletes attachment rows that have been <c>Pending</c> longer than any presign URL could live.
    /// The delete fires the trigger, so their objects are queued for <see cref="DrainDeletionsAsync"/>.
    /// Returns how many rows were reaped.
    /// </summary>
    public async Task<int> ReapAbandonedUploadsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - PendingTtl;

        // ExecuteDelete issues one DELETE, and the row-level trigger fires per row deleted, so the
        // objects are enqueued without loading anything into memory.
        return await _db.Attachments
            .Where(a => a.Status == AttachmentStatus.Pending && a.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
