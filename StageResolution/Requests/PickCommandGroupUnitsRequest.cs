using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #439 - the friendly units a Banner or Musician confers on, picked before the game starts.
    ///
    /// <para>
    /// In GF: Firefight, AoF: Skirmish, GF: Star Quest and AoF: Quest these upgrades read
    /// <i>"This model's unit <b>and up to 2 friendly units picked before the start of the game</b>
    /// get ..."</i>, where Grimdark Future and Age of Fantasy give them to the bearer's own unit and
    /// nobody else.
    /// </para>
    ///
    /// <para>
    /// <b>One request for all the picks, not one per pick</b> (Kai, 2026-11-01: <i>"One print per banner,
    /// choose both and click confirm"</i>). The first version asked once per pick, which meant two
    /// prompts in a row per banner; this collects up to <see cref="MaxPicks"/> units and commits them
    /// together. The reply is the chosen units, so an empty list and a cancel both mean "none" - the
    /// books allow declining, since it is "UP TO 2".
    /// </para>
    ///
    /// <para>
    /// Its own request type with its own resolvers, because nothing else in the engine asks for a plain
    /// multi-select of units: the three list-returning requests that exist
    /// (<see cref="PlaceObjectsRequest{T}"/>, <see cref="ConsolidationMoveRequest"/>,
    /// <see cref="DefineMovementPathRequest"/>) are all about positions rather than a choice from a list.
    /// </para>
    /// </summary>
    public class PickCommandGroupUnitsRequest
        : IStageTaskRequest<CancellableResult<List<DataBinding<UnitData>>>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }
        public string Instructions { get; }
        public IReadOnlyList<ValidOption> ValidOptions { get; }

        /// <summary>The unit carrying the Banner or Musician. For the prompt; not used to resolve.</summary>
        public string BearerUnitName { get; }

        /// <summary>The rule being conferred - "Banner" or "Musician".</summary>
        public string RuleName { get; }

        /// <summary>How many units may be picked at most. Fewer, including none, is a legal answer.</summary>
        public int MaxPicks { get; }

        public PickCommandGroupUnitsRequest(PlayerID targetPlayerID, string bearerUnitName, string ruleName,
            int maxPicks, IReadOnlyList<ValidOption> validOptions)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()),
                // Game-facing, so ASCII only (CLAUDE.md). Phrased "Banner on X" rather than "X's Banner"
                // because unit names are usually plural and that produced "Warriors's Banner".
                $"{ruleName} on {bearerUnitName}: pick up to {maxPicks} friendly units to gain it",
                validOptions, $"Picking units for {ruleName}", bearerUnitName, ruleName, maxPicks)
        {
        }

        [JsonConstructor]
        public PickCommandGroupUnitsRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<ValidOption> validOptions, string? displayName, string bearerUnitName,
            string ruleName, int maxPicks)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            BearerUnitName = bearerUnitName;
            RuleName = ruleName;
            MaxPicks = maxPicks;
            TaskName = $"Pick units for {ruleName}";
            DisplayName = displayName ?? TaskName;
        }

        public Task<CancellableResult<List<DataBinding<UnitData>>>> Resolve(
            CancellableResult<List<DataBinding<UnitData>>> resolution) => Task.FromResult(resolution);

        public record ValidOption(DataBinding<UnitData> Option, string Name);
    }
}
