using Shouldly;
using Xunit;
using Yanban.Domain.Ordering;

namespace Yanban.UnitTests;

public class RankTests
{
    [Fact]
    public void TryBetween_WithNoBounds_EqualsFirst()
    {
        Rank.TryBetween(null, null, out var rank).ShouldBeTrue();
        rank.ShouldBe(Rank.First());
    }

    [Fact]
    public void TryBetween_WithLeftOnly_AppendsLikeAfter()
    {
        var first = Rank.First();
        Rank.TryBetween(first, null, out var rank).ShouldBeTrue();
        rank.ShouldBe(Rank.After(first));
    }

    [Fact]
    public void TryBetween_ProducesRankThatSortsStrictlyBetweenNeighbours()
    {
        var left = Rank.First();
        var right = Rank.After(left);

        Rank.TryBetween(left, right, out var mid).ShouldBeTrue();

        string.CompareOrdinal(left, mid).ShouldBeLessThan(0);
        string.CompareOrdinal(mid, right).ShouldBeLessThan(0);
    }

    [Fact]
    public void TryBetween_BisectingOneSlotRepeatedly_EventuallyReportsExhaustion()
    {
        // A single Gap-wide slot can only be bisected a bounded number of times; the
        // caller relies on the eventual `false` to trigger a rebalance instead of
        // silently producing a duplicate rank.
        var left = Rank.First();
        var right = Rank.After(left); // one Gap apart

        var exhausted = false;
        for (var i = 0; i < 64; i++)
        {
            if (!Rank.TryBetween(left, right, out var mid))
            {
                exhausted = true;
                break;
            }

            // Every intermediate midpoint stays strictly ordered and narrows the window.
            string.CompareOrdinal(left, mid).ShouldBeLessThan(0);
            string.CompareOrdinal(mid, right).ShouldBeLessThan(0);
            right = mid;
        }

        exhausted.ShouldBeTrue();
    }
}
