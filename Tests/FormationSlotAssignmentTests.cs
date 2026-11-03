using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-02: "sometimes the unit CAN make a move, but in the formation when moving them,
    /// certain models can't make the move because their spot in the formation is weird."
    ///
    /// <para>
    /// He is describing a real rule interaction, not a UI annoyance. OPR measures movement <b>per
    /// model</b>, so a group move is only legal if EVERY model can reach its slot inside the unit's
    /// allowance. Which model goes to which slot is therefore not cosmetic - it decides whether a
    /// legal-looking move is legal.
    /// </para>
    ///
    /// <para>
    /// <see cref="FormationLibrary.PlanFormationOffsets"/> assigned them <b>greedily</b>: slot 0 took
    /// the nearest unused model, then slot 1, and so on. Greedy is optimal for neither the total walked
    /// nor - the one that matters - the FURTHEST any single model walks. It routinely leaves the last
    /// slot holding a model that has to cross the whole unit, which fails the per-model check and
    /// rejects the move.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FormationSlotAssignmentTests
    {
        private const float Gap = 0.1f;

        /// <summary>How far the worst-off model has to walk to reach its slot. This is the number that
        /// decides whether a group move is legal, so it is the number to minimise.</summary>
        private static float WorstWalk(IReadOnlyList<Position> from, (float dx, float dz)[] offsets,
            float centreX, float centreZ)
        {
            float worst = 0f;
            for (int i = 0; i < from.Count; i++)
            {
                float dx = centreX + offsets[i].dx - from[i].x;
                float dz = centreZ + offsets[i].dz - from[i].z;
                worst = MathF.Max(worst, MathF.Sqrt(dx * dx + dz * dz));
            }
            return worst;
        }

        private static (float cx, float cz) Centroid(IReadOnlyList<Position> p)
        {
            float cx = 0f, cz = 0f;
            foreach (Position q in p) { cx += q.x; cz += q.z; }
            return (cx / p.Count, cz / p.Count);
        }

        /// <summary>
        /// Five models already standing in a line, re-formed into that same line in place. Every model
        /// is already standing ON a slot, so a sane assignment moves nobody at all - the worst walk is
        /// zero. Anything above zero is the assignment inventing work.
        /// </summary>
        [Test]
        public void AUnitAlreadyInTheShapeDoesNotHaveToMove()
        {
            int n = 5;
            var halfXs = Enumerable.Repeat(0.5f, n).ToArray();
            var halfZs = Enumerable.Repeat(0.5f, n).ToArray();
            var rowCounts = new[] { n };

            // Put them exactly where a line formation would put them.
            var offsets = FormationLibrary.LayoutOffsets(halfXs, halfZs, rowCounts, Gap);
            var positions = offsets.Select(o => new Position(20f + o.dx, 20f + o.dz)).ToList();

            var planned = FormationLibrary.PlanFormationOffsets(positions, halfXs, halfZs, rowCounts, Gap);
            (float cx, float cz) = Centroid(positions);

            Assert.That(WorstWalk(positions, planned, cx, cz), Is.LessThan(0.01f),
                "a unit told to form the shape it is already in should not move a single model.");
        }

        /// <summary>
        /// The one that reproduces Kai's bug. Six models in a line, re-formed into two rows of three,
        /// but the models are listed in an order that leads greedy astray.
        ///
        /// <para>
        /// The assertion is against the BEST POSSIBLE worst-walk, computed by brute force over all
        /// permutations - so it does not hard-code a number that a later spacing tweak would break, and
        /// it says exactly what is being claimed: there is no excuse for walking a model further than
        /// the best arrangement would.
        /// </para>
        /// </summary>
        [Test]
        public void NobodyWalksFurtherThanTheBestArrangementWouldMakeThem()
        {
            int n = 6;
            var halfXs = Enumerable.Repeat(0.5f, n).ToArray();
            var halfZs = Enumerable.Repeat(0.5f, n).ToArray();
            var rowCounts = new[] { 3, 3 };

            // A line, but listed back-to-front - exactly the kind of roster order casualties leave.
            var positions = new List<Position>();
            for (int i = 0; i < n; i++) positions.Add(new Position(30f - i * 1.2f, 20f));

            var planned = FormationLibrary.PlanFormationOffsets(positions, halfXs, halfZs, rowCounts, Gap);
            (float cx, float cz) = Centroid(positions);
            float actual = WorstWalk(positions, planned, cx, cz);

            float best = BestPossibleWorstWalk(positions, halfXs, halfZs, rowCounts, cx, cz);

            Assert.That(actual, Is.LessThanOrEqualTo(best + 0.01f),
                $"the worst model walks {actual:0.##}\" when the best arrangement walks {best:0.##}\". "
                + "The difference is move allowance spent for nothing, and it is what rejects a move "
                + "the unit could legally make.");
        }

        /// <summary>
        /// A scattered unit re-forming, which is the shape of the problem after casualties: the
        /// survivors are nowhere near a tidy grid and the assignment has real work to do.
        /// </summary>
        [Test]
        public void AScatteredUnitReFormsWithoutStrandingAnybody()
        {
            int n = 6;
            var halfXs = Enumerable.Repeat(0.5f, n).ToArray();
            var halfZs = Enumerable.Repeat(0.5f, n).ToArray();
            var rowCounts = new[] { 3, 3 };

            var positions = new List<Position>
            {
                new Position(24f, 24f), new Position(20f, 19f), new Position(26f, 18f),
                new Position(19f, 25f), new Position(25f, 21f), new Position(21f, 22f),
            };

            var planned = FormationLibrary.PlanFormationOffsets(positions, halfXs, halfZs, rowCounts, Gap);
            (float cx, float cz) = Centroid(positions);
            float actual = WorstWalk(positions, planned, cx, cz);
            float best = BestPossibleWorstWalk(positions, halfXs, halfZs, rowCounts, cx, cz);

            Assert.That(actual, Is.LessThanOrEqualTo(best + 0.01f),
                $"worst model walks {actual:0.##}\", best arrangement walks {best:0.##}\".");
        }

        /// <summary>
        /// Every model still ends up in exactly one slot, and every slot is filled once. An assignment
        /// that duplicated or dropped a slot would stack two models on one spot, which no distance
        /// assertion above would notice.
        /// </summary>
        [Test]
        public void EveryModelGetsItsOwnSlot()
        {
            int n = 7;
            var halfXs = Enumerable.Repeat(0.5f, n).ToArray();
            var halfZs = Enumerable.Repeat(0.5f, n).ToArray();
            var rowCounts = new[] { 4, 3 };

            var positions = new List<Position>();
            var rng = new Random(12345);
            for (int i = 0; i < n; i++)
                positions.Add(new Position(20f + (float)rng.NextDouble() * 8f, 20f + (float)rng.NextDouble() * 8f));

            var planned = FormationLibrary.PlanFormationOffsets(positions, halfXs, halfZs, rowCounts, Gap);

            Assert.That(planned, Has.Length.EqualTo(n));
            var distinct = planned.Select(o => ($"{o.dx:0.###}", $"{o.dz:0.###}")).Distinct().ToList();
            Assert.That(distinct, Has.Count.EqualTo(n), "two models were sent to the same spot.");
        }

        /// <summary>
        /// The real proof. Three hundred random units against brute force: for EVERY one, the chosen
        /// assignment must walk its worst model no further than the best possible arrangement does.
        ///
        /// <para>
        /// Three hand-picked cases can be satisfied by an algorithm that happens to suit them. This
        /// cannot - it is the actual claim ("nobody walks further than they have to") checked against
        /// the actual optimum, on shapes and scatters nobody chose. Seeded, so a failure is reproducible
        /// and the seed is printed with it.
        /// </para>
        /// </summary>
        [Test]
        public void AgainstBruteForce_UniformBases_IsExactlyOptimal()
        {
            (float worstRatio, int trials) = SweepAgainstBruteForce(uniformBases: true, seed: 20261102);

            Assert.That(trials, Is.GreaterThan(100), "the sweep did not actually run.");
            Assert.That(worstRatio, Is.LessThanOrEqualTo(1.001f),
                "with every model on the same base there is nothing coupling the assignment to the "
                + "spacing, so the answer must be exactly the best possible - no excuses.");
        }

        /// <summary>
        /// Mixed base sizes are the one case the assignment cannot be exactly optimal in, and the
        /// reason is worth writing down rather than hiding behind a loose tolerance.
        ///
        /// <para>
        /// Who stands where and where the slots ARE depend on each other: a row's height is its tallest
        /// model and each model sits at its own width, so moving a big model into a row moves that
        /// row's slots. The code assigns, re-spaces, and re-assigns until the two agree - which reaches
        /// a self-consistent answer, but a different assignment would produce different slots that
        /// could be better still. Escaping that means searching every permutation, which is n! and not
        /// something a unit of twenty models can afford mid-move.
        /// </para>
        ///
        /// <para>
        /// Measured, not guessed: across 300 random mixed-base units the worst case walked 11% further
        /// than the theoretical best, and most were within 5%. The bound below is set from that with
        /// room to spare. It is a regression guard - if this starts failing, something has undone the
        /// re-assignment loop, not merely rounded differently.
        /// </para>
        /// </summary>
        [Test]
        public void AgainstBruteForce_MixedBases_IsCloseToOptimal()
        {
            (float worstRatio, int trials) = SweepAgainstBruteForce(uniformBases: false, seed: 20261102);

            Assert.That(trials, Is.GreaterThan(100), "the sweep did not actually run.");
            Assert.That(worstRatio, Is.LessThanOrEqualTo(1.20f),
                $"worst model walked {worstRatio:0.###}x the best achievable. Up to ~1.11 is the known "
                + "cost of the assignment and the spacing being coupled; well past that means the "
                + "re-assignment loop has stopped working.");
        }

        /// <summary>
        /// Random units against brute force. Returns the worst ratio of achieved worst-walk to the best
        /// achievable, and how many trials ran. Seeded, so a failure is reproducible.
        /// </summary>
        private static (float WorstRatio, int Trials) SweepAgainstBruteForce(bool uniformBases, int seed)
        {
            var rng = new Random(seed);
            int[][] shapes = { new[] { 5 }, new[] { 3, 3 }, new[] { 4, 2 }, new[] { 2, 2, 2 }, new[] { 4, 3 } };
            float worstRatio = 1f;
            int trials = 0;

            for (int trial = 0; trial < 300; trial++)
            {
                int[] rowCounts = shapes[rng.Next(shapes.Length)];
                int n = rowCounts.Sum();

                var halfXs = new float[n];
                var halfZs = new float[n];
                if (uniformBases)
                {
                    float r = 0.4f + (float)rng.NextDouble() * 0.5f;
                    Array.Fill(halfXs, r);
                    Array.Fill(halfZs, r);
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        halfXs[i] = 0.4f + (float)rng.NextDouble() * 0.5f;
                        halfZs[i] = 0.4f + (float)rng.NextDouble() * 0.5f;
                    }
                }

                var positions = new List<Position>();
                for (int i = 0; i < n; i++)
                    positions.Add(new Position(20f + (float)rng.NextDouble() * 10f,
                                               20f + (float)rng.NextDouble() * 10f));

                var planned = FormationLibrary.PlanFormationOffsets(positions, halfXs, halfZs, rowCounts, Gap);
                (float cx, float cz) = Centroid(positions);

                float actual = WorstWalk(positions, planned, cx, cz);
                float best = BestPossibleWorstWalk(positions, halfXs, halfZs, rowCounts, cx, cz);

                // Still a real assignment: one model per slot, whatever the distances say.
                Assert.That(planned.Select(o => ($"{o.dx:0.###}", $"{o.dz:0.###}")).Distinct().Count(),
                    Is.EqualTo(n), $"trial {trial}: two models were sent to the same spot.");

                if (best > 0.01f) worstRatio = MathF.Max(worstRatio, actual / best);
                trials++;
            }

            return (worstRatio, trials);
        }

        /// <summary>
        /// Brute force over every permutation: the smallest achievable worst-walk. Only used in tests,
        /// and only for units small enough that n! is nothing (6 or 7 models here).
        /// </summary>
        private static float BestPossibleWorstWalk(IReadOnlyList<Position> positions,
            float[] halfXs, float[] halfZs, int[] rowCounts, float cx, float cz)
        {
            int n = positions.Count;
            float best = float.MaxValue;

            foreach (int[] perm in Permutations(Enumerable.Range(0, n).ToArray()))
            {
                // Lay the slots out with the extents of the models actually standing in them - which is
                // what the real code does, and what makes this comparison honest. Slot positions depend
                // on who is in them when bases differ in size: a row's height is its tallest model and
                // each model sits at its own width, so permuting the models MOVES the slots.
                var permutedHalfXs = new float[n];
                var permutedHalfZs = new float[n];
                for (int slot = 0; slot < n; slot++)
                {
                    permutedHalfXs[slot] = halfXs[perm[slot]];
                    permutedHalfZs[slot] = halfZs[perm[slot]];
                }
                var slots = FormationLibrary.LayoutOffsets(permutedHalfXs, permutedHalfZs, rowCounts, Gap);

                float worst = 0f;
                for (int slot = 0; slot < n; slot++)
                {
                    int model = perm[slot];
                    float dx = cx + slots[slot].dx - positions[model].x;
                    float dz = cz + slots[slot].dz - positions[model].z;
                    worst = MathF.Max(worst, MathF.Sqrt(dx * dx + dz * dz));
                    if (worst >= best) break;
                }
                if (worst < best) best = worst;
            }
            return best;
        }

        private static IEnumerable<int[]> Permutations(int[] items)
        {
            if (items.Length <= 1) { yield return items; yield break; }
            for (int i = 0; i < items.Length; i++)
            {
                int[] rest = items.Where((_, k) => k != i).ToArray();
                foreach (int[] tail in Permutations(rest))
                    yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }
    }
}
