using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #412 Parry, mirroring ExtraHitRuleIntegrationTests: proves the
    // first hit-CANCELLING rule flows through the real RollToHitStage. The defender's rule fires at
    // Shooting_OnHitRollComplete from the Subject seat, the RuleEvaluator queues IgnoreHitsOnRoll, the
    // HitIgnoreSink pools the dice, and the stage throws them and removes hits from the successful-hit
    // list - the mirror of the injection path those tests cover.
    //
    // FixedDiceRoller(6) makes every die a 6, so every parry die cancels; a 1-roller proves the rule is
    // actually rolled rather than applied flat. Both are needed: with only the 6 case, a bug that
    // ignored the roll entirely would pass.
    [TestFixture]
    public class ParryRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(3, 5);

        private GameDataStore _store = null!;

        private static SpecialRuleDefinition Parry(int dicePerModel = 1) =>
            new SpecialRuleDefinition("Parry",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.And(new Condition.AllModelsHaveThisRule(), new Condition.IsMelee()),
                        new Effect.IgnoreHitsOnRoll(new ValueSource.Literal(dicePerModel),
                            SuccessThreshold: 6),
                        ELifetime.ThisAttack,
                        Seat: ERuleSeat.Subject),
                },
                System.Array.Empty<ActivatedAbility>(),
                Valence: EValence.Positive,
                Description: "Test Parry.");

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task NoParry_HitStands()
        {
            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), MakeUnit(DefenderPos),
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(1f), "without Parry the natural 6 simply hits.");
        }

        [Test]
        public async Task Parry_EveryDieSucceeds_CancelsTheHit()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(0f),
                "one hit taken, one parry die, and it rolled a 6 - the hit is ignored.");
        }

        [Test]
        public async Task Parry_EveryDieFails_HitStands()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(1));

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "the parry die rolled a 1, so nothing is cancelled - Parry is rolled, not automatic.");
        }

        [Test]
        public async Task Parry_InShooting_DoesNothing()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(6), isMelee: false);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "Parry is a melee rule; its IsMelee gate must keep it out of shooting.");
        }

        [Test]
        public async Task Parry_PoolCappedAtHitsTaken_CannotGoNegative()
        {
            // Five dice per model against a single hit. The cap ("up to as many dice as hits taken")
            // and the clamp both have to hold, or the hit list goes negative and the save stage owes
            // the defender dice that do not exist.
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry(dicePerModel: 5));

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(0f),
                "the one hit is cancelled and the total floors at zero rather than going negative.");
        }

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, IDiceRoller roller, bool isMelee = true)
        {
            var ctx = new TestGameContext(_store, roller);
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: false);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1));

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList) total += hit.HitCount;
            return total;
        }

        private static void Attach(DataBinding<UnitData> unit, SpecialRuleDefinition definition) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(definition.Name, definition,
                new RuleArgument[] { new RuleArgument.Int(1) }));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
