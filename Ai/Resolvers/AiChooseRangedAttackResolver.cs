using FDG.StageResolution;
using FDG.StageResolution.Requests;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a ranged attack target: prefer targets not in cover; among equal candidates, prefer the one
    /// with the most shooters.
    ///
    /// <para>
    /// <b>#419 - this is the FROZEN BASELINE and must not change behaviourally.</b> Plan D1
    /// (docs/ai-agent-plan.md, signed off 2026-07-09) makes the solo-rules bot the permanent benchmark
    /// yardstick: every Tactician and Strategist gate was measured against it, so a behavioural change here
    /// silently invalidates all of those numbers. It was briefly changed on 2026-10-31 and then split back
    /// out; the Solo &amp; Co-Op book's real shooting priority lives in
    /// <see cref="Solo.SoloCoOpRangedAttackResolver"/> instead.
    /// </para>
    /// <para>
    /// Note what the old summary line claimed: "the solo-rules priority". It never was. The book says
    /// closest target, preferring one that has not activated yet - and distance is not in this ordering at
    /// all. That gap is the whole reason the official bot needed its own resolver.
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

            var best = options
                .OrderBy(o => o.stats.HasCover ? 1 : 0)
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
