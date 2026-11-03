using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Odd-shaped deployment zones (Kai, 2026-11-02: "what if I wanted to do an L shaped deployment
    /// zone?").
    ///
    /// <para>
    /// The zone used here throughout is an L: a 20x6 band along the bottom, with a 6x14 arm going up
    /// its left end. The interesting part of an L is the <b>notch</b> - the rectangle that the bounding
    /// box covers and the zone does not - because every bug this type can have shows up there.
    /// </para>
    ///
    ///     z=20 +------+
    ///          | arm  |
    ///          |      |         (the notch is here, to the right of the arm
    ///          |      |          and above the band - inside Bounds, outside
    ///      z=6 +------+-------+  the zone)
    ///          |     band     |
    ///      z=0 +--------------+
    ///         x=0    x=6     x=20
    /// </summary>
    [TestFixture]
    public class MultiRectZoneTests
    {
        private static MultiRectZone L() => new(new List<RectangularZone>
        {
            new RectangularZone(0f, 20f, 0f, 6f),    // band
            new RectangularZone(0f, 6f, 6f, 20f),    // arm
        });

        [Test]
        public void BoundsAreTheBoundingBoxOfEveryPart()
        {
            ZoneBounds b = L().Bounds;
            Assert.That(b.Left, Is.EqualTo(0f));
            Assert.That(b.Right, Is.EqualTo(20f));
            Assert.That(b.Bottom, Is.EqualTo(0f));
            Assert.That(b.Top, Is.EqualTo(20f));
        }

        /// <summary>
        /// The reason this type can exist at all: it IS a RectangularZone, so the deployment pipeline -
        /// typed to RectangularZone from the request through the data bindings to the save - needs no
        /// re-typing. If that ever stops being true, odd-shaped zones become a very different job.
        /// </summary>
        [Test]
        public void ItIsARectangularZone()
        {
            Assert.That(L(), Is.InstanceOf<RectangularZone>());
            Assert.That(L(), Is.InstanceOf<IBoundedZone>());
        }

        [Test]
        public void PointsInEitherArmAreInside()
        {
            MultiRectZone zone = L();
            Assert.That(zone.IsPointWithinZone(new Float2(18f, 3f)), Is.True, "far end of the band.");
            Assert.That(zone.IsPointWithinZone(new Float2(3f, 18f)), Is.True, "top of the arm.");
            Assert.That(zone.IsPointWithinZone(new Float2(3f, 3f)), Is.True, "the corner where they meet.");
        }

        /// <summary>The whole point of an L: the notch is inside the bounding box and outside the zone.</summary>
        [Test]
        public void TheNotchIsOutsideTheZoneButInsideItsBounds()
        {
            MultiRectZone zone = L();
            var notch = new Float2(15f, 15f);

            ZoneBounds b = zone.Bounds;
            Assert.That(notch.X, Is.InRange(b.Left, b.Right));
            Assert.That(notch.Y, Is.InRange(b.Bottom, b.Top));

            Assert.That(zone.IsPointWithinZone(notch), Is.False,
                "if this passes, the zone is just its bounding box and the L means nothing.");
        }

        [Test]
        public void PointsOutsideEverythingAreOutside()
        {
            MultiRectZone zone = L();
            Assert.That(zone.IsPointWithinZone(new Float2(-1f, 3f)), Is.False);
            Assert.That(zone.IsPointWithinZone(new Float2(25f, 3f)), Is.False);
            Assert.That(zone.IsPointWithinZone(new Float2(3f, 25f)), Is.False);
        }

        /// <summary>
        /// A footprint wholly inside one arm is fine; one that pokes into the notch is not. This is the
        /// test a centre-only check fails - and it is not academic, because the notch of a deployment
        /// zone is usually ground somebody else is entitled to.
        /// </summary>
        [Test]
        public void AFootprintMayNotPokeIntoTheNotch()
        {
            MultiRectZone zone = L();

            Assert.That(zone.ContainsFootprint(3f, 3f, 0.6f, 0.6f), Is.True,
                "well inside the corner where the two arms meet.");

            // Centre inside the arm, but a 1.2"-wide base reaches past x=6 into the notch.
            Assert.That(zone.ContainsFootprint(5.7f, 15f, 0.6f, 0.6f), Is.False,
                "centre is in the arm, but the base hangs over the notch.");

            Assert.That(zone.ContainsFootprint(5.2f, 15f, 0.6f, 0.6f), Is.True,
                "pulled back far enough that the whole base is in the arm.");
        }

        /// <summary>
        /// The footprint test works by carving the base's box against each part, which is exact where
        /// testing corners is not: a box can have all four corners inside different parts and still
        /// have uncovered ground in the middle.
        /// </summary>
        [Test]
        public void AFootprintSpanningTwoTouchingPartsIsInside()
        {
            // Two rectangles meeting along x = 10, forming one 20x6 band with a seam down the middle.
            var seamed = new MultiRectZone(new List<RectangularZone>
            {
                new RectangularZone(0f, 10f, 0f, 6f),
                new RectangularZone(10f, 20f, 0f, 6f),
            });

            Assert.That(seamed.ContainsFootprint(10f, 3f, 1f, 1f), Is.True,
                "a base straddling the seam is inside - the two parts together cover it, and a "
                + "per-part test would have rejected it for fitting in neither.");
        }

        [Test]
        public void AFootprintStraddlingAGapIsNotInside()
        {
            // The same two rectangles, but with a real gap between them.
            var gapped = new MultiRectZone(new List<RectangularZone>
            {
                new RectangularZone(0f, 9f, 0f, 6f),
                new RectangularZone(11f, 20f, 0f, 6f),
            });

            Assert.That(gapped.ContainsFootprint(10f, 3f, 1f, 1f), Is.False,
                "the base is over the gap between the two parts.");
        }

        [Test]
        public void APathThroughEitherArmIntersects()
        {
            MultiRectZone zone = L();
            Assert.That(zone.DoesPathIntersectZone(new Float2(-5f, 3f), new Float2(25f, 3f)), Is.True,
                "straight along the band.");
            Assert.That(zone.DoesPathIntersectZone(new Float2(3f, -5f), new Float2(3f, 25f)), Is.True,
                "straight up the arm.");
        }

        /// <summary>A path crossing only the notch must not count as crossing the zone.</summary>
        [Test]
        public void APathThroughOnlyTheNotchDoesNotIntersect()
        {
            MultiRectZone zone = L();
            Assert.That(zone.DoesPathIntersectZone(new Float2(10f, 10f), new Float2(18f, 18f)), Is.False);
        }

        [Test]
        public void ASingleRectangleIsNotWrapped()
        {
            // Of() keeps the common case a plain rectangle, so an ordinary map carries no extra
            // machinery and behaves exactly as it did.
            RectangularZone one = MultiRectZone.Of(new List<RectangularZone>
            {
                new RectangularZone(0f, 20f, 0f, 6f),
            });

            Assert.That(one, Is.Not.InstanceOf<MultiRectZone>());
            Assert.That(MultiRectZone.Of(new List<RectangularZone>
            {
                new RectangularZone(0f, 20f, 0f, 6f),
                new RectangularZone(0f, 6f, 6f, 20f),
            }), Is.InstanceOf<MultiRectZone>());
        }

        [Test]
        public void NoPartsIsRefused()
        {
            Assert.Throws<ArgumentException>(() => new MultiRectZone(new List<RectangularZone>()));
        }

        /// <summary>
        /// A plain rectangle must answer the footprint question exactly as it always has - this type
        /// made that method virtual, and a regression there would change every ordinary deployment.
        /// </summary>
        [Test]
        public void APlainRectangleStillInsetsItsEdges()
        {
            var plain = new RectangularZone(0f, 20f, 0f, 6f);
            Assert.That(plain.ContainsFootprint(10f, 3f, 1f, 1f), Is.True);
            Assert.That(plain.ContainsFootprint(0.5f, 3f, 1f, 1f), Is.False, "overhangs the left edge.");
            Assert.That(plain.ContainsFootprint(10f, 5.5f, 1f, 1f), Is.False, "overhangs the top edge.");
        }
    }
}
