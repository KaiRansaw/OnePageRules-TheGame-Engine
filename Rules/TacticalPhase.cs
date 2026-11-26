using System.Runtime.CompilerServices;
using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG
{
    /// <summary>A forward position marker (Tactical Phase).</summary>
    public sealed class ForwardMarker
    {
        public int Slot { get; set; }
        public int Number { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
        public bool Locked { get; set; }
    }

    /// <summary>A trapped, fortified or sabotaged terrain piece (Battle Preparations).</summary>
    public sealed class PrepPiece
    {
        /// <summary>The seat that made it (trap / fortification / sabotage owner).</summary>
        public int Slot { get; set; }
        public int Round { get; set; }
        public string Terrain { get; set; } = "";
        /// <summary>Fortify: true = friendly units ignore its movement penalty; false = +1 Defense inside.</summary>
        public bool IgnorePenalty { get; set; }
    }

    /// <summary>Shoot Pathfinders, waiting for the shooter's first turn (shooter, target unit ids).</summary>
    public sealed class FreeShot
    {
        public int Slot { get; set; }
        public string Shooter { get; set; } = "";
        public string Target { get; set; } = "";
    }

    public sealed class TacticalPhaseState
    {
        public List<ForwardMarker> Markers { get; set; } = new();
        /// <summary>The table edge each seat set its markers on ("bottom", "top", "left", "right").</summary>
        public Dictionary<int, string> Edges { get; set; } = new();
        public bool MarkersDone { get; set; }
        public bool MarkersRemoved { get; set; }
        public bool PreparationsDone { get; set; }
        public List<PrepPiece> Traps { get; set; } = new();
        public List<PrepPiece> Fortified { get; set; } = new();
        public List<PrepPiece> Sabotaged { get; set; } = new();
        /// <summary>Units moved with Move Pathfinders (unit id -> seat).</summary>
        public Dictionary<string, int> Pathfinders { get; set; } = new();
        public List<FreeShot> FreeShots { get; set; } = new();
        /// <summary>The free shot being taken now (set when the shooter's turn starts).</summary>
        public FreeShot? Active { get; set; }
        /// <summary>What each seat picked each preparation round ("round:slot" -> tactic), revealed lines for the panel.</summary>
        public List<string> Log { get; set; } = new();
    }

    /// <summary>
    /// Tactical Phase (GF: Firefight p46, AoF: Skirmish p46 - the same words in both; Grimdark Future and Age of Fantasy
    /// don't print it). Kai, 2026-11-25: "continue" (the queue after Meat Grinder). Two optional parts, played in order:
    /// <list type="bullet">
    /// <item><b>Forward Positions Setup</b> - after the objectives, each side sets 3 markers on its table edge and the
    /// sides take turns moving them up to 12" (staying within 12" of a friendly marker, stopping within 12" of an enemy
    /// one). Units then deploy fully within 3" of their markers or within 12" of the corner furthest from the enemy's.</item>
    /// <item><b>Battle Preparations</b> - after deployment, 3 rounds where each player secretly picks a tactic: Plant /
    /// Disarm Traps, Fortify / Sabotage Terrain, Move / Shoot Pathfinders.</item>
    /// </list>
    /// Two sides only (the book is written for two). Judgement calls: house rules C, "Tactical Phase readings".
    /// </summary>
    public static class TacticalPhase
    {
        public const string ForwardPositionsName = "Forward Positions Setup";
        public const string BattlePreparationsName = "Battle Preparations";

        public static IReadOnlyList<(string Name, EAdvancedRules Flag)> All { get; } = new[]
        {
            (ForwardPositionsName, EAdvancedRules.ForwardPositions),
            (BattlePreparationsName, EAdvancedRules.BattlePreparations),
        };

        public static bool BooksHaveIt(EGameSystem s) => s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public static string BookText(string name) => name switch
        {
            ForwardPositionsName =>
                "Before starting with setting up forward positions, players must prepare the mission using the normal front line deployment zones for the purpose of placing objectives.\n"
                + "After all of the objectives have been placed, each player must take 3 forward position markers, and then roll-off to see who goes first. The player that goes first picks one table edge, and then must place all 3 markers anywhere within 1\" of the edge, and within 12\" of at least one other marker. Then their opponent does the same, but on the opposite table edge.\n"
                + "Starting with the player that won the roll-off, players must then alternate in moving one marker each by up to 12\" in any direction, whilst remaining within 12\" of at least one other friendly marker.\n"
                + "As soon as a player moves a friendly marker within 12\" of an enemy marker, the marker stops on the spot, and the next player may move a marker. Markers that are within 12\" of an enemy marker are locked on the spot, and may not be moved. Players may also choose to not move a marker and lock it in its spot instead, which may then not be moved anymore.\n"
                + "Once all markers have been locked in place or can't be moved anymore, the forward positions setup is over, and players move on to deploying their armies.\n"
                + "Deployment follows all the regular rules, except that all units must be placed fully within 3\" of the forward position markers, or within 12\" of the table corner that is furthest from all enemy forward position markers.",
            BattlePreparationsName =>
                "After units have been deployed, a total of 3 battle preparation rounds are played, after which the game begins. During each battle preparation round, each player must pick one of the following tactics in secret, then both players reveal their tactics and apply their effects.\n"
                + "1. Plant Traps: Choose up to D3 pieces of terrain in secret. If an enemy unit moves within 1\" of that terrain, then it takes D3 wounds, and the trap is removed.\n"
                + "2. Disarm Traps: If the opposing player chose the \"plant traps\" tactic during this round, then that tactic has no effect. If the opposing player chose it during a previous round, then they must remove all traps from one of the previous \"plant traps\" tactics.\n"
                + "3. Fortify Terrain: Choose up to D3 pieces of Difficult terrain openly, and then pick if all friendly units may ignore its movement penalty, or if all friendly units inside of it get +1 to Defense rolls.\n"
                + "4. Sabotage Terrain: If the opposing player chose the \"fortify terrain\" tactic during this or any previous round, then you may pick one fortified terrain piece in secret. The first time an enemy unit moves into the terrain it takes D3 wounds.\n"
                + "5. Move Pathfinders: Choose one friendly unit that is already deployed, and place it anywhere within 6\" of its current position, ignoring terrain.\n"
                + "6. Shoot Pathfinders: If the opposing player chose the \"move pathfinders\" tactic during this or any previous round, then one of your units may shoot at a pathfinder unit that is in range and line of sight.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            ForwardPositionsName => "After the objectives: you pick an edge (if you win the roll-off), place your 3 markers on it, then the sides take turns moving a marker 12\" forward, to a spot you pick, or locking it. You then deploy within 3\" of your markers or in the corner furthest from the enemy's. Two players only.",
            BattlePreparationsName => "At the start of round 1: three rounds of secret tactic picks. Traps and sabotage hurt enemy units that move near / into the piece; fortified pieces work for your side all game; a pathfinder shot is taken at the start of your first turn. Two players only.",
            _ => "",
        };

        public const string PlantTraps = "Plant Traps";
        public const string DisarmTraps = "Disarm Traps";
        public const string FortifyTerrain = "Fortify Terrain";
        public const string SabotageTerrain = "Sabotage Terrain";
        public const string MovePathfinders = "Move Pathfinders";
        public const string ShootPathfinders = "Shoot Pathfinders";
        public static readonly string[] Tactics = { PlantTraps, DisarmTraps, FortifyTerrain, SabotageTerrain, MovePathfinders, ShootPathfinders };

        public static readonly TextColor Olive = new(170, 190, 90, 255);
        public const float MarkerRadius = 0.6f;
        public const string MarkerPrefix = "Forward position ";

        private static float W => GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        private static float H => GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

        public static bool IsOn(GameSettings s, EAdvancedRules flag) =>
            BooksHaveIt(s.GameSystem) && s.GameMode == EGameMode.Standard && AdvancedRules.IsOn(s, flag);

        private static bool On(IGameContext c, EAdvancedRules flag) =>
            IsOn(c.Settings, flag) && Seats(c).Count == 2
            && c.TableState.Teams.Objects.Count(t => t.Players.Any()) == 2;

        // ---------------------------------------------------------------- state

        private static readonly ConditionalWeakTable<IReadableGameDataStore, TacticalPhaseState> Pending = new();

        public static TacticalPhaseState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.TacticalPhase
            ?? (Pending.TryGetValue(store, out TacticalPhaseState? s) ? s : null);

        private static TacticalPhaseState StateOf(IGameContext c) => State(c.GameDataStore) ?? new TacticalPhaseState();

        internal static void Save(IGameContext c, TacticalPhaseState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.TacticalPhase = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        private static List<IPlayerSlotInfo> Seats(IGameContext c) =>
            c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();

        private static IPlayerSlotInfo? Seat(IGameContext c, int slot) => Seats(c).FirstOrDefault(p => p.SlotID == slot);

        private static int Other(IGameContext c, int slot) => Seats(c).Select(p => p.SlotID).FirstOrDefault(s => s != slot, -1);

        private static int SlotOf(IGameContext c, PlayerID p) => Seats(c).FirstOrDefault(s => s.PlayerID.Equals(p))?.SlotID ?? -1;

        private static int D6(IGameContext c) => c.DiceRoller.RollDecisiveFace();
        private static int D3(IGameContext c) => (D6(c) + 1) / 2;

        private static async Task<string> Ask(IGameContext c, PlayerID who, string question, IReadOnlyList<string> options,
            string displayName, string? recommended = null, IReadOnlyDictionary<string, string>? descriptions = null)
        {
            var request = new StringSelectionRequest(who, question, options.ToList(), new List<StringSelectionRequest.InvalidOption>(),
                optionDescriptions: descriptions?.ToDictionary(kv => kv.Key, kv => kv.Value), displayName: displayName)
            { Recommended = recommended ?? options[0] };
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            return options.Contains(pick) ? pick : options[0];
        }

        // ================================================================ forward positions

        private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        public const string EdgeBottom = "Bottom long edge";
        public const string EdgeTop = "Top long edge";
        public const string EdgeLeft = "Left short edge";
        public const string EdgeRight = "Right short edge";

        private static string Opposite(string edge) => edge switch
        {
            EdgeBottom => EdgeTop, EdgeTop => EdgeBottom, EdgeLeft => EdgeRight, _ => EdgeLeft,
        };

        /// <summary>The 1" strip along an edge, as a placement band.</summary>
        private static RectangularZone EdgeBand(string edge, float lo = float.MinValue, float hi = float.MaxValue)
        {
            const float inset = 0.6f; // keeps the marker on the table
            return edge switch
            {
                EdgeBottom => new RectangularZone(Math.Max(inset, lo), Math.Min(W - inset, hi), inset, 1f),
                EdgeTop => new RectangularZone(Math.Max(inset, lo), Math.Min(W - inset, hi), H - 1f, H - inset),
                EdgeLeft => new RectangularZone(inset, 1f, Math.Max(inset, lo), Math.Min(H - inset, hi)),
                _ => new RectangularZone(W - 1f, W - inset, Math.Max(inset, lo), Math.Min(H - inset, hi)),
            };
        }

        private static bool AlongX(string edge) => edge is EdgeBottom or EdgeTop;

        /// <summary>The edge on a player's left as they sit at <paramref name="edge"/> facing the far edge.</summary>
        private static string LeftOf(string edge) => edge switch
        {
            EdgeBottom => EdgeLeft, EdgeTop => EdgeRight, EdgeLeft => EdgeTop, _ => EdgeBottom,
        };

        /// <summary>Straight away from the edge, toward the enemy's.</summary>
        private static (float X, float Z) Forward(string edge) => edge switch
        {
            EdgeBottom => (0f, 1f), EdgeTop => (0f, -1f), EdgeLeft => (1f, 0f), _ => (-1f, 0f),
        };

        /// <summary>The edge of a team's own core deployment zone (the bot's pick, and the default).</summary>
        private static string ZoneEdge(ZoneRect? z)
        {
            if (z == null) return EdgeBottom;
            float cx = (z.Left + z.Right) / 2f, cz = (z.Bottom + z.Top) / 2f;
            bool topBottom = MathF.Abs(cz - H / 2f) / H >= MathF.Abs(cx - W / 2f) / W;
            if (topBottom) return cz < H / 2f ? EdgeBottom : EdgeTop;
            return cx < W / 2f ? EdgeLeft : EdgeRight;
        }

        /// <summary>
        /// After the objectives and the deployment zones (ChooseMapSideStage): the roll-off, the edges, each side's three
        /// markers, then the marker moves until every marker is locked.
        /// </summary>
        public static async Task ForwardPositions(IGameContext c, IReadOnlyDictionary<int, ZoneRect> zonesByTeam)
        {
            if (!On(c, EAdvancedRules.ForwardPositions)) return;
            TacticalPhaseState s = StateOf(c);
            if (s.MarkersDone) return;
            List<IPlayerSlotInfo> seats = Seats(c);
            int a = D6(c), b = D6(c);
            for (int reroll = 0; a == b && reroll < 20; reroll++) { a = D6(c); b = D6(c); }
            if (a == b) a++; // a dice roller that only ever ties (tests): the first seat goes first
            IPlayerSlotInfo first = a > b ? seats[0] : seats[1], second = a > b ? seats[1] : seats[0];
            c.Log($"Forward Positions: roll-off {seats[0].Name} {a}, {seats[1].Name} {b} - {first.Name} goes first.");
            await c.Announce("Forward Positions Setup", Olive, EBannerTier.Headline);

            zonesByTeam.TryGetValue(first.TeamNumber, out ZoneRect? firstZone);
            // The choices are named from where the player sits (their own deployment zone's edge, facing the enemy).
            string home = ZoneEdge(firstZone);
            var labels = new Dictionary<string, string>
            {
                ["Your own table edge"] = home,
                ["The enemy's table edge"] = Opposite(home),
                [AlongX(home) ? "The short edge on your left" : "The long edge on your left"] = LeftOf(home),
                [AlongX(home) ? "The short edge on your right" : "The long edge on your right"] = Opposite(LeftOf(home)),
            };
            string picked = await Ask(c, first.PlayerID,
                "Forward Positions: you won the roll-off. Pick your table edge - the opponent takes the opposite one.",
                labels.Keys.ToList(), "Pick your table edge", labels.Keys.First());
            string edge = labels[picked];
            s.Edges[first.SlotID] = edge;
            s.Edges[second.SlotID] = Opposite(edge);
            c.Log($"Forward Positions: {first.Name} takes the {edge.ToLowerInvariant()}, {second.Name} the {Opposite(edge).ToLowerInvariant()}.");

            foreach (IPlayerSlotInfo seat in new[] { first, second })
            {
                for (int n = 1; n <= 3; n++)
                {
                    Position p = await PlaceMarker(c, s, seat, s.Edges[seat.SlotID], n);
                    s.Markers.Add(new ForwardMarker { Slot = seat.SlotID, Number = n, X = p.x, Z = p.z });
                    Draw(c, s);
                }
            }
            Save(c, s);

            // Alternate moves, the roll-off winner first, until no marker can move.
            IPlayerSlotInfo turn = first;
            for (int guard = 0; guard < 40; guard++)
            {
                LockNearEnemies(s);
                bool firstCan = s.Markers.Any(m => m.Slot == first.SlotID && !m.Locked);
                bool secondCan = s.Markers.Any(m => m.Slot == second.SlotID && !m.Locked);
                if (!firstCan && !secondCan) break;
                if (s.Markers.Any(m => m.Slot == turn.SlotID && !m.Locked)) await MoveOne(c, s, turn);
                turn = turn == first ? second : first;
                Draw(c, s);
                Save(c, s);
            }
            foreach (ForwardMarker m in s.Markers) m.Locked = true;
            s.MarkersDone = true;
            Draw(c, s);
            Save(c, s);
            foreach (IPlayerSlotInfo seat in seats)
                c.Log($"Forward Positions: {seat.Name}'s markers at "
                      + string.Join(", ", s.Markers.Where(m => m.Slot == seat.SlotID).Select(m => $"({m.X:0.0}, {m.Z:0.0})")) + ".");
            await c.Announce("Forward positions locked - deploy within 3\" of your markers", Olive, EBannerTier.Toast);
        }

        private static async Task<Position> PlaceMarker(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat, string edge, int n)
        {
            // "within 12" of at least one other marker": the band along the edge is cut to 12" either side of the ones down.
            var mine = s.Markers.Where(m => m.Slot == seat.SlotID).ToList();
            float lo = float.MinValue, hi = float.MaxValue;
            if (mine.Count > 0)
            {
                IEnumerable<float> along = mine.Select(m => AlongX(edge) ? m.X : m.Z);
                lo = along.Min() - 12f;
                hi = along.Max() + 12f;
            }
            RectangularZone band = EdgeBand(edge, lo, hi);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Position wanted;
                try
                {
                    wanted = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(new PlaceObjectiveRequest(seat.PlayerID,
                        $"Forward Positions: place marker {n} of 3 within 1\" of your table edge" + (mine.Count > 0 ? ", within 12\" of another of yours" : ""),
                        n, 3, band, 1.5f));
                }
                catch (InvalidOperationException) { break; }
                float off = edge switch
                {
                    EdgeBottom => wanted.z, EdgeTop => H - wanted.z, EdgeLeft => wanted.x, _ => W - wanted.x,
                };
                var snapped = Snap(edge, wanted);
                if (off > 2f) { c.Log("  Forward Positions: markers go within 1\" of your table edge - place it again."); continue; }
                if (band.IsPointWithinZone(new Float2(snapped.x, snapped.z))
                    && (mine.Count == 0 || mine.Any(m => Dist(m.X, m.Z, snapped.x, snapped.z) <= 12.01f))
                    && mine.All(m => Dist(m.X, m.Z, snapped.x, snapped.z) >= 1.2f))
                    return snapped;
                c.Log("  Forward Positions: that spot isn't allowed - place it again.");
            }
            // The game places it: next along the edge, 10" from the last one.
            float baseAlong = AlongX(edge) ? W / 2f - 10f : H / 2f - 10f;
            float alongPos = mine.Count == 0 ? baseAlong : (AlongX(edge) ? mine.Max(m => m.X) : mine.Max(m => m.Z)) + 10f;
            return Snap(edge, AlongX(edge) ? new Position(alongPos, 0f) : new Position(0f, alongPos));
        }

        /// <summary>Puts a point on the edge strip (half an inch in) at its position along the edge.</summary>
        private static Position Snap(string edge, Position p)
        {
            float along = AlongX(edge) ? Math.Clamp(p.x, 0.6f, W - 0.6f) : Math.Clamp(p.z, 0.6f, H - 0.6f);
            return edge switch
            {
                EdgeBottom => new Position(along, 0.8f),
                EdgeTop => new Position(along, H - 0.8f),
                EdgeLeft => new Position(0.8f, along),
                _ => new Position(W - 0.8f, along),
            };
        }

        private static void LockNearEnemies(TacticalPhaseState s)
        {
            foreach (ForwardMarker m in s.Markers.Where(m => !m.Locked))
                if (s.Markers.Any(e => e.Slot != m.Slot && Dist(e.X, e.Z, m.X, m.Z) <= 12f)) m.Locked = true;
        }

        public const string LockPrefix = "Lock marker ";
        public const string ForwardPrefix = "Move marker ";
        public const string ChoosePrefix = "Choose where marker ";

        private static async Task MoveOne(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat)
        {
            var free = s.Markers.Where(m => m.Slot == seat.SlotID && !m.Locked).OrderBy(m => m.Number).ToList();
            string edge = s.Edges[seat.SlotID];
            var options = new List<string>();
            var notes = new Dictionary<string, string>();
            // How far each marker would actually get straight forward (0 = it can't move forward at all).
            var gain = free.ToDictionary(m => m, m =>
            {
                (float x, float z) = StopNearEnemy(s, m, ForwardSpot(s, m, edge));
                return Dist(m.X, m.Z, x, z);
            });
            foreach (ForwardMarker m in free.Where(m => gain[m] >= 0.5f))
            {
                string fwd = $"{ForwardPrefix}{m.Number} {gain[m]:0}\" forward";
                options.Add(fwd);
                notes[fwd] = "Straight toward the enemy's edge, as far as it can go (12\", or until it is within 12\" of an enemy marker, or as far as keeps it within 12\" of another of yours).";
            }
            foreach (ForwardMarker m in free)
            {
                string choose = $"{ChoosePrefix}{m.Number} goes";
                options.Add(choose);
                notes[choose] = "Pick a spot up to 12\" away that stays within 12\" of another of your markers.";
            }
            foreach (ForwardMarker m in free)
            {
                string lockIt = $"{LockPrefix}{m.Number}";
                options.Add(lockIt);
                notes[lockIt] = "Leave it where it is; it can't be moved again.";
            }
            // The bot (and the suggestion): the marker furthest back that can still go forward a useful distance,
            // so the three leapfrog up the table together; when none can, lock the one furthest back.
            ForwardMarker? best = free.Where(m => gain[m] >= 2f).OrderBy(m => Progress(m, edge)).FirstOrDefault();
            string recommended = best != null
                ? options.First(o => o.StartsWith($"{ForwardPrefix}{best.Number} ", StringComparison.Ordinal))
                : $"{LockPrefix}{free.OrderBy(m => Progress(m, edge)).First().Number}";
            string pick = await Ask(c, seat.PlayerID, "Forward Positions: move one of your markers, or lock one in place.",
                options, "Forward positions", recommended, notes);
            int number = int.Parse(new string(pick.SkipWhile(ch => !char.IsDigit(ch)).TakeWhile(char.IsDigit).ToArray()));
            ForwardMarker marker = free.First(m => m.Number == number);
            if (pick.StartsWith(LockPrefix, StringComparison.Ordinal))
            {
                marker.Locked = true;
                c.Log($"Forward Positions: {seat.Name} locks marker {number}.");
                return;
            }
            (float X, float Z) to;
            if (pick.StartsWith(ForwardPrefix, StringComparison.Ordinal))
                to = ForwardSpot(s, marker, s.Edges[seat.SlotID]);
            else
                to = await ChosenSpot(c, s, seat, marker) ?? ForwardSpot(s, marker, s.Edges[seat.SlotID]);
            (float X, float Z) end = StopNearEnemy(s, marker, to);
            bool stopped = end != to;
            marker.X = end.X;
            marker.Z = end.Z;
            if (stopped) marker.Locked = true;
            c.Log($"Forward Positions: {seat.Name} moves marker {number} to ({end.X:0.0}, {end.Z:0.0})" + (stopped ? " - within 12\" of an enemy marker, it stops and locks." : "."));
        }

        /// <summary>How far a marker is from its own edge.</summary>
        private static float Progress(ForwardMarker m, string edge) => edge switch
        {
            EdgeBottom => m.Z, EdgeTop => H - m.Z, EdgeLeft => m.X, _ => W - m.X,
        };

        /// <summary>Straight forward, up to 12", the furthest point that stays within 12" of another friendly marker.</summary>
        internal static (float X, float Z) ForwardSpot(TacticalPhaseState s, ForwardMarker m, string edge)
        {
            (float fx, float fz) = Forward(edge);
            var friends = s.Markers.Where(o => o.Slot == m.Slot && o != m).ToList();
            for (float d = 12f; d > 0.4f; d -= 0.5f)
            {
                float x = Math.Clamp(m.X + fx * d, 0.6f, W - 0.6f), z = Math.Clamp(m.Z + fz * d, 0.6f, H - 0.6f);
                if (friends.Count == 0 || friends.Any(o => Dist(o.X, o.Z, x, z) <= 12f)) return (x, z);
            }
            return (m.X, m.Z);
        }

        private static async Task<(float X, float Z)?> ChosenSpot(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat, ForwardMarker m)
        {
            var friends = s.Markers.Where(o => o.Slot == m.Slot && o != m).ToList();
            var reach = new CircularZone(m.X, m.Z, 12f);
            var band = new RectangularZone(Math.Max(0.6f, m.X - 12f), Math.Min(W - 0.6f, m.X + 12f), Math.Max(0.6f, m.Z - 12f), Math.Min(H - 0.6f, m.Z + 12f));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Position p;
                try
                {
                    p = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(new PlaceObjectiveRequest(seat.PlayerID,
                        $"Forward Positions: where does marker {m.Number} go? Up to 12\", within 12\" of another of your markers.", m.Number, 3, band, 0f, reach));
                }
                catch (InvalidOperationException) { return null; }
                if (Dist(m.X, m.Z, p.x, p.z) <= 12.01f && (friends.Count == 0 || friends.Any(o => Dist(o.X, o.Z, p.x, p.z) <= 12.01f)))
                    return (Math.Clamp(p.x, 0.6f, W - 0.6f), Math.Clamp(p.z, 0.6f, H - 0.6f));
                c.Log("  Forward Positions: that spot isn't allowed - pick again.");
            }
            return null;
        }

        /// <summary>"As soon as a player moves a friendly marker within 12" of an enemy marker, the marker stops on the spot."</summary>
        internal static (float X, float Z) StopNearEnemy(TacticalPhaseState s, ForwardMarker m, (float X, float Z) to)
        {
            var enemies = s.Markers.Where(e => e.Slot != m.Slot).ToList();
            float len = Dist(m.X, m.Z, to.X, to.Z);
            if (len < 0.01f || enemies.Count == 0) return to;
            int steps = (int)MathF.Ceiling(len / 0.1f);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = m.X + (to.X - m.X) * t, z = m.Z + (to.Z - m.Z) * t;
                if (enemies.Any(e => Dist(e.X, e.Z, x, z) <= 12f)) return (x, z);
            }
            return to;
        }

        /// <summary>The markers on the table: pieces with no rules, named so they can be seen (and found again).</summary>
        private static void Draw(IGameContext c, TacticalPhaseState s)
        {
            EraseMarkers(c);
            if (s.MarkersRemoved) return;
            foreach (ForwardMarker m in s.Markers)
            {
                string owner = Seat(c, m.Slot)?.Name ?? $"Player {m.Slot + 1}";
                c.GameDataStore.Create(new TerrainData(ETerrainType.None, new CircularZone(new Float2(m.X, m.Z), MarkerRadius),
                    heightInches: 0f, name: $"{MarkerPrefix}{m.Number} ({owner})", kind: "Marker"));
            }
        }

        private static void EraseMarkers(IGameContext c)
        {
            var doomed = c.GameDataStore.GetAllDataReferences<TerrainData>()
                .Where(r => c.GameDataStore.IsValid(r, out _) && (c.GameDataStore.GetValue<TerrainData>(r).Name ?? "").StartsWith(MarkerPrefix, StringComparison.Ordinal))
                .ToList();
            foreach (DataReference r in doomed) c.GameDataStore.Destroy(r);
        }

        public static bool IsMarker(ITerrain t) => t.Name != null && t.Name.StartsWith(MarkerPrefix, StringComparison.Ordinal);

        /// <summary>"All units must be placed fully within 3" of the forward position markers, or within 12" of the table
        /// corner that is furthest from all enemy forward position markers." Null when the rule isn't in play.</summary>
        public static IBoundedZone? DeployZone(IGameContext c, UnitData unit)
        {
            if (!On(c, EAdvancedRules.ForwardPositions) || State(c.GameDataStore) is not { MarkersDone: true } s) return null;
            int slot = SlotOf(c, unit.PlayerID);
            if (slot < 0) return null;
            float reach = unit.Models.Where(m => m.GetIsAlive()).Select(m => m.BaseShape.BoundingRadiusInches).DefaultIfEmpty(0.5f).Max();
            var parts = new List<IZone>();
            foreach (ForwardMarker m in s.Markers.Where(m => m.Slot == slot))
                parts.Add(new CircularZone(m.X, m.Z, MathF.Max(0.3f, 3f - reach)));
            (float cx, float cz) = Corner(s, slot);
            parts.Add(new TableCornerZone(new Float2(cx, cz), MathF.Max(0.5f, 12f - reach), W, H));
            return new MissionPlacementZone(W, H, new CompositeZone(parts), null);
        }

        /// <summary>The table corner furthest from all of the enemy's markers (the nearest enemy marker furthest away).</summary>
        internal static (float X, float Z) Corner(TacticalPhaseState s, int slot)
        {
            var enemies = s.Markers.Where(m => m.Slot != slot).ToList();
            var corners = new[] { (0f, 0f), (W, 0f), (0f, H), (W, H) };
            if (enemies.Count == 0) return corners[0];
            return corners.OrderByDescending(k => enemies.Min(e => Dist(e.X, e.Z, k.Item1, k.Item2))).First();
        }

        // ================================================================ battle preparations

        private static string Key(ITerrain t)
        {
            ZoneBounds? b = t.Shape is IBoundedZone bz ? bz.Bounds : null;
            return b == null ? t.Name ?? "" : $"{t.Name}|{b.Value.Left:0.00},{b.Value.Right:0.00},{b.Value.Bottom:0.00},{b.Value.Top:0.00}";
        }

        private static (float X, float Z) Centre(ITerrain t) =>
            t.Shape is IBoundedZone bz ? ((bz.Bounds.Left + bz.Bounds.Right) / 2f, (bz.Bounds.Bottom + bz.Bounds.Top) / 2f) : (W / 2f, H / 2f);

        private static string Label(ITerrain t)
        {
            (float x, float z) = Centre(t);
            string name = string.IsNullOrWhiteSpace(t.Name) ? (string.IsNullOrWhiteSpace(t.Kind) ? "Terrain" : t.Kind) : t.Name;
            string side = x < W / 3f ? "left" : x > 2f * W / 3f ? "right" : "middle";
            string depth = z < H / 3f ? "near the bottom edge" : z > 2f * H / 3f ? "near the top edge" : "mid-table";
            return $"{name} ({side}, {depth}) [{x:0},{z:0}]";
        }

        /// <summary>The pieces that count as terrain for the tactics (not markers, spells or army-terrain areas).</summary>
        private static List<ITerrain> Pieces(IGameContext c) => c.TableState.Terrain.Objects
            .Where(t => !IsMarker(t) && !LivingSpells.IsMarker(t) && ArmyTerrain.BarrierTeam(t) == null && t.Shape is IBoundedZone)
            .ToList();

        private static ITerrain? PieceByKey(IGameContext c, string key) => Pieces(c).FirstOrDefault(t => Key(t) == key);

        /// <summary>At the start of round 1, before anything else: the forward markers come off the table, and the three
        /// battle preparation rounds are played.</summary>
        public static async Task BeforeRoundOne(IGameContext c, int round)
        {
            Refresh(c);
            if (round != 1) return;
            TacticalPhaseState s = StateOf(c);
            if (s.Markers.Count > 0 && !s.MarkersRemoved)
            {
                // A check line for the log (and the bot sweep): who ended up inside their forward-position areas.
                var placed = c.TableState.Units.Objects.OfType<UnitData>().Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()).ToList();
                var outside = placed.Where(u => !(DeployZone(c, u) is { } z && u.Models.Where(m => m.GetIsAlive())
                    .All(m => z.IsPointWithinZone(new Float2(m.Position.x, m.Position.z))))).ToList();
                c.Log($"Forward Positions: {placed.Count - outside.Count} of {placed.Count} units on the table are within their forward positions"
                      + (outside.Count > 0 ? " (outside: " + string.Join(", ", outside.Select(u => u.Name + (s.Pathfinders.ContainsKey(u.ID.ID.ToString()) ? " [pathfinders]" : ""))) + ")" : "") + ".");
                s.MarkersRemoved = true;
                EraseMarkers(c);
                Save(c, s);
            }
            if (!On(c, EAdvancedRules.BattlePreparations) || s.PreparationsDone) return;
            await c.Announce("Battle Preparations", Olive, EBannerTier.Headline);
            for (int prep = 1; prep <= 3; prep++) await PrepRound(c, s, prep);
            s.PreparationsDone = true;
            Save(c, s);
            Refresh(c);
        }

        private static async Task PrepRound(IGameContext c, TacticalPhaseState s, int prep)
        {
            List<IPlayerSlotInfo> seats = Seats(c);
            var picks = new Dictionary<int, string>();
            var notes = new Dictionary<string, string>
            {
                [PlantTraps] = "Pick up to D3 terrain pieces in secret. An enemy unit that moves within 1\" of one takes D3 wounds (the trap goes).",
                [DisarmTraps] = "Cancels the enemy's Plant Traps this round, or makes them remove one earlier round's traps.",
                [FortifyTerrain] = "Pick up to D3 Difficult pieces, openly: your units ignore their movement penalty, or get +1 Defense inside.",
                [SabotageTerrain] = "If the enemy has fortified terrain: pick one in secret. The first enemy unit to move into it takes D3 wounds.",
                [MovePathfinders] = "Place one of your deployed units again, within 6\" of where it stands, ignoring terrain.",
                [ShootPathfinders] = "If the enemy has moved pathfinders: one of your units shoots one at the start of your first turn.",
            };
            foreach (IPlayerSlotInfo seat in seats)
                picks[seat.SlotID] = await Ask(c, seat.PlayerID, $"Battle Preparations, round {prep} of 3: pick a tactic in secret.",
                    Tactics, $"Battle preparations {prep}/3", BotTactic(c, s, seat, prep), notes);
            string reveal = string.Join(", ", seats.Select(p => $"{p.Name}: {picks[p.SlotID]}"));
            c.Log($"Battle Preparations, round {prep}: {reveal}.");
            s.Log.Add($"Round {prep}: {reveal}");
            await c.Announce($"Preparations {prep}/3 - {reveal}", Olive, EBannerTier.Toast);

            // 1. Plant Traps (unless the other side disarms this round).
            foreach (IPlayerSlotInfo seat in seats.Where(p => picks[p.SlotID] == PlantTraps))
            {
                if (picks[Other(c, seat.SlotID)] == DisarmTraps)
                {
                    c.Log($"  {seat.Name}'s traps are disarmed before they're set.");
                    continue;
                }
                int n = D3(c);
                var untrapped = Pieces(c).Where(t => !s.Traps.Any(x => x.Slot == seat.SlotID && x.Terrain == Key(t))).ToList();
                List<ITerrain> chosen = await PickPieces(c, seat, untrapped, n, $"Plant Traps: pick up to {n} terrain piece(s) to trap (secret).", forEnemy: true);
                foreach (ITerrain t in chosen) s.Traps.Add(new PrepPiece { Slot = seat.SlotID, Round = prep, Terrain = Key(t) });
                c.Log($"  {seat.Name} plants {chosen.Count} trap(s) in secret.");
            }
            // 2. Disarm Traps: against an earlier round's traps.
            foreach (IPlayerSlotInfo seat in seats.Where(p => picks[p.SlotID] == DisarmTraps))
            {
                int other = Other(c, seat.SlotID);
                if (picks[other] == PlantTraps) continue;
                var rounds = s.Traps.Where(t => t.Slot == other && t.Round < prep).Select(t => t.Round).Distinct().OrderBy(r => r).ToList();
                if (rounds.Count == 0)
                {
                    c.Log($"  {seat.Name}'s Disarm Traps finds nothing to disarm.");
                    continue;
                }
                IPlayerSlotInfo planter = Seat(c, other)!;
                var options = rounds.Select(r => $"Remove the traps from round {r}").ToList();
                string pick = await Ask(c, planter.PlayerID, $"{seat.Name} disarms traps: remove all traps from one of your earlier Plant Traps.",
                    options, "Disarm Traps", options[^1]);
                int which = rounds[Math.Max(0, options.IndexOf(pick))];
                int removed = s.Traps.RemoveAll(t => t.Slot == other && t.Round == which);
                c.Log($"  {planter.Name} removes {removed} trap(s) from round {which}.");
            }
            // 3. Fortify Terrain (openly).
            foreach (IPlayerSlotInfo seat in seats.Where(p => picks[p.SlotID] == FortifyTerrain))
            {
                var difficult = Pieces(c).Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult)
                    && !s.Fortified.Any(f => f.Slot == seat.SlotID && f.Terrain == Key(t))).ToList();
                if (difficult.Count == 0)
                {
                    c.Log($"  {seat.Name} has no Difficult terrain to fortify.");
                    continue;
                }
                int n = D3(c);
                List<ITerrain> chosen = await PickPieces(c, seat, difficult, n, $"Fortify Terrain: pick up to {n} Difficult piece(s) - the enemy sees them.", forEnemy: false);
                if (chosen.Count == 0) continue;
                const string ignore = "Your units ignore its movement penalty";
                const string defence = "Your units inside get +1 to Defense rolls";
                string mode = await Ask(c, seat.PlayerID, "Fortify Terrain: what do the fortified pieces give your units?",
                    new[] { defence, ignore }, "Fortify Terrain", defence);
                foreach (ITerrain t in chosen)
                    s.Fortified.Add(new PrepPiece { Slot = seat.SlotID, Round = prep, Terrain = Key(t), IgnorePenalty = mode == ignore });
                c.Log($"  {seat.Name} fortifies {string.Join(", ", chosen.Select(Label))} - {mode.ToLowerInvariant()}.");
            }
            // 4. Sabotage Terrain (in secret), against the other side's fortifications.
            foreach (IPlayerSlotInfo seat in seats.Where(p => picks[p.SlotID] == SabotageTerrain))
            {
                int other = Other(c, seat.SlotID);
                var theirs = s.Fortified.Where(f => f.Slot == other && !s.Sabotaged.Any(x => x.Slot == seat.SlotID && x.Terrain == f.Terrain))
                    .Select(f => PieceByKey(c, f.Terrain)).OfType<ITerrain>().DistinctBy(Key).ToList();
                if (theirs.Count == 0)
                {
                    c.Log($"  {seat.Name}'s Sabotage Terrain finds nothing fortified.");
                    continue;
                }
                List<ITerrain> chosen = await PickPieces(c, seat, theirs, 1, "Sabotage Terrain: pick one fortified piece (secret).", forEnemy: true);
                foreach (ITerrain t in chosen) s.Sabotaged.Add(new PrepPiece { Slot = seat.SlotID, Round = prep, Terrain = Key(t) });
                if (chosen.Count > 0) c.Log($"  {seat.Name} sabotages a fortified piece in secret.");
            }
            Save(c, s);
            Refresh(c);
            // 5. Move Pathfinders.
            foreach (IPlayerSlotInfo seat in seats.Where(p => picks[p.SlotID] == MovePathfinders))
                await MovePathfinder(c, s, seat);
            // 6. Shoot Pathfinders: queued for the shooter's first turn.
            foreach (IPlayerSlotInfo seat in seats.Where(p => picks[p.SlotID] == ShootPathfinders))
                await QueueShot(c, s, seat);
            Save(c, s);
        }

        /// <summary>What a bot picks: shoot a pathfinder it can hit, disarm known traps, sabotage fortifications, fortify
        /// once, else plant traps.</summary>
        private static string BotTactic(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat, int prep)
        {
            int other = Other(c, seat.SlotID);
            var good = new List<string>();
            if (s.Pathfinders.Any(p => p.Value == other) && ShotsFor(c, s, seat).Count > 0) good.Add(ShootPathfinders);
            if (s.Traps.Any(t => t.Slot == other)) good.Add(DisarmTraps);
            if (s.Fortified.Any(f => f.Slot == other) && !s.Sabotaged.Any(x => x.Slot == seat.SlotID)) good.Add(SabotageTerrain);
            if (!s.Fortified.Any(f => f.Slot == seat.SlotID) && Pieces(c).Any(t => t.TerrainType.HasFlag(ETerrainType.Difficult))) good.Add(FortifyTerrain);
            if (!s.Pathfinders.Any(p => p.Value == seat.SlotID)) good.Add(MovePathfinders);
            good.Add(PlantTraps);
            // A bot can't see the other side's pick either, so it mixes them: a counter it knows is useful first, at random.
            return good[c.Rng.Next(good.Count)];
        }

        public const string DonePicking = "Done";

        private static async Task<List<ITerrain>> PickPieces(IGameContext c, IPlayerSlotInfo seat, List<ITerrain> pool, int count,
            string question, bool forEnemy)
        {
            var chosen = new List<ITerrain>();
            // A bot picks the pieces nearest the middle of the table (where the enemy will move), or its own side for
            // fortifications.
            ITeam? team = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(seat.PlayerID));
            ZoneRect? zone = team != null ? GameProgressUtilities.DeploymentZoneOf(c.GameDataStore, team.TeamNumber) : null;
            float zx = zone != null ? (zone.Left + zone.Right) / 2f : W / 2f, zz = zone != null ? (zone.Bottom + zone.Top) / 2f : H / 2f;
            List<ITerrain> Ranked() => pool.Where(t => !chosen.Contains(t)).OrderBy(t =>
            {
                (float x, float z) = Centre(t);
                return forEnemy ? Dist(x, z, W / 2f, H / 2f) : Dist(x, z, zx, zz);
            }).ToList();
            while (chosen.Count < count)
            {
                List<ITerrain> left = Ranked();
                if (left.Count == 0) break;
                var labels = left.Select(Label).ToList();
                var options = labels.Append(DonePicking).ToList();
                string pick = await Ask(c, seat.PlayerID, $"{question} ({chosen.Count} of {count} picked)", options, "Pick terrain", options[0]);
                int i = labels.IndexOf(pick);
                if (i < 0) break;
                chosen.Add(left[i]);
            }
            return chosen;
        }

        private static async Task MovePathfinder(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat)
        {
            var units = c.TableState.Units.Objects.OfType<UnitData>()
                .Where(u => u.PlayerID.Equals(seat.PlayerID) && u.GetIsAlive() && u.GetIsOnBattlefield() && !ReserveRules.IsInReserve(u)
                            && !u.Tokens.HasToken(Rules.Foundation.TokenType.EmbarkedIn))
                .OrderByDescending(u => u.Models.Max(m => m.Position.z * 0f + 1f)).ToList();
            if (units.Count == 0) return;
            // A bot moves its unit nearest the middle of the table.
            units = units.OrderBy(u => u.Models.Where(m => m.GetIsAlive())
                .Min(m => Dist(m.Position.x, m.Position.z, W / 2f, H / 2f))).ToList();
            var labels = units.Select((u, i) => $"{u.Name}" + (units.Take(i).Any(o => o.Name == u.Name) ? $" ({i + 1})" : "")).ToList();
            string pick = await Ask(c, seat.PlayerID, "Move Pathfinders: pick a deployed unit to place again within 6\" of where it stands.",
                labels, "Move Pathfinders", labels[0]);
            UnitData unit = units[Math.Max(0, labels.IndexOf(pick))];
            var living = unit.ModelBindings.Where(b => b.GetValue().GetIsAlive()).ToList();
            var wholeTable = new RectangularZone(0f, W, 0f, H);
            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"Move Pathfinders: {unit.Name}", wholeTable, living,
                maxDistanceFromStartInches: 6f, displayName: $"Placing {unit.Name} (pathfinders, within 6\")");
            List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard.RequestClearPlacement(c, request);
            foreach (PlacedObjectEntry<ModelData> p in placements)
            {
                p.Binding.GetValue().SetPosition(p.Position);
                FacingRules.ApplyPlaced(p.Binding.GetValue(), p.Facing, c.Settings);
            }
            s.Pathfinders[unit.ID.ID.ToString()] = seat.SlotID;
            c.Log($"  {seat.Name} moves {unit.Name} as pathfinders.");
            await c.Announce($"{unit.Name} moves up as pathfinders", Olive, EBannerTier.Toast);
        }

        /// <summary>Each of the seat's units with each enemy pathfinder it could shoot now (range and sight).</summary>
        private static List<(UnitData Shooter, UnitData Target)> ShotsFor(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat)
        {
            int other = Other(c, seat.SlotID);
            var targets = s.Pathfinders.Where(p => p.Value == other)
                .Select(p => c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == p.Key)).OfType<UnitData>()
                .Where(u => u.GetIsAlive()).ToList();
            var result = new List<(UnitData, UnitData)>();
            if (targets.Count == 0) return result;
            foreach (UnitData shooter in c.TableState.Units.Objects.OfType<UnitData>()
                         .Where(u => u.PlayerID.Equals(seat.PlayerID) && u.GetIsAlive() && u.GetIsOnBattlefield() && !ReserveRules.IsInReserve(u)))
            {
                if (UnitPsychology.BindingOf(c, shooter) is not { } binding) continue;
                foreach (UnitData target in targets)
                {
                    bool can;
                    try { can = ChooseRangedAttackStage.HasAnyFireableTarget(binding, c, onlyTarget: target.ID); }
                    catch (Exception) { can = false; }
                    if (can) result.Add((shooter, target));
                }
            }
            return result;
        }

        private static async Task QueueShot(IGameContext c, TacticalPhaseState s, IPlayerSlotInfo seat)
        {
            var shots = ShotsFor(c, s, seat);
            if (shots.Count == 0)
            {
                c.Log($"  {seat.Name}'s Shoot Pathfinders: no unit of theirs can see an enemy pathfinder in range.");
                return;
            }
            var labels = shots.Select(x => $"{x.Shooter.Name} shoots {x.Target.Name}").ToList();
            var unique = labels.Select((l, i) => labels.Take(i).Contains(l) ? $"{l} ({i + 1})" : l).ToList();
            string pick = await Ask(c, seat.PlayerID, "Shoot Pathfinders: pick who shoots which pathfinder. The shot is taken at the start of your first turn.",
                unique.Append(DonePicking).ToList(), "Shoot Pathfinders", unique[0]);
            int i = unique.IndexOf(pick);
            if (i < 0) return;
            s.FreeShots.Add(new FreeShot { Slot = seat.SlotID, Shooter = shots[i].Shooter.ID.ID.ToString(), Target = shots[i].Target.ID.ID.ToString() });
            c.Log($"  {seat.Name}: {shots[i].Shooter.Name} will shoot {shots[i].Target.Name} at the start of their first turn.");
        }

        // ---------------------------------------------------------------- the pathfinder shot in round 1

        /// <summary>At the start of a turn: a queued pathfinder shot for this player becomes the active one (true = take
        /// it now; the turn carries on after it).</summary>
        public static bool StartFreeShot(IGameContext c, PlayerID player)
        {
            if (State(c.GameDataStore) is not { } s || s.FreeShots.Count == 0) return false;
            int slot = SlotOf(c, player);
            FreeShot? shot = s.FreeShots.FirstOrDefault(f => f.Slot == slot);
            if (shot == null) return false;
            s.FreeShots.Remove(shot);
            s.Active = shot;
            Save(c, s);
            return true;
        }

        public static bool HasActiveShot(IGameContext c) => State(c.GameDataStore) is { Active: not null };

        /// <summary>The active pathfinder shot (shooter and target), taken by the directive shooting stage.</summary>
        public static (DataBinding<UnitData> Shooter, UnitID Target)? TakeActiveShot(IGameContext c)
        {
            if (State(c.GameDataStore) is not { Active: { } shot } s) return null;
            s.Active = null;
            Save(c, s);
            IUnit? shooter = c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == shot.Shooter);
            IUnit? target = c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == shot.Target);
            if (shooter == null || target == null || !shooter.GetIsAlive() || !target.GetIsAlive()) return null;
            if (UnitPsychology.BindingOf(c, shooter) is not { } binding) return null;
            c.Log($"Shoot Pathfinders: {shooter.Name} shoots at {target.Name}.");
            return (binding, target.ID);
        }

        // ---------------------------------------------------------------- traps and sabotage during the game

        /// <summary>After a unit moves: an enemy trap within 1" of its path goes off (D3 wounds, the trap goes); an enemy
        /// sabotage on a piece it moved into goes off the first time.</summary>
        public static async Task AfterMove(IGameContext c, IUnit unit, IReadOnlyList<(Position From, IReadOnlyList<Position> Path)> moves)
        {
            if (!On(c, EAdvancedRules.BattlePreparations) || !unit.GetIsAlive() || State(c.GameDataStore) is not { } s) return;
            if (s.Traps.Count == 0 && s.Sabotaged.Count == 0) return;
            int slot = SlotOf(c, unit.PlayerID);
            if (slot < 0) return;
            float reach = unit.Models.Select(m => m.BaseShape.BoundingRadiusInches).DefaultIfEmpty(0.5f).Max();
            bool changed = false;
            foreach (PrepPiece trap in s.Traps.Where(t => t.Slot != slot).ToList())
            {
                if (PieceByKey(c, trap.Terrain) is not { } piece || !Passes(piece, moves, reach + 1f)) continue;
                s.Traps.Remove(trap);
                changed = true;
                await Wound(c, unit, $"{unit.Name} sets off a trap", "Plant Traps");
                if (!unit.GetIsAlive()) break;
            }
            foreach (PrepPiece sab in s.Sabotaged.Where(t => t.Slot != slot).ToList())
            {
                if (!unit.GetIsAlive()) break;
                if (PieceByKey(c, sab.Terrain) is not { } piece || !Passes(piece, moves, 0f)) continue;
                s.Sabotaged.Remove(sab);
                changed = true;
                await Wound(c, unit, $"{unit.Name} moves into sabotaged terrain", "Sabotage Terrain");
            }
            if (changed) Save(c, s);
        }

        /// <summary>Whether any model's move comes within <paramref name="inflate"/> of the piece (0 = moves into it).</summary>
        private static bool Passes(ITerrain piece, IReadOnlyList<(Position From, IReadOnlyList<Position> Path)> moves, float inflate)
        {
            foreach ((Position from, IReadOnlyList<Position> path) in moves)
            {
                Position prev = from;
                foreach (Position q in path)
                {
                    var a = new Float2(prev.x, prev.z);
                    var b = new Float2(q.x, q.z);
                    if (inflate > 0f ? piece.Shape.DoesPathIntersectZone(a, b, inflate) : piece.Shape.DoesPathIntersectZone(a, b)) return true;
                    prev = q;
                }
            }
            return false;
        }

        private static async Task Wound(IGameContext c, IUnit unit, string what, string source)
        {
            int wounds = D3(c);
            c.Log($"{source}: {what} - {wounds} wound{(wounds == 1 ? "" : "s")}.");
            await c.Announce($"{what}! ({wounds} wound{(wounds == 1 ? "" : "s")})", new TextColor(230, 90, 60, 255), EBannerTier.Toast);
            await CasualtyPresentation.ApplyUnitWounds(c, unit, wounds);
        }

        // ---------------------------------------------------------------- fortified terrain

        /// <summary>Fortified pieces whose side ignores their movement penalty (terrain key -> team), for the move check,
        /// which has no game in hand. Rebuilt from the saved state at the start of every round and after the preparations;
        /// emptied in any game without them.</summary>
        private static readonly Dictionary<string, HashSet<int>> IgnoreFor = new();
        private static readonly object Gate = new();

        public static void Refresh(IGameContext c)
        {
            lock (Gate)
            {
                IgnoreFor.Clear();
                if (!On(c, EAdvancedRules.BattlePreparations) || State(c.GameDataStore) is not { } s) return;
                foreach (PrepPiece f in s.Fortified.Where(f => f.IgnorePenalty))
                {
                    if (Seat(c, f.Slot) is not { } seat) continue;
                    if (!IgnoreFor.TryGetValue(f.Terrain, out HashSet<int>? teams)) IgnoreFor[f.Terrain] = teams = new HashSet<int>();
                    teams.Add(seat.TeamNumber);
                }
            }
            ArmyTerrain.RegisterTeams(c);
        }

        /// <summary>True when the moving model's side fortified this piece to ignore its movement penalty.</summary>
        public static bool IgnoresPenalty(ITerrain terrain, IModel mover)
        {
            lock (Gate)
            {
                if (IgnoreFor.Count == 0) return false;
                return IgnoreFor.TryGetValue(Key(terrain), out HashSet<int>? teams)
                       && ArmyTerrain.TeamOfModel(mover) is int team && teams.Contains(team);
            }
        }

        public static bool AnyIgnore { get { lock (Gate) return IgnoreFor.Count > 0; } }

        /// <summary>"All friendly units inside of it get +1 to Defense rolls": most of the unit's models inside a piece its
        /// side fortified for Defense.</summary>
        public static IReadOnlyList<GrantedModifier> SaveModifiers(IGameContext c, IUnit defender)
        {
            if (!On(c, EAdvancedRules.BattlePreparations) || State(c.GameDataStore) is not { Fortified.Count: > 0 } s)
                return Array.Empty<GrantedModifier>();
            int slot = SlotOf(c, defender.PlayerID);
            var living = defender.Models.Where(m => m.GetIsAlive()).ToList();
            if (slot < 0 || living.Count == 0) return Array.Empty<GrantedModifier>();
            foreach (PrepPiece f in s.Fortified.Where(f => !f.IgnorePenalty && f.Slot == slot))
            {
                if (PieceByKey(c, f.Terrain) is not { } piece) continue;
                int inside = living.Count(m => piece.Shape.IsPointWithinZone(new Float2(m.Position.x, m.Position.z)));
                if (inside * 2 > living.Count) return new[] { new GrantedModifier("Fortified terrain", 1) };
            }
            return Array.Empty<GrantedModifier>();
        }

        // ---------------------------------------------------------------- panel

        /// <summary>The Mission panel's lines: the first is the title. Traps and sabotage are shown only to their own side
        /// (<paramref name="viewerSlots"/> null = everyone, e.g. a hot-seat or bots-only game).</summary>
        public static IReadOnlyList<string> PanelLines(IReadableGameDataStore store, GameSettings settings, IReadOnlyCollection<int>? viewerSlots,
            Func<int, string> seatName, Func<string, string?> unitName)
        {
            var lines = new List<string>();
            if (!BooksHaveIt(settings.GameSystem) || State(store) is not { } s) return lines;
            lines.Add("Tactical Phase");
            if (s.Markers.Count > 0 && !s.MarkersRemoved)
                lines.Add("Forward positions: deploy fully within 3\" of your markers, or within 12\" of your far corner.");
            foreach (string l in s.Log) lines.Add("Preparations " + l);
            foreach (IGrouping<int, PrepPiece> g in s.Fortified.GroupBy(f => f.Slot))
                lines.Add($"{seatName(g.Key)} fortified: "
                          + string.Join(", ", g.Select(f => (f.Terrain.Split('|')[0] is { Length: > 0 } n ? n : "terrain") + (f.IgnorePenalty ? " (no move penalty)" : " (+1 Defense)"))));
            foreach (int slot in s.Traps.Select(t => t.Slot).Concat(s.Sabotaged.Select(t => t.Slot)).Distinct().OrderBy(x => x))
            {
                bool mine = viewerSlots == null || viewerSlots.Contains(slot);
                int traps = s.Traps.Count(t => t.Slot == slot), sab = s.Sabotaged.Count(t => t.Slot == slot);
                if (mine)
                {
                    if (traps > 0) lines.Add($"{seatName(slot)}'s traps: " + string.Join(", ", s.Traps.Where(t => t.Slot == slot).Select(t => t.Terrain.Split('|')[0] is { Length: > 0 } n ? n : "terrain")));
                    if (sab > 0) lines.Add($"{seatName(slot)}'s sabotage: " + string.Join(", ", s.Sabotaged.Where(t => t.Slot == slot).Select(t => t.Terrain.Split('|')[0] is { Length: > 0 } n ? n : "terrain")));
                }
                else if (traps + sab > 0) lines.Add($"{seatName(slot)} has secret traps or sabotage on the table.");
            }
            foreach (var p in s.Pathfinders) lines.Add($"Pathfinders: {unitName(p.Key) ?? "a unit"} ({seatName(p.Value)})");
            foreach (FreeShot f in s.FreeShots)
                lines.Add($"{seatName(f.Slot)}: {unitName(f.Shooter) ?? "a unit"} shoots {unitName(f.Target) ?? "a pathfinder"} at the start of their first turn");
            return lines.Count == 1 ? Array.Empty<string>() : lines;
        }
    }
}
