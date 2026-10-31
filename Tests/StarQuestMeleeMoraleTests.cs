using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - Star Quest has no Melee Results step, and that is why its Fear(X) is worded differently.
    //
    // Every other book's Morale section has two triggers: wounds leaving a unit at half strength, and
    // losing a melee ("compare the number of wounds each unit took, the highest total loses, and must
    // take a Morale Test"). GFSQ Core Rules v2.1.0 has only the first. There is no melee-results
    // paragraph anywhere in it.
    //
    // The engine ran the who-won rule in every melee regardless of system, so a Star Quest melee applied
    // a rule Star Quest does not have - and it is a rule with teeth, because the loser of an even
    // exchange gets Shaken while neither unit is anywhere near half strength.
    //
    // It is also the reason the two books word Fear(X) differently:
    //   - GF / AoF / GFF / AoFS: "+X wounds when checking who won."
    //   - GFSQ 2.1.0: "+X wounds when checking if they must take a Morale Test (must deal at least one
    //     wound)."
    // Not a reworded rule so much as the same rule pointed at the only morale trigger that exists.
    //
    // These tests drive the real DetermineMeleeWinnerStage with a real rule evaluator, and the Star Quest
    // ones assert on who ends up SHAKEN rather than on the stage's routing: the routing is a detail, and
    // "did this unit get Shaken" is the thing a player feels.
    [TestFixture]
    public class StarQuestMeleeMoraleTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        /// <summary>A context for one game system. Quality 4 and a fixed roll of 1 makes every morale
        /// test a failure, so "was a test taken" and "was it failed" are the same question - which is
        /// what these tests want to measure.</summary>
        private TestGameContext Context(EGameSystem system, int roll = 1)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            if (system == EGameSystem.GFStarQuest) settings.GameMode = EGameMode.StarQuestMission;
            return new TestGameContext(_store, new FixedDiceRoller(roll), settings: settings);
        }

        // ── The profile ─────────────────────────────────────────────────────────────────────────────

        [TestCase(EGameSystem.GrimdarkFuture, true)]
        [TestCase(EGameSystem.AgeOfFantasy, true)]
        [TestCase(EGameSystem.GFFirefight, true)]
        [TestCase(EGameSystem.AoFSkirmish, true)]
        [TestCase(EGameSystem.GFStarQuest, false)]
        public void OnlyStarQuest_HasNoMeleeResultsStep(EGameSystem system, bool hasIt)
        {
            // Stated on the profile as well as through behaviour, so a failure reads as "the book says
            // Star Quest has no melee results" rather than only as a unit that didn't get Shaken.
            Assert.That(CoreRulesProfile.For(system).MeleeResultsDecideMorale, Is.EqualTo(hasIt));
        }

        // ── Grimdark Future keeps the who-won rule ──────────────────────────────────────────────────

        [Test]
        public async Task InGrimdarkFuture_TheLoserOfAnEvenFightIsStillShaken()
        {
            // The behaviour that must NOT change. A unit that took 2 wounds against 1 loses the melee and
            // tests, at full health - this is the rule Star Quest lacks, and it has to keep working.
            TestGameContext ctx = Context(EGameSystem.GrimdarkFuture);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            Wound(defender, 2);
            Wound(attacker, 1);

            (bool needsRoll, _, DetermineMeleeWinnerResults result) = await Run(ctx, combat);

            Assert.That(needsRoll, Is.True, "the loser must be sent to a morale test");
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
        }

        // ── Star Quest uses the half-strength rule instead ──────────────────────────────────────────

        [Test]
        public async Task InStarQuest_LosingAnEvenFightAtFullHealth_ShakensNobody()
        {
            // The same fight as above. Star Quest's only trigger is half strength, and a 5-wound model
            // that took 2 is not there - so nobody tests, and nobody is Shaken.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            Wound(defender, 2);
            Wound(attacker, 1);

            (bool needsRoll, bool doesntNeedRoll, _) = await Run(ctx, combat);

            Assert.That(needsRoll, Is.False, "there is no who-won morale test in Star Quest");
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(IsShaken(defender), Is.False, "the melee's loser is not at half strength");
            Assert.That(IsShaken(attacker), Is.False);
        }

        [Test]
        public async Task InStarQuest_AUnitTakenToHalfStrength_IsShaken_EvenIfItWonTheMelee()
        {
            // The other half, and the one that shows the triggers really have swapped: the unit that
            // dealt MORE wounds is the one that tests, because it is the one that was hurt enough. Under
            // the who-won rule this unit would be the winner and would test nothing.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            Wound(attacker, 3);   // 3 of 5 wounds: past half
            Wound(defender, 1);

            await Run(ctx, combat);

            Assert.That(IsShaken(attacker), Is.True,
                "half strength is Star Quest's morale trigger, whatever the exchange looked like");
            Assert.That(IsShaken(defender), Is.False);
        }

        [Test]
        public async Task InStarQuest_BothUnitsCanBeShakenFromOneMelee()
        {
            // Another consequence of dropping who-won: the who-won rule can only ever make ONE unit test,
            // because only one side can lose. Half strength is per unit, so a bloody exchange tests both.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            Wound(attacker, 3);
            Wound(defender, 4);

            await Run(ctx, combat);

            Assert.That(IsShaken(attacker), Is.True);
            Assert.That(IsShaken(defender), Is.True);
        }

        [Test]
        public async Task InStarQuest_AUnitThatTookNoWounds_IsNeverShaken()
        {
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            Wound(defender, 4);

            await Run(ctx, combat);

            Assert.That(IsShaken(attacker), Is.False, "it was not touched");
            Assert.That(IsShaken(defender), Is.True);
        }

        // ── Fear(X) in Star Quest ───────────────────────────────────────────────────────────────────

        [Test]
        public async Task InStarQuest_FearPushesTheEnemyOverTheHalfStrengthLine()
        {
            // GFSQ 2.1.0's Fear: "enemies count as having taken +X wounds when checking if they must take
            // a Morale Test". 2 real wounds out of 5 is short of half; Fear(2) on the attacker makes the
            // defender count 4, which is over. The wounds are counted, never dealt - so the test is on
            // whether it is SHAKEN, not on whether it lost models.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            GiveFear(attacker, 2);
            Wound(defender, 2);

            await Run(ctx, combat);

            Assert.That(IsShaken(defender), Is.True, "Fear(2) counts the defender as 2 wounds worse off");
            Assert.That(defender.GetValue().RemainingWounds, Is.EqualTo(3f),
                "and it must not have actually taken them - Fear deals no real wounds");
        }

        [Test]
        public async Task InStarQuest_FearDoesNothingToAnUntouchedEnemy()
        {
            // "(must deal at least one wound)" - the clause that stops Fear being a free Shaken on any
            // enemy it stands next to. No code of its own: a unit that took no real wounds never reaches
            // the half-strength comparison.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, _, DataBinding<UnitData> defender) = BuildMelee(ctx);
            GiveFear(_lastAttacker!, 4);

            await Run(ctx, combat);

            Assert.That(IsShaken(defender), Is.False,
                "Fear must deal at least one real wound before its counted wounds mean anything");
        }

        [Test]
        public async Task InStarQuest_FearOnTheVictimDoesNotProtectIt()
        {
            // Direction check. Fear reads "ENEMIES count as having taken +X wounds", so a unit's own Fear
            // inflates its opponent's count, never its own. Getting this backwards would make Fear a
            // defensive rule, which is the kind of mistake that looks right in play.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            (CombatActionContext combat, DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =
                BuildMelee(ctx);
            GiveFear(defender, 4);
            Wound(defender, 2);

            await Run(ctx, combat);

            Assert.That(IsShaken(defender), Is.False,
                "the defender's own Fear says nothing about the defender's morale");
            Assert.That(IsShaken(attacker), Is.False, "and the attacker took no wounds at all");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private DataBinding<UnitData>? _lastAttacker;

        private async Task<(bool NeedsRoll, bool DoesntNeedRoll, DetermineMeleeWinnerResults Result)>
            Run(TestGameContext ctx, CombatActionContext combat)
        {
            var stage = new DetermineMeleeWinnerStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool needsRoll = false, doesntNeedRoll = false;
            stage.OnNeedsRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION);
            stage.OnDoesntNeedRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION);
            stage.OnNeedsRollToDecide.OnWillActivate += _ => needsRoll = true;
            stage.OnDoesntNeedRollToDecide.OnWillActivate += _ => doesntNeedRoll = true;

            await stage.Enter(combat);

            combat.QueryForResult(out DetermineMeleeWinnerResults result);
            return (needsRoll, doesntNeedRoll, result);
        }

        /// <summary>Two single-model units of 5 wounds, with the combat context built BEFORE any wounds so
        /// the start-of-melee snapshots are full - the shape DetermineMeleeWinnerStageTests established.</summary>
        private (CombatActionContext Combat, DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender)
            BuildMelee(TestGameContext ctx)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", new Position(0, 0));
            DataBinding<UnitData> defender = MakeUnit("Defender", new Position(2, 0));
            _lastAttacker = attacker;

            var combat = new CombatActionContext(ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            return (combat, attacker, defender);
        }

        private static void Wound(DataBinding<UnitData> unit, float wounds) =>
            unit.GetValue().ModelBindings[0].GetValue().DealWounds(wounds);

        private static bool IsShaken(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.HasToken(TokenType.Shaken);

        private static void GiveFear(DataBinding<UnitData> unit, int rating) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fear", CoreRuleCatalog.Fear,
                new RuleArgument[] { new RuleArgument.Int(rating) }));

        private DataBinding<UnitData> MakeUnit(string name, Position position)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            model.SetMaxWounds(5);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
