using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #414 - the two melee corrections from **OPR 3.6.0**, which R1's version move missed because they
    /// live in the core rules' prose rather than in the special-rules list.
    ///
    /// <para>
    /// **1. Melee reach is a flat 3".** Every 3.6.0 core book: *"Charging models must move into base
    /// contact with targets, or as close as possible, and then defenders must do the same by moving up to
    /// 3". Models within 3" of enemies must strike with all melee weapons"* - checked word for word in GF
    /// Core Rules v3.6.0, GFF Core Rules v3.6.0 and GFSQ Core Rules v2.1.0. The engine used 2"
    /// horizontal / 4" vertical, which is the **3.5.1** wording ("within 2" horizontally and 4"
    /// vertically") from the Advanced Rules and the pre-3.6 Firefight book.
    /// </para>
    ///
    /// <para>
    /// **2. A fatigued unit's 6s stop counting as 6s for special rules.** Also new in 3.6.0, and the one
    /// with teeth: a fatigued unit hits ONLY on 6s, so while the clause was missing, every 6-triggered
    /// rule it owned fired on *every hit it scored*. Rending gave all of them AP(+4); Furious and Surge
    /// doubled all of them. Fatigue was a buff to exactly the units it is supposed to blunt.
    /// </para>
    ///
    /// <para>
    /// The fatigue half is driven through the real <see cref="RollToHitStage{T}"/> with the real catalog
    /// rules, on the same harness as <c>ExtraHitRuleIntegrationTests</c> - a rule that stops firing is
    /// the only observable that matters, and it is two stages away from the mask itself.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MeleeReachAndFatigueTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position TargetPos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows a natural 6
        }

        // ── Melee reach ──────────────────────────────────────────────────────────────────────────────

        [Test]
        public void MeleeReach_IsAFlatThreeInches_InBothDirections()
        {
            Assert.That(GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL, Is.EqualTo(3f),
                "3.6.0: \"Models within 3\\\" of enemies must strike with all melee weapons\". Was 2\\\".");
            Assert.That(GameWideConstants.MELEE_RANGE_INCHES_VERTICAL, Is.EqualTo(3f),
                "3.6.0 states one melee distance and no vertical exception. Was 4\\\", which is the 3.5.1 "
                + "Advanced Rules wording - a model on a 4\\\" ledge could fight what was below it.");
        }

        [Test]
        public void MeleeReach_IsFlatThreeInches_ExceptInAoFQuest()
        {
            // This test used to assert reach is NOT per-system, because when Q5 wrote it every book in
            // play said the same thing: GF, GFF and GFSQ v3.6.0/2.1.0 all state one 3" distance with no
            // vertical clause, and the 2"/4" split was dead 3.5.1 wording.
            //
            // #418 brought a book back that still uses it. AoF: Quest is on the 2.0.1 generation -
            // "Models within 2" horizontally and 4" vertically of enemies must strike with all melee
            // weapons" - so the split is a live rule again for exactly one system. The claim this test
            // defends is therefore unchanged in spirit: reach is the flat 3" everywhere, and a system
            // switch here is a regression UNLESS the system's own book keeps the split.
            //
            // The finally is not decoration. ConfigureTable writes GLOBAL statics (table size,
            // coherency, the friendly-overlap flag), so a fixture that leaves it set to the last value
            // in the enum hands every later fixture a 48"x48" Quest table. Writing this test without
            // the restore broke an unrelated reinforcement test two files away, which is the exact trap
            // the "a global decides some rules" note in the to-do list is about.
            try
            {
                foreach (EGameSystem system in System.Enum.GetValues<EGameSystem>())
                {
                    GameWideConstants.ConfigureTable(system);

                    if (system == EGameSystem.AoFQuest)
                    {
                        // AoFQ Core Rules v2.0.1, Melee.
                        Assert.That(GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL, Is.EqualTo(2f),
                            "AoF: Quest's own book still says 2\" horizontally");
                        Assert.That(GameWideConstants.MELEE_RANGE_INCHES_VERTICAL, Is.EqualTo(4f),
                            "AoF: Quest's own book still says 4\" vertically");
                        continue;
                    }

                    Assert.That(GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL, Is.EqualTo(3f),
                        $"{system} must use the same 3\" reach as every other 3.6.0/2.1.0 system.");
                    Assert.That(GameWideConstants.MELEE_RANGE_INCHES_VERTICAL, Is.EqualTo(3f),
                        $"{system} must use the same 3\" reach as every other 3.6.0/2.1.0 system.");
                }
            }
            finally
            {
                GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            }
        }

        // ── Fatigue: the 6s stop counting ────────────────────────────────────────────────────────────

        [Test]
        public async Task Furious_FiresInMelee_WhenNotFatigued()
        {
            // The control. Without it, "Furious didn't fire" below proves nothing - it could be the
            // harness, the charge gate, or the dice.
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, CoreRuleCatalog.Furious);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos), isMelee: true,
                isCharging: true);

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "a fresh charging unit's natural 6 gets its Furious extra hit.");
        }

        [Test]
        public async Task Furious_DoesNotFire_WhileFatigued()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, CoreRuleCatalog.Furious);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos), isMelee: true,
                isCharging: true);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "THE case: a fatigued unit only hits on 6s, and 3.6.0 says those 6s don't count as 6s "
                + "for special rules. The hit still lands - it is the doubling that stops.");
        }

        [Test]
        public async Task Surge_DoesNotFire_InMeleeWhileFatigued()
        {
            // Surge has no charge gate, so it is the one that would otherwise fire on a fatigued
            // strike-back - the most common fatigued melee there is.
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, CoreRuleCatalog.Surge);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos), isMelee: true);

            Assert.That(TotalHits(result), Is.EqualTo(1f));
        }

        [Test]
        public async Task Surge_StillFiresWhenShooting_EvenIfTheUnitIsFatigued()
        {
            // Fatigue is a melee-only condition ("units hit on unmodified rolls of 6 IN MELEE"), so a
            // unit that charged earlier this round and shoots later keeps its full shooting profile.
            // Getting this wrong would quietly nerf every gun in the army after one charge.
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, CoreRuleCatalog.Surge);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos), isMelee: false);

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "shooting is untouched by fatigue, so Surge still doubles a natural 6.");
        }

        [Test]
        public async Task AShakenUnitStrikingBack_AlsoLosesIts6s()
        {
            // "Shaken: must stay idle, but may strike back counting as Fatigued" (3.6.0 Morale). The
            // engine already treats Shaken as fatigued for the to-hit threshold; the new clause has to
            // travel with it, or a Shaken Rending unit would be the best version of itself.
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, CoreRuleCatalog.Surge);
            attacker.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos), isMelee: true);

            Assert.That(TotalHits(result), Is.EqualTo(1f));
        }

        // ── The mask itself ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void HideSixesFromRules_MovesTheSixesWithoutLosingDice()
        {
            // 1 one, 2 fives, 3 sixes.
            IDiceResults rolls = new DiceResults(new float[] { 1f, 0f, 0f, 0f, 2f, 3f });

            IDiceResults masked = FatigueUtilities.HideSixesFromRules(rolls);

            Assert.That(masked.At(6), Is.EqualTo(0f), "no 6s for a rule to see...");
            Assert.That(masked.At(5), Is.EqualTo(5f), "...they land on 5, the nearest face no hit-roll "
                + "rule reads (the face-1 readers are all Shred, which fires on the save hook).");
            Assert.That(masked.TotalRolls, Is.EqualTo(rolls.TotalRolls),
                "the same dice were rolled - relabelled, not deleted, so anything counting dice agrees.");
            Assert.That(rolls.At(6), Is.EqualTo(3f),
                "and the original is untouched: the beat and the log must still show the 6s the player "
                + "actually rolled.");
        }

        [Test]
        public void HideSixesFromRules_IsANoOpWithoutSixes()
        {
            IDiceResults rolls = new DiceResults(new float[] { 2f, 1f, 0f, 1f, 0f, 0f });

            Assert.That(FatigueUtilities.HideSixesFromRules(rolls), Is.SameAs(rolls),
                "nothing to hide, so not even a copy - this runs on every melee hit roll in the game.");
        }

        // --- harness (same shape as ExtraHitRuleIntegrationTests) ------------------------------------

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, bool isMelee = false, bool isCharging = false)
        {
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: isCharging);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears a 4+

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList) total += hit.HitCount;
            return total;
        }

        private DataBinding<UnitData> MakeUnit(Position position, SpecialRuleDefinition? rule = null)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4,
                defense: 4, modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null) binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return binding;
        }
    }
}
