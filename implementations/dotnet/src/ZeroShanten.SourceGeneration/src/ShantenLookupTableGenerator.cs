using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroShanten.SourceGeneration
{
    [Generator]
    public class ShantenLookupTableGenerator : IIncrementalGenerator
    {
        // This class generates a lookup table of the number of insertions and deletions required to bring one group
        // of tiles of the same suit (or honor tiles) of a hand to a 0-distance (winning) state. This table is
        // generated at compile time instead of at runtime in order to reduce the startup time of the program, since
        // generating the table can take up to a full second.
        //
        // The integer representation (see 'IntegerEncodeHand' below) of a group of tiles is used to index into the
        // table. Bits 0-3 and 4-7 of each element in the table represent the insertion and deletion counts when the
        // pair is not present in the group. Bits 8-11 and 12-15 represent the same counts when the pair is present.
        //
        // For honor tiles, the order of the tiles is as follows: east, south, west, north, white, green, red.

        const int MaxIntegerEncodedSuitedHand = 1_943_750; // 444 200 000 (base 5)
        const int MaxIntegerEncodedHonorsHand = 77_750; // 4 442 000 (base 5)

        static readonly string s_suitedArrayInitializer;
        static readonly string s_honorsArrayInitializer;

        static ShantenLookupTableGenerator()
        {
            var buffer = new ushort[Math.Max(MaxIntegerEncodedSuitedHand, MaxIntegerEncodedHonorsHand) + 1];

            // Optimal capacities were obtained by debugging.
            var queue = new Queue<QueueItem>(2_291_406);
            var sb = new StringBuilder(13_111_847, 13_111_847);

            var suited = buffer.AsSpan(0, MaxIntegerEncodedSuitedHand + 1);
            suited.Fill(65535);
            PopulateLookupTable(suited, queue, honors: false, pair: false);
            PopulateLookupTable(suited, queue, honors: false, pair: true);
            s_suitedArrayInitializer = ToArrayInitializer(suited, sb);

            var honors = buffer.AsSpan(0, MaxIntegerEncodedHonorsHand + 1);
            honors.Fill(65535);
            PopulateLookupTable(honors, queue, honors: true, pair: false);
            PopulateLookupTable(honors, queue, honors: true, pair: true);
            s_honorsArrayInitializer = ToArrayInitializer(honors, sb);
        }

        struct QueueItem
        {
            // Represents the number of each kind of the nine numbered tiles or seven honor tiles that make up one
            // group of a hand. Bits 0-2 represent the number of ones (or east tiles), bits 3-5 the number of twos
            // (or south tiles), 6-8 the number of threes (or west tiles) and so on.
            public uint Hand;

            // Bits 0-3 represent the insertion count and bits 4-7 the deletion count.
            public uint Diff;

            public QueueItem(uint hand) : this(hand, diff: 0)
            {
            }

            public QueueItem(uint hand, uint diff)
            {
                Hand = hand;
                Diff = diff;
            }
        }

        static void PopulateLookupTable(Span<ushort> lut, Queue<QueueItem> queue, bool honors, bool pair)
        {
            Debug.Assert(
                lut.Length == (honors ? MaxIntegerEncodedHonorsHand : MaxIntegerEncodedSuitedHand) + 1,
                "Incorrectly sized lookup table.");
            Debug.Assert(queue.Count == 0, "Nonempty queue.");

            var tileKindCount = honors ? 7 : 9;
            var maxTileCount = pair ? 14 : 12;
            var bitOffset = pair ? 8 : 0;
            var bitMask = 255u << bitOffset;

            // Initialize the queue with all 0-distance combinations.
            if (pair)
            {
                // Enqueue all hands that include the pair.
                for (var i = 0; i < tileKindCount; i++)
                {
                    var current = new QueueItem(2u << 3 * i);
                    queue.Enqueue(current);
                    EnqueueDescendants(queue, current, honors, i: 0, depth: 0);
                }
            }
            else
            {
                var current = new QueueItem(); // The empty hand is also a 0-distance hand here.
                queue.Enqueue(current);
                EnqueueDescendants(queue, current, honors, i: 0, depth: 0);
            }

            while (queue.Count != 0)
            {
                var current = queue.Dequeue();

                var (c1, c2, c3, c4, c5, c6, c7, c8, c9) = (
                    (int)(current.Hand >> 3 * 0 & 7u),
                    (int)(current.Hand >> 3 * 1 & 7u),
                    (int)(current.Hand >> 3 * 2 & 7u),
                    (int)(current.Hand >> 3 * 3 & 7u),
                    (int)(current.Hand >> 3 * 4 & 7u),
                    (int)(current.Hand >> 3 * 5 & 7u),
                    (int)(current.Hand >> 3 * 6 & 7u),
                    (int)(current.Hand >> 3 * 7 & 7u),
                    (int)(current.Hand >> 3 * 8 /*& 7u*/));

                var currentHandEncoded = IntegerEncodeHand(c1, c2, c3, c4, c5, c6, c7, c8, c9);

                var element = (uint)lut[currentHandEncoded];

                // Drop the current hand if it has already been recorded in the lookup table.
                if ((element & bitMask) != bitMask)
                {
                    continue;
                }

                // Add the diff values for the current hand to the lookup table.
                lut[currentHandEncoded] = (ushort)(element & ~bitMask | current.Diff << bitOffset);

                var currentTileCount = c1 + c2 + c3 + c4 + c5 + c6 + c7 + c8 + c9;

                // Enqueue all adjacent hands with a tile removed and the deletion count incremented by one.
                if (currentTileCount >= 1)
                {
                    for (var i = 0; i < tileKindCount; i++)
                    {
                        var nextDiff = current.Diff + (1u << 4 * 1);
                        if ((current.Hand & 7u << 3 * i) >= 1u << 3 * i)
                        {
                            Debug.Assert(
                                (current.Diff >> 4 * 1 & 15u) < 15u,
                                "Deletion count will exceed the maximum representable value.");

                            queue.Enqueue(new QueueItem(current.Hand - (1u << 3 * i), nextDiff));
                        }
                    }
                }

                // Enqueue all adjacent hands with a tile added and the insertion count incremented by one.
                if (currentTileCount < maxTileCount)
                {
                    for (var i = 0; i < tileKindCount; i++)
                    {
                        var nextDiff = current.Diff + (1u << 4 * 0);
                        if ((current.Hand & 7u << 3 * i) < 4u << 3 * i)
                        {
                            Debug.Assert(
                                (current.Diff >> 4 * 0 & 15u) < 15u,
                                "Insertion count will exceed the maximum representable value.");

                            queue.Enqueue(new QueueItem(current.Hand + (1u << 3 * i), nextDiff));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Enqueues all descendants of the current hand that include additional sequences and/or triplets.
        /// </summary>
        static void EnqueueDescendants(Queue<QueueItem> queue, QueueItem current, bool honors, int i, int depth)
        {
            var tileKindCount = honors ? 7 : 9;

            if (i >= tileKindCount)
            {
                return;
            }

            // Enqueue all descendants of the current hand that include an additional sequence.
            if (
                !honors
                && i < tileKindCount - 2
                && (current.Hand & 7u << 3 * (i + 0)) < 4u << 3 * (i + 0)
                && (current.Hand & 7u << 3 * (i + 1)) < 4u << 3 * (i + 1)
                && (current.Hand & 7u << 3 * (i + 2)) < 4u << 3 * (i + 2))
            {
                var next = new QueueItem(
                    current.Hand + ((1u << 3 * 0) + (1u << 3 * 1) + (1u << 3 * 2) << 3 * i),
                    current.Diff);
                queue.Enqueue(next);
                if (depth < 3)
                {
                    EnqueueDescendants(queue, next, honors, i, depth + 1);
                }
            }

            // Enqueue all descendants of the current hand that include an additional triplet.
            if ((current.Hand & 7u << 3 * i) < 2u << 3 * i)
            {
                var next = new QueueItem(
                    current.Hand + (3u << 3 * i),
                    current.Diff);
                queue.Enqueue(next);
                if (depth < 3)
                {
                    EnqueueDescendants(queue, next, honors, i, depth + 1);
                }
            }

            EnqueueDescendants(queue, current, honors, i + 1, depth);
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

        static string ToArrayInitializer(Span<ushort> lut, StringBuilder sb)
        {
            sb.Clear();

            sb.Append("new ushort[] { ");
            foreach (var element in lut)
            {
                sb.Append(element);
                sb.Append(", ");
            }
            sb.Length -= 2;
            sb.Append(" }");

            var initializer = sb.ToString();

            return initializer;
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(x => x.AddSource("Shanten.g.cs", $@"// <auto-generated />
namespace ZeroShanten
{{
    public static partial class Shanten
    {{
        static class LookupTables
        {{
            public static readonly ushort[] Suited = {s_suitedArrayInitializer};
            public static readonly ushort[] Honors = {s_honorsArrayInitializer};
        }}
    }}
}}
"));
        }
    }
}
