using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>Bots don't use the Horde shop: they keep their points.</summary>
    public class AiHordeShopResolver : IStageResolver<HordeShopRequest, HordeShopReply>
    {
        public Task<HordeShopReply> Resolve(HordeShopRequest request) => Task.FromResult(HordeShopReply.Nothing);
    }
}
