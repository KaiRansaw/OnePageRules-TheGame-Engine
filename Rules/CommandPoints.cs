using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Stages;

namespace FDG
{
    /// <summary>Command Points state kept in the game's progress record (saves, network).</summary>
    [Serializable]
    public sealed class CommandPointsState
    {
        /// <summary>The playstyle in force (1-6), settled before round 1.</summary>
        public int Playstyle { get; set; }
        /// <summary>Each player's CP (player id -> points).</summary>
        public Dictionary<string, int> Points { get; set; } = new();
        /// <summary>Each player's doctrine (player id -> 1-6).</summary>
        public Dictionary<string, int> Doctrine { get; set; } = new();
        /// <summary>Each player's army points when the game began (player id -> points), what the CP are worked out from.</summary>
        public Dictionary<string, int> ArmyPoints { get; set; } = new();
        /// <summary>The last round CP were handed out for.</summary>
        public int LastRound { get; set; }
        /// <summary>Stratagems used in the current unit activation ("player:stratagem"): "each only being usable once per
        /// unit activation (friendly or enemy)". Cleared when the next turn starts.</summary>
        public List<string> Used { get; set; } = new();
        /// <summary>Offers a player turned down, so the same question isn't asked again until it could mean something new
        /// ("stratagem:when:player" - the round for Seize / Waive Initiative, the deployment for Delayed Deployment).</summary>
        public List<string> Declined { get; set; } = new();
    }

    /// <summary>
    /// Command Points (Advanced Rulebooks 3.6.0: GF p42-43, GFF p39-40, AoF p42-43, AoFS p39-40). "Command Points (CP)
    /// are an extra resource that can be added to the game, giving players access to critical tactical abilities that
    /// boost their units in battle." This file is the framework: the playstyles (how CP are earned), the doctrine each
    /// player picks, and the CP themselves. The stratagems come in their own files. Judgement calls: house rules C,
    /// "Command Points readings".
    /// </summary>
    public static partial class CommandPoints
    {
        public const string Name = "Command Points";
        public const int PickShift = 32;
        public const EAdvancedRules PickMask = (EAdvancedRules)(0x7UL << PickShift);

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (Name, EAdvancedRules.CommandPoints),
        };

        public const int Fixed = 1, Growing = 2, Temporary = 3, FixedRandom = 4, GrowingRandom = 5, TemporaryRandom = 6;

        public static string PlaystyleName(int p) => p switch
        {
            Fixed => "Fixed", Growing => "Growing", Temporary => "Temporary",
            FixedRandom => "Fixed Random", GrowingRandom => "Growing Random", TemporaryRandom => "Temporary Random",
            _ => "Roll for it",
        };

        /// <summary>"X is 4 for every 1000pts in their army" - the step is each book's own: GF 1000, AoF 750, GFF 400, AoFS 300.</summary>
        public static int Step(EGameSystem s) => s switch
        {
            EGameSystem.AgeOfFantasy => 750,
            EGameSystem.GFFirefight => 400,
            EGameSystem.AoFSkirmish => 300,
            _ => 1000,
        };

        public static string PlaystyleText(int p, EGameSystem s)
        {
            int step = Step(s);
            return p switch
            {
                Fixed => $"At the start of the game, players get X CP, where X is 4 for every {step}pts in their army.",
                Growing => $"At the start of each round, players get X CP, where X is 1 for every {step}pts in their army.",
                Temporary => $"At the start of each round, players get X CP, where X is 1 for every {step}pts in their army, and unspent CP are discarded at the end of the round.",
                FixedRandom => $"At the start of the game, players get D3*X CP, where X is 2 for every {step}pts in their army.",
                GrowingRandom => $"At the start of each round, players get D3*X CP, where X is 0.5 (rounding up the total) for every {step}pts in their army.",
                TemporaryRandom => $"At the start of each round, players get D3*X CP, where X is 0.5 (rounding up the total) for every {step}pts in their army, and unspent CP are discarded at the end of the round.",
                _ => "Before the game, roll one die for the playstyle.",
            };
        }

