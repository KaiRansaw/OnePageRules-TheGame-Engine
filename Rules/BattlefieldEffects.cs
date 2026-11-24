using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine;
using FDG.Utilities;
using FDG.Rules.Dispatch;
using FDG.Presentation;

namespace FDG
{
    /// <summary>Battlefield Effects state kept in the game's progress record (saves, network).</summary>
    [Serializable]
    public sealed class BattlefieldEffectsState
    {
        /// <summary>Special Condition in play: 0 none, 1-6 Geological, 7-12 Mysterious (book order).</summary>
        public int Condition { get; set; }
        /// <summary>Dark Skies: the 2D6 rolled before the game.</summary>
        public int DarkSkies { get; set; }
        /// <summary>The condition has been settled (rolled or picked) - only once per game.</summary>
        public bool Settled { get; set; }
        /// <summary>Terrain &amp; Objective Effects: each terrain piece's rolled effect (index in the table's terrain -> 1-6).</summary>
        public Dictionary<int, int> TerrainEffects { get; set; } = new();
        /// <summary>Terrain &amp; Objective Effects: each objective's rolled effect (position key -> 1-6).</summary>
        public Dictionary<string, int> ObjectiveEffects { get; set; } = new();
    }

    /// <summary>
    /// Battlefield Effects (Advanced Rulebooks 3.6.0: GF p38-41, GFF p35-38, AoF p38-41, AoFS p35-38). This file holds
    /// the Special Conditions: "Before the game, you can either pick one of the conditions of your choice, or have it
    /// selected randomly. First roll a D3 to select a condition type: 1-2 None, 3-4 Geological, 5-6 Mysterious. Then roll
    /// one die." Judgement calls: house rules C, "Special Conditions readings".
    /// </summary>
    public static partial class BattlefieldEffects
    {
        public const string SpecialConditions = "Special Conditions";
        public const int PickShift = 20;
        public const EAdvancedRules PickMask = (EAdvancedRules)(0xFUL << PickShift);

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (SpecialConditions, EAdvancedRules.SpecialConditions),
            (TerrainAndObjectives, EAdvancedRules.TerrainObjectiveEffects),
        };

        public const int Earthquake = 1, HeavyStorm = 2, MuddyTerrain = 3, SmellOfDeath = 4, ZeroGravity = 5, DarkSkies = 6,
            Damnation = 7, ArcanePower = 8, Inspiration = 9, DeadlyVortex = 10, MysticalFog = 11, SinisterEchoes = 12;

        private static bool Fantasy(EGameSystem s) => s is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;

        public static string Name(int code, EGameSystem system) => code switch
        {
            Earthquake => "Earthquake",
            HeavyStorm => "Heavy Storm",
            MuddyTerrain => "Muddy Terrain",
            SmellOfDeath => "Smell of Death",
            ZeroGravity => Fantasy(system) ? "Magical Winds" : "Zero Gravity",
            DarkSkies => "Dark Skies",
            Damnation => "Damnation",
            ArcanePower => "Arcane Power",
            Inspiration => "Inspiration",
            DeadlyVortex => "Deadly Vortex",
            MysticalFog => "Mystical Fog",
            SinisterEchoes => "Sinister Echoes",
            _ => "None",
        };

