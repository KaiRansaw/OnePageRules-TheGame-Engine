using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine;
using FDG.Utilities;

namespace FDG
{
    /// <summary>Fog of War state kept in the game's progress record (saves, network).</summary>
    [Serializable]
    public sealed class FogOfWarState
    {
        /// <summary>Prolonged Battle: the round the game now ends after (null until the first continue roll).</summary>
        public int? Limit { get; set; }
        /// <summary>Prolonged Battle: a continue roll failed - the game ends after this round.</summary>
        public bool Ended { get; set; }
    }

    /// <summary>
    /// Fog of War (Advanced Rulebooks 3.6.0: GF p46, GFF p43, AoF p46, AoFS p43). "Fog of War rules add an element of
    /// uncertainty to battles, by affecting how units are deployed and how rounds are played. Pick any of the
    /// following." This file holds the four that change how rounds are played: Ebb &amp; Flow, Shifting Focus, Combat
    /// Hesitation and Prolonged Battle. Judgement calls: house rules C, "Fog of War readings".
    /// </summary>
    public static class FogOfWar
    {
        public const string EbbAndFlowName = "Ebb & Flow";
        public const string ShiftingFocusName = "Shifting Focus";
        public const string CombatHesitationName = "Combat Hesitation";
        public const string ProlongedBattleName = "Prolonged Battle";

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (EbbAndFlowName, EAdvancedRules.EbbAndFlow),
            (ShiftingFocusName, EAdvancedRules.ShiftingFocus),
            (CombatHesitationName, EAdvancedRules.CombatHesitation),
            (ProlongedBattleName, EAdvancedRules.ProlongedBattle),
        };

        private static bool Skirmish(EGameSystem s) => s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public static string BookText(string name, EGameSystem s) => name switch
        {
            EbbAndFlowName => "Before the game begins, each player needs to take as many tokens as units in their army, and each player's tokens must be of a different color. At the beginning of each round, the players must put as many tokens as units they have left on the table in a bag. Then, the players draw a random token from the bag, and the player whose color was drawn may activate one unit. Once the player is done, another token is drawn to see who activates next, and this continues until all tokens have been drawn, at which point the round ends. Note that if a unit is destroyed before it was activated, then you must remove one token of that player's color from the bag.",
            ShiftingFocusName => Skirmish(s)
                ? "Before the game begins, the table must be divided into 2 equal sections along the player's table edge, and they must be numbered section 1 and section 2. When a player needs to choose which unit to activate, first they must roll a D6, and then they may only activate one of their units that is within that table section (results of 1-3 = section 1, 4-6 = section 2). If there are no eligible units in that table section, then they must move on to the next section."
                : "Before the game begins, the table must be divided into 3 equal sections along the long table edges, and each section must be given a number from 1 to 3. When a player needs to choose which unit to activate, first they must roll a D3, and then they may only activate one of their units that is within that table section. If there are no eligible units in that table section, then they must move on to the next section going from left to right.",
            CombatHesitationName => "When it's a player's turn to activate a unit, if an opponent has more units that haven't activated yet, they may roll one die, and if the roll is successful, then they may pass the turn to their opponent without activating a unit: 1st Round = 5+ to pass, 2nd Round = 4+, 3rd Round = 3+, 4th Round = 2+. If the game lasts longer than 4 rounds, then the player may pass their turn without rolling for it.",
            ProlongedBattleName => "Starting from the end of the 4th round, players must roll one die at the end of each round to see if the game continues. Roll one die, and if the roll is successful, then the game continues into the next round: 4th Round: 4+ to continue, 5th Round: 5+, 6th Round & Over: 6+. Note that mission objectives are checked at the end of the final round, and not at the end of the 4th round as usual.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            EbbAndFlowName => "Each activation goes to a player drawn at random, weighted by how many units each has left to activate this round. The battle report says who was drawn.",
            ShiftingFocusName => "Before you pick a unit the game rolls the section; only your units in it can be picked (the others are greyed out with the reason). Sections are counted from your own left.",
            CombatHesitationName => "When you're behind on activations you're asked whether to try to pass; the game rolls for you. Bots never pass.",
            ProlongedBattleName => "From the end of round 4 (or the game's last round, if later) the game rolls to see whether it goes on. The Mission panel shows the round it now ends after.",
            _ => "",
        };

        public static bool IsOn(IGameContext c, EAdvancedRules flag) => AdvancedRules.IsOn(c, flag);

        // ================================================================= state

        public static FogOfWarState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.FogOfWar
            ?? (Pending.TryGetValue(store, out FogOfWarState? s) ? s : null);

        private static readonly ConditionalWeakTable<IReadableGameDataStore, FogOfWarState> Pending = new();

