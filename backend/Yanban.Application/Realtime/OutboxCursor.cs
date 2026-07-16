using Yanban.Application.Activities;

namespace Yanban.Application.Realtime;

/// <summary>
/// The outbox tailer's position in the activity log, and the reason the tailer is not
/// simply <c>WHERE sequence &gt; cursor</c>.
///
/// <para>An activity row takes its <c>Sequence</c> when it is inserted but only becomes
/// visible when its transaction commits. Two concurrent writers can take 100 and 101 with
/// 101 committing first, so a naive tailer would read 101, move its cursor past 100, and
/// lose event 100 when it lands.</para>
///
/// <para>So the cursor lags deliberately. Rows are dispatched the moment they are seen
/// (no added latency), but the cursor only advances past rows older than a grace window,
/// which keeps an in-flight lower sequence inside the re-scanned range until it commits.
/// Re-scanning re-reads already-dispatched rows, so this class remembers what it has sent
/// and de-duplicates.</para>
///
/// <para>No event is lost provided the transaction that wrote it commits within
/// <c>grace</c> of the <c>Record()</c> call that stamped its CreatedAt (plus clock skew,
/// if several instances tail the same log). Yanban's writes are a single
/// <c>SaveChanges</c>, so a 5s window is a wide margin, but it is an assumption this
/// design rests on.</para>
///
/// <para>Not thread-safe: a single tailer loop owns it.</para>
/// </summary>
public sealed class OutboxCursor
{
    private readonly TimeSpan _grace;
    private readonly HashSet<long> _dispatched = new();

    /// <summary>Everything at or below this sequence has been handled and will not be re-read.</summary>
    public long SafeSequence { get; private set; }

    public OutboxCursor(long startSequence, TimeSpan grace)
    {
        SafeSequence = startSequence;
        _grace = grace;
    }

    /// <summary>
    /// Takes the rows currently visible above <see cref="SafeSequence"/> (oldest-first),
    /// returns the ones not yet dispatched, and advances the cursor as far as is safe.
    /// </summary>
    public IReadOnlyList<ActivityDto> Advance(IReadOnlyList<ActivityDto> visible, DateTimeOffset now)
    {
        var toDispatch = new List<ActivityDto>();
        foreach (var row in visible)
            if (_dispatched.Add(row.Sequence)) // false means already sent on an earlier pass
                toDispatch.Add(row);

        // Advance over the leading run of rows that have aged out of the window, and stop
        // at the first young one: passing it could strand an as-yet-uncommitted row behind
        // it. Stalling here is safe (rows are still dispatched, just re-read next tick);
        // overshooting is not.
        var cutoff = now - _grace;
        foreach (var row in visible)
        {
            if (row.CreatedAt >= cutoff)
                break;
            SafeSequence = row.Sequence;
        }

        // The de-dup set only has to cover what can still be re-read.
        _dispatched.RemoveWhere(sequence => sequence <= SafeSequence);

        return toDispatch;
    }
}