        public static string Text(int code, EGameSystem system) => code switch
        {
            Earthquake => "The entire table counts as Dangerous Terrain. Any piece of terrain that already counted as Dangerous Terrain becomes deadlier, and deals wounds on rolls of 1-2.",
            HeavyStorm => "The entire table counts as Cover Terrain. Any piece of terrain that already counted as Cover Terrain becomes more protective, and units get +2 to Defense rolls.",
            MuddyTerrain => "The entire table counts as Difficult Terrain. Any piece of terrain that already counted as Difficult Terrain becomes harder to move through, and units may not move more than 4\" when crossing it.",
            SmellOfDeath => "When a unit needs to take a Morale Test, it gets -1 to its Morale Test roll.",
            ZeroGravity => "All units must move +D3\" in a straight line on Advance actions, and +2D6\" in a straight line on Rush or Charge actions.",
            DarkSkies => "Roll 2D6 once before the game to check how visibility is. For the rest of the game, all units get -2D6\" range when firing their ranged weapons (to a minimum of 3\").",
            Damnation => "At the beginning of each round, you must select one of your units to make a sacrifice and take D3 wounds. If you do, nothing happens. If you don't, then all of your units get -1 to all their to hit, Defense and Morale rolls this round.",
            ArcanePower => "All casters get +1 to their spell casting rolls. On an unmodified result of 1, something went wrong with the spell, and the caster takes D3 wounds.",
            Inspiration => "When a unit needs to take a Morale Test, it gets +1 to its Morale Test roll.",
            DeadlyVortex => "When a unit is activated, roll one die, on a 1 it takes D3 wounds.",
            MysticalFog => "When a unit takes a wound, roll one die, on a 6+ it may ignore that wound.",
            SinisterEchoes => system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy
                ? "When a unit is Shaken, it also takes D3 wounds. When a unit is Routed as a result of losing in melee, all friendly units within 6\" take D3 wounds."
                : "When a unit is Shaken, it also takes D3 wounds. When a unit is Routed after failing a Morale Test, all friendly units within 6\" take D3 wounds.",
            _ => "",
        };

        public static string BookText(string name) => name switch
        {
            TerrainAndObjectives => TerrainBookText,
            SpecialConditions => "Before the game, you can either pick one of the conditions of your choice, or have it selected randomly. First roll a D3 to select a condition type: 1-2 = None, 3-4 = Geological, 5-6 = Mysterious. Then roll one die, and check which special condition you get.\nGeological: 1 Earthquake, 2 Heavy Storm, 3 Muddy Terrain, 4 Smell of Death, 5 Zero Gravity / Magical Winds, 6 Dark Skies.\nMysterious: 1 Damnation, 2 Arcane Power, 3 Inspiration, 4 Deadly Vortex, 5 Mystical Fog, 6 Sinister Echoes.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            TerrainAndObjectives => TerrainGameText,
            SpecialConditions => "Pick one below, or leave it on \"Roll for it\" (the book's D3, then a die). The condition is announced at the start of round 1 and shown on the Mission panel. Zero Gravity: after an Advance or Rush the unit drifts on the rolled distance, straight ahead (stopping at obstacles); a charge reaches that much further. Damnation: each player is asked at the start of every round; a bot sacrifices its unit with the most wounds left.",
            _ => "",
        };

        /// <summary>The picked condition from the lobby (0 = roll for it).</summary>
        public static int Picked(GameSettings s) => (int)(((ulong)s.AdvancedRules >> PickShift) & 0xF);

        public static EAdvancedRules WithPick(EAdvancedRules rules, int pick) =>
            (rules & ~PickMask) | (EAdvancedRules)(((ulong)Math.Clamp(pick, 0, 12)) << PickShift);

        // ================================================================= state

        /// <summary>The state, from the progress record (null before the game has settled it).</summary>
        public static BattlefieldEffectsState? State(IGameContext c) =>
            GameProgressUtilities.TryGetProgress(c.GameDataStore)?.Battlefield ?? Pending(c);

        private static readonly ConditionalWeakTable<IReadableGameDataStore, BattlefieldEffectsState> PendingStates = new();
        private static BattlefieldEffectsState? Pending(IGameContext c) =>
            PendingStates.TryGetValue(c.GameDataStore, out BattlefieldEffectsState? s) ? s : null;

        private static void Save(IGameContext c, BattlefieldEffectsState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                // No progress record yet (a unit test, a very early call): keep it beside the store.
                PendingStates.Remove(c.GameDataStore);
                PendingStates.Add(c.GameDataStore, s);
                return;
            }
            progress.Battlefield = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        /// <summary>The condition in force (0 = none).</summary>
        public static int Condition(IGameContext c) =>
            AdvancedRules.IsOn(c, EAdvancedRules.SpecialConditions) ? State(c)?.Condition ?? 0 : 0;

        public static bool Is(IGameContext c, int code) => Condition(c) == code;

