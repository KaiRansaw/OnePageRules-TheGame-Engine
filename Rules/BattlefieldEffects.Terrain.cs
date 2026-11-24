using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Utilities;

namespace FDG
{
    /// <summary>
    /// Battlefield Effects - Terrain &amp; Objective Effects (Advanced Rulebooks 3.6.0: GF p39, GFF p36, AoF p39, AoFS p36).
    /// "When a unit enters a piece of terrain for the first time, roll one die, and the effect lasts the whole game ...
    /// When a unit seizes an objective for the first time roll one die, and the effect lasts the whole game."
    /// </summary>
    public static partial class BattlefieldEffects
    {
        public const string TerrainAndObjectives = "Terrain & Objective Effects";

        public const int Infestation = 3, DeadlySpores = 4, Vigor = 5, BarbedWire = 6;
        public const int BoobyTraps = 3, GravityField = 4, DefensiveBarrier = 5, HolyMonument = 6;

        private const string TerrainBookText = "Players can choose to add extra rules to their terrain features and objective markers by picking any of the ones they like, or by generating them randomly. When a unit enters a piece of terrain for the first time, roll one die, and the effect lasts the whole game: 1-2 Regular Terrain, 3 Alien / Daemonic Infestation (units that activate in or move through this terrain take D3 wounds), 4 Deadly Spores (... take 2D3 hits), 5 Psychic / Magical Vigor (units inside get +1 to hit rolls when shooting), 6 Barbed Wire / Defensive Stakes (units charging into this terrain get -1 to hit rolls when attacking).\nWhen a unit seizes an objective for the first time, roll one die: 1-2 Regular Objective, 3 Booby Traps (units within 3\" count as inside Dangerous Terrain), 4 Gravity / Magical Field (units within 3\" count as inside Difficult Terrain), 5 Defensive Barrier (units shooting at targets within 3\" get -1 to hit), 6 Holy Monument (units within 3\" get +1 to hit in melee).";

        private const string TerrainGameText = "Generated randomly, as the book allows (picking them piece by piece isn't offered). A piece is rolled the first time a unit moves into or through it; an objective the first time it is seized (at the end of a round). Effects are announced and listed on the Mission panel. \"Within 3\"\" of a marker is measured to the nearest model's base; Gravity Field caps a move that starts within 3\".";

        public static bool TerrainOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.TerrainObjectiveEffects);

        private static bool Fantasy(IGameContext c) => Fantasy(c.Settings.GameSystem);

        public static string TerrainEffectName(int code, EGameSystem system) => code switch
        {
            Infestation => Fantasy(system) ? "Daemonic Infestation" : "Alien Infestation",
            DeadlySpores => "Deadly Spores",
            Vigor => Fantasy(system) ? "Magical Vigor" : "Psychic Vigor",
            BarbedWire => Fantasy(system) ? "Defensive Stakes" : "Barbed Wire",
            _ => "Regular Terrain",
        };

        public static string TerrainEffectText(int code) => code switch
        {
            Infestation => "Units that activate in or move through this terrain take D3 wounds.",
            DeadlySpores => "Units that activate in or move through this terrain take 2D3 hits.",
            Vigor => "Units inside of this terrain get +1 to hit rolls when shooting.",
            BarbedWire => "Units charging into this terrain get -1 to hit rolls when attacking.",
            _ => "No special terrain effects are applied to this piece of terrain.",
        };

        public static string ObjectiveEffectName(int code, EGameSystem system) => code switch
        {
            BoobyTraps => "Booby Traps",
            GravityField => Fantasy(system) ? "Magical Field" : "Gravity Field",
            DefensiveBarrier => "Defensive Barrier",
            HolyMonument => "Holy Monument",
            _ => "Regular Objective",
        };

        public static string ObjectiveEffectText(int code) => code switch
        {
            BoobyTraps => "Units within 3\" of this objective count as being inside Dangerous Terrain.",
            GravityField => "Units within 3\" of this objective count as being inside Difficult Terrain.",
            DefensiveBarrier => "Units shooting at targets within 3\" of this objective get -1 to hit rolls.",
            HolyMonument => "Units within 3\" of this objective get +1 to their hit rolls in melee.",
            _ => "No special objective effects are applied to this objective.",
        };

        public static string ObjectiveKey(IObjective o) => $"{MathF.Round(o.Position.x, 1):0.0},{MathF.Round(o.Position.z, 1):0.0}";

        private static BattlefieldEffectsState Ensure(IGameContext c) => State(c) ?? new BattlefieldEffectsState();

