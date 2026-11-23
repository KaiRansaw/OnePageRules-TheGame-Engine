using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.Stages;

namespace FDG.Missions
{
    /// <summary>
    /// Advanced Missions - Extra Missions (GF / AoF Advanced Rulebooks 3.6.0 p24-26, the same in GF: Firefight and AoF:
    /// Skirmish). Kai, 2026-11-22: "Extra Missions + Solid Buildings". <see cref="None"/> is the core rules' mission,
    /// which the book lists as Face-Off 1 "Duel".
    /// </summary>
    public enum EExtraMission
    {
        None = 0,
        // Face-Off
        SeizeGround,
        RelicHunt,
        Sabotage,
        Breakthrough,
        KingOfTheHill,
        // Progressive Scoring
        PitchedBattle,
        Domination,
        CaptureAndHold,
        Demolition,
        Headquarters,
        MoshPit,
        /// <summary>Rolled before the game, as the book's random mission selection (Attack &amp; Defend re-rolled until
        /// those are in the game).</summary>
        Random = 100,
    }

    public enum EMissionMarkers { D3Plus2, FourQuarters, Three, OneEachOnZoneEdge, TwoEachInZone, OneInTheMiddle }

    public sealed record ExtraMissionInfo(EExtraMission Mission, string Name, bool Progressive, EMissionMarkers Markers,
        bool Carried, bool Owned, string BookText);

    /// <summary>The missions, with the book's own words (p24-25).</summary>
    public static class ExtraMissionCatalog
    {
        private const string Faceoff = "After 4 rounds have been played the game ends, and the player that controls most markers wins.";
        private const string Progress = "At the end of each round players get X VP for each objective marker they control, where X is the current round number.\n"
            + "After 4 rounds have been played the game ends, and the player that scored most VPs wins.";
        private const string Quarters = "Divide the non-deployment zone area of the table into 4 equal quarters, and place one marker at the center of each.";
        private const string Carry = "If a unit seizes a marker, remove it from the table, and it counts as being carried by the unit. If the unit is Shaken or destroyed at any point, the marker is dropped within 1\" (placed by the opponent).";
        private const string OwnEdge = "The players must set up 1 objective marker each on the edge of their deployment zone, over 9\" away from the table edge.\n"
            + "Each objective marker belongs to the player that placed it, and if at any point a unit seizes the enemy objective marker, then the marker is destroyed and removed from play.";
        private const string TwoEach = "The players must set up 2 objective markers each.\nThe objectives must be placed within their deployment zone, over 9\" away from the deployment zone's edge and over 9\" away from their other objective.";
        private const string Middle = "The players must set up only 1 objective marker.\nThe objective marker must be placed over 9\" away from the deployment zones and the table edges.";

        public static readonly IReadOnlyList<ExtraMissionInfo> All = new[]
        {
            new ExtraMissionInfo(EExtraMission.None, "Duel (core)", false, EMissionMarkers.D3Plus2, false, false,
                "The players must set up a total of D3+2 objective markers.\n" + Faceoff + "\nNote that this is the standard mission from the core rules."),
            new ExtraMissionInfo(EExtraMission.SeizeGround, "Seize Ground", false, EMissionMarkers.FourQuarters, false, false,
                "The players must set up a total of 4 objective markers.\n" + Quarters + "\n" + Faceoff),
            new ExtraMissionInfo(EExtraMission.RelicHunt, "Relic Hunt", false, EMissionMarkers.Three, true, false,
                "The players must set up a total of 3 objective markers.\n" + Carry + "\n" + Faceoff),
            new ExtraMissionInfo(EExtraMission.Sabotage, "Sabotage", false, EMissionMarkers.OneEachOnZoneEdge, false, true,
                OwnEdge + "\nAfter 4 rounds have been played the game ends, and the player that managed to destroy the enemy marker whilst keeping their own marker intact wins."),
            new ExtraMissionInfo(EExtraMission.Breakthrough, "Breakthrough", false, EMissionMarkers.TwoEachInZone, false, false,
                TwoEach + "\n" + Faceoff),
            new ExtraMissionInfo(EExtraMission.KingOfTheHill, "King of the Hill", false, EMissionMarkers.OneInTheMiddle, false, false,
                Middle + "\nAfter 4 rounds have been played the game ends, and the player that controls the marker wins."),
            new ExtraMissionInfo(EExtraMission.PitchedBattle, "Pitched Battle", true, EMissionMarkers.D3Plus2, false, false,
                "The players must set up a total of D3+2 objective markers.\n" + Progress),
            new ExtraMissionInfo(EExtraMission.Domination, "Domination", true, EMissionMarkers.FourQuarters, false, false,
                "The players must set up a total of 4 objective markers.\n" + Quarters + "\n" + Progress),
            new ExtraMissionInfo(EExtraMission.CaptureAndHold, "Capture & Hold", true, EMissionMarkers.Three, true, false,
                "The players must set up a total of 3 objective markers.\n" + Carry + "\n" + Progress),
            new ExtraMissionInfo(EExtraMission.Demolition, "Demolition", true, EMissionMarkers.OneEachOnZoneEdge, false, true,
                OwnEdge + "\nAt the end of each round players get X VP if their objective has not been destroyed, and X VP on the round in which they destroy the enemy objective, where X is the current round number.\n"
                + "After 4 rounds have been played the game ends, and the player that scored most VPs wins."),
            new ExtraMissionInfo(EExtraMission.Headquarters, "Headquarters", true, EMissionMarkers.TwoEachInZone, false, false,
                TwoEach + "\n" + Progress),
            new ExtraMissionInfo(EExtraMission.MoshPit, "Mosh Pit", true, EMissionMarkers.OneInTheMiddle, false, false,
                Middle + "\nAt the end of each round players get X VP if they control the objective marker, plus X VP if they were the first to seize it, where X is the current round number.\n"
                + "After 4 rounds have been played the game ends, and the player that scored most VPs wins."),
        };

