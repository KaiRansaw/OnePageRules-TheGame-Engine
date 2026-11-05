using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #456 - a player's unit leaders for the Fast-Play rules, confirmed before the game.
    ///
    /// <para>
    /// The books: "Before the game begins, you must clearly define a single model within each unit to be its
    /// leader." Kai, 2026-11-05: the game picks them and he can change any - so this request carries every
    /// unit that has a real choice, each with the game's suggestion, and the reply is the leaders as the
    /// player left them. One request per player for the whole army, not one per unit: with a 4000-pt army
    /// that would be dozens of prompts, which is the opposite of what Fast-Play is for.
    /// </para>
    /// </summary>
    public class PickUnitLeadersRequest
        : IStageTaskRequest<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }
        public string Instructions { get; }

        /// <summary>Every unit of this player's with more than one possible leader, in army order.</summary>
        public IReadOnlyList<UnitEntry> Units { get; }

        public PickUnitLeadersRequest(PlayerID targetPlayerID, IReadOnlyList<UnitEntry> units)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()),
                // Game-facing, so ASCII only (CLAUDE.md).
                "Fast-Play: each unit is measured from its leader, and the leader is the last of its models to "
                + "die. The game has suggested one for every unit - change any you like, then confirm.",
                units, "Choose unit leaders")
        {
        }

        [JsonConstructor]
        public PickUnitLeadersRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<UnitEntry> units, string? displayName)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            Units = units;
            TaskName = "Choose unit leaders";
            DisplayName = displayName ?? TaskName;
        }

        /// <summary>The game's own suggestions, unchanged - what a bot, the CLI's EOF, or "Confirm" without
        /// touching anything answers with.</summary>
        public List<LeaderPick> Suggested() =>
            Units.Select(entry => new LeaderPick(entry.Unit, entry.Suggested)).ToList();

        public Task<CancellableResult<List<LeaderPick>>> Resolve(CancellableResult<List<LeaderPick>> resolution) =>
            Task.FromResult(resolution);

        /// <param name="Unit">The unit.</param>
        /// <param name="UnitName">Its name, for the prompt.</param>
        /// <param name="Suggested">The leader the game picked (<see cref="FastPlay.AutoPick"/>).</param>
        /// <param name="Models">Every model that may lead it, described ("Model 3: Plasma Rifle").</param>
        public record UnitEntry(DataBinding<UnitData> Unit, string UnitName, ModelID Suggested,
            IReadOnlyList<ModelOption> Models);

        public record ModelOption(ModelID Model, string Description);

        public record LeaderPick(DataBinding<UnitData> Unit, ModelID Model);
    }
}
