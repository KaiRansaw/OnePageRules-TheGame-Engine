using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #003 — Force organization validation. ForceOrgValidator is advisory only: it returns a warning
    // string per exceeded cap and never blocks. These tests pin each of the four GDF caps (cost / hero /
    // copy / unit) plus the clean and empty cases.
    [TestFixture]
    public class ForceOrgValidatorTests
    {
        [Test]
        public void CleanArmy_WithinAllCaps_NoWarnings()
        {
            ArmyListFile army = Army(1000,
                Unit("Warriors", 200),
                Unit("Heavy Gunners", 300),
                Unit("Captain", 150, hero: true));

            Assert.That(ForceOrgValidator.Validate(army), Is.Empty);
        }

        [Test]
        public void EmptyArmy_NoWarnings()
        {
            // A brand-new empty list (the Army Builder's starting state) must not nag.
            Assert.That(ForceOrgValidator.Validate(Army(1000)), Is.Empty);
        }

        [Test]
        public void OverPointsLimit_WarnsCost()
        {
            ArmyListFile army = Army(500, Unit("Warriors", 300), Unit("Tank", 400));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings, Has.Exactly(1).Contains("Over points limit"));
        }

        [Test]
        public void AtExactlyPointsLimit_NoCostWarning()
        {
            // Boundary: equal to the limit is fine; only strictly-over warns.
            ArmyListFile army = Army(500, Unit("Warriors", 200), Unit("Gunners", 300));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Over points limit")), Is.False);
        }

        [Test]
        public void TooManyHeroes_WarnsHero()
        {
            // 1000 pts allows 2 heroes; this army has 3.
            ArmyListFile army = Army(1000,
                Unit("Warriors", 100),
                Unit("Captain", 50, hero: true),
                Unit("Lieutenant", 50, hero: true),
                Unit("Sergeant", 50, hero: true));

            Assert.That(ForceOrgValidator.Validate(army), Has.Exactly(1).Contains("Too many Heroes"));
        }

        [Test]
        public void HeroesAtAllowance_NoHeroWarning()
        {
            // Exactly 2 heroes at 1000 pts is allowed.
            ArmyListFile army = Army(1000,
                Unit("Warriors", 100),
                Unit("Captain", 50, hero: true),
                Unit("Lieutenant", 50, hero: true));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many Heroes")), Is.False);
        }

        [Test]
        public void TooManyCopies_WarnsCopy()
        {
            ArmyListFile army = Army(2000,
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors", 100));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings, Has.Exactly(1).Contains("Too many copies of \"Warriors\""));
        }

        [Test]
        public void LegacyCombinedSuffix_GroupsWithPlainSquads()
        {
            // Army files compiled before 2026-07-24 carry the old "X (Combined)" merge suffix;
            // such an entry must still count into the same copy group as plain "X" squads.
            ArmyListFile army = Army(2000,
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors (Combined)", 200));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings, Has.Exactly(1).Contains("Too many copies of \"Warriors\""));
        }

        [Test]
        public void BlankNamedUnits_DoNotCountAsDuplicates()
        {
            // Four unnamed (incomplete) entries must not read as 4 copies of the same unit.
            ArmyListFile army = Army(2000, Unit("", 100), Unit("", 100), Unit("", 100), Unit("", 100));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many copies")), Is.False);
        }

        [Test]
        public void AllHeroes_WarnsNoNonHeroUnits()
        {
            ArmyListFile army = Army(1500,
                Unit("Captain", 100, hero: true),
                Unit("Lieutenant", 100, hero: true));

            Assert.That(ForceOrgValidator.Validate(army), Has.Exactly(1).Contains("no non-Hero units"));
        }

        [Test]
        public void MultipleViolations_AllReported()
        {
            // Over points AND too many heroes AND no non-hero units, all at once.
            ArmyListFile army = Army(500,
                Unit("Captain", 300, hero: true),
                Unit("Lieutenant", 300, hero: true));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings.Any(w => w.Contains("Over points limit")), Is.True);
            Assert.That(warnings.Any(w => w.Contains("Too many Heroes")), Is.True);
            Assert.That(warnings.Any(w => w.Contains("no non-Hero units")), Is.True);
        }

        [Test]
        public void IsHero_DetectsHeroRuleThroughAlias()
        {
            UnitFileEntry plain = Unit("Warriors", 100);
            UnitFileEntry hero = Unit("Captain", 100, hero: true);
            UnitFileEntry aliasedHero = Unit("Warlord", 100);
            aliasedHero.SpecialRules.Add(new SpecialRuleEntry_Alias("Big Boss", new SpecialRuleEntry_Core("Hero")));

            Assert.That(ForceOrgValidator.IsHero(plain), Is.False);
            Assert.That(ForceOrgValidator.IsHero(hero), Is.True);
            Assert.That(ForceOrgValidator.IsHero(aliasedHero), Is.True, "Hero granted via an alias entry must still count.");
        }

        // ── #409 (OPR 3.6.0): per-system caps, the three caps that were never implemented, and the
        //    model/Tough density cap that is new in 3.6 ────────────────────────────────────────────────

        [Test]
        public void CopyCap_ScalesWithArmySize_InsteadOfAFlatThree()
        {
            // "1+X copies, where X is 1 per 1000pts" - so 4000pts of Grimdark Future allows 5, and four
            // copies that used to trip the old flat cap of 3 are now legal.
            ArmyListFile army = Army(4000,
                Unit("Warriors", 100), Unit("Warriors", 100),
                Unit("Warriors", 100), Unit("Warriors", 100));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many copies")), Is.False);
        }

        [Test]
        public void SingleUnitOverThirtyFivePercent_Warns()
        {
            ArmyListFile army = Army(1000, Unit("Titan", 400), Unit("Warriors", 100));

            Assert.That(ForceOrgValidator.Validate(army),
                Has.Exactly(1).Contains("single-unit cap"));
        }

        [Test]
        public void SingleUnitAtExactlyThirtyFivePercent_DoesNotWarn()
        {
            ArmyListFile army = Army(1000, Unit("Titan", 350), Unit("Warriors", 100));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("single-unit cap")), Is.False,
                "'over 35%' - exactly 35% is inside the cap.");
        }

        [Test]
        public void TooManyUnits_Warns()
        {
            // 1 unit per 200pts, so 400pts allows 2.
            ArmyListFile army = Army(400,
                Unit("A", 50), Unit("B", 50), Unit("C", 50));

            Assert.That(ForceOrgValidator.Validate(army), Has.Exactly(1).Contains("Too many units"));
        }

        [Test]
        public void DensityCap_CountsModelsAndToughTogether()
        {
            // NEW IN 3.6: 1 model-or-Tough per 20pts, so 1000pts allows 50. A 20-model squad plus a
            // Tough(6) walker is 20 + 6 = 26 - inside it.
            ArmyListFile army = Army(1000, Squad("Warriors", 300, modelCount: 20), Tough("Walker", 200, 6));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("models/Tough")), Is.False);
        }

        [Test]
        public void DensityCap_WarnsWhenTheArmyIsTooCrowded()
        {
            // 400pts allows 20 at Grimdark Future's 1-per-20. Two 15-model squads is 30.
            ArmyListFile army = Army(400,
                Squad("Warriors", 100, modelCount: 15), Squad("Raiders", 100, modelCount: 15));

            Assert.That(ForceOrgValidator.Validate(army), Has.Exactly(1).Contains("Too many models/Tough"));
        }

        [Test]
        public void DensityCap_CountsToughPerModel_NotPerUnit()
        {
            // 3 models of Tough(3) is 9 toward the cap, not 3: Tough is a per-model stat.
            Assert.That(ForceOrgValidator.ModelsOrTough(Tough("Beasts", 100, 3, modelCount: 3)),
                Is.EqualTo(9));
        }

        [Test]
        public void DensityCap_ModelWithoutTough_CountsAsOne()
        {
            Assert.That(ForceOrgValidator.ModelsOrTough(Squad("Warriors", 100, modelCount: 5)),
                Is.EqualTo(5));
        }

        [Test]
        public void FirefightArmy_UsesFirefightCaps()
        {
            // GF: Firefight is 1 Hero per 400pts, where Grimdark Future is 1 per 500. At 800pts
            // Firefight allows 2 Heroes; the same army judged as Grimdark Future would allow 1.
            ArmyListFile army = Army(800,
                Unit("Warriors", 100),
                Unit("Sergeant", 100, hero: true),
                Unit("Specialist", 100, hero: true));
            army.GameSystem = ArmyBuilding.GameSystems.GFFirefight;

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many Heroes")), Is.False);

            army.GameSystem = ArmyBuilding.GameSystems.GrimdarkFuture;
            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many Heroes")), Is.True,
                "the same list is over the Hero cap under Grimdark Future's 1-per-500.");
        }

        [Test]
        public void UnknownOrAbsentGameSystem_FallsBackToGrimdarkFuture()
        {
            Assert.That(ForceOrgProfile.For(null), Is.SameAs(ForceOrgProfile.GrimdarkFuture));
            Assert.That(ForceOrgProfile.For("some-opr-custom-thing"),
                Is.SameAs(ForceOrgProfile.GrimdarkFuture),
                "an unrecognised slug stays advisory rather than silently capless.");
        }

        [Test]
        public void EveryProfile_MatchesItsRulebook()
        {
            // Read straight off the four Core Rules v3.6.0 Force Org boxes.
            Assert.Multiple(() =>
            {
                AssertProfile(ForceOrgProfile.GrimdarkFuture, 500, 1000, 200, 1, 20);
                AssertProfile(ForceOrgProfile.AgeOfFantasy, 375, 750, 150, 1, 15);
                AssertProfile(ForceOrgProfile.GrimdarkFutureFirefight, 400, 400, 80, 3, 40);
                AssertProfile(ForceOrgProfile.AgeOfFantasySkirmish, 300, 300, 60, 3, 30);
            });
        }

        private static void AssertProfile(ForceOrgProfile profile, int hero, int copy, int unit,
            int densityAllowance, int densityPoints)
        {
            Assert.That(profile.PointsPerHero, Is.EqualTo(hero), $"{profile.SystemName} Hero cap");
            Assert.That(profile.PointsPerExtraCopy, Is.EqualTo(copy), $"{profile.SystemName} copy cap");
            Assert.That(profile.PointsPerUnit, Is.EqualTo(unit), $"{profile.SystemName} unit cap");
            Assert.That(profile.DensityAllowancePerBlock, Is.EqualTo(densityAllowance),
                $"{profile.SystemName} density allowance");
            Assert.That(profile.DensityPointsPerBlock, Is.EqualTo(densityPoints),
                $"{profile.SystemName} density block");
            Assert.That(profile.MaxSingleUnitPercent, Is.EqualTo(35), $"{profile.SystemName} 35% cap");
        }

        private static UnitFileEntry Squad(string name, int pointCost, int modelCount)
        {
            UnitFileEntry unit = Unit(name, pointCost);
            unit.ModelCount = modelCount;
            return unit;
        }

        private static UnitFileEntry Tough(string name, int pointCost, int tough, int modelCount = 1)
        {
            UnitFileEntry unit = Squad(name, pointCost, modelCount);
            unit.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Tough", tough));
            return unit;
        }

        private static ArmyListFile Army(int pointsLimit, params UnitFileEntry[] units)
            => new ArmyListFile { PointsLimit = pointsLimit, Units = units.ToList() };

        private static UnitFileEntry Unit(string name, int pointCost, bool hero = false)
        {
            UnitFileEntry unit = new UnitFileEntry
            {
                Name = name,
                ModelCount = 1,
                Quality = 4,
                Defense = 4,
                PointCost = pointCost,
            };
            if (hero)
            {
                unit.SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            }
            return unit;
        }
    }
}
