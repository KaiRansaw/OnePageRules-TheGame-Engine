using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #423 - the targeting half of the General Special Rules clause in <b>Solo &amp; Co-Op Rules
    /// v3.6.0</b>:
    ///
    /// <para>
    /// "AI units must always use their special rules as soon as they are activated, <b>targeting the
    /// closest unit that makes sense</b>."
    /// </para>
    /// <para>
    /// Every unit-targeting pick an ability raises arrives as a
    /// <see cref="CancellableSelectionRequest{T}"/> of <see cref="UnitData"/>, and the baseline answers it
    /// with <see cref="AiChooseMeleeDefenderResolver"/> - "the unit with the fewest living models", which
    /// is a reasonable melee-defender heuristic and not this clause. Closest is what the book asks for, and
    /// it is the same answer its Shooting and Melee clauses give ("always ... the closest valid target"),
    /// so the whole book points one way.
    /// </para>
    /// <para>
    /// <b>"That makes sense" is the engine's job, not this class's.</b> The options have already been
    /// filtered by the ability's own <c>TargetSelector</c> - range, friend or foe, a required rule - so
    /// every option offered is a sensible target by construction and the clause reduces to "the closest of
    /// them". Where the engine has offered a choice it considers legal, second-guessing it here would be
    /// inventing a policy the book does not have.
    /// </para>
    /// <para>
    /// <b>Closest to what.</b> These requests carry the options and the player, not the acting unit, so
    /// the distance is measured from the ACTIVATING unit, read off
    /// <see cref="IGameProgress.ActivatingUnit"/> - the same unit whose ability raised the request. With no
    /// activating unit (a request outside an activation, or a test driving the resolver directly) there is
    /// no reference point, so the baseline's answer stands rather than a guess.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpAbilityTargetResolver
        : IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>
    {
        private readonly ITableState _table;
        private readonly AiChooseMeleeDefenderResolver _fallback = new();

        public SoloCoOpAbilityTargetResolver(ITableState table) => _table = table;

        public Task<CancellableResult<DataBinding<UnitData>>> Resolve(
            CancellableSelectionRequest<UnitData> request)
        {
            if (request.ValidOptions.Count == 0) return _fallback.Resolve(request);
            if (_table.Progress.ActivatingUnit is not { } actor) return _fallback.Resolve(request);

            CancellableSelectionRequest<UnitData>.ValidOption closest = request.ValidOptions
                .OrderBy(o => AiCombatMath.Distance(actor, o.Option.GetValue()))
                .First();

            CancellableResult<DataBinding<UnitData>> result =
                new Selected<DataBinding<UnitData>>(closest.Option);
            return Task.FromResult(result);
        }
    }
}
