using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Asks one player to choose a position for a single objective marker.
    /// Emitted once per marker by <see cref="FDG.Stages.PlaceObjectivesStage"/>;
    /// the stage alternates which player it asks until all markers are placed.
    /// </summary>
    /// <remarks>
    /// Result is the chosen <see cref="Position"/>. The stage validates the
    /// result before committing; if invalid (e.g. stale view of the table)
    /// it re-emits the request to the same player.
    /// </remarks>
    public class PlaceObjectiveRequest : IStageTaskRequest<Position>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>1-based index of this marker among the markers to be placed this game.</summary>
        public int MarkerIndex { get; }

        /// <summary>Total markers that will be placed this game (D3+2).</summary>
        public int TotalMarkers { get; }

        /// <summary>Rectangle on the table inside which the marker must land — typically the band between deployment zones.</summary>
        public RectangularZone LegalBand { get; }

        /// <summary>Minimum center-to-center separation from previously-placed markers.</summary>
        public float MinSeparationInches { get; }

        /// <summary>
        /// Optional true shape of the legal area, inside <see cref="LegalBand"/> - a circle for "within X\" of
        /// this hero" (Star Quest grenades, Fog Cloud, Trickster; QA 3.11/3.12/3.17-3.20). The band stays its
        /// bounding box, clipped to the table, so resolvers that scan a rectangle keep working; a point is
        /// legal only when it is inside both. Null for ordinary objective placement.
        /// </summary>
        public IBoundedZone? Reach { get; }

        /// <summary>Whether <paramref name="p"/> is inside the band and, when there is one, the reach.</summary>
        public bool IsInLegalArea(Position p)
        {
            var at = new Float2(p.x, p.z);
            return LegalBand.IsPointWithinZone(at) && (Reach == null || Reach.IsPointWithinZone(at));
        }

        [JsonConstructor]
        public PlaceObjectiveRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            int markerIndex, int totalMarkers, RectangularZone legalBand, float minSeparationInches,
            IBoundedZone? reach = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            MarkerIndex = markerIndex;
            TotalMarkers = totalMarkers;
            LegalBand = legalBand;
            MinSeparationInches = minSeparationInches;
            Reach = reach;
        }

        public PlaceObjectiveRequest(PlayerID targetPlayerID, string taskName,
            int markerIndex, int totalMarkers, RectangularZone legalBand, float minSeparationInches,
            IBoundedZone? reach = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), taskName,
                   markerIndex, totalMarkers, legalBand, minSeparationInches, reach)
        { }

        public Task<Position> Resolve(Position resolution) => Task.FromResult(resolution);
    }
}
