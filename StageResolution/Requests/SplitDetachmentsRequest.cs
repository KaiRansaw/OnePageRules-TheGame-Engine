using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Fog of War - Escalating Battles: "Before the game begins, players have to divide their army into four detachments
    /// of equal point value (or as close as possible)." The player splits their own army (Kai, 2026-11-23: "Let me split
    /// them"). <see cref="Suggested"/> is the game's even split, one detachment number (1-4) per unit in
    /// <see cref="Units"/>' order; the reply is the same shape. The stage keeps any number outside 1-4 at its suggestion.
    /// </summary>
    public class SplitDetachmentsRequest : IStageTaskRequest<List<int>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName => "Split your army into detachments";
        public string DisplayName => "Splitting the army";
        public string Instructions => "Escalating Battles: split your army into four detachments of about equal points";
        public IReadOnlyList<Entry> Units { get; }
        public IReadOnlyList<int> Suggested { get; }
        public int Detachments { get; }

        public SplitDetachmentsRequest(PlayerID targetPlayerID, IReadOnlyList<Entry> units, IReadOnlyList<int> suggested, int detachments = 4)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), units, suggested, detachments) { }

        [JsonConstructor]
        public SplitDetachmentsRequest(PlayerID targetPlayerID, TaskID taskID, IReadOnlyList<Entry> units, IReadOnlyList<int> suggested, int detachments)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Units = units;
            Suggested = suggested;
            Detachments = detachments;
        }

        public Task<List<int>> Resolve(List<int> resolution) => Task.FromResult(resolution);

        /// <summary>One unit of the army: its binding, name and points.</summary>
        public sealed record Entry(DataBinding<UnitData> Unit, string Name, int Points);

        /// <summary>Takes the suggested split - bots, the text-only game and the frozen Raylib build.</summary>
        public sealed class TakeSuggested : IStageResolver<SplitDetachmentsRequest, List<int>>
        {
            public Task<List<int>> Resolve(SplitDetachmentsRequest request) => Task.FromResult(request.Suggested.ToList());
        }
    }
}
