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
    /// activation, shooting, melee and terrain, and say nothing about Command Groups; the rule itself is
    /// written for a player picking before the game. So this is a judgement call, recorded in
    /// <c>claude/house-rules.md</c>, and it is deliberately the dullest one available: take the options
    /// in the order the engine offered them, which is army order.
    /// </para>
    ///
    /// <para>
    /// Picking "the biggest unit" or "the one nearest the bearer" would both be defensible and neither is
    /// in any book - and the second is not even answerable here, because the picks happen before anything
    /// is deployed, so no unit has a position yet. Army order is at least stable, reproducible run to run,
    /// and honest about being arbitrary.
    /// </para>
    ///
    /// <para>The bot always takes its full allowance rather than declining: the buff is free and
    /// <see cref="Selected{T}"/> is the only answer that spends it.</para>
    /// </summary>
    public class AiPickCommandGroupUnitResolver
        : IStageResolver<PickCommandGroupUnitRequest, CancellableResult<DataBinding<UnitData>>>
    {
        public Task<CancellableResult<DataBinding<UnitData>>> Resolve(PickCommandGroupUnitRequest request)
        {
            if (request.ValidOptions.Count == 0)
            {
                // The stage does not issue a request with no options, so this is a programming error
                // rather than a game state - but decline rather than throw, because a bot that cannot
                // answer a free buff should not end the game over it.
                CancellableResult<DataBinding<UnitData>> declined = new Cancelled<DataBinding<UnitData>>();
                return Task.FromResult(declined);
            }

            CancellableResult<DataBinding<UnitData>> result =
                new Selected<DataBinding<UnitData>>(request.ValidOptions[0].Option);
            return Task.FromResult(result);
        }
    }
}