        private static void Save(IGameContext c, FogOfWarState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.FogOfWar = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        private static int D6(IGameContext c) => HordePlacement.RollD6(c);

        /// <summary>One decisive die of <paramref name="sides"/> faces (the bag draw).</summary>
        private static int DieN(IGameContext c, int sides)
        {
            if (sides <= 1) return 1;
            IDiceResults r = c.DiceRoller.RollDecisive(sides);
            for (int v = r.SideMin; v <= r.SideMax; v++) if (r.At(v) > 0f) return v;
            return r.SideMin;
        }

        // ================================================================= Prolonged Battle

        /// <summary>The round the game ends after, with Prolonged Battle's continue rolls (null = no change).</summary>
        public static int? ProlongedLimit(GameSettings settings, IReadableGameDataStore store, int baseLimit)
        {
            if (!AdvancedRules.IsOn(settings, EAdvancedRules.ProlongedBattle)) return null;
            if (settings.GameMode != EGameMode.Standard) return null;
            return State(store)?.Limit is int l && l > baseLimit ? l : null;
        }

        /// <summary>"4th Round: 4+ to continue, 5th Round: 5+, 6th Round &amp; Over: 6+".</summary>
        public static int ContinueOn(int round) => round <= 4 ? 4 : round == 5 ? 5 : 6;

        /// <summary>End of a round: roll to see whether the game continues. Only at the round the game would end after.</summary>
        public static async Task ProlongedRoll(IGameContext c, int roundJustFinished, int currentLimit)
        {
            if (!IsOn(c, EAdvancedRules.ProlongedBattle) || c.Settings.GameMode != EGameMode.Standard) return;
            if (roundJustFinished < 4 || roundJustFinished != currentLimit) return;
            FogOfWarState s = State(c.GameDataStore) ?? new FogOfWarState();
            if (s.Ended) return;
            int need = ContinueOn(roundJustFinished), die = D6(c);
            bool go = die >= need;
            if (go) s.Limit = roundJustFinished + 1;
            else s.Ended = true;
            Save(c, s);
            c.Log($"Prolonged Battle: rolled {die} (needs {need}+) - {(go ? $"the game continues into round {roundJustFinished + 1}" : "the game ends")}.");
            await c.Announce(go ? $"Prolonged Battle: the fight goes on - round {roundJustFinished + 1}!"
                : "Prolonged Battle: the battle ends here.", BattlefieldEffects.Amber, EBannerTier.Notice);
        }

        // ================================================================= Ebb & Flow

        private static readonly ConditionalWeakTable<IReadableGameDataStore, StrongBox<PlayerID?>> PassedTo = new();

        /// <summary>Ebb &amp; Flow: the player who activates next, drawn from the bag (one token per unit they still have to
        /// activate this round). Null when nobody has anything left.</summary>
        public static PlayerID? DrawNext(IGameContext c, ISingleRoundContext round)
        {
            var counts = new List<(PlayerID Player, int Tokens)>();
            foreach ((PlayerID player, List<DataBinding<UnitData>> pool) in round.UnactivatedUnits)
            {
                int n = pool.Count(u => u.GetValue().GetIsAlive());
                if (n > 0 && round.DoesPlayerHaveRemainingActivations(player)) counts.Add((player, n));
            }
            if (counts.Count == 0) return null;
            // Combat Hesitation under Ebb & Flow: a pass hands the turn to the opponent it was passed to.
            if (PassedTo.TryGetValue(c.GameDataStore, out StrongBox<PlayerID?>? box) && box.Value is PlayerID to)
            {
                box.Value = null;
                if (counts.Any(x => x.Player.Equals(to))) return to;
            }
            counts = counts.OrderBy(x => x.Player.ToString()).ToList();
            int total = counts.Sum(x => x.Tokens);
            int pick = DieN(c, total) - 1;
            foreach ((PlayerID player, int tokens) in counts)
            {
                if (pick < tokens)
                {
                    c.Log($"Ebb & Flow: a token of {c.GetPlayerName(player)}'s colour is drawn ({tokens} of {total} in the bag).");
                    return player;
                }
                pick -= tokens;
            }
            return counts[^1].Player;
        }

        // ================================================================= Combat Hesitation

        /// <summary>"1st Round = 5+ to pass ... 4th Round = 2+"; null = no roll needed (round 5+).</summary>
        public static int? PassOn(int round) => round switch { <= 1 => 5, 2 => 4, 3 => 3, 4 => 2, _ => null };

        public const string TryToPass = "Try to pass the turn";
        public const string Activate = "Activate a unit";

        /// <summary>Combat Hesitation, as a player's turn starts: true when they passed it to their opponent.</summary>
        public static async Task<bool> Hesitate(IGameContext c, PlayerID player, int round, bool opponentHasMore)
        {
            if (!IsOn(c, EAdvancedRules.CombatHesitation) || !opponentHasMore) return false;
            int? need = PassOn(round);
            bool want = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(player,
                need is int n
                    ? $"Combat Hesitation: your opponent has more units left to activate. Roll to pass the turn to them ({n}+ to pass)?"
                    : "Combat Hesitation: your opponent has more units left to activate. Pass the turn to them (no roll needed after round 4)?",
                defaultAnswer: false, displayName: "Combat Hesitation"));
            if (!want) return false;
            if (need is int target)
            {
                int die = D6(c);
                if (die < target)
                {
                    c.Log($"Combat Hesitation: {c.GetPlayerName(player)} rolls {die} (needs {target}+) - must activate.");
                    return false;
                }
                c.Log($"Combat Hesitation: {c.GetPlayerName(player)} rolls {die} (needs {target}+) - passes the turn.");
            }
            else c.Log($"Combat Hesitation: {c.GetPlayerName(player)} passes the turn.");
            if (IsOn(c, EAdvancedRules.EbbAndFlow))
            {
                PlayerID? opponent = OpponentWithMost(c, player);
                PassedTo.Remove(c.GameDataStore);
                PassedTo.Add(c.GameDataStore, new StrongBox<PlayerID?>(opponent));
            }
            return true;
        }

