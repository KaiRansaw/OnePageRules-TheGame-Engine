using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #412 Pierce ("ignores all positive Defense modifiers on the
    // target"), through the real RollToHitStage.
    //
    // The case that matters is the third one. Pierce is NOT "clamp the net Save modifier at zero": a
    // +1 defensive bonus against an AP(1) weapon nets to exactly 0, so a clamp would look correct on
    // every single-modifier test and silently delete the attacker's AP whenever both were present. So
    // the tests are: bonus alone (dropped), AP alone (kept), and both together (bonus dropped, AP
    // kept) - only the third distinguishes the two implementations.
    [TestFixture]
    public class PierceRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(3, 5);

        private GameDataStore _store = null!;

        private static SpecialRuleDefinition Pierce() =>
            new SpecialRuleDefinition("Pierce",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.IgnorePositiveDefenseModifiers(),
                        ELifetime.ThisAttack,
                        Seat: ERuleSeat.Actor),
                },
                System.Array.Empty<ActivatedAbility>(),
                Valence: EValence.Positive,
                Description: "Test Pierce.");

        /// <summary> A plain "+N to Defense rolls" the defender carries - what Pierce must discard. </summary>
        private static SpecialRuleDefinition DefenseBonus(int delta) =>
            new SpecialRuleDefinition("TestShield",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Save, delta),
                        ELifetime.ThisAttack,
                        Seat: ERuleSeat.Subject),
                },
                System.Array.Empty<ActivatedAbility>(),
                Valence: EValence.Positive,
                Description: "Test Defense bonus.");

        /// <summary> A plain "AP(+N)" the attacker carries - what Pierce must leave alone. </summary>
        private static SpecialRuleDefinition ArmorPiercing(int delta) =>
            new SpecialRuleDefinition("TestAp",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Save, -delta),
                        ELifetime.ThisAttack,
                        Seat: ERuleSeat.Actor),
                },
                System.Array.Empty<ActivatedAbility>(),
                Valence: EValence.Positive,
                Description: "Test AP.");

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task NoPierce_DefenseBonusStands()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, DefenseBonus(1));

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender);

            Assert.That(result.SaveModifier, Is.EqualTo(1),
                "baseline: the defender's +1 reaches the save stage untouched.");
        }

        [Test]
        public async Task Pierce_DropsTheDefenseBonus()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Pierce());
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, DefenseBonus(1));

            RollToHitResults result = await RunStage(attacker, defender);

            Assert.That(result.SaveModifier, Is.EqualTo(0), "Pierce discards the defender's +1.");
        }

        [Test]
        public async Task Pierce_KeepsTheAttackersAp_WhileDroppingTheBonus()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Pierce());
            Attach(attacker, ArmorPiercing(1));
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, DefenseBonus(1));

            RollToHitResults result = await RunStage(attacker, defender);

            Assert.That(result.SaveModifier, Is.EqualTo(-1),
                "the +1 goes and the AP(1) stays. A net-clamped implementation returns 0 here, which "
                + "is the bug this test exists to catch.");
        }

        [Test]
        public async Task Pierce_WithNoBonusToDrop_LeavesApAlone()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Pierce());
            Attach(attacker, ArmorPiercing(2));

            RollToHitResults result = await RunStage(attacker, MakeUnit(DefenderPos));

            Assert.That(result.SaveModifier, Is.EqualTo(-2), "nothing favourable to discard.");
        }

        // Pierce found building 1.42: cover and the GRANTED buffs (spells, items, Hunker Down, the Challenge Bonus) are
        // added at the save stage, after the hit roll that knew Pierce was used. They must go too.

        [Test]
        public async Task Pierce_AlsoDropsCoverAndGrantedDefenseBuffs_AtTheSaveRoll()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Pierce());
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            defender.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Save), 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));

            int saveNeeded = await RunSave(attacker, defender, cover: 1);

            Assert.That(saveNeeded, Is.EqualTo(4), "Defense 4+, and neither the cover nor the +1 buff counts.");
        }

        [Test]
        public async Task NoPierce_CoverAndGrantedBuffsStand()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            defender.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Save), 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));

            int saveNeeded = await RunSave(MakeUnit(AttackerPos), defender, cover: 1);

            Assert.That(saveNeeded, Is.EqualTo(2), "4+ with cover +1 and the buff +1.");
        }

        [Test]
        public async Task Pierce_KeepsAGrantedDefensePenalty()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, Pierce());
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            defender.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Save), 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(-1)));

            int saveNeeded = await RunSave(attacker, defender, cover: 0);

            Assert.That(saveNeeded, Is.EqualTo(5), "only POSITIVE modifiers are ignored; a -1 still hurts.");
        }

        private async Task<int> RunSave(DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int cover)
        {
            RollToHitResults hits = await RunStage(attacker, defender);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(6));
            var weapon = new Weapon("Test", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var saveStage = new DetermineSaveRollsNeededStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            saveStage.NextStage.Bind("done");
            var meta = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: true, isCharging: false);
            meta.AddResult(hits);
            meta.AddResult(new CoverCheckResults(cover));
            await saveStage.Enter(meta);
            meta.QueryForResult(out DetermineSaveRollNeededResults saves);
            return saves.PendingSaveRollsList[0].SaveNeeded;
        }

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender)
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(6));
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
