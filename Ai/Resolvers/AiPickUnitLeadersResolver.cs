using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #456 - a bot keeps the game's suggested leaders. The suggestion already protects the upgraded model,
    /// which is what a player would pick, and no book tells an AI how to choose.
    /// </summary>
    public class AiPickUnitLeadersResolver
        : IStageResolver<PickUnitLeadersRequest, CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>
    {
        public Task<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>> Resolve(PickUnitLeadersRequest request)
        {
            CancellableResult<List<PickUnitLeadersRequest.LeaderPick>> result =
                new Selected<List<PickUnitLeadersRequest.LeaderPick>>(request.Suggested());
            return Task.FromResult(result);
        }
    }
}
