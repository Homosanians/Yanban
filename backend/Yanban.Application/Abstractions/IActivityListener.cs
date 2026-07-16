namespace Yanban.Application.Abstractions;

/// <summary>
/// Wakes the realtime tailer when there may be new activity to read. Backed by a Postgres
/// LISTEN/NOTIFY doorbell, so the common case returns within milliseconds of a commit rather
/// than on a fixed timer. The notification carries no data; the caller still reads the rows
/// from the activity-log cursor, which keeps the source of truth in the durable log.
/// </summary>
public interface IActivityListener
{
    /// <summary>
    /// Returns when a notification arrives, when the backstop interval elapses, or right after
    /// the listener (re)connects. The caller reads the outbox afterwards regardless of the reason,
    /// so a missed notification only costs latency, never a lost event.
    /// </summary>
    Task WaitForActivityAsync(CancellationToken ct);
}
