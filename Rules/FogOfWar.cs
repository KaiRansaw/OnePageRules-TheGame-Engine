using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
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
        public const string SurpriseEngagementName = "Surprise Engagement";
        public const string EscalatingBattlesName = "Escalating Battles";
        public const string RandomArrivalName = "Escalating Battles: Random Arrival";
        public const int EscalatingRounds = 6, Detachments = 4;

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (EscalatingBattlesName, EAdvancedRules.EscalatingBattles),
            (RandomArrivalName, EAdvancedRules.EscalatingRandomArrival),
            (SurpriseEngagementName, EAdvancedRules.SurpriseEngagement),
            (EbbAndFlowName, EAdvancedRules.EbbAndFlow),
            (ShiftingFocusName, EAdvancedRules.ShiftingFocus),
            (CombatHesitationName, EAdvancedRules.CombatHesitation),
            (ProlongedBattleName, EAdvancedRules.ProlongedBattle),
        };

        private static bool Skirmish(EGameSystem s) => s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public static string BookText(string name, EGameSystem s) => name switch
        {
            EscalatingBattlesName => "Before the game begins, players have to divide their army into four detachments of equal point value (or as close as possible). During deployment, each player must pick one of their detachments and must deploy only units from that detachment (units from that detachment in Ambush may be deployed at a later point as usual). At the beginning of each new round, players must pick one of their detachments, and must then deploy all units from it (units from that detachment in Ambush may be deployed at a later point as usual). When playing escalating battles, the game lasts 6 rounds.",
            RandomArrivalName => "Random Arrival (optional): When a detachment should be deployed, players don't get to choose which one they deploy, but must randomly pick one instead.",
            SurpriseEngagementName => Skirmish(s)
                ? "When deploying armies, the players roll-off to see who goes first, and alternate in placing units as usual, however where they place them is randomized. First, each player divides their deployment zone into 2 equal sections, and numbers them section 1 and section 2. Then, when it's a player's turn to deploy a unit, roll a die to see which section you get, and place the unit fully within (results of 1-3 = section 1, 4-6 = section 2). Units that are deployed differently due to special rules (such as Ambush) have to follow the same rules, however the entire table is divided into 2 equal sections along the player's table edge instead of only the deployment zones."
                : "When deploying armies, the players roll-off to see who goes first, and alternate in placing units as usual, however where they place them is randomized. First, each player divides their deployment zone into 3 equal sections and gives each section a number from 1 to 3. Then, when it's a player's turn to deploy a unit, roll a D3 and place the unit fully within the resulting section. Units that are deployed differently due to special rules (such as Ambush) have to follow the same rules, however the entire table is divided into 3 equal sections along the long table edge, instead of only the deployment zones.",
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
            EscalatingBattlesName => "The game splits each army into four detachments as close in points as it can (biggest units first, each to the lightest detachment). You pick one to deploy; at the start of rounds 2, 3 and 4 you pick the next, and its units are placed in your deployment zone. The game lasts 6 rounds. Flexible Strategies isn't offered yet.",
            RandomArrivalName => "Needs Escalating Battles. Each detachment is picked at random instead of by you.",
            SurpriseEngagementName => "Each time you deploy a unit the game rolls its section; the placement outline shows only that part of your zone. Scout and Ambush units roll a section of the whole table (along its long edge).",
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

        // ================================================================= Surprise Engagement

        /// <summary>A section of a placement area: the area's longer side cut into equal strips. Plain rectangles stay
        /// rectangles (the unit must stand fully within); any other shape is clipped.</summary>
        public static IBoundedZone Section(IBoundedZone zone, int sections, int section)
        {
            ZoneBounds b = zone.Bounds;
            bool alongX = (b.Right - b.Left) >= (b.Top - b.Bottom);
            float len = alongX ? b.Right - b.Left : b.Top - b.Bottom;
            float lo = (alongX ? b.Left : b.Bottom) + len * (section - 1) / sections;
            float hi = (alongX ? b.Left : b.Bottom) + len * section / sections;
            RectangularZone strip = alongX ? new RectangularZone(lo, hi, b.Bottom, b.Top) : new RectangularZone(b.Left, b.Right, lo, hi);
            if (zone.GetType() == typeof(RectangularZone)) return strip;
            return new ClippedZone(zone, strip);
        }

        /// <summary>Surprise Engagement for a normal deployment: rolls the section and returns that part of the zone (null when off).</summary>
        public static IBoundedZone? DeploySection(IGameContext c, UnitData unit, IBoundedZone zone)
        {
            if (!IsOn(c, EAdvancedRules.SurpriseEngagement)) return null;
            EGameSystem sys = c.Settings.GameSystem;
            int n = Sections(sys), section = SectionFromDie(sys, D6(c));
            c.Log($"Surprise Engagement: {unit.Name} must deploy in section {section} of {n}.");
            return Section(zone, n, section);
        }

        /// <summary>Surprise Engagement for Scout / Ambush: "the entire table is divided into sections along the long
        /// edge" - the rolled strip of the whole table, cut out of the area the rule allows.</summary>
        public static IBoundedZone? SpecialSection(IGameContext c, UnitData unit, IBoundedZone area, float tableWidth, float tableHeight)
        {
            if (!IsOn(c, EAdvancedRules.SurpriseEngagement)) return null;
            EGameSystem sys = c.Settings.GameSystem;
            int n = Sections(sys), section = SectionFromDie(sys, D6(c));
            c.Log($"Surprise Engagement: {unit.Name} must arrive in table section {section} of {n}.");
            var table = new RectangularZone(0f, tableWidth, 0f, tableHeight);
            var strip = (RectangularZone)Section(table, n, section);
            return area.GetType() == typeof(RectangularZone) && area is RectangularZone r
                ? new RectangularZone(Math.Max(r.Left, strip.Left), Math.Min(r.Right, strip.Right), Math.Max(r.Bottom, strip.Bottom), Math.Min(r.Top, strip.Top))
                : new ClippedZone(area, strip);
        }

        // ================================================================= Escalating Battles

        /// <summary>"Divide their army into four detachments of equal point value (or as close as possible)": biggest
        /// units first, each into the lightest detachment so far.</summary>
        public static List<List<UnitData>> Split(IEnumerable<UnitData> units, int detachments = Detachments)
        {
            var groups = Enumerable.Range(0, detachments).Select(_ => new List<UnitData>()).ToList();
            foreach (UnitData u in units.OrderByDescending(u => u.PointCost).ThenBy(u => u.Name))
                groups.OrderBy(g => g.Sum(x => x.PointCost)).ThenBy(g => g.Count).First().Add(u);
            return groups;
        }

        private static bool EscalatingOn(IGameContext c) =>
            IsOn(c, EAdvancedRules.EscalatingBattles) && c.Settings.GameMode == EGameMode.Standard;

        private static string Label(int number, IReadOnlyList<UnitData> units) =>
            $"Detachment {number} ({units.Sum(u => u.PointCost)} pts): {string.Join(", ", units.Select(u => u.Name))}";

        /// <summary>Asks (or rolls, with Random Arrival) which detachment comes on. Returns its number.</summary>
        private static async Task<int> PickDetachment(IGameContext c, PlayerID player, IReadOnlyDictionary<int, List<UnitData>> groups, string when)
        {
            var numbers = groups.Keys.OrderBy(k => k).ToList();
            if (numbers.Count == 1) return numbers[0];
            if (IsOn(c, EAdvancedRules.EscalatingRandomArrival))
            {
                int pick = numbers[DieN(c, numbers.Count) - 1];
                c.Log($"Escalating Battles: {c.GetPlayerName(player)} rolls for the detachment {when} - Detachment {pick}.");
                return pick;
            }
            // The bot's pick (first): the detachment worth the most points.
            var ordered = numbers.OrderByDescending(k => groups[k].Sum(u => u.PointCost)).ThenBy(k => k).ToList();
            var labels = ordered.Select(k => Label(k, groups[k])).ToList();
            string chosen = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                $"Escalating Battles: pick the detachment to deploy {when}.", labels,
                new List<StringSelectionRequest.InvalidOption>(), displayName: "Escalating Battles") { Recommended = labels[0] });
            int idx = labels.IndexOf(chosen);
            return ordered[idx < 0 ? 0 : idx];
        }

        /// <summary>Before deployment: split every army, each player picks the detachment that deploys now; the rest wait.</summary>
        public static async Task OpeningDetachments(IGameContext c, IDeploymentTurnContext context)
        {
            if (!EscalatingOn(c) || context.MissionPlan != null) return;
            var held = new HashSet<UnitID>();
            foreach (PlayerID player in context.UndeployedUnits.Keys.ToList())
            {
                var bindings = c.GameDataStore.GetAllValues<ArmyData>().Where(a => a.IsOwnedBy(player))
                    .SelectMany(a => a.UnitBindings).Where(b => b.GetValue().GetIsAlive()).ToList();
                var units = bindings.Select(b => b.GetValue()).ToList();
                if (units.Count < 2) continue;
                var split = Split(units);
                // The player splits their own army (Kai, 2026-11-23); the even split is the suggestion, and what a bot takes.
                var suggested = units.Select(u => split.FindIndex(g => g.Contains(u)) + 1).ToList();
                List<int> answer = await c.PlayerRequester.RequestDecision<SplitDetachmentsRequest, List<int>>(new SplitDetachmentsRequest(player,
                    bindings.Select(b => new SplitDetachmentsRequest.Entry(b, b.GetValue().Name, b.GetValue().PointCost)).ToList(),
                    suggested, Detachments));
                var groups = new Dictionary<int, List<UnitData>>();
                for (int i = 0; i < units.Count; i++)
                {
                    int d = answer != null && i < answer.Count && answer[i] is >= 1 and <= Detachments ? answer[i] : suggested[i];
                    if (!groups.TryGetValue(d, out var g)) groups[d] = g = new List<UnitData>();
                    g.Add(units[i]);
                }
                foreach ((int k, List<UnitData> g) in groups.OrderBy(x => x.Key))
                    c.Log($"Escalating Battles: {c.GetPlayerName(player)}'s {Label(k, g)}.");
                int first = await PickDetachment(c, player, groups, "now");
                c.Log($"Escalating Battles: {c.GetPlayerName(player)} deploys Detachment {first}.");
                foreach ((int k, List<UnitData> g) in groups)
                {
                    if (k == first) continue;
                    foreach (UnitData u in g)
                    {
                        ReserveRules.PlaceInReserve(u);
                        for (int i = 0; i < k; i++) u.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.HeldDetachment));
                        held.Add(u.ID);
                    }
                }
            }
            context.HoldBack(b => held.Contains(b.GetValue().ID));
        }

        /// <summary>Start of rounds 2+: each player with a detachment waiting picks one and deploys all of it.</summary>
        public static async Task EscalatingRoundStart(IGameContext c, int round)
        {
            if (!EscalatingOn(c) || round < 2) return;
            var waiting = c.TableState.Units.Objects.OfType<UnitData>()
                .Where(u => u.GetIsAlive() && u.Tokens.HasToken(TokenType.HeldDetachment)).ToList();
            foreach (IGrouping<PlayerID, UnitData> byPlayer in waiting.GroupBy(u => u.PlayerID))
            {
                PlayerID player = byPlayer.Key;
                var groups = byPlayer.GroupBy(u => u.Tokens.GetTokenCount(TokenType.HeldDetachment))
                    .ToDictionary(g => g.Key, g => g.ToList());
                int pick = await PickDetachment(c, player, groups, $"at the start of round {round}");
                await c.Announce($"Escalating Battles: {c.GetPlayerName(player)} brings on Detachment {pick}.", BattlefieldEffects.Amber, EBannerTier.Notice);
                foreach (UnitData unit in groups[pick])
                {
                    unit.Tokens.RemoveTokens(TokenType.HeldDetachment, unit.Tokens.GetTokenCount(TokenType.HeldDetachment));
                    if (HasAmbush(c, unit) && await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(player,
                            $"Escalating Battles: keep {unit.Name} in Ambush (it arrives later as usual)?", defaultAnswer: true,
                            displayName: $"{unit.Name}: Ambush?")))
                    {
                        c.Log($"Escalating Battles: {unit.Name} stays in Ambush.");
                        continue;
                    }
                    await PlaceInZone(c, unit);
                }
            }
        }

        private static bool HasAmbush(IGameContext c, IUnit unit) =>
            c.RuleEvaluator.Evaluate(unit, Rules.Foundation.ERuleSeat.Actor, new Rules.Dispatch.Contexts.PreDeploymentSelectContext(unit))
                .OfType<Rules.Definitions.RuleOperation.DeferDeployment>().Any(d => d.Timing == Rules.Definitions.EDeferTiming.LaterRound);

        private static async Task PlaceInZone(IGameContext c, UnitData unit)
        {
            ITeam? team = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(unit.PlayerID));
            IBoundedZone zone = team != null && GameProgressUtilities.DeploymentZoneOf(c.GameDataStore, team.TeamNumber) is { } z
                ? (z.Parts is { Count: > 0 } parts ? new PolygonZone(parts) : new RectangularZone(z.Left, z.Right, z.Bottom, z.Top))
                : new RectangularZone(0f, HordePlacement.TableWidth, 0f, 12f);
            if (DeploySection(c, unit, zone) is { } section) zone = section;
            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, "Deploy Detachment", zone, unit.ModelBindings,
                displayName: $"Deploying {unit.Name} (Escalating Battles)");
            List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard.RequestClearPlacement(c, request);
            ReserveRules.ClearReserve(unit);
            foreach (PlacedObjectEntry<ModelData> p in placements)
            {
                p.Binding.GetValue().SetPosition(p.Position);
                FacingRules.ApplyPlaced(p.Binding.GetValue(), p.Facing, c.Settings);
            }
            c.Log($"Escalating Battles: {unit.Name} deploys.");
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
