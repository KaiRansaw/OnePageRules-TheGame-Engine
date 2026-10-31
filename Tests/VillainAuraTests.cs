using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #412 - the Villain aura, on its GFSQ Core Rules v2.1.0 wording: "AI units within 6" use the
    // Villain's Qua for Morale Tests if it's better than their own, as long as the Villain isn't Shaken."
    //
    // Two separate faults are pinned here, and the second one is the reason this fixture exists at all.
    //
    // 1. THE RADIUS. v2.0.1 said 12", v2.1.0 says 6". A number like this is invisible in play - nobody
    //    watching a morale test can tell which radius produced it - so the only thing that can catch a
    //    regression is a test that stands a grunt at 9": inside the old radius, outside the new one.
    //
    // 2. THE RULE NAME. The engine looked for a rule named "Villain". No army book has one. Every Star
    //    Quest enemy book instead defines a rule NAMED "Hero" whose description is Villain's text
    //    verbatim (verified against live Army Forge data for four books on 2026-10-31). So the mechanic
    //    was dead code: it had a radius, a Shaken check, a better-Quality check and a log line, and it
    //    could never fire, because nothing on the table ever satisfied its first condition. That is the
    //    worst shape a bug can have - fully implemented, fully unreachable, and silent - so the mapping
    //    gets a test of its own below, and so does the "Villain" spelling, in case a future book uses it.
    //
    // Everything here goes through the real QuestChallenges.VillainMorale against a real data store, so
    // a change to how rules attach, how friendship is decided or how distance is measured fails here too.
    [TestFixture]
    public class VillainAuraTests
    {
        private const int OwnQuality = 5;      // the grunt's own Quality: a 5+ morale test
        private const int VillainQuality = 3;  // better, so the aura is worth taking
        private static readonly Position VillainSpot = new Position(24f, 24f);

        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _ai;
        private PlayerID _heroes;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _context = new TestGameContext(_store, new FixedDiceRoller(4),
                settings: QuestSettings());
            _ai = new PlayerID(Guid.NewGuid());
            _heroes = new PlayerID(Guid.NewGuid());
        }

        // ── The radius ───────────────────────────────────────────────────────────────────────────────

        [Test]
        public void AGruntWithinSixInches_TestsOnTheVillainsQuality()
        {
            MakeVillain();
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality),
                Is.EqualTo(VillainQuality),
                "a grunt inside the aura takes the Villain's better Quality");
        }

        [Test]
        public void AGruntAtNineInches_TestsOnItsOwn()
        {
            // THE regression test for the version bump: 9" is inside v2.0.1's 12" and outside v2.1.0's
            // 6". If the constant ever drifts back, this is the only test here that notices.
            MakeVillain();
            UnitData grunt = MakeGrunt(Away(9f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality),
                Is.EqualTo(OwnQuality),
                "v2.1.0's aura is 6\", not v2.0.1's 12\" - a grunt 9\" away is on its own");
        }

        [Test]
        public void TheAuraIsSixInches_NotTwelve()
        {
            // Stated directly as well, so a failure reads as "the book says 6" rather than only as a
            // grunt in the wrong place.
            Assert.That(QuestChallenges.VillainAuraInches, Is.EqualTo(6f),
                "GFSQ Core Rules v2.1.0: \"AI units within 6\\\"\"");
        }

        // ── The rule name: "Hero" in the books IS "Villain" in the core rules ────────────────────────

        [Test]
        public void TheRuleTheBooksActuallyCarry_IsHero()
        {
            // The fault that made the whole mechanic dead code. Asserted on the lookup itself rather
            // than only through a morale test, so the message names the cause.
            UnitData asBooksShipIt = MakeVillain(ruleName: "Hero");

            Assert.That(QuestChallenges.IsVillain(asBooksShipIt), Is.True,
                "Star Quest enemy books define Villain under the name \"Hero\" - matching only "
                + "\"Villain\" means no unit in any book is ever a Villain");
        }

        [Test]
        public void AUnitWithTheHeroRule_LendsItsQuality()
        {
            MakeVillain(ruleName: "Hero");
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality),
                Is.EqualTo(VillainQuality),
                "an enemy boss as the books actually ship it must lend its nerve");
        }

        [Test]
        public void AUnitWithTheLiteralVillainRule_StillWorks()
        {
            // The core book's own spelling keeps working, so a future book that uses it needs no change.
            MakeVillain(ruleName: "Villain");
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality),
                Is.EqualTo(VillainQuality));
        }

        [Test]
        public void AUniqueHero_IsAVillainToo()
        {
            // The half that catches the bosses with names. 3.6.0's "Unique Hero" is defined as "Counts
            // as having Hero, and this model may only be taken once per army", and every one of its 147
            // carriers in the bundled corpus has it INSTEAD of plain "Hero" - so without this, the named
            // enemy characters (30 of them in Star Quest) would still lend nothing.
            MakeVillain(ruleName: "Unique Hero");
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality),
                Is.EqualTo(VillainQuality),
                "a named enemy character carries \"Unique Hero\", which counts as having Hero");
        }

        [Test]
        public void AnOrdinaryGrunt_IsNotAVillain()
        {
            // The negative half of the alias: widening the name match must not make every unit a Villain.
            UnitData plain = MakeGrunt(Away(5f), quality: VillainQuality);
            UnitData other = MakeGrunt(Away(6f));

            Assert.That(QuestChallenges.IsVillain(plain), Is.False);
            Assert.That(QuestChallenges.VillainMorale(_context, other, OwnQuality), Is.EqualTo(OwnQuality),
                "a unit with no Villain/Hero rule lends nothing, however good its Quality");
        }

        // ── The rest of the clause ───────────────────────────────────────────────────────────────────

        [Test]
        public void AShakenVillain_LendsNothing()
        {
            UnitData villain = MakeVillain();
            villain.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality), Is.EqualTo(OwnQuality),
                "\"as long as the Villain isn't Shaken\"");
        }

        [Test]
        public void AVillainWithWorseQuality_NeverMakesThingsWorse()
        {
            // "if it's better than their own" - the aura is a floor, not a replacement. Without this a
            // tough grunt standing next to a weak boss would test WORSE for the company it keeps.
            MakeVillain(quality: 6);
            UnitData grunt = MakeGrunt(Away(5f), quality: 3);

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, rollNeeded: 3), Is.EqualTo(3));
        }

        [Test]
        public void AnEnemyVillain_LendsNothingToTheHeroes()
        {
            // "AI units within 6"" - its own side only. The heroes' party does not borrow the boss's nerve.
            MakeVillain();
            UnitData hero = MakeGrunt(Away(5f), owner: _heroes);

            Assert.That(QuestChallenges.VillainMorale(_context, hero, OwnQuality), Is.EqualTo(OwnQuality),
                "a Villain is no comfort to the people shooting at it");
        }

        [Test]
        public void AQuestHero_IsNeverAffected()
        {
            // A player hero tests on its own Quality whatever is standing near it, and a player hero is
            // never a Villain either - both halves matter, because the party's heroes carry the "Hero"
            // rule too (QuestArmy builds them with it), which is exactly what the name alias could have
            // broken.
            MakeVillain();
            UnitData partyHero = MakeGrunt(Away(5f), owner: _heroes, questHero: true);
            UnitData gruntNextToTheHero = MakeGrunt(Away(5.5f));

            Assert.That(QuestChallenges.VillainMorale(_context, partyHero, OwnQuality), Is.EqualTo(OwnQuality),
                "a party hero tests on its own Quality");
            Assert.That(QuestChallenges.VillainMorale(_context, gruntNextToTheHero, OwnQuality),
                Is.EqualTo(VillainQuality),
                "and the enemy grunt still takes its own boss's Quality, not the party hero's");
        }

        [Test]
        public void AVillainOffTheTable_LendsNothing()
        {
            // A boss held back in reserve (Ambush) is not on the table, so its aura is not either.
            UnitData villain = MakeVillain();
            ReserveRules.PlaceInReserve(villain);
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality), Is.EqualTo(OwnQuality),
                "a Villain still in reserve is not within 6\" of anything");
        }

        [Test]
        public void ADeadVillain_LendsNothing()
        {
            UnitData villain = MakeVillain();
            foreach (IModel model in villain.Models) model.DealWounds(model.TotalWounds);
            UnitData grunt = MakeGrunt(Away(5f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality), Is.EqualTo(OwnQuality));
        }

        [Test]
        public void TheClosestOfTwoVillains_DoesNotMatter_TheBestQualityWins()
        {
            // Nothing in the rule picks one Villain, so a grunt inside two auras gets the better of them.
            MakeVillain(quality: 4);
            MakeVillain(quality: 2, at: new Position(VillainSpot.x + 1f, VillainSpot.z));
            UnitData grunt = MakeGrunt(Away(4f));

            Assert.That(QuestChallenges.VillainMorale(_context, grunt, OwnQuality), Is.EqualTo(2));
        }

        // ── Scene building ──────────────────────────────────────────────────────────────────────────

        /// <summary>A spot the given distance from the Villain, measured the way the engine measures it
        /// (centre to centre, less both base radii).</summary>
        private static Position Away(float inches) =>
            new Position(VillainSpot.x, VillainSpot.z + inches + 2f * BaseRadius);

        private const float BaseRadius = 0.5f;

        private UnitData MakeVillain(string ruleName = "Hero", int quality = VillainQuality,
            Position? at = null)
        {
            UnitData villain = MakeGrunt(at ?? VillainSpot, quality: quality, name: "Boss");
            SpecialRuleDefinition definition = ruleName == "Hero"
                ? CoreRuleCatalog.Hero
                : new SpecialRuleDefinition(ruleName, Array.Empty<HookEntry>(),
                    Array.Empty<ActivatedAbility>());
            villain.AttachRuleDefinition(new ResolvedRule(ruleName, definition,
                Array.Empty<RuleArgument>()));
            return villain;
        }

        private UnitData MakeGrunt(Position at, int quality = OwnQuality, PlayerID? owner = null,
            bool questHero = false, string name = "Grunt")
        {
            var model = new ModelData(BaseRadius, new List<Weapon>(), at, _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            PlayerID player = owner ?? _ai;
            var unit = new UnitData(player, name, quality: quality, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            // IsQuestHero is derived from the hero sheet the unit carries, exactly as a real party hero
            // gets it (QuestArmy stamps QuestHeroJson when it builds the party), so there is no flag to
            // set - a unit is a player hero if and only if it has a sheet.
            if (questHero) unit.QuestHeroJson = new Quest.QuestHero { Name = name }.ToJson();
            DataBinding<UnitData> unitBinding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            // VillainMorale looks for Villains through QuestActions.Units, which walks the ArmyData on
            // the table rather than every UnitData in the store - so a unit with no army is invisible to
            // it. One army per unit is enough here (friendship goes by PlayerID, not by army).
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unitBinding }));
            return unit;
        }

        /// <summary>Star Quest settings, so this is a Quest game at all.</summary>
        private static GameSettings QuestSettings()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = EGameMode.StarQuestMission;
            return settings;
        }
    }
}
