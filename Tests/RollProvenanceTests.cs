using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StateMachine;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FDG.Tests
{
    // #407 - every roll should be able to say why it came out that way.
    //
    // Kai, QA Notes, on Vengeful Strike: "i am unsure if the modifiers are correctly being applied." The
    // threshold chips already named PASSIVE rules (#245 - "Stealth -1"), but every GRANTED buff - the
    // Star Quest skills, the items, the injuries, the random events, the Firefight campaign skills, the
    // spell buffs - collapsed into one anonymous "buff +1". With ~120 skills built, that left no way to
    // tell from play whether a given one had fired, which is what put several QA items on standby.
    //
    // These pin the recording half: a granted buff carries what granted it, from the grant through the
    // token to the chip the Battle Report hover draws.
    [TestFixture]
    public class RollProvenanceTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        // ── the token carries its source ────────────────────────────────────────────────────────────

        [Test]
        public void GrantedBuff_RemembersWhatGrantedIt()
        {
            UnitData unit = MakeUnit();
            Grant(unit, ERollKind.Hit, +1, "Vengeful Strike");

            IReadOnlyList<GrantedModifier> detail = GrantedRollModifiers.PeekDetailed(unit, ERollKind.Hit);

            Assert.That(detail.Count, Is.EqualTo(1));
            Assert.That(detail[0].Describe(), Is.EqualTo("Vengeful Strike +1"));
        }

        [Test]
        public void SeveralBuffs_AreKeptApartBySource()
        {
            UnitData unit = MakeUnit();
            Grant(unit, ERollKind.Hit, +1, "Vengeful Strike");
            Grant(unit, ERollKind.Hit, +2, "Rage");

            IReadOnlyList<GrantedModifier> detail = GrantedRollModifiers.PeekDetailed(unit, ERollKind.Hit);

            Assert.That(detail.Select(d => d.Describe()),
                Is.EqualTo(new[] { "Vengeful Strike +1", "Rage +2" }),
                "two buffs of different sources must not merge - that is the whole point.");
            Assert.That(GrantedRollModifiers.PeekNet(unit, ERollKind.Hit), Is.EqualTo(3),
                "and the net the dice use is unchanged.");
        }

        [Test]
        public void TwoCopiesOfOneSkill_ReadAsOneChip()
        {
            UnitData unit = MakeUnit();
            Grant(unit, ERollKind.Hit, +1, "Bless");
            Grant(unit, ERollKind.Hit, +1, "Bless");

            Assert.That(GrantedRollModifiers.PeekDetailed(unit, ERollKind.Hit).Single().Describe(),
                Is.EqualTo("Bless +2"), "stacking copies of one source sum rather than repeat.");
        }

        [Test]
        public void BuffWithNoRecordedSource_StillCountsAndReadsAsAPlainBuff()
        {
            UnitData unit = MakeUnit();
            // What a save written before #407 deserializes to, and what a call site that has not been
            // given its name yet produces: the modifier still applies, it just cannot say what caused it.
            unit.Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Hit), 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(-1)));

            IReadOnlyList<GrantedModifier> detail = GrantedRollModifiers.PeekDetailed(unit, ERollKind.Hit);

            Assert.That(detail.Single().Describe(), Is.EqualTo("buff -1"));
            Assert.That(GrantedRollModifiers.PeekNet(unit, ERollKind.Hit), Is.EqualTo(-1));
        }

        [Test]
        public void DetailIsReadBeforeOneShotGrantsDrain()
        {
            UnitData unit = MakeUnit();
            Grant(unit, ERollKind.Hit, +1, "Vengeful Strike", ELifetime.NextTrigger);

            (int net, IReadOnlyList<GrantedModifier> detail) =
                GrantedRollModifiers.ConsumeDetailed(unit, ERollKind.Hit);

            Assert.Multiple(() =>
            {
                Assert.That(net, Is.EqualTo(1));
                Assert.That(detail.Single().Describe(), Is.EqualTo("Vengeful Strike +1"),
                    "a 'next roll' buff must still name itself on the roll that spends it - reading the "
                    + "detail after consuming would come back empty for exactly the buffs worth naming.");
                Assert.That(GrantedRollModifiers.PeekNet(unit, ERollKind.Hit), Is.EqualTo(0),
                    "and it is spent.");
            });
        }

        [Test]
        public void GrantsForDifferentRolls_DoNotMix()
        {
            UnitData unit = MakeUnit();
            Grant(unit, ERollKind.Hit, +1, "Rage");
            Grant(unit, ERollKind.Save, +1, "Protector");

            Assert.Multiple(() =>
            {
                Assert.That(GrantedRollModifiers.PeekDetailed(unit, ERollKind.Hit).Single().Source,
                    Is.EqualTo("Rage"));
                Assert.That(GrantedRollModifiers.PeekDetailed(unit, ERollKind.Save).Single().Source,
                    Is.EqualTo("Protector"));
            });
        }

        // ── a rule-driven buff takes the firing rule's own name, with no call-site change ────────────

        [Test]
        public void RuleDrivenBuff_TakesTheFiringRulesName()
        {
            UnitData caster = MakeUnit();
            UnitData target = MakeUnit();

            var operations = new List<RuleOperation>();
            new Effect.StatModifier(ERollKind.Hit, +1, ELifetime.ThisRound).Apply(
                new RuleInvocation(Hook: null, Bearer: caster, Arguments: Array.Empty<RuleArgument>(),
                    Target: target, Definition: CoreRuleCatalog.FuriousBuff),
                operations);
            OperationApplier.ApplyTokenOperations(operations);

            Assert.That(GrantedRollModifiers.PeekDetailed(target, ERollKind.Hit).Single().Source,
                Is.EqualTo(CoreRuleCatalog.FuriousBuff.Name),
                "a buff granted by a rule is credited to that rule automatically - no call site passes it.");
        }

        // ── and it reaches the chips the Battle Report hover draws ───────────────────────────────────

        [Test]
        public void ThresholdChips_NameEachGrantedBuff()
        {
            List<string>? tags = DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(
                baseQuality: 4,
                named: Array.Empty<(RuleOperation, string)>(),
                grantedNet: 3,
                fatiguedInMelee: false,
                markerNet: 0,
                grantedDetail: new[]
                {
                    new GrantedModifier("Vengeful Strike", +1),
                    new GrantedModifier("Rage", +2),
                });

            Assert.That(tags, Is.EqualTo(new[] { "Quality 4+", "Vengeful Strike +1", "Rage +2" }),
                "this is the line Kai reads in the Battle Report hover.");
        }

        [Test]
        public void ThresholdChips_FallBackToTheSummedBuff_WhenNoDetailIsSupplied()
        {
            // The pre-roll forecast has a net but no itemised detail; it must still read correctly.
            List<string>? tags = DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(
                baseQuality: 4,
                named: Array.Empty<(RuleOperation, string)>(),
                grantedNet: 2,
                fatiguedInMelee: false);

            Assert.That(tags, Is.EqualTo(new[] { "Quality 4+", "buff +2" }));
        }

        [Test]
        public void ThresholdChips_KeepNamingPassiveRulesToo()
        {
            // #245's behaviour, unchanged: a passive rule was already named. The two kinds of modifier
            // now read the same way, which is the point - a player should not have to know which
            // mechanism produced a number.
            List<string>? tags = DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(
                baseQuality: 4,
                named: new (RuleOperation, string)[]
                {
                    (new RuleOperation.ApplyRollModifier(ERollKind.Hit, -1), "Stealth"),
                },
                grantedNet: 1,
                fatiguedInMelee: false,
                markerNet: 0,
                grantedDetail: new[] { new GrantedModifier("Vengeful Strike", +1) });

            Assert.That(tags,
                Is.EqualTo(new[] { "Quality 4+", "Stealth -1", "Vengeful Strike +1" }));
        }

        // ── stat tests: the same treatment, since that is where a Star Quest hero lives ─────────────
        //
        // StatTest used to sum four sources - a stored skill bonus, Impaired, an injury and a random
        // event - into one "bonus +2" / "penalty -1" chip. A hero failing Str tests could not tell which
        // of them was responsible, or whether two had cancelled out.

        [Test]
        public void StatTest_NamesTheConditionAndTheInjurySeparately()
        {
            UnitData hero = MakeQuestHero(injury: "Broken Arm");
            QuestTreasure.AddCondition(hero, QuestTreasure.Impaired);

            List<string>? tags = CaptureStatTestTags(hero, statType: 0);

            Assert.That(tags, Is.EqualTo(new[] { "Str 4+", "Impaired -1", "Broken Arm -1" }),
                "both penalties name themselves rather than summing into 'penalty -2'.");
        }

        [Test]
        public void StatTest_OnlyNamesTheInjuryThatMatchesTheStat()
        {
            UnitData hero = MakeQuestHero(injury: "Broken Arm");

            Assert.Multiple(() =>
            {
                Assert.That(CaptureStatTestTags(hero, statType: 0),
                    Is.EqualTo(new[] { "Str 4+", "Broken Arm -1" }), "Broken Arm is the Str injury.");
                Assert.That(CaptureStatTestTags(MakeQuestHero(injury: "Broken Arm"), statType: 1),
                    Is.Null, "it does not touch Dex, so there is nothing to show.");
            });
        }

        [Test]
        public void StatTest_WithNoModifiers_ShowsNoChips()
        {
            Assert.That(CaptureStatTestTags(MakeQuestHero(), statType: 2), Is.Null,
                "an unmodified test needs no explanation - the chips exist to explain a change.");
        }

        [Test]
        public void StatTest_NamesAStoredSkillBonusGenerically()
        {
            UnitData hero = MakeQuestHero();
            hero.QuestStatBonus = 2;

            Assert.That(CaptureStatTestTags(hero, statType: 0),
                Is.EqualTo(new[] { "Str 4+", "skill bonus +2" }),
                "QuestStatBonus is a bare int with no room for a source - see the note in StatTest.");
        }

        /// <summary>
        /// Runs one stat test and returns the chips its dice beat carried. The beat is the only place the
        /// breakdown surfaces, so capturing the presenter is the honest way to assert on it.
        /// </summary>
        private List<string>? CaptureStatTestTags(UnitData hero, int statType)
        {
            var sink = new CapturingPresenter();
            var context = new TestGameContext(_store, new FixedDiceRoller(4),
                presenter: sink, settings: QuestSettings());
            QuestActions.StatTest(context, hero, statType, "test", offerPower: false)
                .GetAwaiter().GetResult();
            return sink.LastModifierTags;
        }

        private static GameSettings QuestSettings()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            return settings;
        }

        private UnitData MakeQuestHero(string? injury = null)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f, 10f), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Hero", quality: 4, defense: 5, modelBindings: bindings);
            // Stats pinned rather than left at the sheet's default, so the expected base chip ("Str 4+")
            // is the test's own premise and cannot drift if that default changes.
            var sheet = new FDG.Quest.QuestHero
            {
                Name = "Hero", Power = 3, Strength = 4, Dexterity = 4, Willpower = 4,
            };
            if (injury != null) sheet.Injuries.Add(injury);
            unit.QuestHeroJson = sheet.ToJson();
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }

        /// <summary>Records the modifier chips of the last dice beat presented.</summary>
        private sealed class CapturingPresenter : FDG.Presentation.IPresenter
        {
            public List<string>? LastModifierTags { get; private set; }

            public Task Present(FDG.Presentation.PresentationBeat beat,
                System.Threading.CancellationToken ct = default)
            {
                if (beat is FDG.Presentation.Beats.DiceRolledBeat dice && dice.ModifierTags != null)
                {
                    LastModifierTags = dice.ModifierTags.ToList();
                }
                return Task.CompletedTask;
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────

        private static void Grant(UnitData unit, ERollKind roll, int delta, string source,
            ELifetime lifetime = ELifetime.ThisRound)
        {
            OperationApplier.ApplyTokenOperations(new List<RuleOperation>
            {
                new RuleOperation.GrantTokenToUnit(unit,
                    Effect.BuildStatModifierToken(roll, delta, lifetime, source, ownerUnitID: null)),
            });
        }

        private UnitData MakeUnit()
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0, 0), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Unit", quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
