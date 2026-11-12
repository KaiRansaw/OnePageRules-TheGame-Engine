using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #029 — answers the Aircraft forced move (GF Core 3.6.0): keeps flying the way it faces when the full 30"
    /// fits on the table that way, else turns to the nearest line that fits, and flies the 30" minimum (it never
    /// leaves the table).
    /// </summary>
    public class AiAircraftAdvanceResolver : IStageResolver<AircraftAdvanceRequest, AircraftAdvanceResult>
    {
        public Task<AircraftAdvanceResult> Resolve(AircraftAdvanceRequest request)
        {
            Float2 line = ForcedAircraftMove.LegalHeading(request.UnitDataBinding, request.HeadingNormal);
            float room = ForcedAircraftMove.RoomAlong(request.UnitDataBinding, line);
            return Task.FromResult(new AircraftAdvanceResult(Math.Min(request.MinDistanceInches, room), false, line));
        }
    }
}
