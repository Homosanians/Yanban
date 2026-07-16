namespace Yanban.Application.Common;

public class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>
    /// Safety-net interval for the outbox tailer. A Postgres LISTEN/NOTIFY doorbell wakes the
    /// tailer the moment activity commits, so this is not the primary path. It only bounds how
    /// long a notification that was missed (for example while the listener was reconnecting)
    /// can go unseen, since NOTIFY is not durable and the log is the source of truth.
    /// </summary>
    public int BackstopPollMs { get; set; } = 10000;

    /// <summary>
    /// How long a row stays inside the tailer's re-scan window. The cursor is never
    /// advanced past a row younger than this, so a transaction that takes a sequence
    /// number and commits later (out of sequence order) is still picked up. No event is
    /// lost as long as the transaction that wrote it commits within this window.
    /// </summary>
    public int GraceSeconds { get; set; } = 5;

    /// <summary>Maximum rows read per poll.</summary>
    public int BatchSize { get; set; } = 500;
}
