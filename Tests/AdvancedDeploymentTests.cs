using FDG.Data;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The Advanced Rulebooks' 18 deployment styles, read off Kai's screenshots of the GF: Firefight /
    /// AoF: Skirmish pages (2026-11-05). 4'x4' table, 6x6 grid = 8" a square.
    /// </summary>
    [TestFixture]
    public class AdvancedDeploymentTests
    {
        private static readonly EDeploymentStyle[] AllStyles =
            AdvancedDeployment.InFamily(EDeploymentFamily.Standard)
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Strategic))
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Asymmetric)).ToArray();

        private static bool In(RectangularZone z, float x, float y) => z.IsPointWithinZone(new Float2(x, y));

        /// <summary>Sampled area on a half-inch grid, in square inches.</summary>
        private static float Area(RectangularZone z)
        {
            int n = 0;
            for (float x = 0.25f; x < 48f; x += 0.5f)
                for (float y = 0.25f; y < 48f; y += 0.5f)
                    if (In(z, x, y)) n++;
            return n * 0.25f;
        }

        [Test]
        public void ThereAreEighteen_SixPerFamily()
        {
            Assert.That(AllStyles.Distinct().Count(), Is.EqualTo(18));
        }

        [TestCaseSource(nameof(AllStyles))]
        public void TheTwoZones_NeverOverlap_AndBothHaveGround(EDeploymentStyle style)
        {
            var (a, b) = AdvancedDeployment.Zones(style, 48f, 48f);
            Assert.That(Area(a), Is.GreaterThan(50f));
            Assert.That(Area(b), Is.GreaterThan(50f));
            for (float x = 0.25f; x < 48f; x += 0.5f)
                for (float y = 0.25f; y < 48f; y += 0.5f)
                    Assert.That(In(a, x, y) && In(b, x, y), Is.False, $"{style}: ({x}, {y}) is in both zones");
        }

        /// <summary>"These styles are meant to provide a balanced experience" - so both sides get the same ground.</summary>
        [TestCaseSource(nameof(BalancedStyles))]
        public void BalancedStyles_GiveBothSidesTheSameArea(EDeploymentStyle style)
        {
            var (a, b) = AdvancedDeployment.Zones(style, 48f, 48f);
            Assert.That(Area(a), Is.EqualTo(Area(b)).Within(1f));
        }

        private static IEnumerable<EDeploymentStyle> BalancedStyles() =>
            AdvancedDeployment.InFamily(EDeploymentFamily.Standard)
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Strategic));

        /// <summary>"Note that this is the basic deployment from the core rules." - the scale check.</summary>
        [Test]
        public void FrontLine_IsTheCoreRulesTwelveInches()
        {
            var (a, b) = AdvancedDeployment.Zones(EDeploymentStyle.FrontLine, 48f, 48f);
            Assert.That(Area(a), Is.EqualTo(48f * 12f).Within(1f));
            Assert.That(In(a, 10, 37) && !In(a, 10, 35), Is.True);
            Assert.That(In(b, 10, 11) && !In(b, 10, 13), Is.True);
        }

        [Test]
        public void SpotChecks_AgainstTheDiagrams()
        {
            var gw = AdvancedDeployment.Zones(EDeploymentStyle.GroundWar, 48, 48);
            Assert.That(In(gw.A, 6, 30) && !In(gw.A, 18, 30) && In(gw.B, 40, 18) && !In(gw.B, 30, 18), Is.True, "Ground War");

            var sb = AdvancedDeployment.Zones(EDeploymentStyle.SideBattle, 48, 48);
            Assert.That(In(sb.A, 2, 2) && !In(sb.A, 30, 10) && In(sb.B, 46, 46) && !In(sb.B, 2, 46), Is.True, "Side Battle");

            var sp = AdvancedDeployment.Zones(EDeploymentStyle.Spearhead, 48, 48);
            Assert.That(In(sp.A, 24, 37) && !In(sp.A, 24, 35) && !In(sp.A, 4, 40), Is.True, "Spearhead");

            var lh = AdvancedDeployment.Zones(EDeploymentStyle.LongHaul, 48, 48);
            Assert.That(In(lh.B, 24, 23) && !In(lh.B, 24, 25) && In(lh.A, 2, 46) && !In(lh.A, 24, 40), Is.True, "Long Haul");

            var en = AdvancedDeployment.Zones(EDeploymentStyle.Encircled, 48, 48);
            Assert.That(In(en.A, 3, 24) && !In(en.A, 10, 24) && In(en.B, 24, 24) && !In(en.B, 16, 24), Is.True, "Encircled");

            var ls = AdvancedDeployment.Zones(EDeploymentStyle.LightningStrike, 48, 48);
            Assert.That(In(ls.A, 24, 13) && !In(ls.A, 24, 11) && In(ls.B, 1, 1) && !In(ls.B, 24, 4), Is.True, "Lightning Strike");
        }

        [Test]
        public void ABaseMayNotHangOverASlantedEdge()
        {
            var (a, _) = AdvancedDeployment.Zones(EDeploymentStyle.SideBattle, 48, 48);
            // Zone A's hypotenuse runs (0,24)-(42,0). A point just inside it is in; a 1"-radius base there is not.
            Assert.That(In(a, 10, 18), Is.True);
            Assert.That(a.ContainsFootprint(10f, 18f, 1f, 1f), Is.False);
            Assert.That(a.ContainsFootprint(4f, 4f, 1f, 1f), Is.True);
        }

        [Test]
        public void TheZonesScaleWithTheTable()
        {
            var (a, _) = AdvancedDeployment.Zones(EDeploymentStyle.OpposingForces, 96f, 48f);
            Assert.That(In(a, 46, 40) && !In(a, 50, 40), Is.True);
        }

        [Test]
        public void ARandomStyle_RollsWithinItsFamily_InTheBooksOrder()
        {
            Assert.That(AdvancedDeployment.Resolve(EDeploymentStyle.RandomStandard, () => 2), Is.EqualTo(EDeploymentStyle.GroundWar));
            Assert.That(AdvancedDeployment.Resolve(EDeploymentStyle.RandomStrategic, () => 6), Is.EqualTo(EDeploymentStyle.MeetingEngagement));
            Assert.That(AdvancedDeployment.Resolve(EDeploymentStyle.RandomAsymmetric, () => 1), Is.EqualTo(EDeploymentStyle.OpenWarzone));
            Assert.That(AdvancedDeployment.Resolve(EDeploymentStyle.Encircled, () => throw new AssertionException("no roll")),
                Is.EqualTo(EDeploymentStyle.Encircled));
        }

        [Test]
        public void OnlyTheGamesWhosePagesAreIn()
        {
            Assert.That(AdvancedDeployment.Available(EGameSystem.GFFirefight), Is.True);
            Assert.That(AdvancedDeployment.Available(EGameSystem.AoFSkirmish), Is.True);
            Assert.That(AdvancedDeployment.Available(EGameSystem.GrimdarkFuture), Is.False, "GF's own pages are still to come");
            Assert.That(AdvancedDeployment.Available(EGameSystem.AgeOfFantasy), Is.False);
        }
    }
}
