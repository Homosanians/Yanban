namespace Yanban.Domain.Ordering;

/// <summary>
/// Minimal lexicographic rank: fixed-width base-36 encodings of evenly spaced
/// integers. Lexicographic string order equals numeric order, so ordering rows by
/// the rank column yields position order. Appends leave a <see cref="Gap"/> between
/// neighbours; <see cref="TryBetween"/> bisects that gap for drag-and-drop inserts,
/// and reports exhaustion so the caller can rebalance.
/// </summary>
public static class Rank
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int Base = 36;
    private const int Width = 11;        // 36^11 < long.MaxValue and well under the 64-char column
    private const long Gap = 1L << 16;   // spacing between adjacent items, leaving room for midpoint inserts

    /// <summary>Rank for the first item in an empty list/board.</summary>
    public static string First() => Encode(Gap);

    /// <summary>Rank that sorts immediately after <paramref name="last"/> (the current maximum), or <see cref="First"/> when there is none.</summary>
    public static string After(string? last) =>
        last is null ? First() : Encode(Decode(last) + Gap);

    /// <summary>
    /// Produces a rank that sorts strictly between <paramref name="left"/> and
    /// <paramref name="right"/> (either bound null means start/end of the list). One
    /// unified midpoint covers all four null combinations: a missing left is treated
    /// as 0 and a missing right as one full <see cref="Gap"/> above left, so
    /// <c>TryBetween(null, null)</c> == <see cref="First"/> and
    /// <c>TryBetween(x, null)</c> == <see cref="After"/>(x). Returns <c>false</c> when
    /// the neighbours are adjacent (no integer in between); the caller must rebalance.
    /// </summary>
    public static bool TryBetween(string? left, string? right, out string rank)
    {
        var lo = left is null ? 0 : Decode(left);
        var hi = right is null ? lo + 2 * Gap : Decode(right);

        if (hi - lo <= 1)
        {
            rank = string.Empty;
            return false;
        }

        rank = Encode(lo + (hi - lo) / 2);
        return true;
    }

    private static string Encode(long value)
    {
        Span<char> buffer = stackalloc char[Width];
        for (var i = Width - 1; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(value % Base)];
            value /= Base;
        }
        return new string(buffer);
    }

    private static long Decode(string rank)
    {
        long value = 0;
        foreach (var c in rank)
            value = value * Base + Alphabet.IndexOf(c);
        return value;
    }
}
