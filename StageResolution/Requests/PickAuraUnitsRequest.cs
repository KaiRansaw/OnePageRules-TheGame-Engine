using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Every Banner, Musician and aura a player's army can hand to other friendly units, asked in ONE request
    /// (Kai, 2026-11-13: "one screen, all at once"). Only in the four games whose books print "This model's unit and up
    /// to 2 friendly units picked before the start of the game get X" - see <c>AuraConferral</c>.
    ///
    /// <para>The reply is one list per row of <see cref="Rows"/>, in the same order: the units picked for that row.
    /// Empty (or a missing row) means none - the books say "UP TO 2". The stage re-checks every pick
    /// (<c>PickCommandGroupUnitsStage.AcceptablePicks</c>), so a front end can't hand out more than the book allows.</para>
    /// </summary>
    public class PickAuraUnitsRequest : IStageTaskRequest<List<List<DataBinding<UnitData>>>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName => "Pick units for auras";
        public string DisplayName => "Picking units for auras";
        public string Instructions => "Before the game: pick up to 2 friendly units for each of these to cover too";
        public IReadOnlyList<Row> Rows { get; }

        public PickAuraUnitsRequest(PlayerID targetPlayerID, IReadOnlyList<Row> rows)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), rows) { }

        [JsonConstructor]
        public PickAuraUnitsRequest(PlayerID targetPlayerID, TaskID taskID, IReadOnlyList<Row> rows)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Rows = rows;
        }

        public Task<List<List<DataBinding<UnitData>>>> Resolve(List<List<DataBinding<UnitData>>> resolution) =>
            Task.FromResult(resolution);

        /// <summary>One thing to hand out: <paramref name="Source"/> on <paramref name="BearerName"/> ("Furious Aura on
        /// Warlord"), the rule picked units receive (<paramref name="Granted"/>), and who may receive it.</summary>
        public sealed record Row(DataBinding<UnitData> Bearer, string BearerName, string Source, string Granted,
            int MaxPicks, IReadOnlyList<PickCommandGroupUnitsRequest.ValidOption> Options);

        /// <summary>
        /// Answers the combined request one row at a time through a per-row resolver - for the front ends that
        /// already ask Banner/Musician one carrier at a time (the CLI, the frozen Raylib build). Godot shows the whole
        /// request as one screen instead.
        /// </summary>
        public sealed class PerRowAdapter : IStageResolver<PickAuraUnitsRequest, List<List<DataBinding<UnitData>>>>
        {
            private readonly IStageResolver<PickCommandGroupUnitsRequest, CancellableResult<List<DataBinding<UnitData>>>> _inner;

            public PerRowAdapter(IStageResolver<PickCommandGroupUnitsRequest, CancellableResult<List<DataBinding<UnitData>>>> inner) =>
                _inner = inner;

            public async Task<List<List<DataBinding<UnitData>>>> Resolve(PickAuraUnitsRequest request)
            {
                var replies = new List<List<DataBinding<UnitData>>>();
                foreach (Row row in request.Rows)
                {
                    CancellableResult<List<DataBinding<UnitData>>> one = await _inner.Resolve(new PickCommandGroupUnitsRequest(
                        request.TargetPlayerID, row.BearerName, row.Source, row.MaxPicks, row.Options));
                    replies.Add(one is Selected<List<DataBinding<UnitData>>> s ? s.Value : new List<DataBinding<UnitData>>());
                }
                return replies;
            }
        }
    }
}
