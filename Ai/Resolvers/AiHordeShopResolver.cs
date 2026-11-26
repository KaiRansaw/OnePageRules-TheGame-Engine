using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>Bots don't use the Horde shop: they keep their points. Meat Grinder recruitment is the exception -
    /// the points are the whole game there - so they buy units from their own book (<see cref="ArmyBuilding.RecruitPicker"/>).</summary>
    public class AiHordeShopResolver : IStageResolver<HordeShopRequest, HordeShopReply>
    {
        public Task<HordeShopReply> Resolve(HordeShopRequest request) => Task.FromResult(ArmyBuilding.RecruitPicker.BotReply(request));
    }
}
