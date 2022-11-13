using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroShanten.SourceGeneration;

[Generator]
public class ShantenLookupTableGenerator : IIncrementalGenerator
{
    // This source generator produces two lookup tables that are used to determine the minimum number of insertions
    // and deletions required to complete all groups of a particular suit (the three numbered suits and the seven
    // honor tiles) in constant time. The tables are generated at compile time instead of at runtime in order to
    // reduce the program startup time.
    //
    // The base 5 integer representation (see the 'IntegerEncodeTileCounts' method below) of the number of copies of
    // each kind of tile of a suit is used to index into the tables. Bits 0-3 and 4-7 of each table element represent
    // the insertion and deletion counts when the pair is absent from the suit. Bits 8-11 and 12-15 represent the same
    // counts when the pair is present.
    //
    // For the honor tiles, the order of the tiles is as follows: east, south, west, north, white, green, red

    const int MaxIntegerEncodedNumberedTileCounts = 1_943_750; // 444 200 000 (base 5)
    const int MaxIntegerEncodedHonorTileCounts = 77_750; // 4 442 000 (base 5)

    static readonly string s_numberedArrayInitializer;
    static readonly string s_honorArrayInitializer;

    static ShantenLookupTableGenerator()
    {
        var buffer = new ushort[Math.Max(MaxIntegerEncodedNumberedTileCounts, MaxIntegerEncodedHonorTileCounts) + 1];

        // Optimal capacities obtained by debugging.
        var queue = new Queue<QueueItem>(2_291_406);
        var sb = new StringBuilder(11_168_081, 11_168_081);

        var numberedBuffer = buffer.AsSpan(0, MaxIntegerEncodedNumberedTileCounts + 1);
        numberedBuffer.Fill(65_535);
        PopulateLookupTable(numberedBuffer, queue, honor: false, pair: false);
        PopulateLookupTable(numberedBuffer, queue, honor: false, pair: true);
        s_numberedArrayInitializer = ToArrayInitializer(numberedBuffer, sb);

        var honorBuffer = buffer.AsSpan(0, MaxIntegerEncodedHonorTileCounts + 1);
        honorBuffer.Fill(65_535);
        PopulateLookupTable(honorBuffer, queue, honor: true, pair: false);
        PopulateLookupTable(honorBuffer, queue, honor: true, pair: true);
        s_honorArrayInitializer = ToArrayInitializer(honorBuffer, sb);
    }

    readonly struct QueueItem
    {
        /// <summary>
        /// Represents the number of copies of each kind of tile of a particular suit. Bits 0-2 represent the number
        /// of ones (or east tiles), bits 3-5 the number of twos (or south tiles), bits 6-8 the number of threes (or
        /// west tiles) and so on.
        /// </summary>
        public readonly uint TileCounts;

        /// <summary>
        /// Represents the insertion and deletion counts. Bits 0-3 represent the insertion count and bits 4-7 the
        /// deletion count.
        /// </summary>
        public readonly uint Diff;

        public QueueItem(uint tileCounts, uint diff = 0)
        {
            TileCounts = tileCounts;
            Diff = diff;
        }
    }

