using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>A bot takes the engine's suggested picks (#462). Deterministic, so benchmarks stay reproducible.</summary>
    public class AiPickStringsResolver : IStageResolver<PickStringsRequest, List<string>>
    {
        public Task<List<string>> Resolve(PickStringsRequest request) => Task.FromResult(request.Suggested.ToList());
    }
}
