using FDG.Data;
using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// The "where are we in the game flow" state, promoted into the <see cref="GameDataStore"/> so
    /// that it serializes for save/load and replicates to clients for networked play, exactly like
    /// the rest of the game world. It mirrors the transient round/turn state that otherwise lives
    /// only in <see cref="FDG.Stages.MainPhaseContext"/> and
    /// <see cref="FDG.Stages.SingleRoundContext"/> (which are recreated each round and never
    /// persisted).
    /// <para>
    /// This is the raw persisted record; the player-facing read model over it (round, scores, the
    /// activating unit) is <see cref="IGameProgress"/>, exposed via <see cref="ITableState.Progress"/>.
    /// </para>
    /// <para>
    /// Teams are referenced by <see cref="ITeam.TeamNumber"/> — a stable, serializable identifier —
    /// rather than by <see cref="ITeam"/> object reference. Units are referenced by
    /// <see cref="DataBinding{T}"/>, which serializes to a bare <see cref="DataReference"/>.
    /// </para>
    /// <para>There is exactly one of these in a live store once the main phase has begun.</para>
    /// </summary>
    public class GameProgressData
    {
        /// <summary>Which top-level stage the game is in, so a load can re-enter the right place.</summary>
        public EResumeStage Stage;

        /// <summary>1-based round number (mirrors <see cref="FDG.Stages.IMainPhaseContext.RoundCount"/>).</summary>
        public int RoundCount { get; set; }

        /// <summary>
        /// The unit currently taking its activation, or null between activations (and outside the main
        /// phase). Set at the start of a unit's activation and cleared at its end, so the read model can
        /// spotlight it. Serializes to a bare <see cref="DataReference"/> like the other unit references.
        /// </summary>
        public DataBinding<UnitData>? ActivatingUnit { get; set; }

        /// <summary>
        /// This round's team activation order, by <see cref="ITeam.TeamNumber"/>. Equal to the
        /// activation cursor's team order.
        /// </summary>
        public List<int> TeamActivateOrder;

        /// <summary>
        /// Teams (by <see cref="ITeam.TeamNumber"/>) that have finished all activations this round,
        /// in the order they finished. Seeds the next round's activation order.
        /// </summary>
        public List<int> CurrentRoundTeamFinishOrder;

        /// <summary>The activation cursor's current index into <see cref="TeamActivateOrder"/>.</summary>
        public int CurrentTeamIndex;

        /// <summary>Per-team round-robin player index, keyed by <see cref="ITeam.TeamNumber"/>.</summary>
        public Dictionary<int, int> CurrentPlayerIndexPerTeam;

        /// <summary>
        /// Units (flattened across all players) that have NOT yet activated this round. On resume the
        /// per-player grouping is rebuilt from each unit's <see cref="UnitData.PlayerID"/>.
        /// </summary>
        public List<DataBinding<UnitData>> UnactivatedUnits;

        /// <summary>The game's configured settings, needed to faithfully resume the same game.</summary>
        public GameSettings Settings;

        /// <summary>
        /// Horde Mode's points (null in a standard game, and in every pre-Horde save). Rides this record so
        /// it replicates to every player and is saved; <see cref="GameProgressUtilities.WriteProgress"/>
        /// carries it forward when the per-turn snapshot rewrites the record.
        /// </summary>
        public HordeScore? Horde;

        /// <summary>Star Quest Horde's wave / alertness / XP state (null in every other game). Carried forward
        /// by <see cref="GameProgressUtilities.WriteProgress"/> like <see cref="Horde"/>.</summary>
        public QuestProgress? Quest;

        [JsonConstructor]
        public GameProgressData(
            EResumeStage stage,
            int roundCount,
            List<int> teamActivateOrder,
            List<int> currentRoundTeamFinishOrder,
            int currentTeamIndex,
            Dictionary<int, int> currentPlayerIndexPerTeam,
            List<DataBinding<UnitData>> unactivatedUnits,
            GameSettings settings,
            DataBinding<UnitData>? activatingUnit = null,
            HordeScore? horde = null)
        {
            Horde = horde;
            Stage = stage;
            RoundCount = roundCount;
            TeamActivateOrder = teamActivateOrder;
            CurrentRoundTeamFinishOrder = currentRoundTeamFinishOrder;
            CurrentTeamIndex = currentTeamIndex;
            CurrentPlayerIndexPerTeam = currentPlayerIndexPerTeam;
            UnactivatedUnits = unactivatedUnits;
            Settings = settings;
            ActivatingUnit = activatingUnit;
        }
    }

    /// <summary>
    /// Horde Mode points for the defending side. <see cref="PointsEarned"/> is everything earned in the
    /// mission (what the high score multiplies); <see cref="PointsAvailable"/> is what is left to spend
    /// on upgrades and recruits - the same until the between-wave shop exists.
    /// </summary>
    public sealed class QuestProgress
    {
        /// <summary>The wave being fought (1-10).</summary>
        public int Wave { get; set; } = 1;
        /// <summary>0-6; at 6 the wave ends.</summary>
        public int Alertness { get; set; }
        /// <summary>The last wave the heroes finished.</summary>
        public int WavesFinished { get; set; }
        /// <summary>All XP earned by the party this mission (for the high score).</summary>
        public int PartyXp { get; set; }
        /// <summary>The next round starts a new wave (priority + deployment).</summary>
        public bool WavePending { get; set; }
        /// <summary>The mission ended because the heroes finished wave 10.</summary>
        public bool Won { get; set; }

        /// <summary>Search tokens and treasure markers on the table.</summary>
        public List<QuestToken> Tokens { get; set; } = new();
        /// <summary>Numbers the tokens ("Search 3"), so they can be told apart.</summary>
        public int NextTokenId { get; set; } = 1;

        // ── Star Quest Mission (Campaign Builder) ──
        /// <summary>1 Beginner - 4 Legendary. 0 = not a mission (Horde).</summary>
        public int Difficulty { get; set; }
        /// <summary>Primary / secondary objective rolled (1-6); 0 = none yet.</summary>
        public int Primary { get; set; }
        public int Secondary { get; set; }
        public bool PrimaryDone { get; set; }
        public bool SecondaryDone { get; set; }
        /// <summary>The corner the heroes deployed in (1-4).</summary>
        public int HeroCorner { get; set; }
        /// <summary>Reinforcement waves deployed so far.</summary>
        public int Reinforcements { get; set; }
        /// <summary>Clean Sweep / Interrogate: the units of the watched wave ("first" / "next" reinforcements).</summary>
        public List<string> WatchedWave { get; set; } = new();
        /// <summary>Clean Sweep / Interrogate: 1 = primary, 2 = secondary waiting for its wave to be wiped out.</summary>
        public int WatchingFor { get; set; }
        /// <summary>Objective, delivery and extraction markers.</summary>
        public List<QuestMarker> Markers { get; set; } = new();
        public int NextMarkerId { get; set; } = 1;
        // ── Optional challenges ──
        /// <summary>The challenges picked for this game (names from QuestChallenges); empty = none.</summary>
        public List<string> Challenges { get; set; } = new();
        /// <summary>Increased Alertness: "Full Assault", "Alarmed" or "Vigilant" ("" = not in play).</summary>
        public string AlertLevel { get; set; } = "";
        /// <summary>Time of Day: "Dusk", "Night", "Dawn", "Day" ("" = not in play).</summary>
        public string TimeOfDay { get; set; } = "";
        /// <summary>Weather: "Worsening", "Bad", "Improving", "Good" ("" = not in play).</summary>
        public string Weather { get; set; } = "";
        /// <summary>Random event in play until the next one (e.g. "33 - Dazing Spores"), "" = none.</summary>
        public string Event { get; set; } = "";
        /// <summary>Boss fight: 0 none, 1 triggered (wave pending/on table), 2 boss wave destroyed.</summary>
        public int BossFight { get; set; }
        public List<string> BossWave { get; set; } = new();
        /// <summary>Random event: the hero it targets (unit guid) and the table quarter (1-4) for terrain events.</summary>
        public string EventHero { get; set; } = "";
        public int EventQuarter { get; set; }
        /// <summary>Mission: the first-round random event has happened.</summary>
        public bool FirstEventDone { get; set; }
        /// <summary>Horde Hero Injuries: "unit-guid|injury|wave" - lasts until the end of the next wave.</summary>
        public List<string> WaveInjuries { get; set; } = new();
        /// <summary>Frenzy Injector: "hero-guid:enemies-destroyed-when-used"; Adrenaline Injector: hero guids. This round only.</summary>
        public List<string> Frenzy { get; set; } = new();
        public List<string> Adrenaline { get; set; } = new();
        /// <summary>Fiendish Respite: "hero-guid:enemies-destroyed-when-used" (this round).</summary>
        public List<string> Respite { get; set; } = new();
        /// <summary>Rounds finished so far (counted by the Star Quest round hooks).</summary>
        public int RoundsDone { get; set; }
        /// <summary>AI Ambush units waiting to arrive: "unit-guid:round" (arrive at the start of the round after it).</summary>
        public List<string> Ambushers { get; set; } = new();
        /// <summary>Star Quest skills in play this round: "unit-guid|Flag|value" (Reckless, Vengeful, Dread, Martial,
        /// Infiltrating, SpellRange, Pact, Enchant, Tides, and the Dread / Martial targets). Cleared at the end of each round.</summary>
        public List<string> Flags { get; set; } = new();

        /// <summary>"success" / "failed" once the mission is over.</summary>
        public string Outcome { get; set; } = "";

        public QuestProgress Clone()
        {
            var copy = (QuestProgress)MemberwiseClone();
            copy.Tokens = Tokens.Select(t => t.Clone()).ToList();
            copy.Markers = Markers.Select(m => m.Clone()).ToList();
            copy.WatchedWave = WatchedWave.ToList();
            copy.Challenges = Challenges.ToList();
            copy.BossWave = BossWave.ToList();
            copy.Ambushers = Ambushers.ToList();
            copy.Flags = Flags.ToList();
            copy.WaveInjuries = WaveInjuries.ToList();
            copy.Frenzy = Frenzy.ToList();
            copy.Adrenaline = Adrenaline.ToList();
            copy.Respite = Respite.ToList();
            return copy;
        }
    }

    /// <summary>
    /// Star Quest mission marker. <see cref="Kind"/>: "objective", "delivery" or "extraction". An objective
    /// marker is worked with a skill action and a <see cref="Stat"/> test (0 Str, 1 Dex, 2 Wil); what passing does
    /// is <see cref="Action"/>: "remove", "seize" (then collects a token each round it is held, up to
    /// <see cref="TokensNeeded"/>) or "carry" (a hero picks it up: <see cref="CarriedBy"/>).
    /// </summary>
    public sealed class QuestMarker
    {
        public int Id { get; set; }
        public string Kind { get; set; } = "objective";
        public float X { get; set; }
        public float Z { get; set; }
        public bool ForSecondary { get; set; }
        public int Stat { get; set; } = -1;
        public string Action { get; set; } = "remove";
        public bool Activated { get; set; }
        public int Tokens { get; set; }
        public int TokensNeeded { get; set; }
        /// <summary>Area Search / Scavenge: after removing a marker, this roll or higher completes the objective.</summary>
        public int CompleteOn { get; set; }
        /// <summary>The carrying hero's unit id (Guid string), or "".</summary>
        public string CarriedBy { get; set; } = "";
        /// <summary>Item markers (noise, grease, smoke, fire, phantasm, silence, poison): rounds left (0 = no limit).</summary>
        public int RoundsLeft { get; set; }
        /// <summary>Silence / poison markers sit on a unit (unit guid), or "".</summary>
        public string OnUnit { get; set; } = "";

        public QuestMarker Clone() => (QuestMarker)MemberwiseClone();
    }

    /// <summary>
    /// Star Quest: a search token (contents unknown until revealed) or a treasure marker (revealed, waiting to
    /// be picked up). <see cref="Contents"/> holds item names and credits ("10c").
    /// </summary>
    public sealed class QuestToken
    {
        public int Id { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
        /// <summary>False = search token; true = treasure marker.</summary>
        public bool IsTreasure { get; set; }
        public List<string> Contents { get; set; } = new();

        [JsonIgnore]
        public string Label => IsTreasure ? $"Treasure {Id} ({string.Join(" + ", Contents)})" : $"Search token {Id}";

        public QuestToken Clone() => new() { Id = Id, X = X, Z = Z, IsTreasure = IsTreasure, Contents = Contents.ToList() };
    }

    public sealed class HordeScore
    {
        public int PointsEarned { get; }
        public int PointsAvailable { get; }

        [JsonConstructor]
        public HordeScore(int pointsEarned, int pointsAvailable)
        {
            PointsEarned = pointsEarned;
            PointsAvailable = pointsAvailable;
        }

        public HordeScore WithEarned(int points) => new HordeScore(PointsEarned + points, PointsAvailable + points);

        /// <summary>Points spent in the Horde shop: leaves the earned total (and so the high score) alone.</summary>
        public HordeScore WithSpent(int points) => new HordeScore(PointsEarned, PointsAvailable - points);
    }

    /// <summary>
    /// The top-level stage a saved game was in, so a load can re-enter the state machine at the
    /// right place rather than restarting from map setup.
    /// </summary>
    public enum EResumeStage
    {
        MapSetup,
        Deployment,
        MainPhase,
        VictoryCalculation,
    }
}
