using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #422: the "<b>pick one of them</b>" clause shared by every move-through-enemy rule.
    ///
    /// <para>
    /// Crossing Attack and Crossing Strike are worded identically on this point - "once per activation,
    /// when this model moves through enemy units, <b>pick one of them</b>" - and a flyer's path routinely
    /// crosses two or three units, so which one is attacked is a real decision and often the whole value of
    /// the rule (the squad on the objective, not the first one the path happened to clip).
    /// <see cref="StrafingStage"/> had the prompt; <see cref="CrossingAttackStage"/> took
    /// <c>crossed[0]</c> with a comment conceding the point. Lifting the one implementation out is cheaper
    /// than keeping two, and leaves one place for the wording to be right.
    /// </para>
    /// <para>
    /// <b>One enemy crossed gets a yes/no</b>, which is the ordinary case: a one-option selection dialog is
    /// a worse question than the question it stands for. <b>Several crossed get a cancellable pick and no
    /// yes/no on top of it</b> - an ability that already lets the player decline at the pick is not asked
    /// twice (the same ruling Dash's placement prompt follows). Returns null when the player declines,
    /// either way.
    /// </para>
    /// </summary>
    public static class MoveThroughTargetPick
    {
        /// <param name="weaponName">
        /// The weapon the attack is made with, for rules that attack with one (Crossing Strike / Strafing).
        /// Null for the auto-wound rules (Crossing Attack), which are not a weapon attack at all and whose
        /// prompt must not claim to be.
        /// </param>
        public static async Task<DataBinding<UnitData>?> Pick(IGameContext gameContext, IUnit mover,
            string ruleName, string? weaponName, List<DataBinding<UnitData>> crossed, string pickDisplayName)
        {
            if (crossed.Count == 0) return null;

            if (crossed.Count == 1)
            {
                string what = weaponName == null
                    ? $"{mover.Name} strikes {crossed[0].GetValue().Name} while moving through?"
                    : $"{mover.Name} attacks {crossed[0].GetValue().Name} with its {weaponName} while "
                      + "moving through?";

                var question = new YesNoRequest(mover.PlayerID, $"Use {ruleName}: {what}",
                    defaultAnswer: true, displayName: $"Deciding Whether to Use {ruleName}");
                bool accepted = await gameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(question);
                return accepted ? crossed[0] : null;
            }

            var options = new List<CancellableSelectionRequest<UnitData>.ValidOption>();
            foreach (DataBinding<UnitData> enemy in crossed)
            {
                options.Add(new CancellableSelectionRequest<UnitData>.ValidOption(
                    enemy, enemy.GetValue().Name));
            }

            string instructions = weaponName == null
                ? $"{ruleName}: pick one unit moved through to strike"
                : $"{ruleName}: pick one unit moved through to attack with {weaponName}";

            var request = new CancellableSelectionRequest<UnitData>(mover.PlayerID, instructions,
                options, System.Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>(),
                displayName: pickDisplayName);

            CancellableResult<DataBinding<UnitData>> reply = await gameContext.PlayerRequester
                .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(
                    request);

            return reply is Selected<DataBinding<UnitData>> selected ? selected.Value : null;
        }
    }
}