        private static PlayerID? OpponentWithMost(IGameContext c, PlayerID me)
        {
            ITeam? mine = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(me));
            return c.TableState.Units.Objects
                .Where(u => u.GetIsAlive() && !u.Tokens.HasToken(Rules.Foundation.TokenType.ActivatedThisRound)
                    && (mine == null || !mine.IsPlayerOnTeam(u.PlayerID)))
                .GroupBy(u => u.PlayerID).OrderByDescending(g => g.Count())
                .Select(g => (PlayerID?)g.Key).FirstOrDefault();
        }

        // ================================================================= Shifting Focus

        /// <summary>How many sections: 3 along the long table edges (GF, AoF), 2 along the player's edge (GFF, AoFS).</summary>
        public static int Sections(EGameSystem s) => Skirmish(s) ? 2 : 3;

        /// <summary>The section a die roll gives: a D3 for three sections, 1-3 / 4-6 for two.</summary>
        public static int SectionFromDie(EGameSystem s, int d6) => Skirmish(s) ? (d6 <= 3 ? 1 : 2) : (d6 + 1) / 2;

        /// <summary>True when the player's own table edge is the far one (z high) - their left is then the table's right.</summary>
        private static bool FarSide(IGameContext c, PlayerID player)
        {
            ITeam? team = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(player));
            if (team != null && GameProgressUtilities.DeploymentZoneOf(c.GameDataStore, team.TeamNumber) is { } zone)
                return (zone.Bottom + zone.Top) / 2f > HordePlacement.TableHeight / 2f;
            var mine = c.TableState.Units.Objects.Where(u => u.PlayerID.Equals(player) && u.GetIsAlive())
                .SelectMany(u => u.Models.Where(m => m.GetIsAlive())).ToList();
            return mine.Count > 0 && mine.Average(m => m.Position.z) > HordePlacement.TableHeight / 2f;
        }

        /// <summary>The sections a unit is within (any living model; an embarked unit stands where its transport is).</summary>
        public static IEnumerable<int> SectionsOf(IGameContext c, IUnit unit, PlayerID player)
        {
            int n = Sections(c.Settings.GameSystem);
            float w = HordePlacement.TableWidth / n;
            bool far = FarSide(c, player);
            IUnit where = unit;
            if (TransportUtilities.GetTransportId(unit) is UnitID tid
                && c.TableState.Units.Objects.FirstOrDefault(u => u.ID.Equals(tid)) is { } ride) where = ride;
            var found = new HashSet<int>();
            foreach (IModel m in where.Models.Where(m => m.GetIsAlive()))
            {
                float r = m.BaseRadiusInches;
                for (int i = 0; i < n; i++)
                {
                    float lo = i * w, hi = (i + 1) * w;
                    if (m.Position.x + r > lo && m.Position.x - r < hi)
                        found.Add(far ? n - i : i + 1);
                }
            }
            return found;
        }

        /// <summary>Shifting Focus: roll the section, then move on until one holds an eligible unit. Returns the section and
        /// the units in it (null when the rule is off or no candidate is on the table at all).</summary>
        public static (int Section, HashSet<UnitID> Units)? Focus(IGameContext c, PlayerID player, IReadOnlyList<IUnit> candidates)
        {
            if (!IsOn(c, EAdvancedRules.ShiftingFocus) || candidates.Count == 0) return null;
            EGameSystem sys = c.Settings.GameSystem;
            int n = Sections(sys), die = D6(c), rolled = SectionFromDie(sys, die);
            var bySection = candidates.ToDictionary(u => u.ID, u => SectionsOf(c, u, player).ToHashSet());
            for (int step = 0; step < n; step++)
            {
                int section = (rolled - 1 + step) % n + 1;
                var units = candidates.Where(u => bySection[u.ID].Contains(section)).Select(u => u.ID).ToHashSet();
                if (units.Count == 0) continue;
                c.Log($"Shifting Focus: {c.GetPlayerName(player)} rolled section {rolled}"
                    + (section != rolled ? $" - nothing there, so section {section}" : "") + ".");
                return (section, units);
            }
            return null;
        }
    }
}
