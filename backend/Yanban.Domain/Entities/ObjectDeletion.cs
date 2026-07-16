namespace Yanban.Domain.Entities;

/// <summary>
/// A storage object that outlived its row and must be deleted from S3.
///
/// <para>Enqueued by a database trigger on <c>AFTER DELETE ON attachments</c>, not by application
/// code. The rows are removed by Postgres cascade when a card, list or board is deleted, so the
/// application never sees them and cannot enqueue their objects. The database does the cascade, so
/// the database does the enqueue; that is the only way to catch every path, including a manual
/// <c>DELETE</c> in psql.</para>
///
/// <para>Drained by the same worker, with the same <c>FOR UPDATE SKIP LOCKED</c> claim as the
/// notification outbox.</para>
/// </summary>
public class ObjectDeletion
{
    public long Id { get; set; }

    /// <summary>The S3 key to delete. Copied by the trigger, since the attachment row is already gone.</summary>
    public string StorageKey { get; set; } = null!;

    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>Set once the object is gone from storage. The claim query only takes rows where it is null.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
