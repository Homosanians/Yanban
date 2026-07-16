namespace Yanban.Domain.Enums;

/// <summary>
/// Where a message is in its life. The worker claims <see cref="Pending"/> rows only, which is why
/// this column, not a sequence cursor, is the outbox's access path.
/// </summary>
public enum OutboxStatus
{
    Pending,
    Sent,

    /// <summary>Gave up after the retry budget. Kept, not deleted, for inspection.</summary>
    Failed
}
