namespace ZeroShanten;

public static partial class Shanten
{
    public static int GetShanten(IEnumerable<int> tileCounts)
    {
        if (tileCounts.GetType() == typeof(int[]))
        {
            return GetShanten(((int[])tileCounts).AsSpan());
        }

        using var enumerator = tileCounts.GetEnumerator();
        var span = (stackalloc int[34]);
        for (var i = 0; i < span.Length; i++)
        {
            if (!enumerator.MoveNext())
            {
                throw new ArgumentException(
                    "The specified tile count vector does not have a length of 34.",
                    nameof(tileCounts));
            }
            span[i] = enumerator.Current;
        }
        if (enumerator.MoveNext())
        {
            throw new ArgumentException(
                "The specified tile count vector does not have a length of 34.",
                nameof(tileCounts));
        }

        return GetShanten(span);
    }

    public static int GetShanten(ReadOnlySpan<int> tileCounts)
    {
        if (tileCounts.Length != 34)
        {
            throw new ArgumentException(
                "The specified tile count vector does not have a length of 34.",
                nameof(tileCounts));
        }

        var totalTileCount = 0;
        foreach (var tileCount in tileCounts)
        {
            if (tileCount is not (>= 0 and <= 4))
            {
                throw new ArgumentException(
                    "The specified tile count vector contains a tile count that is less than 0 or greater than 4.",
                    nameof(tileCounts));
            }
            totalTileCount += tileCount;
        }
        if (totalTileCount > 14)
        {
            throw new ArgumentException(
                "The specified tile count vector's total tile count is greater than 14.",
                nameof(tileCounts));
        }

        var shanten = int.MaxValue;
        if (totalTileCount >= 13)
        {
            shanten = GetSevenPairsShanten(tileCounts);
            if (shanten <= 0)
            {
                return shanten;
            }
            shanten = Math.Min(shanten, GetThirteenOrphansShanten(tileCounts));
            if (shanten <= 0)
            {
                return shanten;
            }
        }
        shanten = Math.Min(shanten, GetStandardShanten(tileCounts));

        return shanten;
    }

    static int GetSevenPairsShanten(ReadOnlySpan<int> tileCounts)
    {
        var shanten = 6;
        var kinds = 0;
        foreach (var tileCount in tileCounts)
        {
            if (tileCount != 0)
            {
                kinds++;
                if (tileCount >= 2)
                {
                    shanten--;
                }
            }
        }
        if (kinds < 7)
        {
            shanten += 7 - kinds;
        }

        return shanten;
    }

    static int GetThirteenOrphansShanten(ReadOnlySpan<int> tileCounts)
    {
        var shanten = 13
            - (tileCounts[0] != 0 ? 1 : 0) - (tileCounts[8] != 0 ? 1 : 0)
            - (tileCounts[9] != 0 ? 1 : 0) - (tileCounts[17] != 0 ? 1 : 0)
            - (tileCounts[18] != 0 ? 1 : 0) - (tileCounts[26] != 0 ? 1 : 0)
            - (tileCounts[27] != 0 ? 1 : 0) - (tileCounts[28] != 0 ? 1 : 0)
            - (tileCounts[29] != 0 ? 1 : 0) - (tileCounts[30] != 0 ? 1 : 0)
            - (tileCounts[31] != 0 ? 1 : 0) - (tileCounts[32] != 0 ? 1 : 0) - (tileCounts[33] != 0 ? 1 : 0);
        if (
            tileCounts[0] >= 2 || tileCounts[8] >= 2
                || tileCounts[9] >= 2 || tileCounts[17] >= 2
                || tileCounts[18] >= 2 || tileCounts[26] >= 2
                || tileCounts[27] >= 2 || tileCounts[28] >= 2
                || tileCounts[29] >= 2 || tileCounts[30] >= 2
                || tileCounts[31] >= 2 || tileCounts[32] >= 2 || tileCounts[33] >= 2)
        {
            shanten--;
        }

        return shanten;
    }

