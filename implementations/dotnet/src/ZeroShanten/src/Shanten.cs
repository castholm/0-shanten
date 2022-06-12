namespace ZeroShanten;

public static partial class Shanten
{
    public static int CalculateShanten(IEnumerable<int> hand)
    {
        if (hand is int[] array)
        {
            return CalculateShanten((ReadOnlySpan<int>)array);
        }

        using var enumerator = hand.GetEnumerator();

        Span<int> span = stackalloc int[34];
        for (var i = 0; i < span.Length; i++)
        {
            if (!enumerator.MoveNext())
            {
                throw new ArgumentException("The specified hand vector has an invalid length.", nameof(hand));
            }

            span[i] = enumerator.Current;
        }

        if (enumerator.MoveNext())
        {
            throw new ArgumentException("The specified hand vector has an invalid length.", nameof(hand));
        }

        return CalculateShanten(span);
    }

    public static int CalculateShanten(ReadOnlySpan<int> hand)
    {
        if (hand.Length != 34)
        {
            throw new ArgumentException("The specified hand vector has an invalid length.", nameof(hand));
        }

        var tiles = 0;
        foreach (var copiesOfKind in hand)
        {
            if (copiesOfKind is not (>= 0 and <= 4))
            {
                throw new ArgumentException(
                    "The specified hand contains an invalid number of copies of a tile type.",
                    nameof(hand));
            }

            tiles += copiesOfKind;
        }

        if (tiles > 14)
        {
            throw new ArgumentException("The specified hand contains an invalid number of tiles.", nameof(hand));
        }

        var sevenPairsShanten = int.MaxValue;
        var thirteenOrphansShanten = int.MaxValue;
        if (tiles >= 13)
        {
            sevenPairsShanten = CalculateSevenPairsShanten(hand);
            if (sevenPairsShanten == -1)
            {
                return -1;
            }

            thirteenOrphansShanten = CalculateThirteenOrphansShanten(hand);
            if (thirteenOrphansShanten == -1)
            {
                return -1;
            }
        }

        var standardShanten = CalculateStandardShanten(hand);

        return Math.Min(Math.Min(sevenPairsShanten, thirteenOrphansShanten), standardShanten);
    }

    static int CalculateSevenPairsShanten(ReadOnlySpan<int> hand)
    {
        var shanten = 6;
        var kinds = 0;
        foreach (var copiesOfKind in hand)
        {
            if (copiesOfKind != 0)
            {
                kinds++;
                if (copiesOfKind >= 2)
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

    static int CalculateThirteenOrphansShanten(ReadOnlySpan<int> hand)
    {
        var shanten =
            13
            - (hand[0] != 0 ? 1 : 0) - (hand[8] != 0 ? 1 : 0)
            - (hand[9] != 0 ? 1 : 0) - (hand[17] != 0 ? 1 : 0)
            - (hand[18] != 0 ? 1 : 0) - (hand[26] != 0 ? 1 : 0)
            - (hand[27] != 0 ? 1 : 0) - (hand[28] != 0 ? 1 : 0) - (hand[29] != 0 ? 1 : 0) - (hand[30] != 0 ? 1 : 0)
            - (hand[31] != 0 ? 1 : 0) - (hand[32] != 0 ? 1 : 0) - (hand[33] != 0 ? 1 : 0);

        if (
            hand[0] >= 2 || hand[8] >= 2
            || hand[9] >= 2 || hand[17] >= 2
            || hand[18] >= 2 || hand[26] >= 2
            || hand[27] >= 2 || hand[28] >= 2 || hand[29] >= 2 || hand[30] >= 2
            || hand[31] >= 2 || hand[32] >= 2 || hand[33] >= 2)
        {
            shanten--;
        }

        return shanten;
    }

    static int CalculateStandardShanten(ReadOnlySpan<int> hand)
    {
        var manEncoded = IntegerEncodeHand(
            hand[0], hand[1], hand[2],
            hand[3], hand[4], hand[5],
            hand[6], hand[7], hand[8]);
        var (manNoPair, manPair) = GetSuitedDiffs(manEncoded);

        var pinEncoded = IntegerEncodeHand(
            hand[9], hand[10], hand[11],
            hand[12], hand[13], hand[14],
            hand[15], hand[16], hand[17]);
        var (pinNoPair, pinPair) = GetSuitedDiffs(pinEncoded);

        var souEncoded = IntegerEncodeHand(
            hand[18], hand[19], hand[20],
            hand[21], hand[22], hand[23],
            hand[24], hand[25], hand[26]);
        var (souNoPair, souPair) = GetSuitedDiffs(souEncoded);

        var honorsEncoded = IntegerEncodeHand(
            hand[27], hand[28], hand[29], hand[30],
            hand[31], hand[32], hand[33],
            0, 0);
        var (honorsNoPair, honorsPair) = GetHonorDiffs(honorsEncoded);

        var minDistance = Math.Min(
            Math.Min(
                Math.Min(
                    (manPair + pinNoPair + souNoPair + honorsNoPair).CalculateDistance(),
                    (manNoPair + pinPair + souNoPair + honorsNoPair).CalculateDistance()),
                (manNoPair + pinNoPair + souPair + honorsNoPair).CalculateDistance()),
            (manNoPair + pinNoPair + souNoPair + honorsPair).CalculateDistance());

        var shanten = (minDistance + 1) / 2 - 1;

        return shanten;
    }

    /// <summary>
    /// Interprets the specified hand as a base 5 integer and returns its <see cref="int"/> representation.
    /// </summary>
    static int IntegerEncodeHand(int a, int b, int c, int d, int e, int f, int g, int h, int i) =>
        1 * a
        + 5 * b
        + 25 * c
        + 125 * d
        + 625 * e
        + 3125 * f
        + 15_625 * g
        + 78_125 * h
        + 390_625 * i;

    static (Diff NoPair, Diff Pair) GetSuitedDiffs(int integerEncodedHand)
        => GetDiffs(integerEncodedHand, LookupTables.Suited);

    static (Diff NoPair, Diff Pair) GetHonorDiffs(int integerEncodedHand)
        => GetDiffs(integerEncodedHand, LookupTables.Honors);

    static (Diff NoPair, Diff Pair) GetDiffs(int integerEncodedHand, ushort[] lut)
    {
        var element = (uint)lut[integerEncodedHand];

        return (
            new Diff((int)(element >> 4 * 0 & 15u), (int)(element >> 4 * 1 & 15u)),
            new Diff((int)(element >> 4 * 2 & 15u), (int)(element >> 4 * 3 /*& 15u*/)));
    }

    readonly struct Diff
    {
        public readonly int Insertions;
        public readonly int Deletions;

        public Diff(int insertions, int deletions) => (Insertions, Deletions) = (insertions, deletions);

        public readonly int CalculateDistance()
        {
            // The basic idea behind calculating the distance is to "trade" insertions for deletions. If we run out of
            // one kind we can trade two of the same kind for one of the opposite kind. For each trade we add 2 to a
            // total distance. In the end we will have a remainder of 0 or 1 insertions or deletions which we add to
            // the total to get the final distance.
            //
            // The same idea expressed in code:
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
            //
            //     distance += insertions + deletions;
            //
            // The calculation below is a simplification of the above idea.

            var distance = 2 * Math.Min(Insertions, Deletions) + (4 * Math.Abs(Insertions - Deletions) + 1) / 3;

            return distance;
        }

        public static Diff operator +(Diff a, Diff b) => new(a.Insertions + b.Insertions, a.Deletions + b.Deletions);
    }
}
