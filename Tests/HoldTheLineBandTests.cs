using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #442 - the Hold the Line family's distance band.
    ///
    /// <para>
    /// <b>The books, verbatim and identical in every system that prints them</b> (Army Forge's own text,
    /// #420): <i>"If most models in this unit have this rule, they get +1 to Morale Test rolls when
    /// shot/charged from over 9"/6" away."</i> Four rules share that sentence - <b>Hold the Line</b>
    /// (139 unit entries across all six systems), <b>Hive Bond</b> (104, in the three Grimdark systems),
    /// <b>Battle-Hardened</b> (32, in the four skirmish/adventure systems) and <b>Valiant</b> (published
    /// but used by no bundled unit). Three more are worded as removing the restriction:
    /// <i>"If most models in this unit have Hold the Line, they <b>always</b> get +1 to Morale Test rolls
    /// from Hold the Line (instead of only when shot/charged from over 9"/6" away)."</i>
    /// </para>
    ///
    /// <para>
    /// <b>What was wrong.</b> All three shipped rules fired on <c>always</c>, so the band did not exist:
    /// every unit carrying one got +1 on every morale test at any range. Hold the Line and Hive Bond were
    /// also missing the "most models" gate entirely. And because the base rules were unconditional, the
    /// two Enhanced rules' own +1 <b>stacked on top</b> - a unit with Hold the Line and Enhanced Hold the
    /// Line got <b>+2</b>, which no book grants. The Enhanced rules now carry no modifier of their own
    /// and instead unlock the base rule's band, which is also what their text says: the bonus comes
    /// "from Hold the Line".
    /// </para>
    ///
    /// <para>
    /// The same lesson as #440's Unstable terrain and #441's obituary: the condition
    /// (<see cref="Condition.AttackedFromOverInches"/>) and the capability
    /// (<see cref="IHasAttackOriginDistance"/>) already existed and were correct - the morale hook simply
    /// never fed them, so the whole family's gate was unexpressible and nobody noticed it was missing.
    /// </para>
    /// </summary>
    [TestFixture]
    public class HoldTheLineBandTests
    {
        private const int Quality = 5;

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _us;

        /// <summary>
        /// The shipped shape, as <c>GdfRuleSupplement.json</c> now bakes it into all 13 books that carry
        /// one of these rules. Built here rather than read off disk because this is the engine suite and
        /// has no book assets; <c>FdgRaylib.Tests</c> asserts the shipped data matches this shape, which
        /// is the pairing that makes either test worth having.
        /// </summary>
        private static SpecialRuleDefinition Base(string name, string? enhancedBy) =>
            new(name,
                new[]
                {
                    new HookEntry(EHookID.Morale_OnPreMoraleTest,
                        new Condition.And(
                            new Condition.MostModelsHaveThisRule(),
                            enhancedBy == null
                                ? new Condition.MoraleAttackedFromBeyond(9f, 6f)
                                : new Condition.Or(
                                    new Condition.MoraleAttackedFromBeyond(9f, 6f),
                                    new Condition.UnitHasRuleOrGrant(enhancedBy))),
                        new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                        ELifetime.ThisActivation),
                },
                Array.Empty<ActivatedAbility>());

        /// <summary>The Enhanced rule: no effect of its own, present only to unlock the base rule.</summary>
        private static SpecialRuleDefinition Enhanced(string name) =>
            new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>());

        // One instance per rule, shared by every attachment. MostModelsHaveThisRule compares the firing
        // definition to the attached one by REFERENCE, so handing out a fresh instance per attachment
        // would make a unit whose models all carry the rule look like a unit where none of them does.
        private static readonly SpecialRuleDefinition HoldTheLineDef =
            Base("Hold the Line", "Enhanced Hold the Line");
        private static readonly SpecialRuleDefinition HiveBondDef = Base("Hive Bond", "Enhanced Hive Bond");
        private static readonly SpecialRuleDefinition BattleHardenedDef =
            Base("Battle-Hardened", enhancedBy: null);
        private static readonly SpecialRuleDefinition EnhancedHoldTheLineDef =
            Enhanced("Enhanced Hold the Line");
        private static readonly SpecialRuleDefinition EnhancedHiveBondDef = Enhanced("Enhanced Hive Bond");

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(HoldTheLineDef);
            _resolver.Register(HiveBondDef);
            _resolver.Register(BattleHardenedDef);
            _resolver.Register(EnhancedHoldTheLineDef);
            _resolver.Register(EnhancedHiveBondDef);
            _us = new PlayerID(Guid.NewGuid());
        }

        // -- The condition itself ----------------------------------------------------------------------

        /// <summary>
        /// The two numbers are not interchangeable, and "over" is strict in both - the books say
        /// <i>over</i> 9" and <i>over</i> 6", so landing exactly on the number is outside the band. Each
        /// boundary is checked at the number itself, because an inclusive comparison is the single most
        /// likely way to get this wrong and it differs from the correct one only at that one value.
        /// </summary>
        [TestCase(EMoraleTrigger.Shooting, 9.1f, true)]
        [TestCase(EMoraleTrigger.Shooting, 9f, false)]
        [TestCase(EMoraleTrigger.Shooting, 8.9f, false)]
        [TestCase(EMoraleTrigger.Shooting, 6.1f, false)]
        [TestCase(EMoraleTrigger.Shooting, 0f, false)]
        [TestCase(EMoraleTrigger.Melee, 6.1f, true)]
        [TestCase(EMoraleTrigger.Melee, 6f, false)]
        [TestCase(EMoraleTrigger.Melee, 5.9f, false)]
        [TestCase(EMoraleTrigger.Melee, 0f, false)]
        [TestCase(EMoraleTrigger.Melee, 24f, true)]
        public void TheBand_UsesNineForShootingAndSixForCharging(
            EMoraleTrigger trigger, float distance, bool expected)
        {
            IUnit unit = MakeUnit("Spearmen").GetValue();
            var condition = new Condition.MoraleAttackedFromBeyond(9f, 6f);

            bool fired = condition.Evaluate(new RuleInvocation(
                new PreMoraleTestContext(unit, trigger, distance), unit, Array.Empty<RuleArgument>()));

            Assert.That(fired, Is.EqualTo(expected),
                $"{trigger} at {distance}\": the books say \"shot/charged from over 9\"/6\" away\".");
        }

        /// <summary>
        /// A morale test from neither cause is never in the band, however far away anything is - a spell
        /// that forces a test is not shooting, so this family gives nothing. Pinned at a distance well
        /// past both thresholds, which is where a condition that read only the distance would wrongly fire.
        /// </summary>
        [Test]
        public void ATestFromNeitherCause_IsNeverInTheBand()
        {
            IUnit unit = MakeUnit("Spearmen").GetValue();
            var condition = new Condition.MoraleAttackedFromBeyond(9f, 6f);

            bool fired = condition.Evaluate(new RuleInvocation(
                new PreMoraleTestContext(unit, EMoraleTrigger.OtherSource, 24f), unit,
                Array.Empty<RuleArgument>()));

            Assert.That(fired, Is.False,
                "a spell, a unit rule or a campaign event is not \"shot/charged\" at any range.");
        }

        // -- Through the real morale engine ------------------------------------------------------------

        [TestCase("Hold the Line")]
        [TestCase("Hive Bond")]
        [TestCase("Battle-Hardened")]
        public async Task ShotFromBeyondNine_EasesTheThreshold(string rule)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4), $"{rule}: shot from 12\" is over 9\".");
            Assert.That(outcome.Passed, Is.True);
        }

        /// <summary>
        /// The half that did not exist before this patch, and the only one a played game notices: shot
        /// from close range, the family gives nothing. Every one of these rules passed an equivalent
        /// assertion with the WRONG expectation before #442.
        /// </summary>
        [TestCase("Hold the Line")]
        [TestCase("Hive Bond")]
        [TestCase("Battle-Hardened")]
        public async Task ShotFromInsideNine_GivesNothing(string rule)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 5f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(Quality),
                $"{rule}: \"when shot ... from over 9\" away\" - 5\" is not over 9\".");
            Assert.That(outcome.Passed, Is.False, "and the unmodified Quality-5 test fails on a 4.");
        }

        [TestCase("Hold the Line")]
        [TestCase("Hive Bond")]
        [TestCase("Battle-Hardened")]
        public async Task ChargedFromBeyondSix_EasesTheThreshold(string rule)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Melee,
                attackerLaunchDistanceInches: 8f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                $"{rule}: charged from 8\" is over 6\" - and note 8\" would NOT qualify for shooting, "
                + "which is the whole reason the two numbers are carried separately.");
        }

        [TestCase("Hold the Line")]
        [TestCase("Hive Bond")]
        [TestCase("Battle-Hardened")]
        public async Task ChargedFromInsideSix_GivesNothing(string rule)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Melee,
                attackerLaunchDistanceInches: 4f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(Quality), $"{rule}: 4\" is not over 6\".");
        }

        /// <summary>
        /// A melee the unit was not charged into carries a launch distance of 0, which is outside the
        /// band - so the unit that did the charging gets nothing from this family even though it is in a
        /// melee. The books are specific: the bonus is for being <i>charged</i>.
        /// </summary>
        [Test]
        public async Task AUnitThatDidTheCharging_GetsNothing()
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", "Hold the Line");

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Melee,
                attackerLaunchDistanceInches: 0f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(Quality),
                "0 means no launch distance applies - the charger was not charged from anywhere.");
        }

        /// <summary>A spell's morale test gets nothing, at any range.</summary>
        [Test]
        public async Task ASpellDrivenTest_GetsNothing()
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", "Hold the Line");

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.OtherSource,
                attackerLaunchDistanceInches: 24f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(Quality),
                "the family's wording is about being shot or charged, not about morale in general.");
        }

        // -- "If MOST models in this unit have this rule" ----------------------------------------------

        /// <summary>
        /// Hold the Line and Hive Bond shipped with <b>no "most models" gate at all</b> - the books open
        /// every one of these rules with <i>"If most models in this unit have this rule"</i> and only
        /// Battle-Hardened carried it. The gate is now on all three.
        ///
        /// <para>
        /// What is asserted here is that the gate does not get in the way of the ordinary case: every
        /// bundled book attaches these rules at <b>unit</b> level (checked - e.g. Colonel Rixxon's
        /// <c>rules[2]</c> in Human Defense Force), which makes every non-hero model a carrier. The
        /// interesting negative needs a joined hero, which the shared condition deliberately does not
        /// count as a carrier, and that is already covered for every rule using it by
        /// <see cref="AllModelsRuleGateIntegrationTests"/> - duplicating its hero fixture here would test
        /// that condition twice and this patch not at all. The structural fact that these three rules now
        /// carry the gate is pinned against the shipped JSON in <c>FdgRaylib.Tests</c>.
        /// </para>
        /// </summary>
        [TestCase("Hold the Line")]
        [TestCase("Hive Bond")]
        [TestCase("Battle-Hardened")]
        public async Task AUnitWideAttachment_SatisfiesTheMostModelsGate(string rule)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", modelCount: 3, rule);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                $"{rule}: attached to the unit, so all three models carry it - which is most of them.");
        }

        /// <summary>
        /// Casualties must not break the gate: the condition counts only living models, and a unit-level
        /// attachment makes each survivor a carrier, so a unit shot down to one model still qualifies.
        /// Worth pinning because "most models" invites an implementation that counts against the unit's
        /// STARTING size, which would silently switch the bonus off exactly when it matters most.
        /// </summary>
        [Test]
        public async Task CasualtiesDoNotBreakTheGate()
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", modelCount: 3, "Hold the Line");
            for (int i = 0; i < 2; i++)
            {
                IModel dying = unit.GetValue().ModelBindings[i].GetValue();
                dying.DealWounds(dying.TotalWounds);
            }

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                "one survivor of three still has the rule, and one of one is most of one.");
        }

        // -- The Enhanced rules: unconditional, and never +2 -------------------------------------------

        /// <summary>
        /// The Enhanced rule's whole job: the bonus applies at any range. Checked at point-blank, which
        /// is exactly where the base rule alone gives nothing.
        /// </summary>
        [TestCase("Hold the Line", "Enhanced Hold the Line")]
        [TestCase("Hive Bond", "Enhanced Hive Bond")]
        public async Task WithTheEnhancedRule_TheBonusAppliesAtAnyRange(string rule, string enhanced)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule, enhanced);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 2f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                $"{enhanced}: \"they ALWAYS get +1 ... (instead of only when shot/charged from over "
                + "9\"/6\" away)\".");
        }

        /// <summary>
        /// <b>The live bug this patch fixes, and the one a player would actually have felt.</b> Before
        /// #442 the base rule was unconditional and the Enhanced rule added its own +1, so a unit with
        /// both rolled morale at Quality-2. 27 unit entries across six systems carry both.
        /// </summary>
        [TestCase("Hold the Line", "Enhanced Hold the Line")]
        [TestCase("Hive Bond", "Enhanced Hive Bond")]
        public async Task TheEnhancedRule_NeverStacksWithItsBase(string rule, string enhanced)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule, enhanced);

            // In the band, where BOTH arms of the Or are satisfied - the one case a double-count shows up.
            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                $"exactly +1, never +2: the books grant one bonus, and {enhanced} removes the "
                + $"restriction on {rule}'s rather than adding a second.");
        }

        /// <summary>
        /// And the Enhanced rule on its own, without the base rule, does nothing - its text is entirely
        /// about what happens "if most models in this unit have" the base rule.
        /// </summary>
        [TestCase("Enhanced Hold the Line")]
        [TestCase("Enhanced Hive Bond")]
        public async Task TheEnhancedRule_AloneDoesNothing(string enhanced)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", enhanced);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(Quality),
                $"{enhanced} modifies another rule; with nothing to modify it is inert.");
        }

        /// <summary>
        /// The trap that nearly shipped. In several books the Enhanced rule is never on the unit
        /// statically - it arrives from <i>Enhanced Hold the Line Aura</i> or <i>... Buff</i>, both of
        /// which grant it as a <see cref="TokenType.RuleGrant"/>. A granted rule fires its own hooks but
        /// is <b>invisible</b> to <see cref="Condition.UnitHasRule"/>, which reads only static
        /// <c>RuleDefinitions</c> - so gated that way, every aura-fed unit would have lost its bonus at
        /// close range, the exact opposite of the rule's purpose.
        /// <see cref="Condition.UnitHasRuleOrGrant"/> exists for this, and this test is what holds it in
        /// place.
        /// </summary>
        [TestCase("Hold the Line", "Enhanced Hold the Line")]
        [TestCase("Hive Bond", "Enhanced Hive Bond")]
        public async Task TheEnhancedRule_CountsWhenItArrivesFromAnAura(string rule, string enhanced)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", rule);
            // Exactly what Effect.Aura and Effect.AddRule emit.
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(enhanced, ELifetime.Aura)));

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 2f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                $"an aura-granted {enhanced} must count: that is how several books deliver it at all.");
        }

        /// <summary>And a granted Enhanced rule does not stack either.</summary>
        [Test]
        public async Task AnAuraGrantedEnhancedRule_AlsoNeverStacks()
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", "Hold the Line");
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant("Enhanced Hold the Line", ELifetime.Aura)));

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4), "still exactly +1.");
        }

        /// <summary>
        /// Battle-Hardened has no Enhanced form in any book, so nothing can lift its band. Pinned so a
        /// future copy-paste of the Or arm does not quietly invent one.
        /// </summary>
        [Test]
        public async Task BattleHardened_HasNoEnhancedFormToLiftItsBand()
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", "Battle-Hardened");
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant("Enhanced Battle-Hardened", ELifetime.Aura)));

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 2f);

            Assert.That(outcome.RollNeeded, Is.EqualTo(Quality),
                "no book prints an Enhanced Battle-Hardened, so a rule by that name must not work.");
        }

        // -- Banner is unaffected ----------------------------------------------------------------------

        /// <summary>
        /// Banner and this family share the pre-morale hook and sit next to each other in the same
        /// books, but Banner's text names no distance (#438). It must keep working at point-blank, and
        /// it must stack with this family - they are two different rules granting two different bonuses,
        /// and nothing in either text says otherwise.
        /// </summary>
        [Test]
        public async Task Banner_StillWorksAtPointBlank_AndStacksWithTheFamily()
        {
            SpecialRuleDefinition banner = new("Banner",
                new[]
                {
                    new HookEntry(EHookID.Morale_OnPreMoraleTest,
                        new Condition.MoraleTriggeredByCombat(),
                        new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                        ELifetime.ThisActivation),
                },
                Array.Empty<ActivatedAbility>());
            _resolver.Register(banner);

            DataBinding<UnitData> close = MakeUnit("Spearmen", "Hold the Line");
            close.GetValue().AttachRuleDefinition(new ResolvedRule("Banner", banner));

            MoraleUtilities.MoraleTestOutcome pointBlank = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), close.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 2f);
            Assert.That(pointBlank.RollNeeded, Is.EqualTo(4),
                "Banner alone: it is distance-blind, so point-blank is still +1.");

            DataBinding<UnitData> far = MakeUnit("Riflemen", "Hold the Line");
            far.GetValue().AttachRuleDefinition(new ResolvedRule("Banner", banner));

            MoraleUtilities.MoraleTestOutcome longRange = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), far.GetValue(), Quality, EMoraleTrigger.Shooting,
                attackerLaunchDistanceInches: 12f);
            Assert.That(longRange.RollNeeded, Is.EqualTo(3),
                "both apply beyond 9\": two rules, two bonuses.");
        }

        // -- Helpers -----------------------------------------------------------------------------------

        private static SpecialRuleDefinition Definition(string rule) => rule switch
        {
            "Hold the Line" => HoldTheLineDef,
            "Hive Bond" => HiveBondDef,
            "Battle-Hardened" => BattleHardenedDef,
            "Enhanced Hold the Line" => EnhancedHoldTheLineDef,
            "Enhanced Hive Bond" => EnhancedHiveBondDef,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "not a rule this fixture knows"),
        };

        private TestGameContext Ctx(int die) =>
            new(_store, new FixedDiceRoller(die), ruleResolver: _resolver,
                playerRequester: new NoRequester());

        private sealed class NoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply> =>
                throw new InvalidOperationException(
                    "no prompt is expected on this path: " + request.GetType().Name);
        }

        private DataBinding<UnitData> MakeUnit(string name, params string[] rules) =>
            MakeUnit(name, modelCount: 2, rules);

        private DataBinding<UnitData> MakeUnit(string name, int modelCount, params string[] rules)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_us, name, quality: Quality, defense: 4, modelBindings: models);
            foreach (string rule in rules)
            {
                unit.AttachRuleDefinition(new ResolvedRule(rule, Definition(rule)));
            }

            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
