using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The drag-to-deploy geometry (Kai, 2026-11-02): press where the first model goes, drag to where
    /// the last one goes, release - and the drag may curve.
    ///
    /// <para>
    /// Worth testing hard rather than eyeballing. A unit laid out wrong on screen looks merely untidy,
    /// but the spacing it produces decides whether the placement is legal at all: too far apart breaks
    /// the 1" nearest-neighbour rule, too close overlaps bases. Both get rejected by a validator
    /// somewhere downstream, and the player sees "can't place here" with no idea why.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PathFormationTests
    {
        private static readonly Float2 North = new Float2(0f, 1f);

        private static List<Position> Straight(float fromX, float toX, float z = 20f) =>
            new() { new Position(fromX, z), new Position(toX, z) };

        private static float[] Same(int n, float v) => Enumerable.Repeat(v, n).ToArray();

        /// <summary>Base-to-base gap between consecutive models, assuming equal widths.</summary>
        private static float GapBetween(PathFormation.Placement a, PathFormation.Placement b, float width)
        {
            float dx = b.At.x - a.At.x, dz = b.At.z - a.At.z;
            return MathF.Sqrt(dx * dx + dz * dz) - width;
        }

        [Test]
        public void AStraightDragLaysThemInAStraightLine()
        {
            var path = Straight(10f, 20f);
            var placed = PathFormation.Along(path, Same(5, 1f), Same(5, 1f), North);

            Assert.That(placed, Has.Length.EqualTo(5));
            // All on the same z: the drag was horizontal, so the line must be.
            Assert.That(placed.Select(p => p.At.z), Is.All.EqualTo(20f).Within(0.01f));
            // In order along the drag, left to right.
            for (int i = 1; i < placed.Length; i++)
                Assert.That(placed[i].At.x, Is.GreaterThan(placed[i - 1].At.x),
                    "models must come out in the order they were dragged.");
        }

        [Test]
        public void DraggingFurtherSpreadsThemOut()
        {
            var tight = PathFormation.Along(Straight(10f, 16f), Same(5, 1f), Same(5, 1f), North);
            var wide  = PathFormation.Along(Straight(10f, 19f), Same(5, 1f), Same(5, 1f), North);

            float tightGap = GapBetween(tight[0], tight[1], 1f);
            float wideGap  = GapBetween(wide[0], wide[1], 1f);

            Assert.That(wideGap, Is.GreaterThan(tightGap),
                "the whole point of the gesture is that the length of the drag sets the spacing.");
        }

        /// <summary>
        /// The one that keeps the placement legal. The books allow at most 1" between a model and its
        /// nearest neighbour, so however far the player drags, the unit may not be strung out past
        /// that - otherwise the gesture cheerfully produces a formation the validator will refuse.
        /// </summary>
        [Test]
        public void AnEnormousDragStillKeepsTheUnitTogether()
        {
            var placed = PathFormation.Along(Straight(5f, 65f), Same(5, 1f), Same(5, 1f), North);

            for (int i = 1; i < placed.Length; i++)
            {
                float gap = GapBetween(placed[i - 1], placed[i], 1f);
                Assert.That(gap, Is.LessThanOrEqualTo(PathFormation.MaxGapInches + 0.01f),
                    $"models {i - 1} and {i} are {gap:0.##}\" apart - past the 1\" cohesion rule, so the "
                    + "placement the player just drew would be rejected.");
            }
        }

        [Test]
        public void AnEnormousDragCentresTheUnitOnWhereYouDragged()
        {
            var placed = PathFormation.Along(Straight(5f, 65f), Same(5, 1f), Same(5, 1f), North);
            float midpoint = placed.Average(p => p.At.x);

            Assert.That(midpoint, Is.EqualTo(35f).Within(1f),
                "when the drag is longer than the unit can legally span, the unit sits in the middle "
                + "of the stroke - not bunched at the start, which would read as the drag being ignored.");
        }

        /// <summary>
        /// Kai's sketch, in his words: "shorter drag = 3-2". A drag too short for one row is a request
        /// for a deeper formation, not permission to overlap bases.
        /// </summary>
        [Test]
        public void AShortDragWrapsIntoRowsRatherThanOverlapping()
        {
            // Six 1" models need ~6.5" for one row; give the drag 3".
            var placed = PathFormation.Along(Straight(10f, 13f), Same(6, 1f), Same(6, 1f), North);

            Assert.That(placed, Has.Length.EqualTo(6));

            var rows = placed.Select(p => MathF.Round(p.At.z, 1)).Distinct().ToList();
            Assert.That(rows, Has.Count.GreaterThan(1), "the unit should have wrapped onto more than one row.");

            // And nobody is standing on top of anybody.
            for (int i = 0; i < placed.Length; i++)
            {
                for (int j = i + 1; j < placed.Length; j++)
                {
                    float dx = placed[j].At.x - placed[i].At.x, dz = placed[j].At.z - placed[i].At.z;
                    float centres = MathF.Sqrt(dx * dx + dz * dz);
                    Assert.That(centres, Is.GreaterThan(0.99f),
                        $"models {i} and {j} are {centres:0.##}\" apart centre to centre - their 1\" "
                        + "bases overlap.");
                }
            }
        }

        /// <summary>
        /// The amendment Kai asked for: "can we somehow make it capable of making models curved on the
        /// line?" A curved drag must produce a curved unit, and each model must look outward along
        /// its own part of the curve rather than all facing the same way.
        /// </summary>
        [Test]
        public void ACurvedDragCurvesTheUnitAndTurnsEachModelWithIt()
        {
            // A quarter circle of radius 8 about (20,20), from due east round to due north.
            var path = new List<Position>();
            for (int step = 0; step <= 24; step++)
            {
                float a = step / 24f * (MathF.PI / 2f);
                path.Add(new Position(20f + 8f * MathF.Cos(a), 20f + 8f * MathF.Sin(a)));
            }

            var placed = PathFormation.Along(path, Same(5, 1f), Same(5, 1f), new Float2(0f, -1f));

            // Curved, not straight: the middle model must sit off the chord between the two ends.
            Position a0 = placed[0].At, a4 = placed[4].At, mid = placed[2].At;
            float cx = (a0.x + a4.x) * 0.5f, cz = (a0.z + a4.z) * 0.5f;
            float offChord = MathF.Sqrt((mid.x - cx) * (mid.x - cx) + (mid.z - cz) * (mid.z - cz));
            Assert.That(offChord, Is.GreaterThan(0.5f),
                "the unit came out straight - the curve of the drag was thrown away.");

            // And the facings fan round with it, rather than every model pointing the same way.
            float firstToLast = MathF.Abs(AngleOf(placed[0].Facing) - AngleOf(placed[4].Facing));
            if (firstToLast > MathF.PI) firstToLast = MathF.Tau - firstToLast;
            Assert.That(firstToLast, Is.GreaterThan(0.5f),
                "every model is facing the same way on a curved line - each should look outward along "
                + "its own part of the curve.");
        }

        private static float AngleOf(Float2 f) => MathF.Atan2(f.Y, f.X);

        /// <summary>
        /// A line has two perpendiculars, and picking the wrong one faces the whole unit at its own
        /// table edge. The caller says which way is forward; the facing must be the normal nearer it.
        /// </summary>
        [Test]
        public void TheUnitFacesTheWayItWasToldIsForward()
        {
            var north = PathFormation.Along(Straight(10f, 20f), Same(4, 1f), Same(4, 1f), new Float2(0f, 1f));
            foreach (var p in north)
                Assert.That(p.Facing.Y, Is.GreaterThan(0.9f), "should be facing +z.");

            var south = PathFormation.Along(Straight(10f, 20f), Same(4, 1f), Same(4, 1f), new Float2(0f, -1f));
            foreach (var p in south)
                Assert.That(p.Facing.Y, Is.LessThan(-0.9f), "should be facing -z.");
        }

        [Test]
        public void WrappedRowsStackBehindTheLineNotInFrontOfIt()
        {
            // Facing +z, so later rows must be at LOWER z - behind the front rank, away from the enemy.
            var placed = PathFormation.Along(Straight(10f, 13f), Same(6, 1f), Same(6, 1f), new Float2(0f, 1f));

            float frontZ = placed.Max(p => p.At.z);
            float backZ  = placed.Min(p => p.At.z);
            Assert.That(placed[0].At.z, Is.EqualTo(frontZ).Within(0.01f),
                "the first models dragged are the front rank.");
            Assert.That(placed[^1].At.z, Is.EqualTo(backZ).Within(0.01f),
                "the overflow goes behind, not in front - a second rank in front of the first would "
                + "put models closer to the enemy than the player dragged.");
        }

        [Test]
        public void MixedBaseSizesGetTheirOwnRoom()
        {
            // A big model among small ones must not be crammed into a small model's slot.
            var widths = new[] { 1f, 2f, 1f };
            var placed = PathFormation.Along(Straight(10f, 20f), widths, widths, North);

            float gap01 = GapBetween(placed[0], placed[1], 0f) - (widths[0] + widths[1]) * 0.5f;
            float gap12 = GapBetween(placed[1], placed[2], 0f) - (widths[1] + widths[2]) * 0.5f;

            Assert.That(gap01, Is.GreaterThanOrEqualTo(PathFormation.TightGapInches - 0.01f),
                "the big model's base is overlapping its neighbour.");
            Assert.That(gap12, Is.GreaterThanOrEqualTo(PathFormation.TightGapInches - 0.01f));
        }

        [Test]
        public void ADragThatGoesNowhereStillPlacesEverybody()
        {
            // A click rather than a drag: one point, or a few jittery ones in the same spot. It must
            // not return an empty list or stack every model on one square inch.
            var path = new List<Position> { new Position(20f, 20f), new Position(20.01f, 20.005f) };
            var placed = PathFormation.Along(path, Same(5, 1f), Same(5, 1f), North);

            Assert.That(placed, Has.Length.EqualTo(5));
            for (int i = 0; i < placed.Length; i++)
                for (int j = i + 1; j < placed.Length; j++)
                {
                    float dx = placed[j].At.x - placed[i].At.x, dz = placed[j].At.z - placed[i].At.z;
                    Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.GreaterThan(0.99f),
                        $"models {i} and {j} are on top of each other.");
                }
        }

        [Test]
        public void NoModels_NoPlacements()
        {
            Assert.That(PathFormation.Along(Straight(10f, 20f), Array.Empty<float>(), Array.Empty<float>(), North),
                Is.Empty);
        }

        /// <summary>
        /// A sweep over drag lengths and unit sizes: whatever is dragged, the result must always be a
        /// legal formation - every model present, nobody overlapping, nobody beyond the cohesion
        /// distance from a neighbour. The individual tests above each pin one behaviour; this says the
        /// gesture cannot produce an illegal unit at all.
        /// </summary>
        [Test]
        public void AnyDragProducesALegalFormation()
        {
            foreach (int n in new[] { 1, 2, 3, 5, 8, 12, 20 })
            {
                for (float len = 0.5f; len <= 40f; len += 0.5f)
                {
                    var widths = Same(n, 1f);
                    var placed = PathFormation.Along(Straight(10f, 10f + len), widths, widths, North);

                    Assert.That(placed, Has.Length.EqualTo(n), $"n={n} len={len}: lost a model.");

                    for (int i = 0; i < n; i++)
                    {
                        for (int j = i + 1; j < n; j++)
                        {
                            float dx = placed[j].At.x - placed[i].At.x, dz = placed[j].At.z - placed[i].At.z;
                            float centres = MathF.Sqrt(dx * dx + dz * dz);
                            Assert.That(centres, Is.GreaterThan(0.98f),
                                $"n={n} len={len}: models {i} and {j} overlap ({centres:0.##}\" apart).");
                        }

                        // Every model needs SOMEBODY within the nearest-neighbour rule, or the unit is
                        // not a unit. (Checked as centre distance minus the two 1" bases.)
                        if (n == 1) continue;
                        float nearest = float.MaxValue;
                        for (int j = 0; j < n; j++)
                        {
                            if (i == j) continue;
                            float dx = placed[j].At.x - placed[i].At.x, dz = placed[j].At.z - placed[i].At.z;
                            nearest = MathF.Min(nearest, MathF.Sqrt(dx * dx + dz * dz) - 1f);
                        }
                        Assert.That(nearest, Is.LessThanOrEqualTo(PathFormation.MaxGapInches + 0.01f),
                            $"n={n} len={len}: model {i}'s nearest neighbour is {nearest:0.##}\" away, "
                            + "past the cohesion rule.");
                    }
                }
            }
        }
    }
}
