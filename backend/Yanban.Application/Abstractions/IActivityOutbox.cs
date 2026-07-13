using Yanban.Application.Activities;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Read side of the activity log when it is used as an outbox: unscoped by board and
/// ordered oldest-first, which is what a tailer wants (the board feed in
/// <see cref="IActivityService"/> is the opposite — board-scoped and newest-first).
/// </summary>
public interface IActivityOutbox
{
    /// <summary>The highest sequence currently assigned, or 0 if the log is empty.</summary>
    Task<long> GetLatestSequenceAsync(CancellationToken ct);

    /// <summary>Visible rows with a sequence above the cursor, oldest-first.</summary>
    Task<IReadOnlyList<ActivityDto>> ReadSinceAsync(long afterSequence, int limit, CancellationToken ct);
}
