using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-02, after playing the first drag-to-deploy: "the models will overlap each other
    /// easily." The gesture that caused it is gone - the unit now moves as the rigid block it already
    /// is - and these are the tests that make that fix mean something.
    ///
    /// <para>
    /// The argument the new gesture rests on is: <b>a rigid transform of a legal formation is a legal
    /// formation</b>. Rotation and translation preserve distances, so if the unit does not overlap
    /// itself before the drag it cannot overlap itself during one. That is sound, but it has a
    /// premise - that the formation the unit is IN is legal in the first place - and nothing was
    /// checking it. So: the layout itself never overlaps, and moving it rigidly never changes that.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FormationNoOverlapTests
    {
        private const float Gap = 0.1f;

        /// <summary>
        /// Every pair of models in a laid-out formation must be at least their two half-widths apart.
        /// Checked as an axis-aligned box overlap, which is what the footprints are.
        /// </summary>
        private static void AssertNobodyOverlaps((float dx, float dz)[] offsets,
            float[] halfXs, float[] halfZs, string what)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                for (int j = i + 1; j < offsets.Length; j++)
                {
                    float gapX = MathF.Abs(offsets[i].dx - offsets[j].dx) - (halfXs[i] + halfXs[j]);
                    float gapZ = MathF.Abs(offsets[i].dz - offsets[j].dz) - (halfZs[i] + halfZs[j]);
                    // Separated if there is clear air on EITHER axis.
                    Assert.That(MathF.Max(gapX, gapZ), Is.GreaterThan(-0.001f),
                        $"{what}: models {i} and {j} overlap (x gap {gapX:0.###}, z gap {gapZ:0.###}).");
                }
            }
        }

        [Test]
        public void ALaidOutFormationNeverOverlapsItself()
        {
            var rng = new Random(20261102);

            foreach (int[] rowCounts in new[]
            {
                new[] { 5 }, new[] { 3, 3 }, new[] { 4, 2 }, new[] { 2, 2, 2 },
                new[] { 4, 3 }, new[] { 6, 6, 6 }, new[] { 1, 1 }, new[] { 10 },
            })
            {
                int n = rowCounts.Sum();
                for (int trial = 0; trial < 40; trial++)
                {
                    var halfXs = new float[n];
                    var halfZs = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        // Deliberately lopsided: a 25x50mm cavalry base beside a 28mm infantry one is
                        // the mix that catches row-spacing that assumes everyone is the same size.
                        halfXs[i] = 0.3f + (float)rng.NextDouble() * 1.2f;
                        halfZs[i] = 0.3f + (float)rng.NextDouble() * 1.2f;
                    }

                    var offsets = FormationLibrary.LayoutOffsets(halfXs, halfZs, rowCounts, Gap);
                    AssertNobodyOverlaps(offsets, halfXs, halfZs,
                        $"shape {string.Join("-", rowCounts)} trial {trial}");
                }
            }
        }

        /// <summary>
        /// And the planned version - the one the resolvers actually call, which permutes who stands
        /// where before laying the rows out. The permutation changes the row spacing (a row is as deep
        /// as its deepest model), so a layout that is safe in input order is not automatically safe
        /// after assignment.
        /// </summary>
        [Test]
        public void APlannedFormationNeverOverlapsItself()
        {
            var rng = new Random(987654);

            foreach (int[] rowCounts in new[]
            {
                new[] { 5 }, new[] { 3, 3 }, new[] { 4, 2 }, new[] { 2, 2, 2 }, new[] { 4, 3 },
            })
            {
                int n = rowCounts.Sum();
                for (int trial = 0; trial < 60; trial++)
                {
                    var halfXs = new float[n];
                    var halfZs = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        halfXs[i] = 0.3f + (float)rng.NextDouble() * 1.2f;
                        halfZs[i] = 0.3f + (float)rng.NextDouble() * 1.2f;
                    }

                    var positions = new List<Position>();
                    for (int i = 0; i < n; i++)
                        positions.Add(new Position(20f + (float)rng.NextDouble() * 12f,
                                                   20f + (float)rng.NextDouble() * 12f));

                    var planned = FormationLibrary.PlanFormationOffsets(positions, halfXs, halfZs, rowCounts, Gap);
                    AssertNobodyOverlaps(planned, halfXs, halfZs,
                        $"shape {string.Join("-", rowCounts)} trial {trial}");
                }
            }
        }

        /// <summary>
        /// The premise the whole gesture rests on, stated as a test: rotating and translating a
        /// formation changes no distance between any two models. If this ever fails, "the unit cannot
        /// overlap itself while you drag it" stops being true and the drag needs its own validation.
        /// </summary>
        [Test]
        public void MovingAFormationRigidlyChangesNoDistanceWithinIt()
        {
            int n = 7;
            var halfXs = Enumerable.Repeat(0.55f, n).ToArray();
            var halfZs = Enumerable.Repeat(0.55f, n).ToArray();
            var offsets = FormationLibrary.LayoutOffsets(halfXs, halfZs, new[] { 4, 3 }, Gap);

            var rng = new Random(4242);
            for (int trial = 0; trial < 50; trial++)
            {
                float angle = (float)(rng.NextDouble() * Math.Tau);
                float tx = (float)(rng.NextDouble() * 60f), tz = (float)(rng.NextDouble() * 40f);
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);

                var moved = new (float dx, float dz)[n];
                for (int i = 0; i < n; i++)
                {
                    moved[i] = (tx + offsets[i].dx * cos - offsets[i].dz * sin,
                                tz + offsets[i].dx * sin + offsets[i].dz * cos);
                }

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        float before = Dist(offsets[i], offsets[j]);
                        float after  = Dist(moved[i], moved[j]);
                        Assert.That(after, Is.EqualTo(before).Within(0.001f),
                            $"trial {trial}: models {i} and {j} were {before:0.###}\" apart and are now "
                            + $"{after:0.###}\" - the move was not rigid.");
                    }
                }
            }
        }

        private static float Dist((float dx, float dz) a, (float dx, float dz) b)
        {
            float dx = a.dx - b.dx, dz = a.dz - b.dz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