        public static ExtraMissionInfo Of(EExtraMission m) => All.FirstOrDefault(i => i.Mission == m) ?? All[0];

        public static string Name(EExtraMission m) => m == EExtraMission.Random ? "Random" : Of(m).Name;

        public static string Text(EExtraMission m) => m == EExtraMission.Random
            ? "Roll for a random mission (the book: 1-2 Face-Off, 3-4 Progressive Scoring, 5-6 Attack & Defend - then one die for the mission). Attack & Defend is not in the game yet, so a 5-6 is re-rolled."
            : Of(m).BookText;

        /// <summary>The order the lobby lists them in.</summary>
        public static IReadOnlyList<EExtraMission> LobbyOrder { get; } =
            All.Select(i => i.Mission).Append(EExtraMission.Random).ToList();

        /// <summary>The Advanced Rulebooks have them: GF, GF: Firefight, AoF, AoF: Skirmish (not the Quest games).</summary>
        public static bool Available(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.GFFirefight or EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;

        /// <summary>The book's random selection, re-rolling Attack &amp; Defend (5-6) until it is in the game.</summary>
        public static EExtraMission Roll(Func<int> d6)
        {
            while (true)
            {
                int type = d6();
                if (type >= 5) continue;
                int pick = d6();
                bool progressive = type >= 3;
                // Face-Off: 1 Duel (the core mission), 2-6 Seize Ground..King of the Hill. Progressive: 1-6 Pitched
                // Battle..Mosh Pit.
                if (progressive) return (EExtraMission)((int)EExtraMission.PitchedBattle + pick - 1);
                return pick == 1 ? EExtraMission.None : (EExtraMission)((int)EExtraMission.SeizeGround + pick - 2);
            }
        }
    }

    /// <summary>A marker a unit is carrying (Relic Hunt, Capture &amp; Hold).</summary>
    public sealed class CarriedMarker
    {
        public string Unit { get; set; } = "";
        public int Slot { get; set; }
    }

    /// <summary>An objective that belongs to a side (Sabotage, Demolition), found again by where it stands.</summary>
    public sealed class OwnedMarker
    {
        public float X { get; set; }
        public float Z { get; set; }
        public int Slot { get; set; }
        public bool Destroyed { get; set; }
        public int DestroyedInRound { get; set; }
        public int DestroyedBySlot { get; set; } = -1;
    }

    /// <summary>The mission's running state, saved with the game (<see cref="GameProgressData.ExtraMission"/>).</summary>
    public sealed class ExtraMissionState
    {
        public EExtraMission Mission { get; set; }
        public Dictionary<int, int> Vp { get; set; } = new();
        public List<CarriedMarker> Carried { get; set; } = new();
        public List<OwnedMarker> Owned { get; set; } = new();
        /// <summary>Mosh Pit: the seat that seized the marker first (-1 = nobody yet).</summary>
        public int FirstSeizer { get; set; } = -1;
        public bool OwnedPlaced { get; set; }
    }

