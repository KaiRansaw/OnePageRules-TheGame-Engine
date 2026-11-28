using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Protection Feat (Army Forge 3.6.0, the same words in GF, Firefight, AoF, Skirmish and both Quests): "Once per game,
    // when this unit takes wounds and most of its models have this rule, you may use this rule and roll one die per
    // wound, and on a 5+ it is ignored."
    //
    // Until 2026-11-27 it was a brace taken at the unit's own activation that lasted until its next one - so a unit got a
    // 5+ save against EVERY attack for a whole round, and the offer came before anyone knew which attack was worth it.
    // Now it is asked as the wounds land, through the real AssignWoundsStage and through CasualtyPresentation (wounds
    // from anything else), and a yes spends it.
    [TestFixture]
    public class ProtectionFeatRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // The engine reads the rule by name; the shipped definition carries no hooks (ProtectionFeatShippedDataTests).
        private static readonly SpecialRuleDefinition Feat =
            new(ProtectionFeat.Name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>());

        [Test]
        public async Task AnAttack_IsAskedAsItLands_AndEach5PlusIsIgnored()
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = Combatants(defenderModels: 10);
            Attach(defender);
            var requester = new Requester(useFeat: true);

            await RunAssignWounds(requester, new FixedFaceDiceRoller(5), attacker, defender, failedSaves: 4);

            Assert.That(requester.FeatQuestions, Is.EqualTo(1), "asked once, when the wounds land");
            Assert.That(requester.AssignedWounds, Is.EqualTo(0f), "every die a 5 - all four ignored");
            Assert.That(ProtectionFeat.Spent(defender.GetValue()), Is.True);
        }

        [Test]
        public async Task OncePerGame_TheSecondVolleyIsNotOffered()
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = Combatants(defenderModels: 10);
            Attach(defender);
            var requester = new Requester(useFeat: true);

            await RunAssignWounds(requester, new FixedFaceDiceRoller(4), attacker, defender, failedSaves: 2);
            Assert.That(requester.AssignedWounds, Is.EqualTo(2f), "4s are not ignored");
            await RunAssignWounds(requester, new FixedFaceDiceRoller(6), attacker, defender, failedSaves: 3);

            Assert.That(requester.FeatQuestions, Is.EqualTo(1), "used on the first volley, whatever the dice did");
            Assert.That(requester.AssignedWounds, Is.EqualTo(3f), "the second volley lands in full");
        }

        [Test]
        public async Task SayingNo_KeepsItForLater()
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = Combatants(defenderModels: 10);
            Attach(defender);
            var requester = new Requester(useFeat: false);

            await RunAssignWounds(requester, new FixedFaceDiceRoller(6), attacker, defender, failedSaves: 2);
            Assert.That(requester.AssignedWounds, Is.EqualTo(2f));
            Assert.That(ProtectionFeat.Spent(defender.GetValue()), Is.False);

            requester.UseFeat = true;
            await RunAssignWounds(requester, new FixedFaceDiceRoller(6), attacker, defender, failedSaves: 2);
            Assert.That(requester.FeatQuestions, Is.EqualTo(2));
            Assert.That(requester.AssignedWounds, Is.EqualTo(0f));
        }

        [Test]
        public async Task OnlyWhenMostModelsHaveIt()
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = Combatants(defenderModels: 4);
            // Two of four models carry it - not "most".
            foreach (IModel m in defender.GetValue().Models.Take(2))
                ((ModelData)m).AttachRuleDefinition(new ResolvedRule(ProtectionFeat.Name, Feat, Array.Empty<RuleArgument>()));
            var requester = new Requester(useFeat: true);

            await RunAssignWounds(requester, new FixedFaceDiceRoller(6), attacker, defender, failedSaves: 2);
            Assert.That(requester.FeatQuestions, Is.EqualTo(0));

            ((ModelData)defender.GetValue().Models[2]).AttachRuleDefinition(
                new ResolvedRule(ProtectionFeat.Name, Feat, Array.Empty<RuleArgument>()));
            Assert.That(ProtectionFeat.MostModelsHave(defender.GetValue()), Is.True, "three of four");
        }

        [Test]
        public async Task WoundsItIgnores_BankRegenerativeStrengthTokens()
        {
            // Regenerative Strength: "Place one token on this model when it ignores a wound." - not only Regeneration's.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = Combatants(defenderModels: 10);
            Attach(defender);
            AttachRegenerativeStrength(defender);
            var requester = new Requester(useFeat: true);

            await RunAssignWounds(requester, new FixedFaceDiceRoller(5), attacker, defender, failedSaves: 3);

            Assert.That(requester.AssignedWounds, Is.EqualTo(0f));
            Assert.That(defender.GetValue().Tokens.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker), Is.EqualTo(3f),
                "three wounds ignored by Protection Feat - three tokens");
        }

        [Test]
        public async Task MysticalFogIgnores_BankRegenerativeStrengthTokensToo()
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = Combatants(defenderModels: 10);
            AttachRegenerativeStrength(defender);
            var requester = new Requester(useFeat: false);
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = BattlefieldEffects.WithPick(EAdvancedRules.SpecialConditions, BattlefieldEffects.MysticalFog);

            await RunAssignWounds(requester, new FixedFaceDiceRoller(6), attacker, defender, failedSaves: 2, settings);

            Assert.That(requester.AssignedWounds, Is.EqualTo(0f), "both wounds ignored on 6s");
            Assert.That(defender.GetValue().Tokens.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker), Is.EqualTo(2f));
        }

        [Test]
        public async Task WoundsFromAnythingElse_AreOfferedToo()
        {
            DataBinding<UnitData> unit = MakeUnit("Saurians", 5);
            Attach(unit);
            var requester = new Requester(useFeat: true);
            var context = new WoundTestContext(_store, requester, new FixedFaceDiceRoller(5));

            await CasualtyPresentation.ApplyUnitWounds(context, unit.GetValue(), 3f);

            Assert.That(requester.FeatQuestions, Is.EqualTo(1));
            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(5), "dangerous-terrain style wounds, all ignored");
        }

        [Test]
        public void TheBot_KeepsItForABigBlow()
        {
            DataBinding<UnitData> unit = MakeUnit("Saurians", 10);
            Assert.That(ProtectionFeat.BotUses(unit.GetValue(), 1f), Is.False);
            Assert.That(ProtectionFeat.BotUses(unit.GetValue(), 3f), Is.True);
            DataBinding<UnitData> lone = MakeUnit("Hero", 1);
            Assert.That(ProtectionFeat.BotUses(lone.GetValue(), 1f), Is.True, "enough to wipe the unit out");
        }

        // ---------- helpers ----------

        // Mirrors the shipped Regenerative Strength definition (RegenerativeStrengthRuleIntegrationTests).
        private static void AttachRegenerativeStrength(DataBinding<UnitData> unit)
        {
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Regenerative Strength", new SpecialRuleDefinition(
                "Regenerative Strength",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnWoundIgnored, new Condition.Always(),
                        new Effect.GrantIgnoredWoundMarker(TokenType.RegenerativeStrengthMarker, new TokenClearTrigger.ManualOnly()),
                        ELifetime.UntilEndOfGame, ERuleSeat.Subject),
                },
                Array.Empty<ActivatedAbility>()), Array.Empty<RuleArgument>()));
        }

        private void Attach(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(ProtectionFeat.Name, Feat, Array.Empty<RuleArgument>()));

        private async Task RunAssignWounds(Requester requester, IDiceRoller dice,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int failedSaves, GameSettings? settings = null)
        {
            var context = new WoundTestContext(_store, requester, dice);
            if (settings is GameSettings s)
            {
                context.Settings = s;
                await BattlefieldEffects.RoundStart(context, 1);   // puts the picked Special Condition in force
            }
            var stage = new AssignWoundsStage<ICombatMetadata>(context, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(context, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: false);
            var failed = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
                failed.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed));
            await stage.Enter(metadata);
            metadata.QueryForResult(out AssignWoundsResults landed);
            requester.AssignedWounds = landed.TotalWoundsToAssign;
        }

        private (DataBinding<UnitData>, DataBinding<UnitData>) Combatants(int defenderModels) =>
            (MakeUnit("Attacker", 1), MakeUnit("Saurian Warriors", defenderModels));

        private DataBinding<UnitData> MakeUnit(string name, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(i, 0), gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private sealed class Requester : IPlayerRequestByID
        {
            public Requester(bool useFeat) => UseFeat = useFeat;
            public bool UseFeat { get; set; }
            public int FeatQuestions { get; private set; }
            public float AssignedWounds { get; set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is YesNoRequest yn && yn.DisplayName == ProtectionFeat.Name)
                {
                    FeatQuestions++;
                    return Task.FromResult((TReply)(object)UseFeat);
                }
                if (request is AssignWoundsRequest wounds)
                {
                    var result = new AssignWoundsResults(wounds.UnitReceivingWounds, wounds.Packets);
                    result.AutoFill();
                    return Task.FromResult((TReply)(object)result);
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType().Name);
            }
        }
    }
}
