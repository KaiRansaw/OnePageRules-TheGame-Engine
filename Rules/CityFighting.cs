using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>City Fighting's saved state: burning buildings and what happened.</summary>
    public sealed class CityFightingState
    {
        /// <summary>Burning buildings, by <see cref="CityFighting.Key"/>.</summary>
        public List<string> Burning { get; set; } = new();
        /// <summary>Cover pieces destroyed so far (names, for the panel).</summary>
        public List<string> Destroyed { get; set; } = new();
        /// <summary>The last few things that happened, newest last (the Mission panel).</summary>
        public List<string> Log { get; set; } = new();
    }

    /// <summary>
    /// City Fighting (GF: Firefight / AoF: Skirmish Advanced Rulebooks 3.6.0 p47, Kai 2026-11-26). "City Fighting rules are
    /// optional rules designed to give you more flexibility when fighting in dense urban terrain. Pick any of the
    /// following." Part 1 (Build 3.57): Cornered Units, Destructible Cover, Thrown Objects, Buildings on Fire.
    ///
    /// <para>What counts as a building (the book never says): a piece of the Building or Ruins kind, or any solid
    /// building (house rules C, "City Fighting"). Small Cover = a Cover piece that is not a building and is at most 3.5"
    /// on its long side (the book's "small terrain such as crates and barrels (1"x1" to 3"x3")" and its 1"x3"
    /// barricades).</para>
    /// </summary>
    public static class CityFighting
    {
        public const string Chapter = "City Fighting";

        public const string CorneredUnitsName = "Cornered Units";
        public const string DestructibleCoverName = "Destructible Cover";
        public const string ThrownObjectsName = "Thrown Objects";
        public const string BuildingsOnFireName = "Buildings on Fire";

        public static IReadOnlyList<(string Name, EAdvancedRules Flag)> All { get; } = new[]
        {
            (CorneredUnitsName, EAdvancedRules.CorneredUnits),
            (DestructibleCoverName, EAdvancedRules.DestructibleCover),
            (ThrownObjectsName, EAdvancedRules.ThrownObjects),
            (BuildingsOnFireName, EAdvancedRules.BuildingsOnFire),
        };

        public static bool BooksHaveIt(EGameSystem s) => s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public const string Intro = "City Fighting rules are optional rules designed to give you more flexibility when fighting in dense urban terrain. Pick any of the following.";

        public static string BookText(string name) => name switch
        {
            CorneredUnitsName => "When a unit that is within 1\" of a building and is circled by enemies so that it can't move past them, melee attacks made against it get +1 to hit rolls and AP(+1).",
            DestructibleCoverName => "When a unit shoots at a target that is behind a piece of small Cover terrain and within 1\" of it, roll one die.\n"
                + "On a result of 1, the piece of Cover terrain is destroyed and is removed from the table.",
            ThrownObjectsName => "Units that are on top of buildings may throw bricks and stones at enemies, instead of shooting their weapons.\n"
                + "Pick one enemy unit within 3\" of the bottom of the building and within 6\" horizontal distance, and roll one die.\n"
                + "On a 4+ the target takes one hit.",
            BuildingsOnFireName => "Units that are within 3\" of a building may try to set it on fire, instead of shooting their weapons.\n"
                + "Roll one die, on a 6 the building is set on fire, and at the end of each round all units within that building must take a Dangerous Terrain Test.\n"
                + "Then roll one die, on a result of 1 another random building within 6\" is set on fire, and on a result of 6 the fire stops.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            CorneredUnitsName => "A unit within 1\" of a building (a Building or Ruins piece) with every way out blocked - by the building, impassable ground or enemies (at least one) - is cornered: melee attacks against it get +1 to hit and AP(+1).",
            DestructibleCoverName => "After a unit shoots, one die for each small Cover piece (3.5\" or less) that stood between it and a target within 1\" of the piece: on a 1 the piece is removed.",
            ThrownObjectsName => "A unit on top of a building gets \"Throw objects\" on its action menu, instead of shooting: pick an enemy within 3\" of the building's foot and 6\" across, one die, 4+ = one hit.",
            BuildingsOnFireName => "A unit within 3\" of a building gets \"Set a building on fire\" instead of shooting: 6 = it burns. Each round end, units in a burning building take a Dangerous Terrain Test, then a die: 1 = a building within 6\" catches fire, 6 = the fire goes out.",
            _ => "",
        };

        public static bool IsOn(GameSettings s, EAdvancedRules flag) => BooksHaveIt(s.GameSystem) && AdvancedRules.IsOn(s, flag);

        private static bool On(IGameContext c, EAdvancedRules flag) => IsOn(c.Settings, flag);

        // ---------------------------------------------------------------- state

        private static readonly ConditionalWeakTable<IReadableGameDataStore, CityFightingState> Pending = new();

        public static CityFightingState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.CityFighting
            ?? (Pending.TryGetValue(store, out CityFightingState? s) ? s : null);

        private static CityFightingState StateOf(IGameContext c) => State(c.GameDataStore) ?? new CityFightingState();

        private static void Save(IGameContext c, CityFightingState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.CityFighting = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        private static void Note(IGameContext c, CityFightingState s, string line)
        {
            s.Log.Add(line);
            while (s.Log.Count > 6) s.Log.RemoveAt(0);
            c.Log("City Fighting - " + line);
            if (AuditBots) Console.WriteLine("[CITY AUDIT] " + line);
        }

        // ---------------------------------------------------------------- pieces

        /// <summary>A building for City Fighting: a Building or Ruins piece, or a solid building (house rules C).</summary>
        public static bool IsBuilding(ITerrain t) =>
            SolidBuildings.Is(t)
            || string.Equals(t.Kind, "Building", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Kind, "Ruins", StringComparison.OrdinalIgnoreCase);

        public const float SmallCoverLongSide = 3.5f;

        /// <summary>A small Cover piece: Cover, not a building, at most 3.5" on its long side.</summary>
        public static bool IsSmallCover(ITerrain t)
        {
            if (!t.TerrainType.HasFlag(ETerrainType.Cover) || IsBuilding(t) || IsMarker(t)) return false;
            if (t.Shape is not IBoundedZone b) return false;
            ZoneBounds z = b.Bounds;
            return Math.Max(z.Right - z.Left, z.Top - z.Bottom) <= SmallCoverLongSide + 0.01f;
        }

        private static bool IsMarker(ITerrain t) => LivingSpells.IsMarker(t) || TacticalPhase.IsMarker(t);

        /// <summary>A piece's name and bounds: what names it in the saved state.</summary>
        public static string Key(ITerrain t)
        {
            ZoneBounds? b = t.Shape is IBoundedZone bz ? bz.Bounds : null;
            return b == null ? t.Name ?? "" : $"{t.Name}|{b.Value.Left:0.00},{b.Value.Right:0.00},{b.Value.Bottom:0.00},{b.Value.Top:0.00}";
        }

        private static IReadOnlyList<ITerrain> Terrain(IGameContext c) => c.TableState.Terrain.Objects.ToList();

        private static List<ITerrain> Buildings(IGameContext c) => Terrain(c).Where(IsBuilding).ToList();

        /// <summary>A saved key read back as the panel's label ("Building at (30", 12")").</summary>
        public static string LabelOfKey(string key)
        {
            string[] parts = key.Split('|');
            string what = parts[0].Length > 0 ? parts[0] : "building";
            if (parts.Length < 2) return what;
            float[] b = parts[1].Split(',').Select(v => float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f).ToArray();
            return b.Length == 4 ? $"{what} at ({(b[0] + b[1]) / 2f:0}\", {(b[2] + b[3]) / 2f:0}\")" : what;
        }

        /// <summary>What the panel calls a piece: its name, or its kind, and where it is.</summary>
        public static string Label(ITerrain t)
        {
            string what = !string.IsNullOrWhiteSpace(t.Name) ? t.Name : !string.IsNullOrWhiteSpace(t.Kind) ? t.Kind : "piece";
            return t.Shape is IBoundedZone b
                ? $"{what} at ({(b.Bounds.Left + b.Bounds.Right) / 2f:0}\", {(b.Bounds.Bottom + b.Bounds.Top) / 2f:0}\")"
                : what;
        }

        private static IEnumerable<IModel> Living(IUnit u) => u.Models.Where(m => m.GetIsAlive());

        private static Float2 Flat(IModel m) => new Float2(m.Position.x, m.Position.z);

        /// <summary>The gap between two pieces (edge to edge, by their bounds).</summary>
        private static float Gap(ITerrain a, ITerrain b)
        {
            if (a.Shape is not IBoundedZone ab || b.Shape is not IBoundedZone bb) return float.MaxValue;
            float dx = Math.Max(0f, Math.Max(ab.Bounds.Left - bb.Bounds.Right, bb.Bounds.Left - ab.Bounds.Right));
            float dz = Math.Max(0f, Math.Max(ab.Bounds.Bottom - bb.Bounds.Top, bb.Bounds.Bottom - ab.Bounds.Top));
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        // ================================================================ Cornered Units

        public const int CorneredDirections = 16;
        public const float CorneredProbeInches = 6f;

        /// <summary>
        /// "Within 1" of a building and circled by enemies so that it can't move past them": a living model within 1" of a
        /// building, and in every one of 16 directions out from the unit, a 6" walk is stopped by the building, other
        /// impassable ground, the table edge, or an enemy (a probe within 1" of an enemy's base) - with at least one
        /// direction stopped by an enemy (house rules C, "City Fighting").
        /// </summary>
        public static bool Cornered(IGameContext c, IUnit unit)
        {
            List<IModel> mine = Living(unit).ToList();
            if (mine.Count == 0) return false;
            IReadOnlyList<ITerrain> terrain = Terrain(c);
            List<ITerrain> buildings = terrain.Where(IsBuilding).ToList();
            if (buildings.Count == 0 || !mine.Any(m => TerrainProximityQueries.IsModelWithinInches(m, buildings, 1f))) return false;

            List<IModel> enemies = PoliticalIntrigue.Enemies(c, unit.PlayerID).SelectMany(Living).ToList();
            if (enemies.Count == 0) return false;
            List<ITerrain> walls = terrain.Where(t => IsBuilding(t) || t.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();

            float cx = mine.Average(m => m.Position.x), cz = mine.Average(m => m.Position.z);
            float reach = mine.Max(m => MathF.Sqrt((m.Position.x - cx) * (m.Position.x - cx) + (m.Position.z - cz) * (m.Position.z - cz))
                                        + m.BaseShape.BoundingRadiusInches);
            float W = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, H = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            bool enemyStops = false;
            for (int i = 0; i < CorneredDirections; i++)
            {
                double a = i * 2 * Math.PI / CorneredDirections;
                float dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
                bool stopped = false;
                for (float s = 0.5f; s <= CorneredProbeInches + 0.01f; s += 0.5f)
                {
                    float px = cx + dx * (reach + s), pz = cz + dz * (reach + s);
                    var p = new Float2(px, pz);
                    if (px < 0f || pz < 0f || px > W || pz > H) { stopped = true; break; }
                    if (walls.Any(t => t.IsPointWithinZone(p))) { stopped = true; break; }
                    if (enemies.Any(e => Dist(p, Flat(e)) <= e.BaseShape.BoundingRadiusInches + 1f)) { stopped = true; enemyStops = true; break; }
                }
                if (!stopped) return false;
            }
            return enemyStops;
        }

        private static float Dist(Float2 a, Float2 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>Cornered Units: +1 to hit in melee against a cornered unit.</summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IGameContext c, IUnit defender, bool melee, bool spell)
        {
            if (!melee || spell || !On(c, EAdvancedRules.CorneredUnits) || !Cornered(c, defender)) return Array.Empty<GrantedModifier>();
            c.Log($"City Fighting - Cornered Units: {defender.Name} is cornered (+1 to hit, AP(+1) in melee).");
            if (AuditBots) Console.WriteLine($"[CITY AUDIT] Cornered: {defender.Name}");
            return new[] { new GrantedModifier(CorneredUnitsName, 1) };
        }

        /// <summary>Cornered Units: AP(+1) in melee against a cornered unit.</summary>
        public static int ArmorPenetrationBonus(IGameContext c, IUnit defender, bool melee, bool spell) =>
            melee && !spell && On(c, EAdvancedRules.CorneredUnits) && Cornered(c, defender) ? 1 : 0;

        // ================================================================ Destructible Cover

        private sealed class ShotLog { public readonly List<(string Key, string Target, string Shooter)> Pieces = new(); }

        private static readonly ConditionalWeakTable<IReadableGameDataStore, ShotLog> Shots = new();

        /// <summary>
        /// The small Cover pieces a shot at <paramref name="target"/> passes: within 1" of a living target model, and on the
        /// line from the shooter's centre to that model (the model counts as behind a piece it stands in).
        /// </summary>
        public static List<ITerrain> CoverBehind(IGameContext c, IUnit shooter, IUnit target)
        {
            var found = new List<ITerrain>();
            List<IModel> from = Living(shooter).ToList();
            if (from.Count == 0) return found;
            var origin = new Float2(from.Average(m => m.Position.x), from.Average(m => m.Position.z));
            foreach (ITerrain piece in Terrain(c).Where(IsSmallCover))
            {
                var one = new List<ITerrain> { piece };
                foreach (IModel m in Living(target))
                {
                    if (!TerrainProximityQueries.IsModelWithinInches(m, one, 1f)) continue;
                    if (piece.IsPointWithinZone(Flat(m)) || piece.Shape.DoesPathIntersectZone(origin, Flat(m)))
                    {
                        found.Add(piece);
                        break;
                    }
                }
            }
            return found;
        }

        /// <summary>Called as a shot's cover is checked: remembers the small Cover between shooter and target.</summary>
        public static void NoteShot(IGameContext c, IUnit shooter, IUnit target)
        {
            if (!On(c, EAdvancedRules.DestructibleCover)) return;
            ShotLog log = Shots.GetOrCreateValue(c.GameDataStore);
            foreach (ITerrain piece in CoverBehind(c, shooter, target))
            {
                string key = Key(piece);
                if (!log.Pieces.Any(p => p.Key == key)) log.Pieces.Add((key, target.Name, shooter.Name));
            }
        }

        /// <summary>After the shooting: one die for each piece noted, a 1 removes it from the table.</summary>
        public static void AfterShooting(IGameContext c)
        {
            if (!Shots.TryGetValue(c.GameDataStore, out ShotLog? log) || log.Pieces.Count == 0) return;
            var pieces = log.Pieces.ToList();
            log.Pieces.Clear();
            if (!On(c, EAdvancedRules.DestructibleCover)) return;
            CityFightingState s = StateOf(c);
            bool changed = false;
            foreach ((string key, string target, string shooter) in pieces)
            {
                DataReference? piece = c.GameDataStore.GetAllDataReferences<TerrainData>()
                    .FirstOrDefault(r => c.GameDataStore.IsValid(r, out _) && Key(c.GameDataStore.GetValue<TerrainData>(r)) == key);
                if (piece == null) continue;
                TerrainData data = c.GameDataStore.GetValue<TerrainData>(piece.Value);
                int roll = c.DiceRoller.RollDecisiveFace();
                if (roll != 1)
                {
                    c.Log($"City Fighting - Destructible Cover: {shooter} shot at {target} behind {Label(data)} - rolled {roll}, it stands.");
                    if (AuditBots) Console.WriteLine($"[CITY AUDIT] Cover stands: {Label(data)}");
                    continue;
                }
                string label = Label(data);
                c.GameDataStore.Destroy(piece.Value);
                s.Destroyed.Add(label);
                Note(c, s, $"Destructible Cover: {shooter} shot at {target} behind {label} - rolled 1, the cover is destroyed.");
                changed = true;
            }
            if (changed) Save(c, s);
        }

        // ================================================================ Thrown Objects / Buildings on Fire

        public const string ThrowOption = "Throw objects (City Fighting)";
        public const string FireOption = "Set a building on fire (City Fighting)";

        public static bool IsActionOption(string option) => option == ThrowOption || option == FireOption;

        /// <summary>Test-only (FDG_CITY_AUDIT=1): bots take the city actions whenever offered, and every City Fighting event
        /// is logged with "[CITY AUDIT]" so a sweep can check them.</summary>
        public static bool AuditBots { get; } = Environment.GetEnvironmentVariable("FDG_CITY_AUDIT") == "1";

        /// <summary>The building most of the unit's living models stand on top of (over 1" up), or null.</summary>
        public static ITerrain? RoofOf(IGameContext c, IUnit unit)
        {
            List<IModel> mine = Living(unit).ToList();
            if (mine.Count == 0) return null;
            IReadOnlyList<ITerrain> terrain = Terrain(c);
            var counts = new Dictionary<ITerrain, int>();
            foreach (IModel m in mine)
            {
                Float2 p = Flat(m);
                if (Climbing.StandingHeight(terrain, p) <= 1f) continue;
                ITerrain? under = terrain.Where(IsBuilding).FirstOrDefault(t => t.IsPointWithinZone(p));
                if (under != null) counts[under] = counts.GetValueOrDefault(under) + 1;
            }
            KeyValuePair<ITerrain, int> best = counts.OrderByDescending(kv => kv.Value).FirstOrDefault();
            return best.Key != null && best.Value * 2 > mine.Count ? best.Key : null;
        }

        /// <summary>Thrown Objects targets: an enemy unit with a model at ground level within 3" of the building's foot,
        /// and within 6" across (horizontal distance) of the thrower.</summary>
        public static List<IUnit> ThrowTargets(IGameContext c, IUnit thrower, ITerrain building)
        {
            IReadOnlyList<ITerrain> terrain = Terrain(c);
            var one = new List<ITerrain> { building };
            List<IModel> mine = Living(thrower).ToList();
            return PoliticalIntrigue.Enemies(c, thrower.PlayerID).Where(e => Living(e).Any(m =>
                    Climbing.StandingHeight(terrain, Flat(m)) <= 1f
                    && TerrainProximityQueries.IsModelWithinInches(m, one, 3f)
                    && mine.Any(t => Dist(Flat(t), Flat(m)) - t.BaseShape.BoundingRadiusInches - m.BaseShape.BoundingRadiusInches <= 6f + 0.001f)))
                .ToList();
        }

        /// <summary>Buildings on Fire: the buildings within 3" of the unit that aren't burning yet.</summary>
        public static List<ITerrain> FireTargets(IGameContext c, IUnit unit)
        {
            CityFightingState s = StateOf(c);
            List<IModel> mine = Living(unit).ToList();
            return Buildings(c).Where(b => !s.Burning.Contains(Key(b))
                    && mine.Any(m => TerrainProximityQueries.IsModelWithinInches(m, new List<ITerrain> { b }, 3f)))
                .ToList();
        }

        /// <summary>
        /// The action-menu entries "instead of shooting": offered while the unit could still shoot this activation - it
        /// hasn't attacked, isn't embarked or Shaken, and hasn't moved further than an Advance.
        /// </summary>
        public static List<(string Option, string Text)> ActionOptions(IGameContext c, IUnitActionContext context)
        {
            var list = new List<(string, string)>();
            if (!BooksHaveIt(c.Settings.GameSystem)) return list;
            bool throws = On(c, EAdvancedRules.ThrownObjects), fires = On(c, EAdvancedRules.BuildingsOnFire);
            if (!throws && !fires) return list;
            IUnit unit = context.ActivatingUnit.GetValue();
            if (context.HasAttacked || context.QuestHeldWithoutShooting || TransportUtilities.IsEmbarked(unit)
                || unit.Tokens.HasToken(TokenType.Shaken)) return list;
            if (context.HasMoved && !context.MoveDistance.LessThanOrAlmostEqual(context.MoveShootAllowance)) return list;
            if (throws && RoofOf(c, unit) is { } roof && ThrowTargets(c, unit, roof).Count > 0)
                list.Add((ThrowOption, "Instead of shooting: throw bricks and stones at an enemy within 3\" of this building's foot and 6\" across. One die: 4+ = one hit."));
            if (fires && FireTargets(c, unit).Count > 0)
                list.Add((FireOption, "Instead of shooting: try to set a building within 3\" on fire. One die: 6 = it burns."));
            return list;
        }

        private static async Task<string> Ask(IGameContext c, PlayerID who, string question, IReadOnlyList<string> options, string displayName)
        {
            var request = new StringSelectionRequest(who, question, options.ToList(), new List<StringSelectionRequest.InvalidOption>(),
                displayName: displayName) { Recommended = options[0] };
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            return options.Contains(pick) ? pick : options[0];
        }

        /// <summary>Throw objects: pick the enemy (when there is more than one), one die, 4+ = one hit. True when thrown.</summary>
        public static async Task<bool> Throw(IGameContext c, IUnit thrower)
        {
            if (RoofOf(c, thrower) is not { } roof) return false;
            List<IUnit> targets = ThrowTargets(c, thrower, roof);
            if (targets.Count == 0) return false;
            IUnit target = targets[0];
            if (targets.Count > 1)
            {
                List<string> names = targets.Select((u, i) => $"{u.Name} ({i + 1})").ToList();
                string pick = await Ask(c, thrower.PlayerID, $"{thrower.Name} throws bricks and stones from {Label(roof)}: at which enemy?", names, ThrownObjectsName);
                target = targets[Math.Max(0, names.IndexOf(pick))];
            }
            CityFightingState s = StateOf(c);
            int roll = c.DiceRoller.RollDecisiveFace();
            if (roll >= 4)
            {
                Note(c, s, $"Thrown Objects: {thrower.Name} throws bricks at {target.Name} - rolled {roll}, one hit.");
                Save(c, s);
                await CampaignEventRunner.DealHits(c, target, 1, ThrownObjectsName);
            }
            else
            {
                Note(c, s, $"Thrown Objects: {thrower.Name} throws bricks at {target.Name} - rolled {roll}, missed.");
                Save(c, s);
            }
            return true;
        }

        /// <summary>Set a building on fire: pick it (when there is more than one), one die, 6 = it burns. True when tried.</summary>
        public static async Task<bool> SetOnFire(IGameContext c, IUnit unit)
        {
            List<ITerrain> near = FireTargets(c, unit);
            if (near.Count == 0) return false;
            ITerrain building = near[0];
            if (near.Count > 1)
            {
                List<string> names = near.Select((b, i) => $"{Label(b)} ({i + 1})").ToList();
                string pick = await Ask(c, unit.PlayerID, $"{unit.Name}: which building to set on fire?", names, BuildingsOnFireName);
                building = near[Math.Max(0, names.IndexOf(pick))];
            }
            CityFightingState s = StateOf(c);
            int roll = c.DiceRoller.RollDecisiveFace();
            if (roll == 6)
            {
                s.Burning.Add(Key(building));
                Note(c, s, $"Buildings on Fire: {unit.Name} rolled 6 - {Label(building)} is on fire.");
            }
            else Note(c, s, $"Buildings on Fire: {unit.Name} tried to set {Label(building)} on fire - rolled {roll}, nothing.");
            Save(c, s);
            return true;
        }

        /// <summary>The burning pieces still on the table.</summary>
        public static List<ITerrain> BurningBuildings(IGameContext c)
        {
            CityFightingState? s = State(c.GameDataStore);
            if (s == null || s.Burning.Count == 0) return new();
            return Buildings(c).Where(b => s.Burning.Contains(Key(b))).ToList();
        }

        /// <summary>Whether any living model of the unit is inside the building's footprint.</summary>
        public static bool Inside(IUnit unit, ITerrain building) => Living(unit).Any(m => building.IsPointWithinZone(Flat(m)));

        /// <summary>
        /// End of round: units in each burning building take a Dangerous Terrain Test, then one die per burning building -
        /// 1 = another random building within 6" catches fire, 6 = the fire stops.
        /// </summary>
        public static async Task RoundEnd(IGameContext c)
        {
            if (!On(c, EAdvancedRules.BuildingsOnFire)) return;
            CityFightingState s = StateOf(c);
            List<ITerrain> burning = BurningBuildings(c);
            if (burning.Count == 0) return;
            var services = new GameOperationServices(c);
            foreach (ITerrain b in burning)
            {
                List<IUnit> caught = c.TableState.Units.Objects
                    .Where(u => u.GetIsAlive() && u.GetIsOnBattlefield() && !TransportUtilities.IsEmbarked(u) && Inside(u, b)).Cast<IUnit>().ToList();
                foreach (IUnit u in caught)
                {
                    c.Log($"City Fighting - Buildings on Fire: {u.Name} is in the burning {Label(b)} - Dangerous Terrain Test.");
                    await services.ForceDangerousTerrainTest(u);
                }
            }
            List<ITerrain> all = Buildings(c);
            foreach (ITerrain b in burning)
            {
                string key = Key(b);
                if (!s.Burning.Contains(key)) continue;
                int roll = c.DiceRoller.RollDecisiveFace();
                if (roll == 6)
                {
                    s.Burning.Remove(key);
                    Note(c, s, $"Buildings on Fire: the fire in {Label(b)} stops (rolled 6).");
                }
                else if (roll == 1)
                {
                    List<ITerrain> next = all.Where(o => !s.Burning.Contains(Key(o)) && !ReferenceEquals(o, b) && Gap(o, b) <= 6f + 0.001f).ToList();
                    if (next.Count == 0) { Note(c, s, $"Buildings on Fire: {Label(b)} rolled 1, but no building within 6\" to spread to."); continue; }
                    ITerrain spread = next[(c.DiceRoller.RollDecisiveFace(next.Count) - 1) % next.Count];
                    s.Burning.Add(Key(spread));
                    Note(c, s, $"Buildings on Fire: the fire spreads from {Label(b)} to {Label(spread)} (rolled 1).");
                }
                else c.Log($"City Fighting - Buildings on Fire: {Label(b)} keeps burning (rolled {roll}).");
            }
            Save(c, s);
        }

        // ================================================================ the Mission panel

        public static List<string> PanelLines(GameSettings settings, IReadableGameDataStore store)
        {
            var lines = new List<string>();
            if (!BooksHaveIt(settings.GameSystem)) return lines;
            var on = All.Where(o => IsOn(settings, o.Flag)).Select(o => o.Name).ToList();
            if (on.Count == 0) return lines;
            lines.Add("City Fighting: " + string.Join(", ", on));
            CityFightingState? s = State(store);
            if (s == null) return lines;
            if (s.Burning.Count > 0)
                lines.Add("On fire: " + string.Join("; ", s.Burning.Select(LabelOfKey)));
            if (s.Destroyed.Count > 0) lines.Add($"Cover destroyed: {s.Destroyed.Count}");
            foreach (string l in s.Log.TakeLast(3)) lines.Add(l);
            return lines;
        }
    }
}