        public static string DoctrineName(int d) => d switch
        {
            1 => "Strategic Doctrine", 2 => "Defensive Doctrine", 3 => "Shock Doctrine",
            4 => "Hunting Doctrine", 5 => "Valorous Doctrine", 6 => "Tactical Doctrine",
            _ => "No doctrine",
        };

        public static string BookText(EGameSystem s) =>
            "Before the game begins, pick one of the following playstyles, or roll one die to randomly get one: 1. Fixed, 2. Growing, 3. Temporary, 4. Fixed Random, 5. Growing Random, 6. Temporary Random. "
            + "At any point during the game, players may then spend their CP in order to activate stratagems, with each only being usable once per unit activation (friendly or enemy). "
            + "Players always have access to all stratagems from the universal doctrine, as well as those from one other doctrine. Before the game begins, players must pick one of the following doctrines, or roll one die to randomly get one: "
            + "1. Strategic, 2. Defensive, 3. Shock, 4. Hunting, 5. Valorous, 6. Tactical.";

        public const string GameText = "Pick the playstyle below (or roll for it). Each player picks a doctrine before deployment; bots roll. "
            + "You're only offered a stratagem when you have the CP and it would change something right now; bots spend their own. CP show on the Mission panel.";

        public static bool On(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.CommandPoints);

        /// <summary>The playstyle picked in the lobby (0 = roll for it).</summary>
        public static int Picked(GameSettings s) => (int)(((ulong)s.AdvancedRules >> PickShift) & 0x7);

        public static EAdvancedRules WithPick(EAdvancedRules rules, int pick) =>
            (rules & ~PickMask) | (EAdvancedRules)(((ulong)Math.Clamp(pick, 0, 6)) << PickShift);

        // ================================================================= state

        public static CommandPointsState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.CommandPoints
            ?? (Pending.TryGetValue(store, out CommandPointsState? s) ? s : null);

        private static readonly ConditionalWeakTable<IReadableGameDataStore, CommandPointsState> Pending = new();

