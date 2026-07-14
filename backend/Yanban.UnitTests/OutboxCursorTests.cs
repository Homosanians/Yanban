using Shouldly;
using Yanban.Application.Activities;
using Yanban.Application.Realtime;

namespace Yanban.UnitTests;

/// <summary>
/// The rule the realtime tailer lives by: dispatch immediately, advance reluctantly.
/// These pin the hazard ADR-8 called out — a sequence number is taken at insert but the
/// row only appears at commit, so a cursor that races ahead of the grace window silently
/// *loses* events rather than merely skipping them.
/// </summary>
public class OutboxCursorTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private static ActivityDto Row(long sequence, DateTimeOffset createdAt) =>
        new(sequence, Guid.NewGuid(), Guid.NewGuid(), "Actor", "Created", "Card", Guid.NewGuid(),
            null, null, null, createdAt);

    [Fact]
    public void AgedRows_AreDispatchedAndTheCursorMovesPastThem()
    {
        var cursor = new OutboxCursor(startSequence: 0, Grace);
        var rows = new[] { Row(1, Now.AddMinutes(-1)), Row(2, Now.AddMinutes(-1)) };

        cursor.Advance(rows, Now).Select(r => r.Sequence).ShouldBe(new long[] { 1, 2 });
        cursor.SafeSequence.ShouldBe(2);
    }

    [Fact]
    public void YoungRows_AreDispatchedButNotPassedOver()
    {
        var cursor = new OutboxCursor(startSequence: 0, Grace);

        // Fresh off the press: inside the window, so it goes out at once — but the cursor
        // stays put, because a lower sequence may still be in flight behind it.
        cursor.Advance(new[] { Row(7, Now) }, Now).Count.ShouldBe(1);
        cursor.SafeSequence.ShouldBe(0);
    }

    [Fact]
    public void ARowInsideTheWindow_IsNotDispatchedTwice()
    {
        var cursor = new OutboxCursor(startSequence: 0, Grace);
        var row = Row(7, Now);

        cursor.Advance(new[] { row }, Now).Count.ShouldBe(1);

        // The cursor has not moved, so the next poll re-reads the same row. Nobody wants
        // to hear about it twice.
        cursor.Advance(new[] { row }, Now).ShouldBeEmpty();
    }

    [Fact]
    public void ASequenceThatCommitsLate_IsStillDelivered()
    {
        var cursor = new OutboxCursor(startSequence: 0, Grace);

        // Two writers took 10 and 11; 11 committed first, so it is all the tailer can see.
        var late = Row(10, Now);
        var early = Row(11, Now);
        cursor.Advance(new[] { early }, Now).Single().Sequence.ShouldBe(11);

        // A naive `WHERE sequence > cursor` would now sit at 11 and never look back — event
        // 10 would be lost, not merely late. The window keeps the door open for it.
        cursor.SafeSequence.ShouldBeLessThan(10);
        cursor.Advance(new[] { late, early }, Now.AddSeconds(1)).Single().Sequence.ShouldBe(10);
    }

    [Fact]
    public void TheCursorStopsAtTheFirstYoungRow_EvenIfOlderOnesFollowIt()
    {
        var cursor = new OutboxCursor(startSequence: 0, Grace);

        // Row 2 is still inside the window; 3 has aged out. Advancing to 3 would strand
        // anything not yet committed below it, so the cursor stalls at 1 — costing a
        // re-read, which is the cheap failure.
        var rows = new[] { Row(1, Now.AddMinutes(-1)), Row(2, Now), Row(3, Now.AddMinutes(-1)) };

        cursor.Advance(rows, Now).Count.ShouldBe(3);
        cursor.SafeSequence.ShouldBe(1);
    }
}
