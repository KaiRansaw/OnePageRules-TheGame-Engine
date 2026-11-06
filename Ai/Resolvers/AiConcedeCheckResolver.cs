using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>#466 - a bot never concedes a campaign mission. It plays every game out.</summary>
    public class AiConcedeCheckResolver : IStageResolver<ConcedeCheckRequest, bool>
    {
        public Task<bool> Resolve(ConcedeCheckRequest request) => Task.FromResult(false);
    }
}