        internal static void Save(IGameContext c, CommandPointsState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.CommandPoints = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        internal static int D6(IGameContext c) => HordePlacement.RollD6(c);
        private static int D3(IGameContext c) => (D6(c) + 1) / 2;

        /// <summary>A player's CP now (0 when the rule is off).</summary>
        public static int Of(IGameContext c, PlayerID player) =>
            State(c.GameDataStore)?.Points.GetValueOrDefault(player.ToString()) ?? 0;

        /// <summary>A player's doctrine (1-6), or 0.</summary>
        public static int DoctrineOf(IGameContext c, PlayerID player) =>
            State(c.GameDataStore)?.Doctrine.GetValueOrDefault(player.ToString()) ?? 0;

        /// <summary>Spends CP if the player has them. False (and nothing spent) when they don't.</summary>
        public static bool Spend(IGameContext c, PlayerID player, int cost, string what)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) return false;
            string key = player.ToString();
            int have = s.Points.GetValueOrDefault(key);
            if (have < cost) return false;
            s.Points[key] = have - cost;
            Save(c, s);
            c.Log($"Command Points: {c.GetPlayerName(player)} spends {cost} CP on {what} ({have - cost} left).");
            return true;
        }

        // ================================================================= earning

        /// <summary>
        /// What a player earns at one hand-out. "X is 4 for every 1000pts" counts whole steps of the army, at least one;
        /// the random ones roll a D3 each time and multiply ("0.5 ... rounding up the total").
        /// </summary>
        public static int Earned(int playstyle, int armyPoints, EGameSystem system, Func<int> d3)
        {
            int steps = Math.Max(1, armyPoints / Step(system));
            return playstyle switch
            {
                Fixed => 4 * steps,
                Growing or Temporary => steps,
                FixedRandom => d3() * 2 * steps,
                GrowingRandom or TemporaryRandom => (int)Math.Ceiling(d3() * 0.5 * steps),
                _ => 0,
            };
        }

        /// <summary>Whether this playstyle hands out CP at the start of every round (else once, before round 1).</summary>
        public static bool EveryRound(int playstyle) => playstyle is Growing or Temporary or GrowingRandom or TemporaryRandom;

        /// <summary>Whether unspent CP are discarded at the end of each round.</summary>
        public static bool Discards(int playstyle) => playstyle is Temporary or TemporaryRandom;

        public const string RollDoctrine = "Roll for it";

        /// <summary>"Before the game begins": settle the playstyle, each player's doctrine, and a Fixed playstyle's CP.
        /// Called before deployment (Delayed / Hidden Deployment are spent there), and at round 1 if it hasn't run.</summary>
        public static async Task Settle(IGameContext c)
        {
            if (!On(c)) return;
            CommandPointsState s = State(c.GameDataStore) ?? new CommandPointsState();
            if (s.Playstyle != 0) return;
            var players = c.TableState.Teams.Objects.SelectMany(t => t.Players).Distinct().ToList();
            {
                int pick = Picked(c.Settings);
                if (pick == 0)
                {
                    pick = D6(c);
                    c.Log($"Command Points: rolled {pick} for the playstyle - {PlaystyleName(pick)}.");
                }
                s.Playstyle = pick;
                foreach (PlayerID p in players)
                    s.ArmyPoints[p.ToString()] = c.GameDataStore.GetAllValues<ArmyData>().Where(a => a.IsOwnedBy(p))
                        .SelectMany(a => a.UnitBindings).Sum(b => b.GetValue().PointCost);
                foreach (PlayerID p in players)
                {
                    // The doctrine: asked of each player; "Roll for it" is the first option, so a bot rolls.
                    var options = new List<string> { RollDoctrine };
                    options.AddRange(Enumerable.Range(1, 6).Select(DoctrineName));
                    string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(p,
                        "Command Points: pick your doctrine. You always have the Universal stratagems, plus the four of this doctrine.",
                        options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Pick your doctrine") { Recommended = RollDoctrine });
                    int d = options.IndexOf(answer);
                    if (d <= 0)
                    {
                        d = D6(c);
                        c.Log($"Command Points: {c.GetPlayerName(p)} rolls {d} for the doctrine - {DoctrineName(d)}.");
                    }
                    else c.Log($"Command Points: {c.GetPlayerName(p)} picks the {DoctrineName(d)}.");
                    s.Doctrine[p.ToString()] = d;
                }
                await c.Announce($"Command Points - {PlaystyleName(s.Playstyle)}: {PlaystyleText(s.Playstyle, c.Settings.GameSystem)}",
                    BattlefieldEffects.Amber, EBannerTier.Notice);
            }
            if (!EveryRound(s.Playstyle)) Hand(c, s, players);
            Save(c, s);
        }

        private static void Hand(IGameContext c, CommandPointsState s, IEnumerable<PlayerID> players)
        {
            foreach (PlayerID p in players)
            {
                string key = p.ToString();
                int gain = Earned(s.Playstyle, s.ArmyPoints.GetValueOrDefault(key), c.Settings.GameSystem, () => D3(c));
                s.Points[key] = s.Points.GetValueOrDefault(key) + gain;
                c.Log($"Command Points: {c.GetPlayerName(p)} gets {gain} CP ({s.Points[key]} now).");
            }
        }

        /// <summary>Start of each round: the Growing and Temporary playstyles hand out CP.</summary>
        public static async Task RoundStart(IGameContext c, int round)
        {
            if (!On(c)) return;
            await Settle(c);
            CommandPointsState s = State(c.GameDataStore)!;
            var players = c.TableState.Teams.Objects.SelectMany(t => t.Players).Distinct().ToList();
            if (s.LastRound < round && EveryRound(s.Playstyle))
                Hand(c, s, players);
            s.LastRound = Math.Max(s.LastRound, round);
            Save(c, s);
        }

        /// <summary>End of a round: the Temporary playstyles discard unspent CP.</summary>
        public static void RoundEnd(IGameContext c)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s || !Discards(s.Playstyle)) return;
            foreach (string key in s.Points.Keys.ToList())
                if (s.Points[key] > 0) s.Points[key] = 0;
            c.Log("Command Points: unspent CP are discarded at the end of the round.");
            Save(c, s);
        }
    }
}