    /// <summary>The rules that make the Extra Missions play.</summary>
    public static class ExtraMissions
    {
        /// <summary>The mission in play: picked in the lobby, a standard game of a system whose Advanced book has it,
        /// without Mission Cards (they bring their own objectives and scoring).</summary>
        public static EExtraMission Of(GameSettings s) =>
            s.ExtraMission != EExtraMission.None && s.GameMode == EGameMode.Standard && ExtraMissionCatalog.Available(s.GameSystem)
            && !MissionCardGame.IsOn(s)
                ? s.ExtraMission : EExtraMission.None;

        /// <summary>The mission actually being played (a Random pick resolved), from the saved state when there is one.</summary>
        public static EExtraMission Current(IGameContext c)
        {
            EExtraMission picked = Of(c.Settings);
            if (picked == EExtraMission.None) return EExtraMission.None;
            if (GameProgressUtilities.ExtraMission(c.GameDataStore) is { } state) return state.Mission;
            return picked == EExtraMission.Random ? EExtraMission.None : picked;
        }

        public static bool IsOn(IGameContext c) => Current(c) != EExtraMission.None;

        private static ExtraMissionState State(IGameContext c)
        {
            if (GameProgressUtilities.ExtraMission(c.GameDataStore) is { } s) return s;
            var fresh = new ExtraMissionState { Mission = Of(c.Settings) };
            GameProgressUtilities.SetExtraMission(c.GameDataStore, fresh);
            return fresh;
        }

        private static void Save(IGameContext c, ExtraMissionState s) => GameProgressUtilities.SetExtraMission(c.GameDataStore, s);

        /// <summary>Settles the mission before the objectives are set up (a Random one is rolled), and says which it is.</summary>
        public static async Task Begin(IGameContext c)
        {
            EExtraMission picked = Of(c.Settings);
            if (picked == EExtraMission.None) return;
            if (GameProgressUtilities.ExtraMission(c.GameDataStore) is { } already && already.Mission != EExtraMission.None) return;
            EExtraMission mission = picked == EExtraMission.Random
                ? ExtraMissionCatalog.Roll(() => c.DiceRoller.RollDecisiveFace())
                : picked;
            Save(c, new ExtraMissionState { Mission = mission });
            if (mission == EExtraMission.None)
            {
                c.Log("Mission: Duel (the core rules' mission).");
                return;
            }
            ExtraMissionInfo info = ExtraMissionCatalog.Of(mission);
            c.Log($"Mission: {info.Name}. {info.BookText.Replace('\n', ' ')}");
            await c.Announce($"Mission: {info.Name}", new TextColor(255, 210, 80, 255), EBannerTier.Headline);
        }

        /// <summary>How many markers the map setup places (null = the core D3+2 roll). The markers each side sets up in
        /// its own deployment zone are placed after the zones are chosen, so the map setup places none.</summary>
        public static int? MarkerCount(IGameContext c) => Current(c) is var m && m == EExtraMission.None ? null
            : ExtraMissionCatalog.Of(m).Markers switch
            {
                EMissionMarkers.FourQuarters => 4,
                EMissionMarkers.Three => 3,
                EMissionMarkers.OneInTheMiddle => 1,
                EMissionMarkers.OneEachOnZoneEdge or EMissionMarkers.TwoEachInZone => 0,
                _ => null,
            };

        /// <summary>Whether a drawn map's own markers may be used: only when the mission leaves their number open (Duel,
        /// Pitched Battle) or it matches (Relic Hunt / Capture &amp; Hold need 3).</summary>
        public static bool MapMarkersAllowed(GameSettings s, int mapMarkers)
        {
            EExtraMission m = Of(s);
            if (m is EExtraMission.None or EExtraMission.Random) return m == EExtraMission.None;
            return ExtraMissionCatalog.Of(m).Markers switch
            {
                EMissionMarkers.D3Plus2 => true,
                EMissionMarkers.Three => mapMarkers == 3,
                _ => false,
            };
        }

