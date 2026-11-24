using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>Political Intrigue state kept in the game's progress record (saves, network).</summary>
    [Serializable]
    public sealed class PoliticalIntrigueState
    {
        /// <summary>The round the "used" lists below belong to; a new round clears them.</summary>
        public int Round { get; set; }
        /// <summary>Directives each player has used this round (player id -> directive names).</summary>
        public Dictionary<string, List<string>> Used { get; set; } = new();
        /// <summary>Players who said "no more directives this round".</summary>
        public List<string> Declined { get; set; } = new();
        /// <summary>Industry: the unit about to shoot and its one target (unit ids), read by DirectiveShootStage.</summary>
        public string? PendingShooter { get; set; }
        public string? PendingTarget { get; set; }
        /// <summary>This round's directives, for the Mission panel ("Kai: Influence - Objective 2").</summary>
        public List<string> RoundLog { get; set; } = new();

        // ---- politicians (build 3.30)
        /// <summary>1 Open, 2 Open + Hidden, 3 Hidden; 0 = not settled yet.</summary>
        public int Playstyle { get; set; }
        /// <summary>Each player's two politicians (player id -> names).</summary>
        public Dictionary<string, List<string>> Politicians { get; set; } = new();
        /// <summary>Politicians picked in secret and not used yet (player id -> names).</summary>
        public Dictionary<string, List<string>> Hidden { get; set; } = new();
        /// <summary>Politicians used this round (player id -> names).</summary>
        public Dictionary<string, List<string>> PoliticiansUsed { get; set; } = new();
        /// <summary>"Once per game" politicians already used (player id -> names).</summary>
        public Dictionary<string, List<string>> Spent { get; set; } = new();
        /// <summary>Mastermind on Influence: "playerId|x|z" - +1 to hit against enemies within 3" of the point, this round.</summary>
        public List<string> MastermindAreas { get; set; } = new();
        /// <summary>Escalator on Industry: the shooter gets a second shooting (unit id).</summary>
        public string? SecondShot { get; set; }
    }

    /// <summary>
    /// Political Intrigue (Advanced Rulebooks 3.6.0, GF p49 and AoF p49 - word for word the same; Firefight and Skirmish
    /// don't have the chapter). This file is the directives: "Twice per round, instead of activating one of their units,
    /// players may choose to use one directive that hasn't been used yet this round. Note that players may not use
    /// directives if they've run out of units to activate." Judgement calls: house rules C, "Political Intrigue readings".
    /// </summary>
    public static partial class PoliticalIntrigue
    {
        public const string Directives = "Directives";
        public const string Influence = "Influence";
        public const string Propaganda = "Propaganda";
        public const string Recruitment = "Recruitment";
        public const string Industry = "Industry";
        public const string Mobilization = "Mobilization";
        public const int PerRound = 2;
        public const float IndustryRangeInches = 12f;
        public const float MobilizationInches = 6f;

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (Directives, EAdvancedRules.PoliticalDirectives),
            (PoliticiansOption, EAdvancedRules.Politicians),
        };

        public static readonly IReadOnlyDictionary<string, string> DirectiveText = new Dictionary<string, string>
        {
            [Influence] = "Pick one objective that is controlled by either player, which becomes neutral again.",
            [Propaganda] = "Pick one enemy unit, which must take a Morale Test. If failed, the unit is Shaken.",
            [Recruitment] = "Pick one friendly unit, which restores D3 models/wounds. Note that new models may only be restored if they can be placed in coherency with non-restored models.",
            [Industry] = "Pick one friendly unit, which may immediately shoot at one enemy unit within 12\".",
            [Mobilization] = "Pick one friendly unit, which may immediately move by up to 6\" in any direction.",
        };

        public static IEnumerable<string> Order => new[] { Influence, Propaganda, Recruitment, Industry, Mobilization };

        public static bool BooksHaveIt(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy;

        public static string BookText(string name) => name == PoliticiansOption ? PoliticiansBookText :
            "Twice per round, instead of activating one of their units, players may choose to use one directive that hasn't been used yet this round. "
            + "Note that players may not use directives if they've run out of units to activate.\n"
            + string.Join("\n", Order.Select(d => $"{d}: {DirectiveText[d]}"));

        public static string GameText(string name) => name == PoliticiansOption ? PoliticiansGameText :
            "At the start of each of your turns you're asked whether to use a directive instead of activating a unit (twice per round, "
            + "each directive once). Answer \"no more this round\" to stop being asked until the next round. Bots use them too.";

        /// <summary>Directives are on with either box ticked (politicians use directives).</summary>
        public static bool On(IGameContext c) =>
            AdvancedRules.IsOn(c, EAdvancedRules.PoliticalDirectives) || AdvancedRules.IsOn(c, EAdvancedRules.Politicians);

        public static PoliticalIntrigueState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.PoliticalIntrigue
            ?? (Pending.TryGetValue(store, out PoliticalIntrigueState? p) ? p : null);

        /// <summary>Politicians are picked before the progress record exists; held here until it does.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadableGameDataStore, PoliticalIntrigueState> Pending = new();

        private static PoliticalIntrigueState StateFor(IGameContext c, int round)
        {
            PoliticalIntrigueState s = State(c.GameDataStore) ?? new PoliticalIntrigueState { Round = round };
            if (s.Round != round)
            {
                s.Round = round;
                s.Used.Clear();
                s.Declined.Clear();
                s.RoundLog.Clear();
                s.PoliticiansUsed.Clear();
                s.MastermindAreas.Clear();
            }
            return s;
        }

        internal static void Save(IGameContext c, PoliticalIntrigueState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.PoliticalIntrigue = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        private static List<string> UsedBy(PoliticalIntrigueState s, PlayerID player) =>
            s.Used.TryGetValue(player.ToString(), out var l) ? l : s.Used[player.ToString()] = new List<string>();

        // ---------------------------------------------------------------- who and what

        private static ITeam? TeamOf(IGameContext c, PlayerID p) => c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(p));

        private static bool SameSide(IGameContext c, PlayerID player, PlayerID other) =>
            other.Equals(player) || (TeamOf(c, player) is { } team && team.IsPlayerOnTeam(other));

        private static bool Friendly(IGameContext c, PlayerID player, IUnit u) => SameSide(c, player, u.PlayerID);

        private static IEnumerable<IUnit> OnTable(IGameContext c) =>
            c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield() && !TransportUtilities.IsEmbarked(u));

        public static List<IUnit> Mine(IGameContext c, PlayerID player) => OnTable(c).Where(u => u.PlayerID.Equals(player)).ToList();

        public static List<IUnit> Enemies(IGameContext c, PlayerID player) => OnTable(c).Where(u => !Friendly(c, player, u)).ToList();

        private static bool Missing(IUnit u) => u.Models.Any(m => !m.GetIsAlive() || m.WoundsDealt > 0.999f);

        /// <summary>The enemy units a friendly unit can shoot under Industry: within 12" and a legal target.</summary>
        public static List<IUnit> IndustryTargets(IGameContext c, IUnit shooter)
        {
            if (shooter.Tokens.HasToken(TokenType.Shaken) || UnitPsychology.BindingOf(c, shooter) is not { } binding) return new();
            return Enemies(c, shooter.PlayerID)
                .Where(e => UnitCompareUtilities.MinDistanceBetweenUnits(shooter, e, out _, out _, includeVertical: true) <= IndustryRangeInches + 0.001f)
                .Where(e => ChooseRangedAttackStage.HasAnyFireableTarget(binding, c, onlyTarget: e.ID))
                .ToList();
        }

        /// <summary>What each directive could be used on now (empty = not offered).</summary>
        public static int TargetCount(IGameContext c, PlayerID player, string directive) => directive switch
        {
            Influence => c.TableState.Objectives.Objects.Count(o => o.OwnerID != null),
            Propaganda => Enemies(c, player).Count(u => !u.Tokens.HasToken(TokenType.Shaken)),
            Recruitment => Mine(c, player).Count(Missing),
            Industry => Mine(c, player).Count(u => IndustryTargets(c, u).Count > 0),
            Mobilization => Mine(c, player).Count,
            _ => 0,
        };

        // ---------------------------------------------------------------- the offer

        public const string ActivateUnit = "Activate a unit";
        public const string NoMoreThisRound = "Activate a unit - no more directives this round";

        public enum EOutcome { None, Used, Shoot }

        /// <summary>
        /// At the start of a turn: offer a directive instead of activating a unit. Used = the turn is spent; Shoot = Industry
        /// picked a shooter and target, and the turn's shooting step runs it.
        /// </summary>
        public static async Task<EOutcome> Offer(IGameContext c, PlayerID player, int round, bool hasUnitsToActivate)
        {
            if (!On(c) || !hasUnitsToActivate) return EOutcome.None;
            PoliticalIntrigueState s = StateFor(c, round);
            List<string> used = UsedBy(s, player);
            if (used.Count >= PerRound || s.Declined.Contains(player.ToString())) return EOutcome.None;
            var open = Order.Where(d => !used.Contains(d) && TargetCount(c, player, d) > 0).ToList();
            if (open.Count == 0) return EOutcome.None;

            var options = new List<string> { ActivateUnit, NoMoreThisRound };
            options.AddRange(open.Select(d => $"{d}: {DirectiveText[d]}"));
            string? bot = BotPick(c, player, open);
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                $"Political Intrigue: use a directive instead of activating a unit? ({PerRound - used.Count} left this round)",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Political Intrigue - directive")
            { Recommended = bot != null ? options[open.IndexOf(bot) + 2] : ActivateUnit });
            int idx = options.IndexOf(answer);
            if (idx == 1) { s.Declined.Add(player.ToString()); Save(c, s); return EOutcome.None; }
            if (idx < 2) return EOutcome.None;
            string directive = open[idx - 2];
            EOutcome outcome = await Use(c, s, player, directive);
            if (outcome == EOutcome.None) return EOutcome.None;   // backed out of the pick
            used.Add(directive);
            Save(c, s);
            return outcome;
        }

        /// <summary>
        /// The bot's pick (the Solo &amp; Co-Op books predate the chapter): a free shot first, then taking an objective
        /// from the enemy, healing a hurt unit, and shaking the enemy's most expensive unit. Never Mobilization (a move
        /// without a plan behind it).
        /// </summary>
        public static string? BotPick(IGameContext c, PlayerID player, IReadOnlyList<string> open)
        {
            if (open.Contains(Industry)) return Industry;
            if (open.Contains(Influence) && c.TableState.Objectives.Objects.Any(o => o.OwnerID is PlayerID p && !SameSide(c, player, p)))
                return Influence;
            if (open.Contains(Recruitment)) return Recruitment;
            if (open.Contains(Propaganda)) return Propaganda;
            return null;
        }

        private static async Task<IUnit?> PickUnit(IGameContext c, PlayerID player, List<IUnit> units, string question, IUnit? suggest)
        {
            if (units.Count == 0) return null;
            var names = new List<string>();
            foreach (IUnit u in units)
            {
                string n = u.Name;
                for (int i = 2; names.Contains(n); i++) n = $"{u.Name} ({i})";
                names.Add(n);
            }
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                question, names, new List<StringSelectionRequest.InvalidOption>(), displayName: "Political Intrigue - pick")
            { Recommended = suggest != null ? names[units.IndexOf(suggest)] : names[0] });
            int idx = names.IndexOf(answer);
            return idx >= 0 ? units[idx] : units[0];
        }

        private static int Cost(IUnit u) => (u as UnitData)?.PointCost ?? 0;

        private static string ObjectiveLabel(IGameContext c, IObjective o)
        {
            int n = c.TableState.Objectives.Objects.ToList().IndexOf(o) + 1;
            string holder = o.OwnerID is PlayerID p ? c.GetPlayerName(p) : "nobody";
            return $"Objective {n} (held by {holder})";
        }

        private static async Task<EOutcome> Use(IGameContext c, PoliticalIntrigueState s, PlayerID player, string directive)
        {
            string me = c.GetPlayerName(player);
            switch (directive)
            {
                case Influence:
                {
                    var held = c.TableState.Objectives.Objects.Where(o => o.OwnerID != null).ToList();
                    var labels = held.Select(o => ObjectiveLabel(c, o)).ToList();
                    IObjective? suggest = held.FirstOrDefault(o => o.OwnerID is PlayerID p && !SameSide(c, player, p));
                    string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                        "Influence: pick an objective - it becomes neutral again.", labels, new List<StringSelectionRequest.InvalidOption>(),
                        displayName: "Political Intrigue - pick") { Recommended = suggest != null ? labels[held.IndexOf(suggest)] : labels[0] });
                    int idx = labels.IndexOf(answer);
                    IObjective target = held[idx >= 0 ? idx : 0];
                    string label = ObjectiveLabel(c, target);
                    string? politician = await Declare(c, s, player, directive, null, target);
                    target.SetOwner(null);
                    await Note(c, s, $"{me}: Influence{By(politician)} - {label} is neutral");
                    await PoliticianEffect(c, s, player, politician, directive, null, target);
                    return EOutcome.Used;
                }
                case Propaganda:
                {
                    var enemies = Enemies(c, player).Where(u => !u.Tokens.HasToken(TokenType.Shaken)).ToList();
                    if (await PickUnit(c, player, enemies, "Propaganda: pick an enemy unit - it takes a Morale Test, and is Shaken if it fails.",
                            enemies.OrderByDescending(Cost).FirstOrDefault()) is not { } enemy) return EOutcome.None;
                    string? politician = await Declare(c, s, player, directive, enemy, null);
                    MoraleUtilities.MoraleTestOutcome test = await MoraleUtilities.TakeMoraleTest(c, enemy, enemy.Quality,
                        EMoraleTrigger.OtherSource, failureCausesShakenOrRout: true);
                    if (!test.Passed && !enemy.Tokens.HasToken(TokenType.Shaken) && UnitPsychology.BindingOf(c, enemy) is { } b)
                        await MoraleUtilities.ApplyShakenWithPresentation(c, b);
                    await Note(c, s, $"{me}: Propaganda{By(politician)} - {enemy.Name} {(test.Passed ? "holds firm" : "is Shaken")}");
                    await PoliticianEffect(c, s, player, politician, directive, enemy, null);
                    return EOutcome.Used;
                }
                case Recruitment:
                {
                    var hurt = Mine(c, player).Where(Missing).ToList();
                    if (await PickUnit(c, player, hurt, "Recruitment: pick one of your units - it restores D3 models/wounds.",
                            hurt.OrderByDescending(Cost).FirstOrDefault()) is not { } unit) return EOutcome.None;
                    string? politician = await Declare(c, s, player, directive, unit, null);
                    await new GameOperationServices(c).RestoreD3(unit, "Recruitment");
                    await Note(c, s, $"{me}: Recruitment{By(politician)} - {unit.Name}");
                    await PoliticianEffect(c, s, player, politician, directive, unit, null);
                    return EOutcome.Used;
                }
                case Industry:
                {
                    var shooters = Mine(c, player).Where(u => IndustryTargets(c, u).Count > 0).ToList();
                    if (await PickUnit(c, player, shooters, "Industry: pick one of your units - it shoots at one enemy unit within 12\" now.",
                            shooters.OrderByDescending(Cost).FirstOrDefault()) is not { } shooter) return EOutcome.None;
                    var targets = IndustryTargets(c, shooter);
                    if (await PickUnit(c, player, targets, $"Industry: pick the enemy unit {shooter.Name} shoots at.",
                            targets.OrderBy(t => UnitCompareUtilities.MinDistanceBetweenUnits(shooter, t, out _, out _, includeVertical: true)).FirstOrDefault()) is not { } target)
                        return EOutcome.None;
                    string? politician = await Declare(c, s, player, directive, shooter, null);
                    s.PendingShooter = shooter.ID.ID.ToString();
                    s.PendingTarget = target.ID.ID.ToString();
                    await Note(c, s, $"{me}: Industry{By(politician)} - {shooter.Name} shoots at {target.Name}");
                    await PoliticianEffect(c, s, player, politician, directive, shooter, null);   // before the shot: +1 to hit, AP...
                    return EOutcome.Shoot;
                }
                case Mobilization:
                {
                    var units = Mine(c, player);
                    if (await PickUnit(c, player, units, "Mobilization: pick one of your units - it may move up to 6\" in any direction now.",
                            units.FirstOrDefault()) is not { } unit) return EOutcome.None;
                    string? politician = await Declare(c, s, player, directive, unit, null);
                    await Note(c, s, $"{me}: Mobilization{By(politician)} - {unit.Name}");
                    if (politician == Deceiver) await PoliticianEffect(c, s, player, politician, directive, unit, null);   // before the move
                    await new GameOperationServices(c).MoveUnit(unit, MobilizationInches, isOptional: true);
                    if (politician != Deceiver) await PoliticianEffect(c, s, player, politician, directive, unit, null);
                    return EOutcome.Used;
                }
            }
            return EOutcome.None;
        }

        private static string By(string? politician) => politician != null ? $" (by the {politician})" : "";

        private static async Task Note(IGameContext c, PoliticalIntrigueState s, string line)
        {
            s.RoundLog.Add(line);
            c.Log($"Political Intrigue: {line}.");
            await c.Announce(line, BattlefieldEffects.Amber, EBannerTier.Toast);
        }

        /// <summary>Industry's shooter and its one target, taken off the state (once).</summary>
        public static (DataBinding<UnitData> Shooter, UnitID Target)? TakePendingShot(IGameContext c)
        {
            if (State(c.GameDataStore) is not { PendingShooter: { } sid, PendingTarget: { } tid } s) return null;
            s.PendingShooter = null;
            s.PendingTarget = null;
            Save(c, s);
            IUnit? shooter = c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == sid);
            IUnit? target = c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == tid);
            if (shooter == null || target == null || !shooter.GetIsAlive() || !target.GetIsAlive()) return null;
            if (UnitPsychology.BindingOf(c, shooter) is not { } binding) return null;
            return (binding, target.ID);
        }
    }
}
