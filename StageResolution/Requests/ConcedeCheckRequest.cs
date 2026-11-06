using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #466 - "At the beginning of any round players may concede" (the 3.6.0 campaign books). Asked of every
    /// player at the top of each round of a campaign mission. It is not a question on screen: Kai chose a
    /// Concede button (2026-11-06), so a player's machine answers at once with whether they pressed it. That
    /// keeps the choice inside the request flow - it reaches the host over the network exactly as every
    /// other decision does, and it is taken at the book's moment, the start of a round.
    /// A bot never concedes.
    /// </summary>
    public class ConcedeCheckRequest : IStageTaskRequest<bool>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }

        /// <summary>The round about to begin.</summary>
        public int Round { get; }

        public ConcedeCheckRequest(PlayerID targetPlayerID, int round)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), round)
        {
        }

        [JsonConstructor]
        public ConcedeCheckRequest(PlayerID targetPlayerID, TaskID taskID, int round)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Round = round;
            TaskName = "Concede the mission?";
            DisplayName = "Checking for a concession";
        }

        public Task<bool> Resolve(bool resolution) => Task.FromResult(resolution);
    }
}