        private static int D6(IGameContext c) => HordePlacement.RollD6(c);
        private static int D3(IGameContext c) => (D6(c) + 1) / 2;

        // ================================================================= start of round

        /// <summary>Start of every round: settle the condition before round 1, then refresh what it puts on units and
        /// ask Damnation's sacrifice.</summary>
        public static async Task RoundStart(IGameContext c, int round)
        {
            if (!AdvancedRules.IsOn(c, EAdvancedRules.SpecialConditions)) return;
            BattlefieldEffectsState s = State(c) ?? new BattlefieldEffectsState();
            if (!s.Settled)
            {
                s.Settled = true;
                int pick = Picked(c.Settings);
                if (pick > 0) s.Condition = pick;
                else
                {
                    int type = D6(c);   // the book says "roll a D3" but lists 1-2 / 3-4 / 5-6: read as a die (house rules C)
                    s.Condition = type <= 2 ? 0 : (type <= 4 ? 0 : 6) + D6(c);
                    c.Log($"Special Conditions: rolled {type} for the type ({(type <= 2 ? "None" : type <= 4 ? "Geological" : "Mysterious")})"
                        + (s.Condition > 0 ? $", then {(s.Condition - 1) % 6 + 1}." : "."));
                }
                if (s.Condition == DarkSkies) s.DarkSkies = D6(c) + D6(c);
                Save(c, s);
                if (s.Condition == 0)
                    await c.Announce("Special Conditions: none this game.", new TextColor(200, 200, 200, 255), EBannerTier.Toast);
                else
                    await c.Announce($"Special Condition - {Name(s.Condition, c.Settings.GameSystem)}: {Text(s.Condition, c.Settings.GameSystem)}"
                        + (s.Condition == DarkSkies ? $" Visibility: -{s.DarkSkies}\" range." : ""),
                        new TextColor(255, 200, 90, 255), EBannerTier.Notice);
            }
            ApplyGlobals(c);
            RefreshUnits(c);
            if (Is(c, Damnation)) await AskDamnation(c);
        }

        /// <summary>Things that sit on every unit or in the rules' globals: Dark Skies' range, Muddy Terrain's 4" cap.
        /// Called at round start and when a unit activates (units that arrive mid-game, a resumed save).</summary>
        public static void ApplyGlobals(IGameContext c)
        {
            GameWideConstants.DifficultTerrainMoveCapInches = Is(c, MuddyTerrain) ? 4f : GameWideConstants.BookDifficultTerrainMoveCapInches;
        }

