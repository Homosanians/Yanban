namespace Yanban.Domain.Ordering;

/// <summary>
/// Minimal lexicographic rank: fixed-width base-36 encodings of evenly spaced
/// integers. Lexicographic string order equals numeric order, so ordering rows by
/// the rank column yields position order. M2 only appends (<see cref="After"/>);
/// midpoint insertion / rebalancing for drag-and-drop moves arrives in M3 and works
/// on the very same encoding — no data migration needed.
/// </summary>
public static class Rank
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int Base = 36;
    private const int Width = 11;        // 36^11 < long.MaxValue and well under the 64-char column
    private const long Gap = 1L << 16;   // spacing between adjacent items, leaving room for M3 inserts

    /// <summary>Rank for the first item in an empty list/board.</summary>
    public static string First() => Encode(Gap);

    /// <summary>Rank that sorts immediately after <paramref name="last"/> (the current maximum), or <see cref="First"/> when there is none.</summary>
    public static string After(string? last) =>
        last is null ? First() : Encode(Decode(last) + Gap);

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