        /// <summary>Seize Ground / Domination: "Divide the non-deployment zone area of the table into 4 equal quarters,
        /// and place one marker at the center of each." The non-deployment area is the band between the core zones.</summary>
        public static IReadOnlyList<Position>? FixedSpots(IGameContext c)
        {
            if (Current(c) is var m && m == EExtraMission.None || ExtraMissionCatalog.Of(m).Markers != EMissionMarkers.FourQuarters)
                return null;
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float deep = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES, band = h - 2 * deep;
            return new[]
            {
                new Position(w * 0.25f, deep + band * 0.25f), new Position(w * 0.75f, deep + band * 0.25f),
                new Position(w * 0.25f, deep + band * 0.75f), new Position(w * 0.75f, deep + band * 0.75f),
            };
        }

        /// <summary>King of the Hill / Mosh Pit: "over 9" away from the deployment zones and the table edges".</summary>
        public static RectangularZone? LegalBand(IGameContext c)
        {
            if (Current(c) is var m && m == EExtraMission.None || ExtraMissionCatalog.Of(m).Markers != EMissionMarkers.OneInTheMiddle)
                return null;
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float deep = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            float bottom = deep + 9.01f, top = h - deep - 9.01f;
            if (top <= bottom) { bottom = h / 2f - 0.5f; top = h / 2f + 0.5f; }
            return new RectangularZone(9.01f, w - 9.01f, bottom, top);
        }

        // ---------------------------------------------------------------- markers in the deployment zones

        /// <summary>
        /// Sabotage / Demolition / Breakthrough / Headquarters: each side's own markers, placed once the deployment zones
        /// are known. Judgement calls (house rules C, "Extra Missions"): one per side (team) - the books are written for
        /// two players; placed by the game at a random legal spot - a marker "on the edge" of the zone sits on the zone's
        /// side facing the table centre; "within the zone, over 9" from its edge" sits 1.5" in from the back.
        /// </summary>
        public static async Task PlaceOwnedMarkers(IGameContext c, IReadOnlyDictionary<int, ZoneRect> zonesByTeam)
        {
            EExtraMission m = Current(c);
            if (m == EExtraMission.None) return;
            ExtraMissionInfo info = ExtraMissionCatalog.Of(m);
            if (info.Markers is not (EMissionMarkers.OneEachOnZoneEdge or EMissionMarkers.TwoEachInZone)) return;
            ExtraMissionState state = State(c);
            if (state.OwnedPlaced) return;
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            List<ITerrain> blocked = c.TableState.Terrain.Objects.Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();
            bool Clear(Position p) => !blocked.Any(t => t.Shape.DoesPathIntersectZone(p.Position2D, p.Position2D, 0.6f));

            foreach ((int team, ZoneRect zone) in zonesByTeam.OrderBy(kv => kv.Key))
            {
                IPlayerSlotInfo? seat = c.TableState.Players.Objects.Where(p => p.IsFilled && p.TeamNumber == team).OrderBy(p => p.SlotID).FirstOrDefault();
                if (seat == null) continue;
                // Which way the zone faces the middle of the table.
                float cx = (zone.Left + zone.Right) / 2f, cz = (zone.Bottom + zone.Top) / 2f;
                bool alongX = MathF.Abs(cz - h / 2f) / h >= MathF.Abs(cx - w / 2f) / w; // the zone sits at the top or bottom
                var spots = new List<Position>();
                float lo = MathF.Max(alongX ? zone.Left : zone.Bottom, 9.5f);
                float hi = MathF.Min(alongX ? zone.Right : zone.Top, (alongX ? w : h) - 9.5f);
                if (hi < lo) { lo = alongX ? zone.Left + 1 : zone.Bottom + 1; hi = alongX ? zone.Right - 1 : zone.Top - 1; }
                float RandomAlong() => lo + (float)c.Rng.NextDouble() * (hi - lo);
                float inner, back;
                if (alongX) { bool low = cz < h / 2f; inner = low ? zone.Top : zone.Bottom; back = low ? zone.Bottom : zone.Top; }
                else { bool low = cx < w / 2f; inner = low ? zone.Right : zone.Left; back = low ? zone.Left : zone.Right; }
                Position At(float along, float across) => alongX ? new Position(along, across) : new Position(across, along);

                if (info.Markers == EMissionMarkers.OneEachOnZoneEdge)
                {
                    Position p = At(RandomAlong(), inner);
                    for (int tries = 0; tries < 30 && !Clear(p); tries++) p = At(RandomAlong(), inner);
                    spots.Add(p);
                }
                else
                {
                    float across = back + MathF.Sign(inner - back) * 1.5f;
                    float span = hi - lo;
                    float a = lo + span * 0.25f, b = lo + span * 0.75f;
                    if (b - a < 9.5f) { a = lo; b = MathF.Min(hi, lo + 9.5f); }
                    foreach (float along in new[] { a, b })
                    {
                        Position p = At(along, across);
                        for (float nudge = 1f; nudge < 6f && !Clear(p); nudge += 1f) p = At(along + nudge, across);
                        spots.Add(p);
                    }
                }

                foreach (Position p in spots)
                {
                    var objective = new ObjectiveData(p, c.GameDataStore);
                    c.GameDataStore.Create(objective);
                    if (info.Owned)
                    {
                        objective.SetOwner(seat.PlayerID);
                        state.Owned.Add(new OwnedMarker { X = p.x, Z = p.z, Slot = seat.SlotID });
                    }
                    c.Log($"  {info.Name}: {seat.Name}'s marker at ({p.x:0.0}, {p.z:0.0}).");
                }
            }
            state.OwnedPlaced = true;
            Save(c, state);
            await Task.CompletedTask;
        }