        public static void RefreshUnits(IGameContext c)
        {
            int skies = Is(c, DarkSkies) ? State(c)?.DarkSkies ?? 0 : 0;
            foreach (IUnit unit in c.TableState.Units.Objects)
            {
                int have = unit.Tokens.GetTokenCount(TokenType.DarkSkies);
                if (have < skies) for (int i = have; i < skies; i++) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.DarkSkies));
                else if (have > skies) unit.Tokens.RemoveTokens(TokenType.DarkSkies, have - skies);
            }
        }

        /// <summary>A unit activates: keep the globals right, and Deadly Vortex's die.</summary>
        public static async Task OnActivation(IGameContext c, IUnit unit)
        {
            await TerrainOnActivation(c, unit);
            if (Condition(c) == 0) return;
            ApplyGlobals(c);
            if (unit.Tokens.GetTokenCount(TokenType.DarkSkies) != (Is(c, DarkSkies) ? State(c)?.DarkSkies ?? 0 : 0)) RefreshUnits(c);
            if (Is(c, ZeroGravity))
            {
                // Zero Gravity's charge reach for this activation: +2D6".
                int have = unit.Tokens.GetTokenCount(TokenType.ZeroGravityCharge);
                if (have > 0) unit.Tokens.RemoveTokens(TokenType.ZeroGravityCharge, have);
                int extra = D6(c) + D6(c);
                for (int i = 0; i < extra; i++) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ZeroGravityCharge));
            }
            if (Is(c, DeadlyVortex))
            {
                int die = D6(c);
                if (die == 1)
                {
                    int wounds = D3(c);
                    c.Log($"Deadly Vortex: {unit.Name} rolls a 1 as it activates - it takes {wounds} wound{(wounds == 1 ? "" : "s")}.");
                    await c.Announce($"Deadly Vortex: {unit.Name} takes {wounds} wound{(wounds == 1 ? "" : "s")}.", Amber, EBannerTier.Toast);
                    await CasualtyPresentation.ApplyUnitWounds(c, unit, wounds);
                }
                else c.Log($"Deadly Vortex: {unit.Name} rolls {die} as it activates - nothing happens.");
            }
        }

        public static readonly TextColor Amber = new(255, 190, 80, 255);

        // ================================================================= Damnation

        public const string RefuseOption = "Refuse the sacrifice";

        private static async Task AskDamnation(IGameContext c)
        {
            var players = c.TableState.Teams.Objects.SelectMany(t => t.Players).Distinct().ToList();
            foreach (PlayerID player in players)
            {
                List<IUnit> units = c.TableState.Units.Objects
                    .Where(u => u.PlayerID.Equals(player) && u.GetIsAlive() && !ReserveRules.IsInReserve(u))
                    .ToList();
                if (units.Count == 0) continue;
                // The bot's pick first (automated answerers take the first option): the unit with the most wounds left.
                List<IUnit> ordered = units.OrderByDescending(u => u.Models.Where(m => m.GetIsAlive()).Sum(m => m.RemainingWounds)).ToList();
                var labels = new List<string>();
                var byLabel = new Dictionary<string, IUnit>();
                foreach (IUnit u in ordered)
                {
                    string label = $"Sacrifice {u.Name}";
                    int n = 2;
                    while (byLabel.ContainsKey(label)) label = $"Sacrifice {u.Name} ({n++})";
                    labels.Add(label);
                    byLabel[label] = u;
                }
                labels.Add(RefuseOption);
                string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(
                    new StringSelectionRequest(player,
                        "Damnation: select one of your units to make a sacrifice and take D3 wounds. If you don't, all of your units get -1 to hit, Defense and Morale rolls this round.",
                        labels, new List<StringSelectionRequest.InvalidOption>(), displayName: "Damnation") { Recommended = labels[0] });
                if (byLabel.TryGetValue(pick, out IUnit? victim))
                {
                    int wounds = D3(c);
                    c.Log($"Damnation: {c.GetPlayerName(player)} sacrifices {victim.Name} - {wounds} wound{(wounds == 1 ? "" : "s")}.");
                    await c.Announce($"Damnation: {victim.Name} is sacrificed - {wounds} wound{(wounds == 1 ? "" : "s")}.", Amber, EBannerTier.Toast);
                    await CasualtyPresentation.ApplyUnitWounds(c, victim, wounds);
                }
                else
                {
                    c.Log($"Damnation: {c.GetPlayerName(player)} refuses - all their units get -1 to hit, Defense and Morale rolls this round.");
                    await c.Announce($"Damnation: {c.GetPlayerName(player)} refuses the sacrifice - -1 to all their rolls this round.", Amber, EBannerTier.Toast);
                    foreach (IUnit u in c.TableState.Units.Objects.Where(u => u.PlayerID.Equals(player)))
                        if (!u.Tokens.HasToken(TokenType.Damned)) u.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Damned));
                }
            }
        }

        /// <summary>End of the round: Damnation's -1 ends.</summary>
        public static void RoundEnd(IGameContext c)
        {
            foreach (IUnit unit in c.TableState.Units.Objects)
            {
                int n = unit.Tokens.GetTokenCount(TokenType.Damned);
                if (n > 0) unit.Tokens.RemoveTokens(TokenType.Damned, n);
            }
        }

        /// <summary>Damnation's -1 to hit / Defense (the unit carries the round's token).</summary>
        public static int DamnedPenalty(IUnit unit) => unit.Tokens.HasToken(TokenType.Damned) ? -1 : 0;

        // ================================================================= the rest, where they hook in

        /// <summary>Smell of Death -1, Inspiration +1, Damnation -1 - to a Morale Test roll.</summary>
        public static int MoraleModifier(IGameContext c, IUnit unit)
        {
            int m = DamnedPenalty(unit);
            int cond = Condition(c);
            if (cond == SmellOfDeath) m -= 1;
            if (cond == Inspiration) m += 1;
            return m;
        }

        /// <summary>Heavy Storm: the shooting cover bonus. Everyone is in cover (+1); a unit in a cover piece gets +2.</summary>
        public static int CoverBonus(IGameContext c, bool inCoverPiece, int bonus) =>
            !Is(c, HeavyStorm) ? bonus : inCoverPiece ? 2 : 1;

        /// <summary>Earthquake: every move and activation is in Dangerous terrain.</summary>
        public static bool WholeTableDangerous(IGameContext c) => Is(c, Earthquake);

        /// <summary>Earthquake: real Dangerous pieces wound on 1-2.</summary>
        public static int DangerousWoundsOn(IGameContext c, bool realPiece) => realPiece && Is(c, Earthquake) ? 2 : 1;

        /// <summary>Muddy Terrain: the whole table counts as Difficult (6" cap).</summary>
        public static bool WholeTableDifficult(IGameContext c) => Is(c, MuddyTerrain);

        /// <summary>Dark Skies: "-2D6" range ... (to a minimum of 3")" - carried on the unit as tokens.</summary>
        public static float DarkSkiesRange(IUnit attacker, float range)
        {
            int x = attacker.Tokens.GetTokenCount(TokenType.DarkSkies);
            if (x <= 0 || range <= 3f) return range;
            return MathF.Max(3f, range - x);
        }

        /// <summary>Arcane Power: +1 to casting rolls.</summary>
        public static int CastBonus(IGameContext c) => Is(c, ArcanePower) ? 1 : 0;

        /// <summary>Arcane Power: "On an unmodified result of 1 ... the caster takes D3 wounds".</summary>
        public static async Task AfterCast(IGameContext c, IUnit caster, int face)
        {
            if (!Is(c, ArcanePower) || face != 1) return;
            int wounds = D3(c);
            c.Log($"Arcane Power: an unmodified 1 - something went wrong with the spell, and {caster.Name} takes {wounds} wound{(wounds == 1 ? "" : "s")}.");
            await c.Announce($"Arcane Power backfires: {caster.Name} takes {wounds} wound{(wounds == 1 ? "" : "s")}.", Amber, EBannerTier.Toast);
            await CasualtyPresentation.ApplyUnitWounds(c, caster, wounds);
        }

        /// <summary>Mystical Fog: one die per wound, each 6 ignores it. Returns the wounds left.</summary>
        public static async Task<float> FogIgnore(IGameContext c, IUnit unit, float wounds)
        {
            if (wounds <= 0f || !Is(c, MysticalFog)) return wounds;
            IDiceResults roll = c.DiceRoller.Roll(wounds);
            float ignored = roll.AtOrAbove(6);
            await c.Presenter.Present(DiceRolledBeat.From(roll, 6, c.Settings.RandomnessType, "Mystical Fog",
                $"{ignored:0.##} ignored", category: ERollBeatCategory.Defense, context: unit.Name));
            if (ignored > 0f) c.Log($"Mystical Fog: {unit.Name} ignores {ignored:0.##} of {wounds:0.##} wound(s).");
            return MathF.Max(0f, wounds - ignored);
        }

        /// <summary>Sinister Echoes: "When a unit is Shaken, it also takes D3 wounds."</summary>
        public static async Task OnShaken(IGameContext c, IUnit unit)
        {
            if (!Is(c, SinisterEchoes) || !unit.GetIsAlive()) return;
            int wounds = D3(c);
            c.Log($"Sinister Echoes: {unit.Name} is Shaken and takes {wounds} wound{(wounds == 1 ? "" : "s")}.");
            await CasualtyPresentation.ApplyUnitWounds(c, unit, wounds);
        }

        /// <summary>The friendly units within 6" of a unit about to be routed (measured before it is removed).</summary>
        public static List<IUnit> FriendsNear(IGameContext c, IUnit unit)
        {
            if (!Is(c, SinisterEchoes)) return new List<IUnit>();
            return c.TableState.Units.Objects
                .Where(u => u.GetIsAlive() && !u.ID.Equals(unit.ID) && u.PlayerID.Equals(unit.PlayerID) && !ReserveRules.IsInReserve(u)
                    && UnitCompareUtilities.MinDistanceBetweenUnits(u, unit, out _, out _, includeVertical: true) <= 6f + 0.001f)
                .ToList();
        }

        /// <summary>Sinister Echoes: "When a unit is Routed ..., all friendly units within 6" take D3 wounds."</summary>
        public static async Task OnRouted(IGameContext c, IUnit routed, List<IUnit> friends)
        {
            foreach (IUnit friend in friends.Where(f => f.GetIsAlive()))
            {
                int wounds = D3(c);
                c.Log($"Sinister Echoes: {routed.Name} routed - {friend.Name} (within 6\") takes {wounds} wound{(wounds == 1 ? "" : "s")}.");
                await CasualtyPresentation.ApplyUnitWounds(c, friend, wounds);
            }
        }

        /// <summary>
        /// Zero Gravity: "All units must move +D3" in a straight line on Advance actions, and +2D6" in a straight line on
        /// Rush or Charge actions." After an Advance or Rush the unit carries on straight ahead (stopping at obstacles
        /// and the table edge); a move that ended in contact (a charge) does not drift.
        /// </summary>
        public static async Task Drift(IGameContext c, UnitData unit, IReadOnlyList<Position> starts, bool rush)
        {
            if (!Is(c, ZeroGravity) || !unit.GetIsAlive() || starts.Count == 0) return;
            List<ModelData> living = unit.Models.OfType<ModelData>().Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return;
            float sx = starts.Average(p => p.x), sz = starts.Average(p => p.z);
            float ex = living.Average(m => m.Position.x), ez = living.Average(m => m.Position.z);
            float dx = ex - sx, dz = ez - sz, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.05f) return;   // it didn't go anywhere: no direction to carry on in
            bool inContact = c.TableState.Units.Objects.Any(o => o.GetIsAlive() && !o.PlayerID.Equals(unit.PlayerID)
                && UnitCompareUtilities.MinDistanceBetweenUnits(o, unit, out _, out _, includeVertical: false) <= 1f);
            if (inContact) return;
            int inches = rush ? D6(c) + D6(c) : D3(c);
            float mx = dx / len * inches, mz = dz / len * inches;
            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;
            float t = 1f;
            foreach (ModelData m in living)
            {
                float r = m.BaseShape.CircumscribedRadiusInches;
                t = MathF.Min(t, QuestActions.Reach(m.Position.x, mx, r, w - r));
                t = MathF.Min(t, QuestActions.Reach(m.Position.z, mz, r, h - r));
            }
            t = MathF.Max(0f, t);
            t = MathF.Min(t, QuestActions.FreeTravel(c, unit, living, mx, mz, t, inches));
            foreach (ModelData m in living) m.SetPosition(new Position(m.Position.x + mx * t, m.Position.z + mz * t));
            string name = Name(ZeroGravity, c.Settings.GameSystem);
            c.Log($"{name}: {unit.Name} carries on {inches * t:0.#}\" of {inches}\" straight ahead after its {(rush ? "Rush" : "Advance")}.");
            if (inches * t > 0.05f)
                await c.Presenter.Present(new UnitMovedBeat(unit.ID, unit.Name,
                    living.Select(m => new ModelMove(m.ID, new List<Position> { new(m.Position.x - mx * t, m.Position.z - mz * t), m.Position })).ToList(),
                    PresentationDurations.ForMoveDistance(inches * t)));
        }

        /// <summary>Zero Gravity's charge reach for this activation (rolled as the unit activates).</summary>
        public static float ChargeBonus(IUnit unit) => unit.Tokens.GetTokenCount(TokenType.ZeroGravityCharge);
    }
}
