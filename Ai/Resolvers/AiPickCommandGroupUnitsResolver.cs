using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #439 - which friendly units a bot's Banner or Musician confers on.
    ///
    /// <para>
    /// <b>No book tells the AI how to choose this.</b> The Solo &amp; Co-Op flowcharts cover deployment,
    /// activation, shooting, melee and terrain and say nothing about Command Groups; the rule is written
    /// for a player picking before the game. So this is a judgement call, recorded in
    /// <c>claude/house-rules.md</c>, and it is deliberately the dullest one available: take the first
    /// <see cref="PickCommandGroupUnitsRequest.MaxPicks"/> options in the order the engine offered them,
    /// which is army order.
    /// </para>
    ///
    /// <para>
    /// "Biggest unit" or "nearest unit" would both be defensible and neither is in any book - and the
    /// second is not even answerable here, because the picks happen before anything is deployed, so no
    /// unit has a position yet. Army order is at least stable and reproducible run to run, which matters:
    /// a bot that picked at random would make every AI-vs-AI benchmark irreproducible.
    /// </para>
    ///
    /// <para>The bot always takes its full allowance rather than declining - the buff is free.</para>
    /// </summary>
    public class AiPickCommandGroupUnitsResolver
        : IStageResolver<PickCommandGroupUnitsRequest, CancellableResult<List<DataBinding<UnitData>>>>
    {
        public Task<CancellableResult<List<DataBinding<UnitData>>>> Resolve(
            PickCommandGroupUnitsRequest request)
        {
            List<DataBinding<UnitData>> picks = request.ValidOptions
                .Take(Math.Max(request.MaxPicks, 0))
                .Select(option => option.Option)
                .ToList();

            CancellableResult<List<DataBinding<UnitData>>> result =
                new Selected<List<DataBinding<UnitData>>>(picks);
            return Task.FromResult(result);
        }
    }
}

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// The bot's answer to the one-screen aura picks (2026-11-13): each row the same way as
    /// <see cref="AiPickCommandGroupUnitsResolver"/> - its full allowance, in army order. Same judgement call, same reasons.
    /// </summary>
    public class AiPickAuraUnitsResolver : IStageResolver<PickAuraUnitsRequest, List<List<DataBinding<UnitData>>>>
    {
        public Task<List<List<DataBinding<UnitData>>>> Resolve(PickAuraUnitsRequest request) =>
            Task.FromResult(request.Rows
                .Select(row => row.Options.Take(Math.Max(row.MaxPicks, 0)).Select(o => o.Option).ToList())
                .ToList());
    }
}
