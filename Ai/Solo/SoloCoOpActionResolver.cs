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
        }

        private string? TryCharge(Func<string, string?> has, bool movementDeclined)
        {
            if (movementDeclined || _triedCharge) return null;
            string? option = has(ChooseActionStage.CHARGE_CHOICE_NAME);
            if (option != null) _triedCharge = true;
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
