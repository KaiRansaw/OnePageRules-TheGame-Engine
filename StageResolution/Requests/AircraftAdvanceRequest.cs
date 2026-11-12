using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #029 — the Aircraft forced-move decision: which straight line to fly and how far (30-36"). GF Core 3.6.0:
    /// "moving in a straight line ... Aircraft can't move into the table edges to move less than 30"" - so any
    /// direction is allowed as long as the full 30" fits on the table (<see cref="ForcedAircraftMove.IsLegalHeading"/>),
    /// and the Aircraft never leaves the table. <see cref="HeadingNormal"/> is the way it faces now, the suggestion.
    /// </summary>
    public class AircraftAdvanceRequest : IStageTaskRequest<AircraftAdvanceResult>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitDataBinding { get; }

        /// <summary>The way the unit faces now — a unit vector in the table's X/Z plane. The suggested line; the reply may pick another.</summary>
        public Float2 HeadingNormal { get; }

        public float MinDistanceInches { get; }

        public float MaxDistanceInches { get; }

        [JsonConstructor]
        public AircraftAdvanceRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, Float2 headingNormal,
            float minDistanceInches, float maxDistanceInches)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            HeadingNormal = headingNormal;
            MinDistanceInches = minDistanceInches;
            MaxDistanceInches = maxDistanceInches;
        }

        public AircraftAdvanceRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitDataBinding, Float2 headingNormal,
            float minDistanceInches, float maxDistanceInches)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), taskName, unitDataBinding, headingNormal,
                  minDistanceInches, maxDistanceInches)
        { }

        public Task<AircraftAdvanceResult> Resolve(AircraftAdvanceResult resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    /// <summary>
    /// The chosen distance and straight line. <paramref name="Heading"/> null keeps the line the Aircraft faces.
    /// A line the book doesn't allow (runs into an edge before 30") is turned to the nearest allowed one, and the
    /// distance is cut to what fits on the table. <paramref name="FliesOffTable"/> is retired (3.6.0 has no flying
    /// off the table) and ignored; it stays only so old saves and resolvers still load.
    /// </summary>
    public record AircraftAdvanceResult(float DistanceInches, bool FliesOffTable = false, Float2? Heading = null);
}
