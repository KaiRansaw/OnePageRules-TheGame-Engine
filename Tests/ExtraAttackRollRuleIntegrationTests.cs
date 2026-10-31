using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #412 Psychotic / Crazed, mirroring ExtraHitRuleIntegrationTests:
    // proves that "for each unmodified 6 to hit, roll +1 attack" flows through the real RollToHitStage as
    // extra DICE rather than extra hits. The whole reason this path exists separately from the injection
    // sink is that an extra die can miss, so the tests that matter are the two roller extremes:
    //
    //   FixedDiceRoller(6) - the trigger fires AND the extra die lands  -> 2 hits from 1 attack.
    //   a 6-then-1 roller  - the trigger fires and the extra die MISSES -> still 1 hit.
    //
    // Without the second case, a bug that routed this effect through InsertExtraHits (handing over a
    // guaranteed hit, roughly double the real value) would pass unnoticed.
    [TestFixture]
    public class ExtraAttackRollRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(3, 5);

        private GameDataStore _store = null!;

        /// <summary>
        /// Rolls <paramref name="first"/> for the opening volley and <paramref name="rest"/> for every
        /// throw after it - which is exactly the shape this mechanic needs tested, since the extra dice
        /// are a separate throw from the attack that earned them.
        /// </summary>
        private sealed class FirstThenRestDiceRoller : IDiceRoller
        {
            private readonly int _first;
            private readonly int _rest;
            private bool _used;

            public FirstThenRestDiceRoller(int first, int rest)
            {
                _first = first;
                _rest = rest;
            }

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                int face = _used ? _rest : _first;
                _used = true;
                return new FixedDiceRoller(face).Roll(sideCount, rollCount);
            }
        }

        private static SpecialRuleDefinition Psychotic() =>
            new SpecialRuleDefinition("Psychotic",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.AddExtraAttackRoll(OnRollValue: 6, Count: 1),
                        ELifetime.ThisAttack,
                        Seat: ERuleSeat.Actor),
                },
                System.Array.Empty<ActivatedAbility>(),
                Valence: EValence.Positive,
                Description: "Test Psychotic.");

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task NoRule_OneAttackIsOneHit()
        {
            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), MakeUnit(DefenderPos),
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "without the rule the natural 6 is simply one hit.");
        }

        [Test]
        public async Task ExtraAttack_Lands_AddsAHit()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Psychotic());

            RollToHitResults result = await RunStage(attacker, MakeUnit(DefenderPos),
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "the unmodified 6 bought one extra attack die, and it also rolled a 6.");
        }

        [Test]
        public async Task ExtraAttack_Misses_AddsNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Psychotic());

            // 6 on the volley (fires the trigger), 1 on the extra attack die (misses at 4+).
            RollToHitResults result = await RunStage(attacker, MakeUnit(DefenderPos),
                new FirstThenRestDiceRoller(first: 6, rest: 1));

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "the extra attack is a DIE, not a free hit - a 1 adds nothing. This is the whole "
                + "difference between this path and the extra-hit injection sink.");
        }

        [Test]
        public async Task NoTrigger_NoExtraAttack()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Psychotic());

            // 5 hits at 4+ but is not an unmodified 6, so nothing is earned.
            RollToHitResults result = await RunStage(attacker, MakeUnit(DefenderPos),
                new FixedDiceRoller(5));

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "a hit that is not an unmodified 6 earns no extra attack.");
        }

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, IDiceRoller roller)
        {
            var ctx = new TestGameContext(_store, roller);
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: true, isCharging: false);
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
                System.Array.Empty<RuleArgument>()));

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