        /// <summary>The effect on a terrain piece, 0 when it hasn't been rolled.</summary>
        public static int TerrainEffect(IGameContext c, ITerrain t)
        {
            if (!TerrainOn(c) || State(c) is not { } s) return 0;
            int i = c.TableState.Terrain.Objects.ToList().IndexOf(t);
            return i >= 0 && s.TerrainEffects.TryGetValue(i, out int code) ? code : 0;
        }

        public static int ObjectiveEffect(IGameContext c, IObjective o)
        {
            if (!TerrainOn(c) || State(c) is not { } s) return 0;
            return s.ObjectiveEffects.TryGetValue(ObjectiveKey(o), out int code) ? code : 0;
        }

        private static List<(ITerrain Piece, int Effect)> PiecesWith(IGameContext c, params int[] effects)
        {
            var list = new List<(ITerrain, int)>();
            if (!TerrainOn(c) || State(c) is not { } s || s.TerrainEffects.Count == 0) return list;
            var pieces = c.TableState.Terrain.Objects.ToList();
            foreach ((int i, int code) in s.TerrainEffects)
                if (i < pieces.Count && effects.Contains(code)) list.Add((pieces[i], code));
            return list;
        }

        private static List<(IObjective Marker, int Effect)> MarkersWith(IGameContext c, params int[] effects)
        {
            var list = new List<(IObjective, int)>();
            if (!TerrainOn(c) || State(c) is not { } s || s.ObjectiveEffects.Count == 0) return list;
            foreach (IObjective o in c.TableState.Objectives.Objects)
                if (s.ObjectiveEffects.TryGetValue(ObjectiveKey(o), out int code) && effects.Contains(code)) list.Add((o, code));
            return list;
        }

        private static bool InPiece(IUnit unit, ITerrain piece) => unit.Models.Any(m => m.GetIsAlive()
            && Rules.Definitions.TerrainProximityQueries.IsModelWithinInches(m, new[] { piece }, 0f));

        /// <summary>Within 3" of a marker: the nearest model's base edge (house rules C).</summary>
        public static bool NearMarker(IUnit unit, IObjective o, float inches = 3f) => unit.Models.Any(m => m.GetIsAlive()
            && MathF.Sqrt((m.Position.x - o.Position.x) * (m.Position.x - o.Position.x) + (m.Position.z - o.Position.z) * (m.Position.z - o.Position.z))
               - m.BaseRadiusInches <= inches + 0.001f);

        // ================================================================= entering terrain

        /// <summary>
        /// After a move: roll for each piece the unit moved into or through for the first time, then the pieces' "move
        /// through" effects (Infestation, Spores) and the Booby-Trapped markers it came within 3" of.
        /// </summary>
        public static async Task AfterMove(IGameContext c, UnitData unit, IReadOnlyList<(Position From, IReadOnlyList<Position> Path)> moves)
        {
            if (!TerrainOn(c) || !unit.GetIsAlive()) return;
            var pieces = c.TableState.Terrain.Objects.ToList();
            var touched = new List<int>();
            for (int i = 0; i < pieces.Count; i++)
            {
                ITerrain t = pieces[i];
                bool through = moves.Any(m =>
                {
                    Position prev = m.From;
                    foreach (Position p in m.Path)
                    {
                        if (t.Shape.DoesPathIntersectZone(new Float2(prev.x, prev.z), new Float2(p.x, p.z))
                            || t.Shape.IsPointWithinZone(new Float2(p.x, p.z))) return true;
                        prev = p;
                    }
                    return false;
                });
                if (through) touched.Add(i);
            }
            if (touched.Count > 0)
            {
                BattlefieldEffectsState s = Ensure(c);
                bool changed = false;
                foreach (int i in touched.Where(i => !s.TerrainEffects.ContainsKey(i)))
                {
                    int die = HordePlacement.RollD6(c);
                    s.TerrainEffects[i] = die;
                    changed = true;
                    string piece = Describe(pieces[i]);
                    c.Log($"Terrain effect: {unit.Name} enters {piece} for the first time - rolled {die}: {TerrainEffectName(die, c.Settings.GameSystem)}. {TerrainEffectText(die)}");
                    if (die >= 3)
                        await c.Announce($"{piece} is {TerrainEffectName(die, c.Settings.GameSystem)}: {TerrainEffectText(die)}", Amber, EBannerTier.Toast);
                }
                if (changed) Save(c, s);
                foreach (int i in touched)
                    if (s.TerrainEffects.TryGetValue(i, out int code) && code is Infestation or DeadlySpores)
                        await PieceHurts(c, unit, pieces[i], code);
            }
            foreach ((IObjective o, int _) in MarkersWith(c, BoobyTraps))
                if (unit.GetIsAlive() && NearMarker(unit, o)) await BoobyTrap(c, unit);
        }

        private static string Describe(ITerrain t) => string.IsNullOrWhiteSpace(t.Name) ? "the terrain" : t.Name;