        // ---------------------------------------------------------------- during the game

        private static IUnit? UnitById(IGameContext c, string id) =>
            c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == id);

        private static OwnedMarker? OwnedAt(ExtraMissionState s, IObjective o) =>
            s.Owned.FirstOrDefault(m => !m.Destroyed && MathF.Abs(m.X - o.Position.x) < 0.01f && MathF.Abs(m.Z - o.Position.z) < 0.01f);

        private static DataReference? ReferenceOf(IGameContext c, IObjective o)
        {
            foreach (DataReference r in c.GameDataStore.GetAllDataReferences<ObjectiveData>())
                if (ReferenceEquals(c.GameDataStore.GetDataBinding<ObjectiveData>(r).GetValue(), o)) return r;
            return null;
        }

        /// <summary>
        /// "If the unit is Shaken or destroyed at any point, the marker is dropped within 1" (placed by the opponent)."
        /// Checked after every activation and at the end of each round; the marker goes down where the carrier's first
        /// model stands (or stood), neutral.
        /// </summary>
        public static void DropCarried(IGameContext c)
        {
            if (!IsOn(c)) return;
            ExtraMissionState s = State(c);
            if (s.Carried.Count == 0) return;
            bool changed = false;
            foreach (CarriedMarker carried in s.Carried.ToList())
            {
                IUnit? unit = UnitById(c, carried.Unit);
                bool gone = unit == null || !unit.GetIsAlive();
                bool shaken = unit != null && unit.Tokens.HasToken(TokenType.Shaken);
                if (!gone && !shaken) continue;
                IModel? spot = unit?.Models.FirstOrDefault(m => m.GetIsAlive()) ?? unit?.Models.FirstOrDefault();
                Position at = spot?.Position ?? new Position(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f);
                c.GameDataStore.Create(new ObjectiveData(new Position(at.x, at.z), c.GameDataStore));
                s.Carried.Remove(carried);
                changed = true;
                c.Log($"{unit?.Name ?? "A unit"} is {(gone ? "destroyed" : "Shaken")} and drops the marker it carried.");
            }
            if (changed) Save(c, s);
        }

        /// <summary>The markers each seat controls now: objectives it holds plus markers its units carry.</summary>
        public static Dictionary<int, int> Controlled(IGameContext c)
        {
            var bySlot = new Dictionary<int, int>();
            foreach (IObjective o in c.TableState.Objectives.Objects)
            {
                if (o.OwnerID is not PlayerID owner) continue;
                int slot = MissionCardGame.SlotOf(c, owner);
                if (slot >= 0) bySlot[slot] = bySlot.GetValueOrDefault(slot) + 1;
            }
            if (GameProgressUtilities.ExtraMission(c.GameDataStore) is { } s)
                foreach (CarriedMarker carried in s.Carried)
                    bySlot[carried.Slot] = bySlot.GetValueOrDefault(carried.Slot) + 1;
            return bySlot;
        }

