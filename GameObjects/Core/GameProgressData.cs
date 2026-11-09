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

        /// <summary>
        /// #429 - each team's deployment zone, by <see cref="ITeam.TeamNumber"/>, kept for the whole game.
        /// Null in a pre-#429 save, and in a game whose deployment never chose zones.
        /// <para>
        /// The zones used to exist only on the <c>DeploymentContext</c>, which is thrown away the moment
        /// deployment ends - so from the first round onwards <b>nothing in the game could say where
        /// anyone had deployed</b>. That is a gap with at least three customers: the campaign's
        /// Intimidator special objective ("most of your units inside the enemy deployment zone at the end
        /// of the game"), the Solo AI's three sections, which the book draws along the AI's own
        /// deployment edge (see <c>WorkItems/424</c>), and any future mission that scores a zone.
        /// </para>
        /// <para>
        /// Stored as plain numbers rather than a <c>DataBinding&lt;RectangularZone&gt;</c> on purpose: the
        /// zone objects are created for deployment and nothing guarantees their lifetime afterwards,
        /// while four floats per team survive a save, a reload and a networked catch-up with no
        /// references to keep valid.
        /// </para>
        /// </summary>
        public Dictionary<int, ZoneRect>? DeploymentZones { get; set; }

        /// <summary>
        /// #434 - each army's remaining Underdog Bonus points for THIS mission, keyed by
        /// <see cref="Players.PlayerSlotInfo.SlotID"/>. Null outside a campaign game and in every
        /// pre-#434 save.
        ///
        /// <para>
        /// Campaign Rules v3.6.0, identically in all four books: <i>"Any army that has a point value
        /// lower than at least one other gets an underdog bonus. You get 1 underdog point for every
        /// 65pts difference between your army and the one with the highest value. Each underdog point can
        /// be spent to modify all dice in a friendly or enemy roll by +1 or -1, but only one point can be
        /// spent per roll."</i> (65 in Grimdark Future; 25 / 50 / 20 in the other three.)
        /// </para>
        /// <para>
        /// Per MISSION, not per campaign: the pool is recomputed from what each side brings each time, so
        /// it is progress state rather than something on the army sheet. Rides this record for the same
        /// reasons <see cref="Horde"/> and <see cref="DeploymentZones"/> do - it replicates to every
        /// player, it is saved, and <c>GameProgressUtilities.WriteProgress</c> carries it forward when the
        /// per-turn snapshot rewrites the record.
        /// </para>
        /// <para>
        /// Keyed by slot id rather than <c>PlayerID</c> because a Guid-keyed dictionary needs a converter
        /// to survive the save, and the slot id is already how <see cref="DeploymentZones"/>' sibling
        /// state identifies a seat.
        /// </para>
        /// </summary>
        public Dictionary<int, int>? Underdog { get; set; }

        /// <summary>
        /// The Mission Cards (Mission Cards v3.6.0, Kai 2026-11-05): decks, cards on the table, victory
        /// points and the events the cards ask about. Null in a game played without them and in every older
        /// save. Carried forward and pending-hopped by <c>GameProgressUtilities.WriteProgress</c> like
        /// <see cref="Underdog"/>.
        /// </summary>
        public FDG.Missions.MissionCardState? MissionCards { get; set; }

        /// <summary>One entry per finished round, oldest first (the round summary card). Carried forward by
        /// <c>GameProgressUtilities.WriteProgress</c>; null in older saves.</summary>
        public List<RoundSummary>? RoundSummaries { get; set; }

        /// <summary>King of the Hill: each seat's combat bonus points (slot id -> points). Null outside it.</summary>
        public Dictionary<int, int>? CombatBonus { get; set; }

        /// <summary>King of the Hill: the activation each seat last spent a point in ("round:unit"), for the
        /// book's "once per activation".</summary>
        public Dictionary<int, string>? CombatBonusSpentIn { get; set; }

        /// <summary>
        /// Challenge Bonus "activate D3 units": the AI seat partway through its run of activations and how many
        /// it has left after the one in progress. Null when no run is under way, and in every older save.
        /// Saved so a game reloaded mid-run finishes it rather than handing the turn back early.
        /// </summary>
        public ChallengeActivationRun? ChallengeRun { get; set; }

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

    /// <summary>The Challenge Bonus run of D3 activations an AI seat is partway through.</summary>
    public sealed class ChallengeActivationRun
    {
        /// <summary>The AI's seat (slot id).</summary>
        public int Slot { get; set; }

        /// <summary>The round the run was rolled in - a run never carries into the next round.</summary>
        public int Round { get; set; }

        /// <summary>Activations still owed after the current one.</summary>
        public int Left { get; set; }
    }

    /// <summary>
    /// #429 - a deployment zone as four numbers, so it outlives the zone objects deployment built it
    /// from and survives a save, a reload and a networked catch-up without a reference to keep valid.
    /// Matches <see cref="RectangularZone"/>'s own field order.
    /// </summary>
    public sealed class ZoneRect
    {
        public float Left { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public float Top { get; set; }

        public ZoneRect() { }

        public ZoneRect(float left, float right, float bottom, float top)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
        }

        /// <summary>
        /// The zone's real shape when it is not a plain rectangle (2026-11-05, for the mission cards that ask
        /// what stands "within your deployment zone"): each part as a convex polygon, x0, z0, x1, z1, ... An
        /// L-shaped map zone or an Advanced deployment style's triangles would otherwise be judged by their
        /// bounding box. Null for a plain rectangle and in every older save - <see cref="Contains"/> then
        /// uses the four edges, exactly as before.
        /// </summary>
        public List<float[]>? Parts { get; set; }

        public static ZoneRect From(RectangularZone zone)
        {
            var rect = new ZoneRect(zone.Left, zone.Right, zone.Bottom, zone.Top);
            if (zone is PolygonZone poly)
                rect.Parts = poly.Polygons.Select(p => p.ToArray()).ToList();
            else if (zone is MultiRectZone multi)
                rect.Parts = multi.Parts.Select(r => new[] { r.Left, r.Bottom, r.Right, r.Bottom, r.Right, r.Top, r.Left, r.Top }).ToList();
            return rect;
        }

        /// <summary>Whether a point is inside the zone. Edges count as inside, as they do for deployment.</summary>
        public bool Contains(Position point)
        {
            if (!(point.x >= Left && point.x <= Right && point.z >= Bottom && point.z <= Top)) return false;
            if (Parts is not { Count: > 0 }) return true;
            foreach (float[] part in Parts)
                if (part.Length >= 6 && new PolygonZone(new[] { part }).IsPointWithinZone(new Float2(point.x, point.z)))
                    return true;
            return false;
        }

        public ZoneRect Clone() => new(Left, Right, Bottom, Top) { Parts = Parts?.Select(p => p.ToArray()).ToList() };
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
        /// <summary>Every random event rolled this game, in order ("33 - Dazing Spores"), for the campaign
        /// journal's record of the mission. <see cref="Event"/> only holds the one in play.</summary>
        public List<string> EventLog { get; set; } = new();
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
            copy.EventLog = (EventLog ?? new List<string>()).ToList();
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
