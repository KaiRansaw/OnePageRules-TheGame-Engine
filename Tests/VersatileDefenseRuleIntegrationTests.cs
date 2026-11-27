using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Versatile Defense (Army Forge 3.6.0, the same words in every system that has it): "When a unit where most models
    // have this rule is shot/charged from over 9"/6" away, pick one effect: the unit either gets +1 to Defense rolls, or
    // enemy units get -1 to hit rolls."
    //
    // Until 2026-11-27 the data played the older wording - a stance picked when the unit deployed or activated, lasting
    // until its next activation. Now the defender's player picks as each enemy's shooting / charge comes in, and the pick
    // holds for every weapon of that attack. Driven through the real DetermineHitRollStage and
    // DetermineSaveRollsNeededStage.
    [TestFixture]
    public class VersatileDefenseRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private static readonly SpecialRuleDefinition Rule =
            new(VersatileDefense.Name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>());

        [Test]
        public async Task ShotFromOver9_Evasion_GivesTheShooterMinus1ToHit()
        {
            var (attacker, defender) = Pair(gap: 15f);
            var asked = new Picker(VersatileDefense.Evasion);
            var ctx = Context(asked);

            int needed = await HitNeeded(ctx, attacker, defender, melee: false);
            Assert.That(needed, Is.EqualTo(5), "Quality 4+ becomes 5+");
            Assert.That(await SaveNeeded(ctx, attacker, defender, melee: false), Is.EqualTo(4), "no Defense bonus with Evasion");
            Assert.That(asked.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Guard_GivesPlus1Defense_AndIsAskedOncePerAttack()
        {
            var (attacker, defender) = Pair(gap: 15f);
            var asked = new Picker(VersatileDefense.Guard);
            var ctx = Context(asked);

            Assert.That(await HitNeeded(ctx, attacker, defender, melee: false), Is.EqualTo(4));
            Assert.That(await HitNeeded(ctx, attacker, defender, melee: false), Is.EqualTo(4), "a second weapon of the same shooting");
            Assert.That(await SaveNeeded(ctx, attacker, defender, melee: false), Is.EqualTo(3), "Defense 4+ becomes 3+");
            Assert.That(asked.Count, Is.EqualTo(1), "one pick for the whole shooting");

            VersatileDefense.EndOfActivation(ctx);
            await HitNeeded(ctx, attacker, defender, melee: false);
            Assert.That(asked.Count, Is.EqualTo(2), "the next activation's shooting is a new pick");
        }

        [Test]
        public async Task Within9_OrAStrikeBack_IsNotOffered()
        {
            var (attacker, defender) = Pair(gap: 6f);
            var asked = new Picker(VersatileDefense.Evasion);
            var ctx = Context(asked);

            Assert.That(await HitNeeded(ctx, attacker, defender, melee: false), Is.EqualTo(4), "shot from 6\"");
            Assert.That(await HitNeeded(ctx, attacker, defender, melee: true, charging: false), Is.EqualTo(4), "a strike-back is not a charge");
            Assert.That(await HitNeeded(ctx, attacker, defender, melee: true, charging: true, chargeOrigin: 5f), Is.EqualTo(4), "charged from 5\"");
            Assert.That(asked.Count, Is.EqualTo(0));

            Assert.That(await HitNeeded(ctx, attacker, defender, melee: true, charging: true, chargeOrigin: 8f), Is.EqualTo(5), "charged from 8\"");
            Assert.That(asked.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task OnlyWhenMostModelsHaveIt()
        {
            var (attacker, defender) = Pair(gap: 15f, attach: false, defenderModels: 2);
            ((ModelData)defender.GetValue().Models[0]).AttachRuleDefinition(new ResolvedRule(VersatileDefense.Name, Rule, Array.Empty<RuleArgument>()));
            var asked = new Picker(VersatileDefense.Evasion);
            var ctx = Context(asked);

            Assert.That(await HitNeeded(ctx, attacker, defender, melee: false), Is.EqualTo(4), "one of two is not most");
            Assert.That(asked.Count, Is.EqualTo(0));
        }

        [Test]
        public void SpellsAndExactly9Inches_AreOutsideTheBand()
        {
            // The bot's pick is the request's Recommended answer; pinned through InBand + the choice rule.
            Assert.That(VersatileDefense.InBand(melee: false, spell: true, charging: false, distance: 20f, chargeOrigin: 0f), Is.False,
                "spells are not shooting");
            Assert.That(VersatileDefense.InBand(melee: false, spell: false, charging: false, distance: 9f, chargeOrigin: 0f), Is.False,
                "exactly 9\" is not over 9\"");
        }

        // ---------- helpers ----------

        private TestGameContext Context(IPlayerRequestByID requester) =>
            new(_store, new FixedDiceRoller(4), playerRequester: requester);

        private (DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender) Pair(float gap, bool attach = true, int defenderModels = 1)
        {
            DataBinding<UnitData> attacker = MakeUnit("Shooters", new Position(0f, 5f), 1);
            DataBinding<UnitData> defender = MakeUnit("Disciples", new Position(gap + 1.5f, 5f), defenderModels);
            if (attach)
                defender.GetValue().AttachRuleDefinition(new ResolvedRule(VersatileDefense.Name, Rule, Array.Empty<RuleArgument>()));
            return (attacker, defender);
        }

        private static async Task<int> HitNeeded(TestGameContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            bool melee, bool charging = false, float chargeOrigin = 0f)
        {
            var stage = new DetermineHitRollStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var weapon = new Weapon("Gun", rangeInches: melee ? 0f : 24f, attacks: 1, armorPenetration: 0);
            var meta = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1, isMelee: melee, isCharging: charging,
                chargeOriginDistanceInches: chargeOrigin);
            await stage.Enter(meta);
            meta.QueryForResult(out DetermineHitRollResults results);
            return results.HitRollNeeded;
        }

        private static async Task<int> SaveNeeded(TestGameContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool melee)
        {
            var weapon = new Weapon("Gun", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var hitStage = new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            hitStage.NextStage.Bind("done");
            var hitMeta = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1, isMelee: melee);
            hitMeta.AddResult(new DetermineHitRollResults(2, attackCount: 1));
            await hitStage.Enter(hitMeta);
            hitMeta.QueryForResult(out RollToHitResults hits);

            var saveStage = new DetermineSaveRollsNeededStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            saveStage.NextStage.Bind("done");
            var saveMeta = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1, isMelee: melee);
            saveMeta.AddResult(hits);
            saveMeta.AddResult(new CoverCheckResults(0));
            await saveStage.Enter(saveMeta);
            saveMeta.QueryForResult(out DetermineSaveRollNeededResults saves);
            return saves.PendingSaveRollsList[0].SaveNeeded;
        }

        private DataBinding<UnitData> MakeUnit(string name, Position at, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                    initialPosition: new Position(at.x, at.z + i * 1.6f), gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private sealed class Picker : IPlayerRequestByID
        {
            private readonly string _pick;
            public Picker(string pick) => _pick = pick;
            public int Count { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest s && s.DisplayName == VersatileDefense.Name)
                {
                    Count++;
                    return Task.FromResult((TReply)(object)_pick);
                }
                if (request is YesNoRequest yn) return Task.FromResult((TReply)(object)yn.DefaultAnswer);
                throw new InvalidOperationException("Unexpected request: " + request.GetType().Name);
            }
        }
    }
}
