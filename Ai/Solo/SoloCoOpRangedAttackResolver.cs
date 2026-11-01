using FDG.StageResolution;
using FDG.StageResolution.Requests;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - the official AI's shooting priority, from <b>Solo &amp; Co-Op Rules v3.6.0</b> (identical
    /// wording in all four books - GF, Firefight, AoF and AoF: Skirmish):
    ///
    /// <para>
    /// "AI units always shoot at the <b>closest</b> valid target, <b>prioritizing units that haven't
    /// activated yet</b>. If the closest target is in Cover but there is another valid target in the open,
    /// the AI must <b>prioritize the target in the open</b>."
    /// </para>
    /// <para>
    /// Its own class rather than a change to <see cref="Resolvers.AiChooseRangedAttackResolver"/>, which is
    /// the frozen benchmark baseline that plan D1 forbids changing. That resolver's summary used to call
    /// itself "the solo-rules priority" and it never was: it ordered by cover, then by how many of its own
    /// models could shoot, with <b>distance absent from the ordering entirely</b>, so it would shoot across
    /// the table past a unit at 6". It also had nothing for the not-yet-activated clause, which is the one
    /// that makes the AI play the clock - a unit shot before it acts loses its whole activation.
    /// </para>
    /// <para>
    /// Cover comes from the request's own <see cref="WeaponTargetStats.HasCover"/>, the engine's real
    /// determination for this weapon against this target, rather than the footprint estimate a brain uses
    /// before it has a weapon in hand. "Most shooters" stays as the final tie-break: not in the book, but
    /// between two targets the book ranks equally it is the better answer and it keeps the pick
    /// deterministic, which the book's own "Multiple Choices" rule would otherwise settle with a die.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpRangedAttackResolver
        : IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>
    {
        private readonly ITableState? _table;

        /// <param name="table">
        /// #423: needed for the Blast and Deadly clauses, which are the two places the four books
        /// disagree about shooting - so the pick has to know which book is being played. Optional so the
        /// parameterless construction in older tests still compiles; without it those two clauses are
        /// skipped and the general keys decide alone, which is exactly the behaviour before #423.
        /// </param>
        public SoloCoOpRangedAttackResolver(ITableState? table = null) => _table = table;

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

            // #423: Blast and Deadly name which KIND of unit to shoot, and the books state them in their
            // own Special Rules section after the shooting clause - so they decide the band and the
            // general keys below decide the order inside it. A weapon with neither rule puts everything
            // in band 0, so nothing changes for the other 95% of shots.
            SoloCoOpProfile? profile = _table == null
                ? null
                : SoloCoOpProfile.For(_table.Progress.GameSystem);

            var best = options
                .OrderBy(o => profile == null
                    ? 0
                    : SoloAiSpecialRuleClauses.TargetBand(profile, o.Weapon, o.Stats.TargetUnit.GetValue()))
                .ThenBy(o => o.Stats.HasCover ? 1 : 0)
                .ThenBy(o => SoloAiTargeting.HasActivated(o.Stats.TargetUnit.GetValue()) ? 1 : 0)
                .ThenBy(o => AiCombatMath.Distance(shooter, o.Stats.TargetUnit.GetValue()))
                .ThenByDescending(o => o.Stats.modelsThatCanShoot.Count)
                .First();

            CancellableResult<RangedAttackChoice> result =
                new Selected<RangedAttackChoice>(new RangedAttackChoice(best.Weapon, best.Stats.TargetUnit));
            return Task.FromResult(result);
        }

        private static List<(Weapon Weapon, WeaponTargetStats Stats)> BuildOptions(
            ChooseRangedAttackRequest request, bool requireShooters)
        {
            var result = new List<(Weapon, WeaponTargetStats)>();
            foreach (WeaponOption weaponOption in request.WeaponOptions)
                foreach (WeaponTargetStats targetStats in weaponOption.WeaponTargetStats)
                {
                    if (targetStats.UnselectableReason != null) continue;
                    if (!requireShooters || targetStats.modelsThatCanShoot.Count > 0)
                        result.Add((weaponOption.Weapon, targetStats));
                }
            return result;
        }
    }
}
