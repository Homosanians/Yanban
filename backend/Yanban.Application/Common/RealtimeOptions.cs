namespace Yanban.Application.Common;

public class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>How often the outbox tailer polls for new activity. This is the upper
    /// bound on realtime latency.</summary>
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>
    /// How long a row stays inside the tailer's re-scan window. The cursor is never
    /// advanced past a row younger than this, so a transaction that takes a sequence
    /// number and commits later (out of sequence order) is still picked up. The
    /// guarantee it buys: no event is lost as long as the transaction that wrote it
    /// commits within this window. See ADR-11.
    /// </summary>
    public int GraceSeconds { get; set; } = 5;

    /// <summary>Maximum rows read per poll.</summary>
    public int BatchSize { get; set; } = 500;
}
