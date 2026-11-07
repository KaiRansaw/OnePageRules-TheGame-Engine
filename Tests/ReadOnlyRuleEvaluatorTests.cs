using System;
using System.Collections.Generic;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Read-only movement questions (the bot's reach, the unit card, the Advance button) must see a rule an
    /// aura GRANTS by name from the army's own definitions - High Elf Fleets' Highborn Boost and the like.
    /// They used to ask the core catalog alone, which has never heard of an army rule, so the bot planned
    /// and the card showed 6" for a unit the game would let Advance 8" (bot audit, 2026-11-07).
    /// </summary>
    [TestFixture]
    public class ReadOnlyRuleEvaluatorTests
    {
        private const string ArmyRule = "Swift Boost";

        private static SpecialRuleDefinition SwiftBoost() => new SpecialRuleDefinition(ArmyRule,
            new[]
            {
                new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                    new Condition.ActionTypeIs(EActionType.Advance),
                    new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: EValence.Positive,
            Description: "Moves +2\" when using Advance.");

        [Test]
        public void AnAuraGrantedArmyRule_CountsTowardTheBotsReach()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(Guid.NewGuid());
            var model = store.GetDataBinding<ModelData>(store.Create(
                new ModelData(0.5f, new List<Weapon>(), new Position(10f, 10f), store)));
            DataBinding<UnitData> unit = store.GetDataBinding<UnitData>(store.Create(
                new UnitData(me, "Sky Riders", 4, 4, new List<DataBinding<ModelData>> { model })));
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(), new TokenPayload.RuleGrant(ArmyRule, ELifetime.Aura)));

            var army = new ArmyData(me, new List<DataBinding<UnitData>> { unit });
            army.PersistRuleData(new[] { SwiftBoost() }, Array.Empty<SpellDefinition>());
            store.Create(army);

            var table = new TableState(store);
            Assert.That(SoloAiBrain.AdvanceReach(unit.GetValue()), Is.EqualTo(6f).Within(0.01f),
                "without the table the army's rule is unknown - the old behaviour");
            Assert.That(SoloAiBrain.AdvanceReach(unit.GetValue(), table), Is.EqualTo(8f).Within(0.01f),
                "with the table's army definitions the granted rule counts");
        }
    }
}
