using FDG.Stages;

using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Where a Scout may stand: its own deployment zone, extended toward the middle of the table.
    ///
    /// <para>
    /// Reported by Kai, 2026-11-02, from a map he drew himself: <i>"scout (9") doesn't seem to work with
    /// the custom deployment zones on the custom map i made."</i> The zone maths assumed every deployment
    /// zone is a band across the bottom or the top of the table and extended only in Z. Every zone the
    /// game generates is such a band, so nothing had ever disagreed with it - a hand-drawn one can be
    /// anything.
    /// </para>
    /// <para>
    /// And it did not fail safe. A zone spanning the table's full height sits with its centre exactly on
    /// the midline, so the old code took its "top zone" branch and pushed the zone's BOTTOM edge up to
    /// the middle - handing the Scout the top half of its own zone and taking the rest away. The unit was
    /// worse off for having the rule than without it.
    /// </para>
    ///
    /// <para>
    /// A 72x48 table throughout, so the middle is (36, 24), except where a test says otherwise.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ScoutForwardZoneTests
    {
        private const float MiddleX = 36f, MiddleZ = 24f, Range = 9f;

        private static RectangularZone Forward(RectangularZone zone, float range = Range,
                                               float middleX = MiddleX, float middleZ = MiddleZ) =>
            PlaceDeferredUnitsStage.ForwardZone(zone, range, middleX, middleZ);

        private static float Area(RectangularZone z) => (z.Right - z.Left) * (z.Top - z.Bottom);

        // -- The standard bands, which must not have changed -----------------------------------------

        [Test]
        public void ABottomBandExtendsUpTowardTheMiddle()
        {
            RectangularZone forward = Forward(new RectangularZone(0f, 72f, 0f, 12f));

            Assert.That(forward.Bottom, Is.EqualTo(0f), "the back edge does not move.");
            Assert.That(forward.Top, Is.EqualTo(21f), "12 + 9 forward.");
            Assert.That(forward.Left, Is.EqualTo(0f));
            Assert.That(forward.Right, Is.EqualTo(72f), "a full-width band has no forward along X.");
        }

        [Test]
        public void ATopBandExtendsDownTowardTheMiddle()
        {
            RectangularZone forward = Forward(new RectangularZone(0f, 72f, 36f, 48f));

            Assert.That(forward.Top, Is.EqualTo(48f));
            Assert.That(forward.Bottom, Is.EqualTo(27f), "36 - 9 forward.");
        }

        [Test]
        public void AScoutCannotCrossTheMiddle()
        {
            // 20" of zone with 9" of Scout would reach 29", past the midline at 24".
            RectangularZone forward = Forward(new RectangularZone(0f, 72f, 0f, 20f));

            Assert.That(forward.Top, Is.EqualTo(MiddleZ), "clamped at the middle, not 29.");
        }

        // -- The hand-drawn shapes that broke it -----------------------------------------------------

        /// <summary>
        /// The exact shape of Kai's bug: a zone down the left edge, spanning the full height. The old
        /// code read its centre as being on the midline, took the top-zone branch, and cut the zone in
        /// half.
        /// </summary>
        [Test]
        public void ALeftHandZoneExtendsRight_AndKeepsItsWholeHeight()
        {
            var zone = new RectangularZone(0f, 12f, 0f, 48f);

            RectangularZone forward = Forward(zone);

            Assert.That(forward.Right, Is.EqualTo(21f), "12 + 9 forward, along X this time.");
            Assert.That(forward.Left, Is.EqualTo(0f));
            Assert.That(forward.Bottom, Is.EqualTo(0f),
                "the full height is kept - this is the edge the old code moved to the middle.");
            Assert.That(forward.Top, Is.EqualTo(48f));
        }

        [Test]
        public void ARightHandZoneExtendsLeft()
        {
            RectangularZone forward = Forward(new RectangularZone(60f, 72f, 0f, 48f));

            Assert.That(forward.Left, Is.EqualTo(51f), "60 - 9 forward.");
            Assert.That(forward.Right, Is.EqualTo(72f));
        }

        /// <summary>A corner zone is forward on both axes - there is no reason it would not be.</summary>
        [Test]
        public void ACornerZoneExtendsOnBothAxes()
        {
            RectangularZone forward = Forward(new RectangularZone(0f, 18f, 0f, 12f));

            Assert.That(forward.Right, Is.EqualTo(27f));
            Assert.That(forward.Top, Is.EqualTo(21f));
        }

        /// <summary>
        /// The invariant that would have caught this on its own: Scout may only ever ADD room. A unit
        /// with the rule can always stand anywhere it could have stood without it.
        /// </summary>
        [Test]
        public void ScoutNeverTakesRoomAway()
        {
            var zones = new[]
            {
                new RectangularZone(0f, 72f, 0f, 12f),      // bottom band
                new RectangularZone(0f, 72f, 36f, 48f),     // top band
                new RectangularZone(0f, 12f, 0f, 48f),      // left edge - the reported one
                new RectangularZone(60f, 72f, 0f, 48f),     // right edge
                new RectangularZone(0f, 18f, 0f, 12f),      // corner
                new RectangularZone(0f, 72f, 0f, 48f),      // the whole table
                new RectangularZone(30f, 42f, 18f, 30f),    // straddling the middle on both axes
            };

            foreach (RectangularZone zone in zones)
            {
                RectangularZone forward = Forward(zone);
                Assert.That(forward.Left, Is.LessThanOrEqualTo(zone.Left), $"{zone.Left}..{zone.Right}");
                Assert.That(forward.Right, Is.GreaterThanOrEqualTo(zone.Right));
                Assert.That(forward.Bottom, Is.LessThanOrEqualTo(zone.Bottom));
                Assert.That(forward.Top, Is.GreaterThanOrEqualTo(zone.Top));
                Assert.That(Area(forward), Is.GreaterThanOrEqualTo(Area(zone)));
            }
        }

        /// <summary>
        /// A zone already spanning the middle has no "forward" along that axis, and must be left alone
        /// rather than pushed somewhere arbitrary.
        /// </summary>
        [Test]
        public void AZoneStraddlingTheMiddleIsUnchangedOnThatAxis()
        {
            RectangularZone forward = Forward(new RectangularZone(30f, 42f, 18f, 30f));

            Assert.That(forward.Left, Is.EqualTo(30f));
            Assert.That(forward.Right, Is.EqualTo(42f));
            Assert.That(forward.Bottom, Is.EqualTo(18f));
            Assert.That(forward.Top, Is.EqualTo(30f));
        }

        /// <summary>
        /// The middle comes from the LIVE table: Firefight and Skirmish play on 48x48, so a Scout there
        /// clamps at 24 across rather than at a 72-wide table's 36.
        /// </summary>
        [Test]
        public void TheMiddleFollowsTheTableSize()
        {
            // A left-edge zone on a 48x48 table: 9" forward from 20" would reach 29", past that middle.
            RectangularZone forward = Forward(new RectangularZone(0f, 20f, 0f, 48f),
                middleX: 24f, middleZ: 24f);

            Assert.That(forward.Right, Is.EqualTo(24f), "clamped at this table's middle, not a 72's.");
        }

        /// <summary>Quest's 12" reaches further than Grimdark Future's 9", by exactly 3".</summary>
        [Test]
        public void TheRangeIsWhateverTheSystemSays()
        {
            var zone = new RectangularZone(0f, 72f, 0f, 6f);

            Assert.That(Forward(zone, range: 9f).Top, Is.EqualTo(15f));
            Assert.That(Forward(zone, range: 12f).Top, Is.EqualTo(18f));
        }
    }
}