    static int GetStandardShanten(ReadOnlySpan<int> tileCounts)
    {
        var manEncoded = IntegerEncodeTileCounts(
            tileCounts[0], tileCounts[1], tileCounts[2],
            tileCounts[3], tileCounts[4], tileCounts[5],
            tileCounts[6], tileCounts[7], tileCounts[8]);
        var (manNoPair, manPair) = GetNumberedDiffs(manEncoded);
        var pinEncoded = IntegerEncodeTileCounts(
            tileCounts[9], tileCounts[10], tileCounts[11],
            tileCounts[12], tileCounts[13], tileCounts[14],
            tileCounts[15], tileCounts[16], tileCounts[17]);
        var (pinNoPair, pinPair) = GetNumberedDiffs(pinEncoded);
        var souEncoded = IntegerEncodeTileCounts(
            tileCounts[18], tileCounts[19], tileCounts[20],
            tileCounts[21], tileCounts[22], tileCounts[23],
            tileCounts[24], tileCounts[25], tileCounts[26]);
        var (souNoPair, souPair) = GetNumberedDiffs(souEncoded);
        var honorEncoded = IntegerEncodeTileCounts(
            tileCounts[27], tileCounts[28], tileCounts[29], tileCounts[30],
            tileCounts[31], tileCounts[32], tileCounts[33]);
        var (honorNoPair, honorPair) = GetHonorDiffs(honorEncoded);

        var minDistance = Math.Min(
            Math.Min(
                Math.Min(
                    (manPair + pinNoPair + souNoPair + honorNoPair).GetDistance(),
                    (manNoPair + pinPair + souNoPair + honorNoPair).GetDistance()),
                (manNoPair + pinNoPair + souPair + honorNoPair).GetDistance()),
            (manNoPair + pinNoPair + souNoPair + honorPair).GetDistance());
        var shanten = (minDistance + 1) / 2 - 1;

        return shanten;
    }

    /// <summary>
    /// Interprets the specified tile counts as the digits of a base 5 integer.
    /// </summary>
    static int IntegerEncodeTileCounts(int a, int b, int c, int d, int e, int f, int g, int h = 0, int i = 0) =>
        1 * a + 5 * b + 25 * c + 125 * d + 625 * e + 3125 * f + 15_625 * g + 78_125 * h + 390_625 * i;

    static (Diff NoPair, Diff Pair) GetNumberedDiffs(int encodedTileCounts) =>
        GetDiffs(encodedTileCounts, LookupTables.Numbered);

    static (Diff NoPair, Diff Pair) GetHonorDiffs(int encodedTileCounts) =>
        GetDiffs(encodedTileCounts, LookupTables.Honor);

    static (Diff NoPair, Diff Pair) GetDiffs(int encodedTileCounts, ushort[] lut)
    {
        uint element = lut[encodedTileCounts];

        return (
            new Diff((int)(element >> 4 * 0 & 15u), (int)(element >> 4 * 1 & 15u)),
            new Diff((int)(element >> 4 * 2 & 15u), (int)(element >> 4 * 3 & 15u)));
    }

    readonly struct Diff
    {
        public readonly int Insertions;
        public readonly int Deletions;

        public Diff(int insertions, int deletions) => (Insertions, Deletions) = (insertions, deletions);

        public readonly int GetDistance()
        {
            // The calculation below is a simplification of the following code:
            //
            //     var insertions = Insertions;
            //     var deletions = Deletions;
            //     var distance = 0;
            //     while (insertions + deletions > 1)
            //     {
            //         if (insertions >= 1 && deletions >= 1)
            //         {
            //             insertions--;
            //             deletions--;
            //             distance += 2;
            //         }
            //         else if (insertions == 0)
            //         {
            //             deletions--;
            //             insertions += 2;
            //         }
            //         else if (deletions == 0)
            //         {
            //             insertions--;
            //             deletions += 2;
            //         }
            //         else
            //         {
            //             break;
            //         }
            //     }
            //     distance += insertions + deletions;

            var distance = 2 * Math.Min(Insertions, Deletions) + (4 * Math.Abs(Insertions - Deletions) + 1) / 3;

            return distance;
        }

        public static Diff operator +(Diff a, Diff b) => new(a.Insertions + b.Insertions, a.Deletions + b.Deletions);
    }
}
