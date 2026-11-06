using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Pick between <see cref="MinPicks"/> and <see cref="MaxPicks"/> options from a list of labels, in one
    /// prompt with a Confirm. Built for the Mission Cards' custom decks (#462): building an 18-card deck
    /// from 36, discarding any number of cards from a hand, placing cards on the table, shuffling up to two
    /// discards back. Every other list request in the engine picks units or positions; this one picks text.
    ///
    /// <para>
    /// <see cref="Suggested"/> is the engine's own answer - what a bot uses, what EOF on the command line
    /// takes, and what the screen offers with one click. It is always a legal answer.
    /// </para>
    /// </summary>
    public class PickStringsRequest : IStageTaskRequest<List<string>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }
        public string Instructions { get; }
        public IReadOnlyList<string> Options { get; }
        public Dictionary<string, string>? Descriptions { get; }
        public int MinPicks { get; }
        public int MaxPicks { get; }
        public IReadOnlyList<string> Suggested { get; }

        public PickStringsRequest(PlayerID targetPlayerID, string instructions, IReadOnlyList<string> options,
            int minPicks, int maxPicks, IReadOnlyList<string>? suggested = null,
            Dictionary<string, string>? descriptions = null, string? displayName = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, options, minPicks, maxPicks,
                suggested ?? Array.Empty<string>(), descriptions, displayName)
        {
        }

        [JsonConstructor]
        public PickStringsRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<string> options, int minPicks, int maxPicks, IReadOnlyList<string> suggested,
            Dictionary<string, string>? descriptions, string? displayName)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            Options = options;
            MaxPicks = Math.Clamp(maxPicks, 0, options.Count);
            MinPicks = Math.Clamp(minPicks, 0, MaxPicks);
            Descriptions = descriptions;
            TaskName = "Pick from a list";
            DisplayName = displayName ?? TaskName;
            Suggested = Legal(suggested) ? suggested : options.Take(MinPicks).ToList();
        }

        /// <summary>Whether a set of picks is a legal answer: known options, no repeats, within the bounds.</summary>
        public bool Legal(IReadOnlyCollection<string> picks) =>
            picks.Count >= MinPicks && picks.Count <= MaxPicks
            && picks.Distinct().Count() == picks.Count && picks.All(p => Options.Contains(p));

        /// <summary>The answer the engine uses: the picks when legal, otherwise the suggestion.</summary>
        public List<string> Sanitise(IReadOnlyCollection<string>? picks) =>
            picks != null && Legal(picks) ? picks.ToList() : Suggested.ToList();

        public Task<List<string>> Resolve(List<string> resolution) => Task.FromResult(Sanitise(resolution));
    }
}