        /// <summary>
        /// The end of a round, after objectives are seized: carried markers are picked up, enemy markers destroyed, the
        /// first seizer noted, and progressive VP scored (X = the round number).
        /// </summary>
        public static async Task AfterSeizing(IGameContext c, int round)
        {
            EExtraMission m = Current(c);
            if (m == EExtraMission.None) return;
            ExtraMissionInfo info = ExtraMissionCatalog.Of(m);
            ExtraMissionState s = State(c);
            DropCarried(c);
            s = State(c);

            foreach (IObjective o in c.TableState.Objectives.Objects.ToList())
            {
                if (o.OwnerID is not PlayerID owner) continue;
                int slot = MissionCardGame.SlotOf(c, owner);
                if (slot < 0) continue;

                if (m == EExtraMission.MoshPit && s.FirstSeizer < 0)
                {
                    s.FirstSeizer = slot;
                    c.Log($"Mosh Pit: {c.GetPlayerName(owner)} is the first to seize the marker.");
                }

                if (info.Owned && OwnedAt(s, o) is { } mine && mine.Slot != slot)
                {
                    mine.Destroyed = true;
                    mine.DestroyedInRound = round;
                    mine.DestroyedBySlot = slot;
                    if (ReferenceOf(c, o) is { } r) c.GameDataStore.Destroy(r);
                    c.Log($"{info.Name}: {c.GetPlayerName(owner)} seizes the enemy marker - it is destroyed.");
                    await c.Announce("Enemy marker destroyed!", new TextColor(255, 80, 60, 255), EBannerTier.Headline);
                    continue;
                }

                if (info.Carried)
                {
                    IUnit? carrier = c.TableState.Units.Objects
                        .Where(u => u.PlayerID.Equals(owner) && u.GetIsAlive() && !u.Tokens.HasToken(TokenType.Shaken))
                        .OrderBy(u => u.Models.Where(md => md.GetIsAlive()).Select(md => Position.GetDistance2D(md.Position, o.Position)).DefaultIfEmpty(float.MaxValue).Min())
                        .FirstOrDefault();
                    if (carrier == null) continue;
                    s.Carried.Add(new CarriedMarker { Unit = carrier.ID.ID.ToString(), Slot = slot });
                    if (ReferenceOf(c, o) is { } r) c.GameDataStore.Destroy(r);
                    c.Log($"{info.Name}: {carrier.Name} picks up the marker and carries it.");
                }
            }

            if (info.Progressive)
            {
                Dictionary<int, int> held = Controlled(c);
                foreach (IPlayerSlotInfo seat in c.TableState.Players.Objects.Where(p => p.IsFilled))
                {
                    int slot = seat.SlotID, gained;
                    if (m == EExtraMission.Demolition)
                    {
                        bool intact = s.Owned.Any(o => o.Slot == slot && !o.Destroyed);
                        bool destroyedNow = s.Owned.Any(o => o.Slot != slot && o.Destroyed && o.DestroyedInRound == round && o.DestroyedBySlot == slot);
                        gained = (intact ? round : 0) + (destroyedNow ? round : 0);
                    }
                    else if (m == EExtraMission.MoshPit)
                        gained = (held.GetValueOrDefault(slot) > 0 ? round : 0) + (s.FirstSeizer == slot ? round : 0);
                    else
                        gained = held.GetValueOrDefault(slot) * round;
                    s.Vp[slot] = s.Vp.GetValueOrDefault(slot) + gained;
                    c.Log($"{info.Name}, round {round}: {seat.Name} scores {gained} VP ({s.Vp[slot]} in all).");
                }
            }
            Save(c, s);
        }

        /// <summary>The final tally the winner is read from (seat -> score), and what the score is called.</summary>
        public static (Dictionary<int, int> Score, string Unit)? FinalScore(IGameContext c)
        {
            EExtraMission m = Current(c);
            if (m == EExtraMission.None) return null;
            DropCarried(c);
            ExtraMissionInfo info = ExtraMissionCatalog.Of(m);
            ExtraMissionState s = State(c);
            if (info.Progressive) return (new Dictionary<int, int>(s.Vp), "VP");
            if (m == EExtraMission.Sabotage)
            {
                // "the player that managed to destroy the enemy marker whilst keeping their own marker intact wins".
                var won = new Dictionary<int, int>();
                foreach (IPlayerSlotInfo seat in c.TableState.Players.Objects.Where(p => p.IsFilled))
                {
                    bool intact = s.Owned.Any(o => o.Slot == seat.SlotID && !o.Destroyed);
                    bool destroyed = s.Owned.Any(o => o.Slot != seat.SlotID && o.Destroyed && o.DestroyedBySlot == seat.SlotID);
                    won[seat.SlotID] = intact && destroyed ? 1 : 0;
                }
                return (won, "marker destroyed");
            }
            return (Controlled(c), "markers");
        }
    }
}
