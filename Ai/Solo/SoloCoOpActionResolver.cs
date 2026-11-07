using System;
using System.Collections.Generic;
using FDG.Ai.Resolvers;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - picks the action for an official-AI unit from <see cref="SoloAiBrain"/>'s plan, which is the
    /// decision tree printed in <b>Solo &amp; Co-Op Rules v3.6.0</b>.
    ///
    /// <para>
    /// What this replaces: the baseline's ranking of Charge &gt; Move &gt; Shoot &gt; Pass, which has no
    /// notion of objectives at all. Objectives decide the winner in every mode the engine has, so the one
    /// bot meant to follow the book was the one least able to win with it. The tree's five steps are about
    /// nothing else: hold a contested objective, go take an unattended one, and only then fight.
    /// </para>
    /// <para>
    /// <b>Why the per-activation family latch is not optional.</b> A unit gets one action per activation, so
    /// each family should be picked at most once - and when a move cannot be carried out, the engine's Back
    /// affordance reopens this menu with Move still on it. An AI that re-picks from an unchanged plan loops
    /// until the ~1.5M-decision watchdog fires. That exact bug cost three wrong guesses on the hero AI
    /// earlier today: one Horde seed went from finishing in under a second to not finishing in ten minutes.
    /// So a family tried this activation is never offered to the plan again; the fallbacks below take over.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpActionResolver : IStageResolver<ChooseActionRequest, string>
    {
        private readonly ITableState _table;
        private readonly PlayerID _me;
        private readonly SoloMoveDeclineLatch? _latch;
        private readonly AiStringSelectionResolver _fallback;

        /// <summary>The activation this resolver's per-activation state belongs to.</summary>
        private UnitID? _activation;
        private int _activationRound = -1;
        private bool _triedMove;
        private bool _triedCharge;
        private bool _triedCast;
        private bool _triedDisembark;
        private readonly HashSet<string> _triedRuleActions = new(StringComparer.Ordinal);

        public SoloCoOpActionResolver(ITableState table, PlayerID me, SoloMoveDeclineLatch? latch,
            AiStringSelectionResolver fallback)
        {
            _table = table;
            _me = me;
            _latch = latch;
            _fallback = fallback;
        }

        public Task<string> Resolve(ChooseActionRequest request)
        {
            IUnit? unit = AiCombatMath.FindUnit(_table, request.ActivatingUnitID);
            if (unit == null) return _fallback.Resolve(request);

            BeginActivationIfNew(request.ActivatingUnitID);

            IReadOnlyList<string> options = request.ValidOptions;
            string? Has(string option) => options.Contains(option) ? option : null;

            // #358's latch: our own path resolver just declined this unit's move, so the movement family is
            // off the table for this one pick or we re-decline forever.
            bool movementDeclined = _latch?.Consume() == true;

            // #423 Transport: "units inside of them must always disembark on their FIRST ACTIVATION (if
            // possible)." Unconditional and ranked above everything, because that is what the book says -
            // the baseline bot instead waits until its ride is within 12" of something worth having
            // (AiStringSelectionResolver.ShouldDisembark), which is a better tactic and not this
            // rulebook's instruction.
            //
            // It also fixes something larger than the clause: an embarked unit fails GetCanMove
            // ("Embarked; disembark first") and this resolver never offered Disembark, so cargo the
            // official bot loaded at deployment rode inside until the transport died.
            //
            // DELIBERATELY NOT gated to Grimdark Future, which is the one book that prints the clause.
            // Age of Fantasy has the Transport RULE with no AI clause for it, and the alternative there
            // is cargo that never gets out of its transport at all - which is not a reading of the
            // book's silence, it is a failure to act. The activation-ORDER half of the same clause IS
            // gated to GF (see SoloRulesActivationResolver), because an order the book does not ask to
            // change is one the rest of the book already decides.
            if (TryDisembark(Has) is { } disembark) return Task.FromResult(disembark);

            // #423 General Special Rules: "AI units must always use their special rules AS SOON AS THEY
            // ARE ACTIVATED, targeting the closest unit that makes sense." The last of the seven clauses,
            // and the same shape of fault as Caster: the switch over the decision tree below covers
            // Charge, Move, Shoot and Pass, so a rule-named action - Teleport, a Storm of X, Mend, a
            // campaign skill, any faction rule a book authors as an activated ability - was never picked
            // by the official bot at all.
            //
            // "As soon as they are activated" is taken literally: ahead of the move, and ahead of the
            // tree. Caster is the one documented exception, and it says so in its own clause ("always
            // cast spells AFTER moving"), which is why Cast is excluded here and handled below.
            //
            // Each rule is tried at most once per activation (a cancelled custom action reopens this menu
            // with the option still on it), and the targeting half is
            // SoloCoOpAbilityTargetResolver.
            if (TryRuleAction(options) is { } ruleAction) return Task.FromResult(ruleAction);

            // #423: the Special Rules section overrides the decision tree for two rules. "AI units with
            // Indirect weapons that are in range of enemies ALWAYS use Hold actions and shoot", and the
            // same sentence for Relentless - no tree, no objective run, stand still and fire. Checked
            // before the plan is even built, because the plan is what it overrides.
            //
            // A Hold action is a non-move, not a non-action, so "Hold and shoot" is simply Shoot without
            // having moved - the same reading the tree's own Hold step uses below. If the shot is not on
            // the menu (nothing targetable from here after all), fall through to the tree rather than
            // passing the activation away.
            if (SoloAiSpecialRuleClauses.MustHoldAndShoot(_table, _me, unit)
                && Has(ChooseActionStage.SHOOT_CHOICE_NAME) is { } holdAndShoot)
            {
                return Task.FromResult(holdAndShoot);
            }

            SoloAiPlan plan = SoloAiBrain.Plan(_table, _me, unit);

            // #423 Caster: "AI units always cast spells AFTER MOVING, selecting a random spell by rolling
            // D3+X..." - and before #423 this resolver never picked Cast at all, in any branch, so the
            // official bot's casters never cast a spell in any mode. The random pick itself is
            // SoloCoOpSpellResolver's; what belongs here is the window.
            //
            // "After moving" is read as "once no move is still to come": the unit has already moved, or
            // cannot, or the tree is holding it in place (a Hold action is a non-move, and a holding
            // caster that never cast would be the clause read backwards). Cast is preferred over Shoot at
            // that point because casting is once per activation and attacking closes the window - a bot
            // that shot first would never cast at all, which is the fault being fixed.
            bool moveStillToCome = !movementDeclined
                && plan.Kind != ESoloAiMove.Hold
                && ((!_triedMove && options.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME))
                    || (!_triedCharge && options.Contains(ChooseActionStage.CHARGE_CHOICE_NAME)));
            if (!moveStillToCome && TryCast(Has) is { } cast) return Task.FromResult(cast);

            string? pick = null;

            switch (plan.Kind)
            {
                case ESoloAiMove.Charge:
                    pick = TryCharge(Has, movementDeclined) ?? TryMove(Has, movementDeclined);
                    break;

                case ESoloAiMove.AdvanceAndShoot:
                case ESoloAiMove.AdvanceToObjective:
                    // Already in range of the target it picked? Shoot from here rather than walking into
                    // the open for no gain - the book's own step 2 holds position for the same reason.
                    bool inRange = plan.Target != null && plan.Range > 0f
                        && AiCombatMath.Distance(unit, plan.Target) <= plan.Range;
                    pick = inRange
                        ? Has(ChooseActionStage.SHOOT_CHOICE_NAME) ?? TryMove(Has, movementDeclined)
                        : TryMove(Has, movementDeclined) ?? Has(ChooseActionStage.SHOOT_CHOICE_NAME);
                    break;

                case ESoloAiMove.RushToObjective:
                    pick = TryMove(Has, movementDeclined);
                    break;

                case ESoloAiMove.Hold:
                    // "Hold position" - but if something wandered into range meanwhile, shoot it. A Hold
                    // action in OPR is a non-move, not a non-action.
                    pick = AnythingToShoot(unit) ? Has(ChooseActionStage.SHOOT_CHOICE_NAME) : null;
                    break;
            }

            // Moved already (or holding), and something is in range: take the shot before ending.
            pick ??= AnythingToShoot(unit) ? Has(ChooseActionStage.SHOOT_CHOICE_NAME) : null;

            // The fallback MUST stay inside ValidOptions - ChooseActionStage faults on an option it did not
            // offer, and it auto-passes when the list is empty, so options[0] is always safe.
            pick ??= Has(ChooseActionStage.PASS_CHOICE_NAME) ?? options[0];
            return Task.FromResult(pick);
        }

        /// <summary>Resets the per-activation latches when a different unit (or round) starts acting.</summary>
        private void BeginActivationIfNew(UnitID activating)
        {
            int round = _table.Progress.RoundCount ?? 0;
            if (_activation != null && _activation.Value.Equals(activating) && _activationRound == round) return;

            _activation = activating;
            _activationRound = round;
            _triedMove = false;
            _triedCharge = false;
            _triedCast = false;
            _triedDisembark = false;
            _triedRuleActions.Clear();
        }

        private string? TryCharge(Func<string, string?> has, bool movementDeclined)
        {
            if (movementDeclined || _triedCharge) return null;
            string? option = has(ChooseActionStage.CHARGE_CHOICE_NAME);
            if (option != null) _triedCharge = true;
            return option;
        }

        /// <summary>
        /// #423: the first offered special-rule action this activation has not tried yet.
        ///
        /// <para>
        /// Everything the action menu can carry is either one of the engine's own choices (Move, Charge,
        /// Shoot, Cast, Pass), a transport action, a Star Quest hero action, or a rule-named action - and
        /// the last of those is what this clause is about. So the test is a denylist of the first three
        /// rather than a list of rules, because the rules come from the army BOOKS: a faction rule
        /// authored as an activated ability has to be used without the engine ever having heard of it,
        /// which is the whole point of the rule graph.
        /// </para>
        /// <para>
        /// <b>Embark is excluded, deliberately.</b> "Units should very rarely embark into a transport
        /// AFTER deployment" is the owner's own ruling (2026-08-15, the half of #335 that survived), and a
        /// mid-game re-board has no plan behind it - the bot would climb back into the hull it was just
        /// told to leave. Disembark is excluded because the Transport clause above already handles it,
        /// first and unconditionally.
        /// </para>
        /// </summary>
        private string? TryRuleAction(IReadOnlyList<string> options)
        {
            foreach (string option in options)
            {
                if (IsEngineChoice(option)) continue;
                if (_triedRuleActions.Contains(option)) continue;
                _triedRuleActions.Add(option);
                return option;
            }

            return null;
        }

        private static bool IsEngineChoice(string option) =>
            option == ChooseActionStage.MOVEMENT_CHOICE_NAME
            || option == ChooseActionStage.CHARGE_CHOICE_NAME
            || option == ChooseActionStage.SHOOT_CHOICE_NAME
            || option == ChooseActionStage.CAST_CHOICE_NAME        // its own clause: after moving
            || option == ChooseActionStage.PASS_CHOICE_NAME
            || option == Rules.Dispatch.CoreRuleCatalog.DisembarkRuleName   // handled above, first
            || option == Rules.Dispatch.CoreRuleCatalog.EmbarkRuleName      // see remarks
            || option == QuestActions.SkillOption
            || option == QuestActions.HoldWithoutShootingOption
            || option == QuestActions.EndActivationOption;

        /// <summary>
        /// #423: Disembark, latched like the rest. The placement that follows can be declined (nowhere
        /// legal within 6" of the hull), which reopens this menu with Disembark still on it - and an AI
        /// that re-picks an unchanged option spins until the engine's decision watchdog fires.
        /// </summary>
        private string? TryDisembark(Func<string, string?> has)
        {
            if (_triedDisembark) return null;
            string? option = has(Rules.Dispatch.CoreRuleCatalog.DisembarkRuleName);
            if (option != null) _triedDisembark = true;
            return option;
        }

        /// <summary>
        /// #423: Cast, once per activation. Latched for the same reason Move and Charge are - a cast that
        /// is cancelled (no castable spell after all) reopens this menu with Cast still on it, and an AI
        /// that re-picks from an unchanged plan spins until the engine's decision watchdog fires.
        /// </summary>
        private string? TryCast(Func<string, string?> has)
        {
            if (_triedCast) return null;
            string? option = has(ChooseActionStage.CAST_CHOICE_NAME);
            if (option != null) _triedCast = true;
            return option;
        }

        private string? TryMove(Func<string, string?> has, bool movementDeclined)
        {
            if (movementDeclined || _triedMove) return null;
            string? option = has(ChooseActionStage.MOVEMENT_CHOICE_NAME);
            if (option != null) _triedMove = true;
            return option;
        }

        private bool AnythingToShoot(IUnit unit)
        {
            float range = AiCombatMath.MaxRange(unit);
            if (range <= 0f) return false;
            return AiCombatMath.Enemies(_table, _me).Any(e => AiCombatMath.Distance(unit, e) <= range);
        }
    }
}
