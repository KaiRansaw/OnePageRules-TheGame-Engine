using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Stages;

namespace FDG
{
    /// <summary>
    /// Command Points - the Universal Doctrine stratagems (GF p42, GFF p39, AoF p42, AoFS p39; the same text in all four
    /// books apart from the worked examples). "Players always have access to all stratagems from the universal doctrine."
    ///
    /// Kai's call (2026-11-23): a stratagem is only offered when the player has the CP and it would change something right
    /// now; bots spend their own. The offers go through the ordinary questions (the actions area - mockup 3A).
    /// Judgement calls: house rules C, "Command Points - Universal stratagems".
    /// </summary>
    public static partial class CommandPoints
    {
        public const string HighCommand = "High Command", SupremeCommand = "Supreme Command",
            SeizeInitiative = "Seize Initiative", WaiveInitiative = "Waive Initiative",
            DelayedDeployment = "Delayed Deployment", HiddenDeployment = "Hidden Deployment";

        /// <summary>Each Universal stratagem: its cost and the book's text.</summary>
        public static readonly IReadOnlyList<(string Name, int Cost, string Text)> Universal = new (string, int, string)[]
        {
            (HighCommand, 1, "Add +1 to the result of any single die."),
            (SupremeCommand, 2, "Add +1 to the result of all dice in a single roll."),
            (SeizeInitiative, 2, "When it's the enemy turn to activate a unit, you may activate one of your units instead. The opposing player may spend 2 CP in order to stop this ability from having any effect."),
            (WaiveInitiative, 2, "When it's your turn to activate a unit, you force the opponent to activate one of their units instead. The opposing player may spend 2 CP in order to stop this ability from having any effect."),
            (DelayedDeployment, 2, "During the deployment phase, when it's your turn to place a unit, you may pass the turn to your opponent. The opposing player may spend 2 CP in order to stop this ability from having any effect."),
            (HiddenDeployment, 3, "After all units have deployed, you may remove up to 3 friendly units and place them again in any order. The opposing player may spend 3 CP to do the same, in which case players alternate in placing those units again."),
        };

        public static int CostOf(string stratagem) =>
            Universal.FirstOrDefault(u => u.Name == stratagem).Cost is int u and > 0 ? u
            : DoctrineStratagems.FirstOrDefault(d => d.Name == stratagem).Cost;

        private static string UseKey(PlayerID player, string stratagem) => $"{player}:{stratagem}";

