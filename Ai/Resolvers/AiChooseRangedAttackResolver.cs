using FDG.Ai.Solo;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a ranged attack target by the <b>Solo &amp; Co-Op Rules v3.6.0</b> shooting priority:
    ///
    /// <para>
    /// "AI units always shoot at the <b>closest</b> valid target, <b>prioritizing units that haven't
    /// activated yet</b>. If the closest target is in Cover but there is another valid target in the open,
    /// the AI must <b>prioritize the target in the open</b>."
    /// </para>
    /// <para>
    /// #419 fixed two of those three. Before, the order was cover, then how many of its own models could
    /// shoot - and <b>distance was not in it at all</b>, so the bot would shoot a unit at the far end of the
    /// table over one at 6" whenever the far one was in the open or more guns could see it. "Prioritizing
    /// units that haven't activated yet" was missing outright, and it is the clause that makes the AI play
    /// the clock: a unit shot before it acts loses its whole activation, one shot afterwards loses nothing.
    /// </para>
    /// <para>
    /// Cover comes from the request's own <see cref="WeaponTargetStats.HasCover"/> - the engine's real
    /// determination for this weapon against this target - rather than from
    /// <see cref="SoloAiTargeting.InCover"/>, which is the estimate a brain uses before it has picked a
    /// weapon. "Most shooters" stays as the last tie-break: it is not in the book, but between two targets
    /// the book ranks equally, more guns firing is the better answer and it keeps the pick deterministic.
    /// </para>
    /// </summary>
    public class AiChooseRangedAttackResolver
        : IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>
    {
        public Task<CancellableResult<RangedAttackChoice>> Resolve(ChooseRangedAttackRequest request)
        {
            // Prefer options where at least one model has LOS and is in range.
            var options = BuildOptions(request, requireShooters: true);

            // Fall back to all options if LOS blocks every shot (engine will cancel gracefully).
            if (options.Count == 0)
                options = BuildOptions(request, requireShooters: false);

            if (options.Count == 0)
                throw new InvalidOperationException("AI received a ranged attack request with no options at all.");

            IUnit shooter = request.AttackingUnit.GetValue();
            var best = options
                .OrderBy(o => o.stats.HasCover ? 1 : 0)
                .ThenBy(o => SoloAiTargeting.HasActivated(o.stats.TargetUnit.GetValue()) ? 1 : 0)
                .ThenBy(o => AiCombatMath.Distance(shooter, o.stats.TargetUnit.GetValue()))
                .ThenByDescending(o => o.stats.modelsThatCanShoot.Count)
                .First();

            CancellableResult<RangedAttackChoice> result =
                new Selected<RangedAttackChoice>(new RangedAttackChoice(best.weapon, best.stats.TargetUnit));
            return Task.FromResult(result);
        }

        private static List<(Weapon weapon, WeaponTargetStats stats)> BuildOptions(
            ChooseRangedAttackRequest request, bool requireShooters)
        {
            var result = new List<(Weapon, WeaponTargetStats)>();
            foreach (var weaponOption in request.WeaponOptions)
                foreach (var targetStats in weaponOption.WeaponTargetStats)
                {
                    if (targetStats.UnselectableReason != null) continue;
                    if (!requireShooters || targetStats.modelsThatCanShoot.Count > 0)
                        result.Add((weaponOption.Weapon, targetStats));
                }
            return result;
        }
    }
}
