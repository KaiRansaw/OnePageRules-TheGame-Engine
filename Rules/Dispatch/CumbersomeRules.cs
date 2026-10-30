using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Cumbersome (#409, OPR 3.6.0) - "May only use Hold or Advance actions, but can't shoot when moving."
    ///
    /// <para>The action half rides the existing <see cref="Effect.RestrictActions"/> plumbing that Immobile
    /// and Aircraft already use. This class is the shooting half: whether a unit that has moved may still
    /// fire. The ordinary rule is that it may - an Advance is a move-and-shoot action - so this is a
    /// permission being WITHDRAWN, the exact mirror of <see cref="ShootAfterRushRules"/>, and it is
    /// detected the same way: structurally, by the operation a rule produces rather than by its name, so
    /// an army-flavoured alias or a renamed copy behaves identically.</para>
    ///
    /// <para>Read at the action gate (<c>ChooseActionStage.GetCanShoot</c>) rather than inside the shoot
    /// stage, so a Cumbersome unit that has moved is simply never offered Shoot - the same shape Immobile
    /// takes, and what #200 means by the action gate and the stage never disagreeing.</para>
    /// </summary>
    public static class CumbersomeRules
    {
        /// <summary>
        /// Whether <paramref name="unit"/>'s rules (including anything an aura or grant confers on it)
        /// forbid shooting in an activation where it moved. Non-logging - safe to call while building UI
        /// or an AI plan, which is why it uses the non-consuming evaluation.
        /// </summary>
        public static bool CannotShootAfterMoving(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new ActionChoiceContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.ForbidShootAfterMove) return true;
            }
            return false;
        }

        /// <summary>
        /// The alias-aware display name of the rule that forbids shooting after moving, or null if none
        /// does - so the action menu can say WHY Shoot is greyed out in the army's own words.
        /// </summary>
        public static string? ShootAfterMoveBlockSource(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new ActionChoiceContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.ForbidShootAfterMove) return ruleName;
            }
            return null;
        }
    }
}