        private static async Task PieceHurts(IGameContext c, IUnit unit, ITerrain piece, int code)
        {
            if (!unit.GetIsAlive()) return;
            string name = TerrainEffectName(code, c.Settings.GameSystem);
            if (code == Infestation)
            {
                int wounds = (HordePlacement.RollD6(c) + 1) / 2;
                c.Log($"{name}: {unit.Name} is in {Describe(piece)} - {wounds} wound{(wounds == 1 ? "" : "s")}.");
                await CasualtyPresentation.ApplyUnitWounds(c, unit, wounds);
            }
            else
            {
                int hits = (HordePlacement.RollD6(c) + 1) / 2 + (HordePlacement.RollD6(c) + 1) / 2;
                c.Log($"{name}: {unit.Name} is in {Describe(piece)} - {hits} hits.");
                await CampaignEventRunner.DealHits(c, unit, hits, name);
            }
        }

        private static async Task BoobyTrap(IGameContext c, IUnit unit)
        {
            c.Log($"Booby Traps: {unit.Name} is within 3\" of a booby-trapped objective - it counts as in Dangerous Terrain.");
            var result = MovementExecutor.RollForcedDangerousTerrain(c, unit,
                MovementRuleQueries.IgnoresAllTerrain(unit, c.RuleEvaluator));
            await MovementExecutor.ResolveDangerousTerrain(c, result);
        }

        /// <summary>A unit activates: Infestation / Spores pieces it stands in, Booby-Trapped markers it is near.</summary>
        public static async Task TerrainOnActivation(IGameContext c, IUnit unit)
        {
            if (!TerrainOn(c)) return;
            foreach ((ITerrain piece, int code) in PiecesWith(c, Infestation, DeadlySpores))
                if (InPiece(unit, piece)) await PieceHurts(c, unit, piece, code);
            foreach ((IObjective o, int _) in MarkersWith(c, BoobyTraps))
                if (unit.GetIsAlive() && NearMarker(unit, o)) await BoobyTrap(c, unit);
        }

        // ================================================================= seizing objectives

        /// <summary>End of round, after the objectives are reconciled: roll for each marker seized for the first time.</summary>
        public static async Task AfterObjectives(IGameContext c)
        {
            if (!TerrainOn(c)) return;
            BattlefieldEffectsState s = Ensure(c);
            bool changed = false;
            foreach (IObjective o in c.TableState.Objectives.Objects)
            {
                if (o.OwnerID == null) continue;
                string key = ObjectiveKey(o);
                if (s.ObjectiveEffects.ContainsKey(key)) continue;
                int die = HordePlacement.RollD6(c);
                s.ObjectiveEffects[key] = die;
                changed = true;
                string name = ObjectiveEffectName(die, c.Settings.GameSystem);
                c.Log($"Objective effect: the marker at ({o.Position.x:0},{o.Position.z:0}) is seized for the first time - rolled {die}: {name}. {ObjectiveEffectText(die)}");
                if (die >= 3) await c.Announce($"Objective seized: {name} - {ObjectiveEffectText(die)}", Amber, EBannerTier.Toast);
            }
            if (changed) Save(c, s);
        }

        // ================================================================= modifiers

        /// <summary>Vigor +1 (shooter inside), Defensive Barrier -1 (target near), Holy Monument +1 (melee attacker near),
        /// Barbed Wire -1 (a charger in the piece).</summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IGameContext c, IUnit attacker, IUnit defender, bool melee, bool spell, bool charging)
        {
            var list = new List<GrantedModifier>();
            if (!TerrainOn(c) || spell) return list;
            EGameSystem sys = c.Settings.GameSystem;
            if (!melee && PiecesWith(c, Vigor).Any(p => InPiece(attacker, p.Piece)))
                list.Add(new GrantedModifier(TerrainEffectName(Vigor, sys), 1));
            if (melee && charging && PiecesWith(c, BarbedWire).Any(p => InPiece(attacker, p.Piece)))
                list.Add(new GrantedModifier(TerrainEffectName(BarbedWire, sys), -1));
            if (!melee && MarkersWith(c, DefensiveBarrier).Any(m => NearMarker(defender, m.Marker)))
                list.Add(new GrantedModifier("Defensive Barrier", -1));
            if (melee && MarkersWith(c, HolyMonument).Any(m => NearMarker(attacker, m.Marker)))
                list.Add(new GrantedModifier("Holy Monument", 1));
            return list;
        }

        /// <summary>Gravity Field: a unit within 3" counts as in Difficult terrain (its move is capped at 6").</summary>
        public static bool InGravityField(IGameContext c, IUnit unit) =>
            TerrainOn(c) && MarkersWith(c, GravityField).Any(m => NearMarker(unit, m.Marker));


    }
}
