using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - "Unique Hero" is a Hero, and you may only take one of it.
    //
    // OPR 3.6.0: "Counts as having Hero, and this model may only be taken once per army." 147 units in the
    // bundled corpus carry it - every named enemy character, 30 of them in Star Quest - and NOT ONE of
    // them also carries plain "Hero". So every place in the engine that asks "is this a Hero?" by name
    // answered no for all 147.
    //
    // That matters in four separate places, which is why this fixture asserts each one rather than just
    // the predicate:
    //
    //   - the force-org Hero cap (a named character didn't count, so an army could field more Heroes than
    //     the book allows and the validator said nothing);
    //   - the Hero unit-join ("Heroes with up to Tough(6) may deploy as part of one multi-model unit"),
    //     so a named character could not be attached to a squad at all;
    //   - base-size estimation, where a Hero gets a 40/50/60mm round base by bulk and everything else
    //     gets infantry sizing - so a named character with no declared base came out the wrong size;
    //   - the once-per-army limit itself, which was enforced NOWHERE.
    //
    // The in-game half already worked: the supplement definition grants "Hero" at Lifecycle_OnUnitCreated,
    // so once a unit exists its rules are right. Everything above happens at army-build and army-setup
    // time, reading the army FILE, where no token grant has happened yet - which is exactly why a bug
    // this wide could sit behind a rule that looks implemented.
    [TestFixture]
    public class UniqueHeroTests
    {
        private const string UniqueHero = "Unique Hero";

        private static UnitFileEntry Unit(string name, int points, params string[] rules)
        {
            var entry = new UnitFileEntry { Name = name, ModelCount = 1, PointCost = points, Quality = 3, Defense = 3 };
            foreach (string rule in rules) entry.SpecialRules.Add(new SpecialRuleEntry_Core(rule));
            return entry;
        }

        private static ArmyListFile Army(int limit, params UnitFileEntry[] units)
        {
            var army = new ArmyListFile { Name = "Test", PointsLimit = limit, GameSystem = GameSystems.GrimdarkFuture };
            army.Units.AddRange(units);
            return army;
        }

        // ── Is it a Hero at all? ────────────────────────────────────────────────────────────────────

        [Test]
        public void AUniqueHero_CountsAsAHero()
        {
            // The single shared answer every site below depends on.
            Assert.That(ForceOrgValidator.IsHero(Unit("Trasara Ioncrinius", 150, UniqueHero)), Is.True,
                "3.6.0: \"Counts as having Hero\" - so the one place that answers \"is this a Hero?\" "
                + "must say yes");
        }

        [Test]
        public void APlainUnit_IsStillNotAHero()
        {
            Assert.That(ForceOrgValidator.IsHero(Unit("Grunts", 90)), Is.False);
            Assert.That(ForceOrgValidator.IsHero(Unit("Walker", 200, "Tough(6)", "Fear(2)")), Is.False);
        }

        // ── The force-org Hero cap ──────────────────────────────────────────────────────────────────

        [Test]
        public void NamedCharacters_CountTowardTheHeroCap()
        {
            // Grimdark Future allows 1 Hero per 500 pts, so a 1000 pt army may take 2. Three named
            // characters is over, and before this the validator counted none of them.
            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(Army(1000,
                Unit("Named A", 150, UniqueHero),
                Unit("Named B", 150, UniqueHero),
                Unit("Named C", 150, UniqueHero),
                Unit("Grunts", 90)));

            Assert.That(warnings.Any(w => w.Contains("Too many Heroes")), Is.True,
                $"three Heroes in a 1000 pt army is over the cap of 2. Warnings: [{string.Join(" | ", warnings)}]");
        }

        [Test]
        public void AMixOfHeroesAndNamedCharacters_IsCountedTogether()
        {
            // The two spellings are not two separate allowances.
            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(Army(1000,
                Unit("Captain", 150, "Hero"),
                Unit("Named A", 150, UniqueHero),
                Unit("Named B", 150, UniqueHero),
                Unit("Grunts", 90)));

            Assert.That(warnings.Any(w => w.Contains("Too many Heroes")), Is.True,
                string.Join(" | ", warnings));
        }

        // ── Once per army ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void TakingTheSameNamedCharacterTwice_IsWarnedAbout()
        {
            // The half that was enforced nowhere at all. The copy cap doesn't catch it: at 2000 pts the
            // books allow 1+X copies of a unit, which is 3 or more, so two of the same named character
            // sat comfortably inside it.
            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(Army(2000,
                Unit("Trasara Ioncrinius", 150, UniqueHero),
                Unit("Trasara Ioncrinius", 150, UniqueHero),
                Unit("Grunts", 90)));

            Assert.That(warnings.Any(w => w.Contains("Trasara Ioncrinius") && w.Contains("once")), Is.True,
                "3.6.0: \"this model may only be taken once per army\". Warnings: ["
                + string.Join(" | ", warnings) + "]");
        }

        [Test]
        public void OneOfEachNamedCharacter_IsFine()
        {
            // The negative half: the limit is per model, not a limit of one named character per army.
            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(Army(2000,
                Unit("Trasara Ioncrinius", 150, UniqueHero),
                Unit("Mordred the Grim", 150, UniqueHero),
                Unit("Grunts", 90),
                Unit("Grunts", 90)));

            Assert.That(warnings.Any(w => w.Contains("once")), Is.False,
                "different named characters are different models. Warnings: ["
                + string.Join(" | ", warnings) + "]");
        }

        [Test]
        public void AnOrdinaryUnitTakenTwice_IsNotWarnedAboutAsUnique()
        {
            // Two squads of Grunts is normal play and must not trip the once-per-army message.
            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(Army(2000,
                Unit("Grunts", 90),
                Unit("Grunts", 90)));

            Assert.That(warnings.Any(w => w.Contains("once")), Is.False, string.Join(" | ", warnings));
        }

        // ── Base size ───────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ANamedCharacterWithNoDeclaredBase_IsSizedAsAHero()
        {
            // A Hero is given a round base sized by bulk (40/50/60mm); anything else is sized as
            // infantry. So a named character used to come out on the wrong base, which is visible on the
            // table and affects what it can reach.
            BaseFileEntry named = DefaultBaseEstimator.Estimate(
                Unit("Trasara Ioncrinius", 150, UniqueHero, "Tough(6)").SpecialRules, out string why);
            BaseFileEntry plain = DefaultBaseEstimator.Estimate(
                Unit("Captain", 150, "Hero", "Tough(6)").SpecialRules, out _);

            Assert.That(named.Shape, Is.EqualTo(plain.Shape));
            Assert.That(named.DiameterInches, Is.EqualTo(plain.DiameterInches).Within(1e-4f),
                $"a named character is a Hero, so it gets a Hero's base. Chose: {why}");
        }

        // ── The unit join ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void TheHeroRuleNames_AreTheOnlyTwo_AndTheJoinKnowsBoth()
        {
            // The join resolves against rule DEFINITIONS rather than file entries, so it needs the same
            // set from the other side. Asserted through the shared list so the two can't drift: a third
            // spelling added in a future book must be added once, here, and reach every site.
            Assert.That(CoreRuleCatalog.HeroRuleNames, Is.EquivalentTo(new[] { "Hero", UniqueHero }));
            Assert.That(CoreRuleCatalog.IsHeroRuleName(UniqueHero), Is.True);
            Assert.That(CoreRuleCatalog.IsHeroRuleName("Hero"), Is.True);
            Assert.That(CoreRuleCatalog.IsHeroRuleName("Tough"), Is.False);
        }

        [Test]
        public void TheCatalog_DoesNotDefineUniqueHero_BecauseTheSupplementDoes()
        {
            // A trap worth a test of its own. Unique Hero's "counts as having Hero" half is a HOOK - an
            // addRule grant at Lifecycle_OnUnitCreated - and that definition lives in the rule
            // supplement. The obvious-looking tidy-up is to add a marker definition here next to Hero,
            // the way Hero and Transport are markers; that would shadow the supplement's and silently
            // delete the grant, leaving a rule that still resolves, still validates, and no longer does
            // the thing it is for. The catalog therefore carries only the NAME.
            Assert.That(CoreRuleCatalog.All.Any(r => r.Name == UniqueHero), Is.False,
                "Unique Hero's definition belongs to the rule supplement, which gives it the addRule "
                + "grant of \"Hero\". A copy here would shadow it and drop the grant.");
            Assert.That(CoreRuleCatalog.UniqueHeroRuleName, Is.EqualTo(UniqueHero));
        }

        // ── The blocking check, in the Army Forge ───────────────────────────────────────────────────
        //
        // ListValidator is the authoritative once-per-army check: it runs while a list is being built
        // against its book and raises a blocking Error. It has done so since the Forge existed, and it
        // matched "Unique" - 3.5's name. 3.6 folded the rule into "Unique Hero", and the bundled corpus
        // has 213 references to the new name and ZERO to the old one, so for every book in the game that
        // Error could not fire. The code was there, the test was there, and the rule was not enforced.

        [Test]
        public void TheForge_RefusesASecondCopyOfANamedCharacter()
        {
            BookFile book = DemoBook.Build();
            book.Units[0].Rules.Add(new SpecialRuleEntry_Core(UniqueHero));
            var list = new BuilderList { Name = "L", PointsLimit = 100000 };
            list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });
            list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });

            var issues = ListValidator.Validate(book, list, ListCompiler.Compile(book, list));

            Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Error && i.Message.Contains("Unique")),
                Is.True, "3.6.0's \"may only be taken once per army\" must block in the Forge. Issues: ["
                + string.Join(" | ", issues.Select(i => i.Message)) + "]");
        }

        [Test]
        public void TheForge_StillRefusesASecondCopyUnderTheOldName()
        {
            // "Unique" has to keep working: a book imported before the rename, or a list saved from one,
            // still spells it that way. Fixing the new name by replacing the old one would just move the
            // dead spot.
            BookFile book = DemoBook.Build();
            book.Units[0].Rules.Add(new SpecialRuleEntry_Core("Unique"));
            var list = new BuilderList { Name = "L", PointsLimit = 100000 };
            list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });
            list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });

            Assert.That(ListValidator.Validate(book, list, ListCompiler.Compile(book, list))
                    .Any(i => i.Severity == ListIssueSeverity.Error && i.Message.Contains("Unique")),
                Is.True);
        }

        [Test]
        public void TheForge_AllowsOneCopy()
        {
            BookFile book = DemoBook.Build();
            book.Units[0].Rules.Add(new SpecialRuleEntry_Core(UniqueHero));
            var list = new BuilderList { Name = "L", PointsLimit = 100000 };
            list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });

            Assert.That(ListValidator.Validate(book, list, ListCompiler.Compile(book, list)).Any(i => i.Message.Contains("Unique")), Is.False);
        }
    }
}
