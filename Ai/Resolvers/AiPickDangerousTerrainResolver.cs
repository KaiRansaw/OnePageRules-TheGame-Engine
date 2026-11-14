using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// The bot's pick in the Generic terrain generator's Dangerous terrain step: the piece nearest the middle of
    /// the table, where both armies will have to cross (house rule - the book says nothing about how a bot picks).
    /// Deterministic, so a seeded game replays.
    /// </summary>
    public class AiPickDangerousTerrainResolver : IStageResolver<PickDangerousTerrainRequest, int>
    {
        public Task<int> Resolve(PickDangerousTerrainRequest request) => Task.FromResult(Pick(request));

        public static int Pick(PickDangerousTerrainRequest request)
        {
            float cx = request.TableWidthInches / 2f, cz = request.TableHeightInches / 2f;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < request.Options.Count; i++)
            {
                PickDangerousTerrainRequest.TerrainOption o = request.Options[i];
                float d = (o.X - cx) * (o.X - cx) + (o.Z - cz) * (o.Z - cz);
                if (d < bestDistance - 0.0001f) { bestDistance = d; best = i; }
            }
            return best;
        }
    }
}