    static void PopulateLookupTable(Span<ushort> lut, Queue<QueueItem> queue, bool honor, bool pair)
    {
        Debug.Assert(
            lut.Length == (honor ? MaxIntegerEncodedHonorTileCounts : MaxIntegerEncodedNumberedTileCounts) + 1,
            "Incorrectly sized lookup table.");
        Debug.Assert(queue.Count == 0, "Nonempty queue.");

        var kindCount = honor ? 7 : 9;
        var maxTileCount = pair ? 14 : 12;
        var bitOffset = pair ? 8 : 0;
        var bitMask = 255u << bitOffset;

        // Initialize the queue with all combinations of complete groups.
        if (pair)
        {
            // Enqueue all combinations that include the pair.
            for (var i = 0; i < kindCount; i++)
            {
                var current = new QueueItem(2u << 3 * i);
                queue.Enqueue(current);
                EnqueueDescendants(queue, current, honor, i: 0, depth: 0);
            }
        }
        else
        {
            // Enqueue all combinations that don't include the pair.
            var current = new QueueItem(0); // No tiles is also considered a valid combination here.
            queue.Enqueue(current);
            EnqueueDescendants(queue, current, honor, i: 0, depth: 0);
        }

        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            var (c1, c2, c3, c4, c5, c6, c7, c8, c9) = (
                (int)(current.TileCounts >> 3 * 0 & 7),
                (int)(current.TileCounts >> 3 * 1 & 7),
                (int)(current.TileCounts >> 3 * 2 & 7),
                (int)(current.TileCounts >> 3 * 3 & 7),
                (int)(current.TileCounts >> 3 * 4 & 7),
                (int)(current.TileCounts >> 3 * 5 & 7),
                (int)(current.TileCounts >> 3 * 6 & 7),
                (int)(current.TileCounts >> 3 * 7 & 7),
                (int)(current.TileCounts >> 3 * 8 & 7));
            var encodedTileCounts = IntegerEncodeTileCounts(c1, c2, c3, c4, c5, c6, c7, c8, c9);

            uint element = lut[encodedTileCounts];

            // Drop the current combination if it has already been recorded in the lookup table.
            if ((element & bitMask) != bitMask)
            {
                continue;
            }

            // Add the insertion/deletion counts for the current combination to the lookup table.
            lut[encodedTileCounts] = (ushort)(element & ~bitMask | current.Diff << bitOffset);

            var currentTotalTileCount = c1 + c2 + c3 + c4 + c5 + c6 + c7 + c8 + c9;

            // Enqueue all adjacent combinations with a tile removed and the deletion count incremented by one.
            if (currentTotalTileCount >= 1)
            {
                for (var i = 0; i < kindCount; i++)
                {
                    var nextDiff = current.Diff + (1u << 4 * 1);
                    if ((current.TileCounts & 7u << 3 * i) >= 1u << 3 * i)
                    {
                        Debug.Assert(
                            (current.Diff >> 4 * 1 & 15) < 15,
                            "Deletion count will exceed the maximum representable value.");

                        queue.Enqueue(new QueueItem(current.TileCounts - (1u << 3 * i), nextDiff));
                    }
                }
            }

            // Enqueue all adjacent combinations with a tile added and the insertion count incremented by one.
            if (currentTotalTileCount < maxTileCount)
            {
                for (var i = 0; i < kindCount; i++)
                {
                    var nextDiff = current.Diff + (1u << 4 * 0);
                    if ((current.TileCounts & 7u << 3 * i) < 4u << 3 * i)
                    {
                        Debug.Assert(
                            (current.Diff >> 4 * 0 & 15) < 15,
                            "Insertion count will exceed the maximum representable value.");

                        queue.Enqueue(new QueueItem(current.TileCounts + (1u << 3 * i), nextDiff));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Enqueues all descendants of the current combination that include additional sequences and/or triplets.
    /// </summary>
    static void EnqueueDescendants(Queue<QueueItem> queue, QueueItem current, bool honor, int i, int depth)
    {
        var kindCount = honor ? 7 : 9;
        if (i >= kindCount)
        {
            return;
        }

        // Enqueue all descendants of the current combination that include an additional sequence.
        if (
            !honor
                && i < kindCount - 2
                && (current.TileCounts & 7u << 3 * (i + 0)) < 4u << 3 * (i + 0)
                && (current.TileCounts & 7u << 3 * (i + 1)) < 4u << 3 * (i + 1)
                && (current.TileCounts & 7u << 3 * (i + 2)) < 4u << 3 * (i + 2))
        {
            var next = new QueueItem(
                current.TileCounts + ((1u << 3 * 0) + (1u << 3 * 1) + (1u << 3 * 2) << 3 * i),
                current.Diff);
            queue.Enqueue(next);
            if (depth < 3)
            {
                EnqueueDescendants(queue, next, honor, i, depth + 1);
            }
        }

        // Enqueue all descendants of the current combination that include an additional triplet.
        if ((current.TileCounts & 7u << 3 * i) < 2u << 3 * i)
        {
            var next = new QueueItem(
                current.TileCounts + (3u << 3 * i),
                current.Diff);
            queue.Enqueue(next);
            if (depth < 3)
            {
                EnqueueDescendants(queue, next, honor, i, depth + 1);
            }
        }

        EnqueueDescendants(queue, current, honor, i + 1, depth);
    }

    /// <summary>
    /// Interprets the specified tile counts as the digits of a base 5 integer.
    /// </summary>
    static int IntegerEncodeTileCounts(int a, int b, int c, int d, int e, int f, int g, int h, int i) =>
        1 * a + 5 * b + 25 * c + 125 * d + 625 * e + 3_125 * f + 15_625 * g + 78_125 * h + 390_625 * i;

    static string ToArrayInitializer(Span<ushort> lut, StringBuilder sb)
    {
        sb.Clear();

        foreach (var element in lut)
        {
            sb.Append(element);
            sb.Append(',');
        }
        sb.Length -= 1;

        return sb.ToString();
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource(
            "Shanten.LookupTables.g.cs",
            $$"""
            // <auto-generated />
            namespace ZeroShanten;

            public static partial class Shanten
            {
                static class LookupTables
                {
                    public static readonly ushort[] Numbered = {{{s_numberedArrayInitializer}}};
                    public static readonly ushort[] Honor = {{{s_honorArrayInitializer}}};
                }
            }
            """));
    }
}