        /// <summary>Whether the player could spend on this stratagem now: the rule is on, they have the CP, and they haven't
        /// used it in this unit activation.</summary>
        public static bool Can(IGameContext c, PlayerID player, string stratagem)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) return false;
            return s.Points.GetValueOrDefault(player.ToString()) >= CostOf(stratagem)
                && !s.Used.Contains(UseKey(player, stratagem))
                && HasDoctrine(c, player, stratagem);
        }

        /// <summary>Spends the stratagem's CP and marks it used for this activation.</summary>
        private static bool Use(IGameContext c, PlayerID player, string stratagem)
        {
            if (!Can(c, player, stratagem) || !Spend(c, player, CostOf(stratagem), stratagem)) return false;
            CommandPointsState s = State(c.GameDataStore)!;
            s.Used.Add(UseKey(player, stratagem));
            Save(c, s);
            return true;
        }

        /// <summary>A new turn (a new unit activation): every stratagem can be used again.</summary>
        public static void NewActivation(IGameContext c)
        {
            if (!On(c) || State(c.GameDataStore) is not { Used.Count: > 0 } s) return;
            s.Used.Clear();
            Save(c, s);
        }

        private static bool WasDeclined(IGameContext c, string key) => State(c.GameDataStore)?.Declined.Contains(key) == true;

        private static void Decline(IGameContext c, string key)
        {
            if (State(c.GameDataStore) is not { } s || s.Declined.Contains(key)) return;
            s.Declined.Add(key);
            Save(c, s);
        }

        private static string Cp(IGameContext c, PlayerID p) => $"you have {Of(c, p)} CP";

        /// <summary>The question's heading - the screen shows the part before ": " as the title and the rest underneath
        /// (Kai, 2026-11-25: say what the CP are for and against, short).</summary>
        internal static string Head(IGameContext c, PlayerID p, string stratagem, int cost) =>
            $"{stratagem}? {cost} CP ({Of(c, p)} left)";

        // ================================================================= High / Supreme Command

        public const string NoThanks = "No thanks";

        /// <summary>Moves <paramref name="count"/> dice from <paramref name="face"/> up to the next face: "+1 to the result".</summary>
        public static IDiceResults Lift(IDiceResults roll, int face, float count)
        {
            int n = roll.SideMax - roll.SideMin + 1;
            float[] sides = new float[n];
            for (int f = roll.SideMin; f <= roll.SideMax; f++) sides[f - roll.SideMin] = roll.At(f);
            int i = face - roll.SideMin;
            if (i < 0 || i + 1 >= n) return roll;
            float moved = Math.Min(count, sides[i]);
            sides[i] -= moved;
            sides[i + 1] += moved;
            return new DiceResults(sides, roll.SideMin);
        }

        /// <summary>The dice a +1 would turn from a failure into a success: those one short of the target. A natural 1 never
        /// counts - "a roll of 1 always fails" (house rules C).</summary>
        public static int OneShort(IDiceResults roll, int need) =>
            need - 1 < 2 ? 0 : (int)Math.Floor(roll.At(need - 1) + 0.0001f);

        /// <summary>
        /// High Command / Supreme Command, offered after a roll when at least one die missed by exactly 1. Returns how many
        /// dice get +1 (0 when nothing is spent). Supreme Command is only offered when it would lift two or more dice
        /// (otherwise High Command does the same for less). Bots take High Command whenever it's offered and Supreme
        /// Command whenever it lifts 2+ dice.
        /// </summary>
        public static Task<int> CommandDice(IGameContext c, PlayerID player, IDiceResults roll, int need, string what, string? about = null) =>
            CommandDice(c, player, OneShort(roll, need), need, what, about);

        /// <summary>The same, given how many dice missed by 1 (a Defense roll can mix save targets; <paramref name="need"/> is
        /// only shown).</summary>
        public static async Task<int> CommandDice(IGameContext c, PlayerID player, int shy, int need, string what, string? about = null)
        {
            if (!On(c) || shy <= 0) return 0;
            var options = new List<string>();
            string supreme = $"{SupremeCommand} (2 CP): +1 to every die - {shy} more {Noun(what, shy)}";
            string high = $"{HighCommand} (1 CP): +1 to one die - 1 more {Noun(what, 1)}";
            if (shy >= 2 && Can(c, player, SupremeCommand)) options.Add(supreme);
            if (Can(c, player, HighCommand)) options.Add(high);
            if (options.Count == 0) return 0;
            options.Add(NoThanks);
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                $"Command Points? {Of(c, player)} CP left: {(about != null ? about + " - " : "")}{shy} {(shy == 1 ? "die" : "dice")} on the {what} missed the {need}+ by 1. Add +1 to {(shy == 1 ? "it" : "them")}?",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Deciding on a stratagem")
            { Recommended = options[0] });
            int lifted = answer == supreme && Use(c, player, SupremeCommand) ? shy
                : answer == high && Use(c, player, HighCommand) ? 1 : 0;
            if (lifted > 0)
                await c.Announce($"{(lifted == shy && answer == supreme ? SupremeCommand : HighCommand)}: +1 to {(lifted == 1 ? "one die" : "every die")} - {lifted} more {Noun(what, lifted)}.",
                    BattlefieldEffects.Amber, EBannerTier.Toast);
            return lifted;
        }

        private static string Noun(string what, int n) => what switch
        {
            "hit roll" => n == 1 ? "hit" : "hits",
            "Defense roll" => n == 1 ? "save" : "saves",
            _ => n == 1 ? "success" : "successes",
        };

        // ================================================================= Seize / Waive Initiative

        /// <summary>"The opposing player may spend 2 CP in order to stop this ability from having any effect." Asked of each
        /// opponent who can afford it; bots always stop it when they can.</summary>
        private static async Task<bool> Stopped(IGameContext c, string stratagem, PlayerID user, IEnumerable<PlayerID> opponents)
        {
            int cost = CostOf(stratagem);
            foreach (PlayerID o in opponents)
            {
                if (!On(c) || Of(c, o) < cost) continue;
                bool stop = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(o,
                    $"Stop {stratagem}? {cost} CP ({Of(c, o)} left): {c.GetPlayerName(user)} used {stratagem}. Spend {cost} CP so it has no effect?",
                    defaultAnswer: true, displayName: "Deciding whether to stop a stratagem"));
                if (stop && Spend(c, o, cost, $"stopping {stratagem}"))
                {
                    await c.Announce($"{c.GetPlayerName(o)} stops {stratagem}.", BattlefieldEffects.Amber, EBannerTier.Toast);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The start of a turn, before the unit is picked: the active player may Waive Initiative, else each opponent with a
        /// unit left may Seize Initiative. True when the turn passes (the caller hands it on, as for a Delayed Action).
        /// Only asked when the other side has a unit left to activate; a "no" isn't asked again that round. Bots don't use
        /// either (they do stop them).
        /// </summary>
        public static async Task<bool> Initiative(IGameContext c, PlayerID active, IReadOnlyList<PlayerID> opponentsWithUnits, int round)
        {
            if (!On(c) || opponentsWithUnits.Count == 0) return false;
            string waiveKey = $"{WaiveInitiative}:{round}:{active}";
            if (Can(c, active, WaiveInitiative) && !WasDeclined(c, waiveKey))
            {
                bool waive = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(active,
                    $"{Head(c, active, WaiveInitiative, 2)}: pass this turn so your opponent activates a unit first? (They may pay 2 CP to stop it.)",
                    defaultAnswer: false, displayName: "Deciding on a stratagem"));
                if (!waive) Decline(c, waiveKey);
                else if (Use(c, active, WaiveInitiative))
                {
                    await c.Announce($"{c.GetPlayerName(active)} waives the initiative!", BattlefieldEffects.Amber, EBannerTier.Toast);
                    if (!await Stopped(c, WaiveInitiative, active, opponentsWithUnits)) return true;
                }
            }
            foreach (PlayerID o in opponentsWithUnits)
            {
                string seizeKey = $"{SeizeInitiative}:{round}:{o}";
                if (!Can(c, o, SeizeInitiative) || WasDeclined(c, seizeKey)) continue;
                bool seize = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(o,
                    $"{Head(c, o, SeizeInitiative, 2)}: it's {c.GetPlayerName(active)}'s turn - activate one of your units instead? (They may pay 2 CP to stop it.)",
                    defaultAnswer: false, displayName: "Deciding on a stratagem"));
                if (!seize) { Decline(c, seizeKey); continue; }
                if (!Use(c, o, SeizeInitiative)) continue;
                await c.Announce($"{c.GetPlayerName(o)} seizes the initiative!", BattlefieldEffects.Amber, EBannerTier.Toast);
                if (!await Stopped(c, SeizeInitiative, o, new[] { active })) return true;
            }
            return false;
        }

        // ================================================================= Delayed / Hidden Deployment

        /// <summary>
        /// Delayed Deployment: on a player's deployment turn, while an opponent still has units to place, they may pass the
        /// turn. True when it passes (the caller moves the turn on). A "no" isn't asked again this deployment. Bots don't use it.
        /// </summary>
        public static async Task<bool> DelayDeployment(IGameContext c, PlayerID player, IReadOnlyList<PlayerID> opponentsStillPlacing)
        {
            if (!On(c) || opponentsStillPlacing.Count == 0) return false;
            string key = $"{DelayedDeployment}:deploy:{player}";
            if (!Can(c, player, DelayedDeployment) || WasDeclined(c, key)) return false;
            bool pass = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(player,
                $"{Head(c, player, DelayedDeployment, 2)}: pass this deployment turn so your opponent places a unit first? (They may pay 2 CP to stop it. No = not asked again this deployment.)",
                defaultAnswer: false, displayName: "Deciding on a stratagem"));
            if (!pass) { Decline(c, key); return false; }
            if (!Use(c, player, DelayedDeployment)) return false;
            NewActivation(c); // each deployment turn is its own moment - the next one may delay again
            await c.Announce($"{c.GetPlayerName(player)} delays their deployment.", BattlefieldEffects.Amber, EBannerTier.Toast);
            return !await Stopped(c, DelayedDeployment, player, opponentsStillPlacing);
        }

        /// <summary>
        /// Hidden Deployment, after all units have deployed: each player who spends 3 CP may remove up to 3 friendly units
        /// and place them again ("The opposing player may spend 3 CP to do the same, in which case players alternate").
        /// Returns the extra re-deploys each player bought; the Re-Deployment step does the picking up and placing,
        /// alternating. Bots don't use it.
        /// </summary>
        public static async Task<Dictionary<PlayerID, int>> HiddenDeploys(IGameContext c, IReadOnlyList<PlayerID> playersInOrder)
        {
            var extra = new Dictionary<PlayerID, int>();
            if (!On(c)) return extra;
            await Settle(c);
            PlayerID? first = null;
            foreach (PlayerID p in playersInOrder)
            {
                if (!Can(c, p, HiddenDeployment)) continue;
                string text = first is { } f
                    ? $"{Head(c, p, HiddenDeployment, 3)}: {c.GetPlayerName(f)} used Hidden Deployment. Do the same - pick up to 3 of your units and place them again, taking turns?"
                    : $"{Head(c, p, HiddenDeployment, 3)}: pick up to 3 of your units and place them again? (Your opponent may pay 3 CP to do the same.)";
                bool yes = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(p, text,
                    defaultAnswer: false, displayName: "Deciding on a stratagem"));
                if (!yes || !Use(c, p, HiddenDeployment)) continue;
                extra[p] = 3;
                first ??= p;
                await c.Announce($"{c.GetPlayerName(p)} uses Hidden Deployment.", BattlefieldEffects.Amber, EBannerTier.Toast);
            }
            return extra;
        }
    }
}
