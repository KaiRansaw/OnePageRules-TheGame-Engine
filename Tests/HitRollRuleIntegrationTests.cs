using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Stealth's -1-to-hit flows through the
    // REAL DetermineHitRollStage. The stage fires the OnHitRollModifier "when", the
    // RuleEvaluator evaluates both seats, and the RollModifierSink folds the result into the
    // hit threshold — none of it interpreted by the stage. Stealth is a Subject-seat rule
    // gated on distance > 9". Units are quality 4, so the base hit roll needed is 4.
    [TestFixture]
    public class HitRollRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position FarPos      = new Position(20, 5);  // ~18.5" base-to-base → > 9"
        private static readonly Position NearPos     = new Position(3, 5);   // ~1.5" → ≤ 9"
        // #409: Artillery's thresholds moved from 9" to 12", so the tests need a distance that is
        // comfortably past 9 but short of 12. Centre-to-centre 12" minus two 0.75" bases = 10.5".
        private static readonly Position MidPos      = new Position(12, 5);  // ~10.5" → > 9" but < 12"

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            // Resolver-equipped so granted-rule read-back resolves a claimed mark's rule by name.
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
        }

        [Test]
        public async Task DefenderWithStealth_BeyondNine_RaisesHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1 because Stealth applies -1 to hit from beyond 9\".");
        }

        [Test]
        public async Task DefenderWithStealth_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth's distance condition fails within 9\", so no modifier is applied.");
        }

        [Test]
        public async Task AttackerWithStealth_DoesNotFire_SeatMismatch()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(attacker); // wrong seat: Stealth is Subject-only

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth is a Subject-seat rule; it must not fire when its bearer is the attacker.");
        }

        [Test]
        public async Task NoRules_BaselineUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "no rules → just the attacker's quality.");
        }

        // #409 (OPR 3.6.0) rewrote Artillery. The attacker-seat (Actor) +1 to hit now needs BOTH a Hold
        // action and a target over 12" away, where 3.5.1 asked only for 9" and no action at all. "Hold" is
        // read off the MovedThisRound token, which RunStage leaves unstamped unless a test says otherwise.
        [Test]
        public async Task ArtilleryAttacker_HoldingBeyondTwelve_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base 4, lowered by 1: a Hold action shooting over 12\" away gets Artillery's +1 to hit.");
        }

        // The band 3.6 took away: 9-12" used to earn the bonus and no longer does.
        [Test]
        public async Task ArtilleryAttacker_BetweenNineAndTwelve_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(MidPos);
            AttachArtillery(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "10.5\" is past the old 9\" line but short of 3.6's 12\", so Artillery gives nothing.");
        }

        [Test]
        public async Task ArtilleryAttacker_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachArtillery(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Artillery's distance condition fails well inside 12\", so no modifier is applied.");
        }

        // #409: the other half of the new Actor gate - a unit that moved is not Holding, so no +1 even at
        // a range that would otherwise qualify. MovedThisRound is what MovementStage stamps when a declared
        // move actually resolves.
        [Test]
        public async Task ArtilleryAttacker_HavingMoved_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(attacker);
            StampMoved(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "the +1 is a Hold-action bonus in 3.6, so a unit that moved gets nothing.");
        }

        // Artillery's defensive facet, as rewritten by #409: enemies shooting it from over 12" take -1
        // (3.5.1: -2 from over 9"), and only while the Artillery unit has not moved this round.
        [Test]
        public async Task ArtilleryDefender_BeyondTwelve_RaisesAttackerHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1: Artillery gives enemies -1 to hit it from over 12\" in 3.6.");
        }

        [Test]
        public async Task ArtilleryDefender_BetweenNineAndTwelve_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(MidPos);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "the defensive penalty starts past 12\" in 3.6, so 10.5\" is inside it.");
        }

        [Test]
        public async Task ArtilleryDefender_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Artillery's distance condition fails inside 12\", so the defensive modifier is not applied.");
        }

        // #409: the condition the defensive arm never had before - an Artillery piece that repositioned
        // this round stops being hard to hit. Read from the far side of the table during the attacker's
        // activation, which is why it rides a unit token rather than per-activation movement state.
        [Test]
        public async Task ArtilleryDefender_HavingMovedThisRound_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(defender);
            StampMoved(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "an Artillery unit that moved this round loses its -1-to-hit-it protection.");
        }

        // Both facets at once, at 3.6 numbers: Artillery attacker +1 (-1 threshold) against an Artillery
        // defender -1 (+1 threshold), both over 12", neither having moved. They cancel: 4 - 1 + 1 = 4.
        [Test]
        public async Task ArtilleryBothSeats_BeyondTwelve_StackActorAndSubject()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(attacker);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Artillery attacker +1 and Artillery defender -1 both fire and cancel: 4 - 1 + 1 = 4.");
        }

        // #409: Indirect no longer touches hit rolls at all. 3.5.1 gave it -1 to hit after moving; 3.6
        // deletes the penalty and makes the whole rule conditional on Holding instead, so there is nothing
        // left for it to do on this stage in either case. Both directions are pinned so a future edit that
        // quietly reinstates a modifier fails here.
        [Test]
        public async Task IndirectAttacker_AfterMoving_NoHitModifier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "3.6 deleted Indirect's -1-after-moving; the rule is a sight permission now, not a penalty.");
        }

        [Test]
        public async Task IndirectAttacker_WithoutMoving_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Indirect never modifies the hit roll in 3.6, held or moved.");
        }

        // Indirect's -1 is gated on Not(IsMelee): a charge sets HasMoved, so without the gate an Indirect
        // carrier would wrongly take -1 on its melee swings. In melee (even after moving) the penalty must
        // not apply — it's a shooting rule.
        [Test]
        public async Task IndirectAttacker_InMelee_NoPenalty()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: true, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Indirect is a shooting rule; its -1-after-moving must not apply to a melee (charge) swing.");
        }

        // Reliable is an attacker-seat rule that FLOORS the base quality to 2+ before per-roll
        // modifiers — a different sink (QualityFloorSink) than the modifier rules above, but riding
        // the same OnHitRollModifier evaluation. Units are quality 4, so Reliable improves 4 → 2.
        [Test]
        public async Task ReliableAttacker_FloorsBaseQualityToTwo()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachReliable(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(2),
                "Reliable floors the attacker's base quality 4 to 2+.");
        }

        // "Still modifiable": Reliable floors the base to 2, then a defender's Stealth (-1 to hit
        // from beyond 9") stacks on top, raising the threshold to 3. Proves the floor sets the BASE,
        // not the final value.
        [Test]
        public async Task ReliableAttacker_WithStealthDefender_FloorThenModifierStacks()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachReliable(attacker);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base floored to 2 by Reliable, then +1 harder from Stealth's -1 to hit.");
        }

        // #015: the stage also determines the attack-dice count (weapon Attacks × weapons firing),
        // which RollToHitStage then rolls. Here a 2-attack weapon fired by 3 weapons → 6 dice.
        [Test]
        public async Task AttackCount_IsWeaponAttacksTimesWeaponCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 2, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 3);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            Assert.That(result.AttackCount, Is.EqualTo(6f),
                "attack count is weapon Attacks (2) × weapon count (3).");
        }

        // Evasive (Subject): -1 to hit against the bearer with no gate, so it raises the threshold in
        // BOTH shooting and melee — the defensive mirror of Precise.
        [Test]
        public async Task DefenderWithEvasive_RaisesHitThreshold_Shooting()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachEvasive(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Evasive gives enemies -1 to hit → +1 threshold (shooting).");
        }

        [Test]
        public async Task DefenderWithEvasive_RaisesHitThreshold_Melee()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachEvasive(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Evasive is un-gated, so the -1 to hit also applies to melee swings.");
        }

        // Melee Evasion (Subject): the same -1 but gated to melee — fires in melee, not shooting.
        [Test]
        public async Task DefenderWithMeleeEvasion_Melee_RaisesThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachMeleeEvasion(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Melee Evasion gives -1 to hit in melee → +1 threshold.");
        }

        [Test]
        public async Task DefenderWithMeleeEvasion_Shooting_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachMeleeEvasion(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender); // shooting

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Melee Evasion is melee-only (IsMelee gate); a shooting attack is unaffected.");
        }

        // Precise (Actor): +1 to hit, any attack → lowers the threshold.
        [Test]
        public async Task AttackerWithPrecise_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachPrecise(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "Precise gives +1 to hit → -1 threshold.");
        }

        // Good Shot (Actor): +1 to hit when shooting only — Not(IsMelee) gate.
        [Test]
        public async Task AttackerWithGoodShot_Shooting_LowersThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachGoodShot(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "Good Shot gives +1 to hit when shooting → -1 threshold.");
        }

        [Test]
        public async Task AttackerWithGoodShot_Melee_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachGoodShot(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Good Shot is shooting-only; a melee swing gets no bonus.");
        }

        // #100 #14 Mark/Tag: a mark on the defender (here naming "Precise") is CLAIMED by the first attack —
        // the marked rule transfers to the attacker for that attack (Precise = +1 to hit → threshold 4-1=3)
        // and the mark is removed, so a second attack into the now-unmarked enemy gets nothing.
        [Test]
        public async Task MarkedDefender_FirstAttack_ClaimsRule_ThenMarkConsumed()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            MarkDefender(defender, "Precise");

            DetermineHitRollResults first = await RunStage(attacker, defender);
            Assert.That(first.HitRollNeeded, Is.EqualTo(3),
                "the first attack claims the Precise mark — the granted +1 to hit lowers the threshold to 3.");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Mark), Is.False,
                "the mark is spent by the attack that claimed it.");

            DetermineHitRollResults second = await RunStage(attacker, defender);
            Assert.That(second.HitRollNeeded, Is.EqualTo(4),
                "the mark was consumed; a later attack into the same enemy gets no bonus.");
        }

        // Control: no mark → no claimed rule, base threshold.
        [Test]
        public async Task UnmarkedDefender_NoClaim_BaselineThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "no mark on the defender → nothing claimed.");
        }

        // #377 — a one-shot range grant ("+6\" range when shooting once", Battle Rune) is spent by the
        // shooting attack that used it: the range check itself only peeks (a per-frame query must not
        // consume), so DetermineHitRollStage runs one live range evaluation per shooting attack. A melee
        // swing never read the range and must not spend it.
        [Test]
        public async Task RangeGrant_SpentByShootingAttack_NotByMelee()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            GrantRangeBonus(attacker);

            await RunStage(attacker, defender, isMelee: true);
            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.True,
                "a melee swing never reads range - the one-shot range grant survives it.");

            await RunStage(attacker, defender);
            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.False,
                "the shooting attack is the range's 'next time' - the grant is spent.");
        }

        private static void GrantRangeBonus(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.FirstTrigger(),
                Payload: new TokenPayload.RuleGrant("+6\" range when shooting", ELifetime.NextTrigger)));

        // #377 — a mark naming a combat-kind-gated phrase rule ("+1 to hit rolls in melee", the Magic
        // Targeting / Raiding Drugs spells): the claim transfers the rule, and the rule's own gate then
        // decides whether it does anything. A melee swing gets the bonus; a shooting claim spends the
        // mark for nothing (#101 doctrine: forcing a wasted buff is legitimate).
        [Test]
        public async Task MarkedGatedHitRule_MeleeSwing_AppliesBonus()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            MarkDefender(defender, "+1 to hit rolls in melee");

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "the claimed \"+1 to hit rolls in melee\" fires on the melee swing: threshold 4 - 1 = 3.");
        }

        [Test]
        public async Task MarkedGatedHitRule_ShootingClaim_SpendsMarkForNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            MarkDefender(defender, "+1 to hit rolls in melee");

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "shooting claims the mark but the melee gate fails - no bonus.");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Mark), Is.False,
                "the mark is spent by the claiming attack even though its gate failed.");
        }

        private static void MarkDefender(DataBinding<UnitData> unit, string ruleName) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.Mark, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.ThisAttack)));

        // ── #422 Unstoppable's modifier half ─────────────────────────────────────────────────────────
        //
        // "This weapon ignores Regeneration, and ignores all negative modifiers WHICH ARE NOT FROM THIS
        // MODEL'S SPECIAL RULES." Only the Regeneration half was built; the second said it "needs a
        // modifier-immunity effect and lands when that's modelled". The obstacle was that the modifier
        // stack kept totals - a net, and later Pierce's positive/negative split - and this clause asks
        // where a penalty CAME FROM, which no total can answer. Modifiers now carry their source.

        [Test]
        public async Task UnstoppableAttacker_IgnoresTheTargetsStealth()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender,
                firedWeapon: UnstoppableWeapon());

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth's -1 is not from the attacker's own rules, so Unstoppable ignores it - compare "
                + "DefenderWithStealth_BeyondNine_RaisesHitThreshold, which is the same shot without it.");
        }

        // The half of the clause that makes it a clause rather than "ignores all negative modifiers": a
        // model's OWN penalty stays. Without the source check this would wrongly read 4.
        [Test]
        public async Task UnstoppableAttacker_KeepsItsOwnPenalty()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("-1 to hit rolls when shooting",
                CoreRuleCatalog.HitPenaltyWhenShooting));

            DetermineHitRollResults result = await RunStage(attacker, defender,
                firedWeapon: UnstoppableWeapon());

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "the penalty IS from this model's own special rules, so it is kept.");
        }

        // Both at once, which is the case that proves the two are separated rather than one of them
        // winning: the attacker's own -1 survives and the target's -1 is dropped, so the shot ends one
        // step worse than base rather than two.
        [Test]
        public async Task UnstoppableAttacker_KeepsItsOwnPenalty_AndStillIgnoresTheTargets()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("-1 to hit rolls when shooting",
                CoreRuleCatalog.HitPenaltyWhenShooting));
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender,
                firedWeapon: UnstoppableWeapon());

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "own -1 kept, target's -1 dropped: 4 + 1, not 4 + 2.");
        }

        // Favourable modifiers are untouched - the clause is about negatives only, and a buff the
        // attacker has must not be discarded along with the penalties.
        [Test]
        public async Task UnstoppableAttacker_KeepsItsBonuses()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachGoodShot(attacker);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender,
                firedWeapon: UnstoppableWeapon());

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "Good Shot's +1 survives and Stealth's -1 is dropped, so the two do not cancel as they "
                + "would without Unstoppable.");
        }

        // The rule rides the WEAPON, so a unit that carries it on one weapon does not get immunity when
        // firing another. Same shot as the first case with a plain weapon: the Stealth lands.
        [Test]
        public async Task PlainWeapon_SuffersTheTargetsStealth()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "no Unstoppable on the weapon fired - the penalty applies as usual.");
        }

        private static Weapon UnstoppableWeapon()
        {
            var weapon = new Weapon("Unstoppable Gun", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Unstoppable", CoreRuleCatalog.Unstoppable));
            return weapon;
        }

        private async Task<DetermineHitRollResults> RunStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool attackerMoved = false,
            bool isMelee = false, Weapon? firedWeapon = null)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            // #422: weapon-scoped rules (Unstoppable) have to ride the weapon the attack is made with,
            // so a caller can hand one in; everything else keeps the plain test weapon.
            Weapon weapon = firedWeapon
                ?? new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1, attackerMoved, isMelee);

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "Stage must store a DetermineHitRollResults in metadata.");
            return result;
        }

        private static void AttachStealth(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

        // #409: the "has not moved" half of Artillery's 3.6 gates reads TokenType.MovedThisRound, which in
        // a real game MovementStage stamps when a declared move resolves. These tests drive the hit stage
        // directly, so they stamp it by hand.
        private static void StampMoved(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(
                TokenDefinitionCatalog.Create(FDG.Rules.Foundation.TokenType.MovedThisRound));

        private static void AttachArtillery(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Artillery", CoreRuleCatalog.Artillery));

        private static void AttachIndirect(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));

        private static void AttachReliable(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));

        private static void AttachEvasive(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Evasive", CoreRuleCatalog.Evasive));

        private static void AttachMeleeEvasion(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Melee Evasion", CoreRuleCatalog.MeleeEvasion));

        private static void AttachPrecise(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Precise", CoreRuleCatalog.Precise));

        private static void AttachGoodShot(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Good Shot", CoreRuleCatalog.GoodShot));

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
