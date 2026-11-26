using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution.Requests;

namespace FDG.Missions
{
    /// <summary>Meat Grinder's running state, saved with the mission (<see cref="ExtraMissionState.Grinder"/>).</summary>
    public sealed class MeatGrinderState
    {
        /// <summary>Each seat's army total at the start (slot -> points): the "total points of the army" the tokens and
        /// the end-of-game objective VP are worked out from.</summary>
        public Dictionary<int, int> ArmyPoints { get; set; } = new();

        /// <summary>Respawning: units waiting in reserves (unit id -> tokens left on it).</summary>
        public Dictionary<string, int> Tokens { get; set; } = new();

        /// <summary>Destroyed units that will not come back (no points value - summoned, made by a rule).</summary>
        public List<string> Gone { get; set; } = new();

        /// <summary>Recruitment: the points each seat has banked (slot -> points).</summary>
        public Dictionary<int, int> Bank { get; set; } = new();

        /// <summary>Recruitment: what each seat earned at the end of the last round (display).</summary>
        public Dictionary<int, int> LastIncome { get; set; } = new();

        /// <summary>Recruitment with the underdog system: this round's underdog (-1 = none).</summary>
        public int Underdog { get; set; } = -1;
    }

    /// <summary>
    /// Meat Grinder Battles (Advanced Rulebooks 3.6.0: GF p51, GFF p49, AoF p51, AoFS p49 - the same words in all four,
    /// only the worked examples change). Kai, 2026-11-25: "ok" to building it, Respawning first; recruitment uses the
    /// Horde Shop screen. Played as missions picked in the lobby (Mission row), like Twists &amp; Gambits: 5 objectives,
    /// standard deployment, 6 rounds, two players.
    /// <list type="bullet">
    /// <item>Respawning: a destroyed unit goes into reserves with one token per full 5% of its army's points; at the
    /// start of each round it rolls a die and must beat its tokens to deploy again (else loses one token). A unit at half
    /// strength or less may be withdrawn (destroyed) when activated. Killing a unit scores its points in VP; at the end,
    /// enemy units at half strength or less on the table give half their points, and each objective held gives 20% of
    /// the army's points.</item>
    /// <item>Recruitment: start with 50% of the game size (the starting list comes out of it); at the end of each round
    /// earn 15% plus X% per objective held (X = the round); at the start of each round spend up to 30% on new units,
    /// deployed in the own zone, which can't seize or contest objectives that round. VP: X per objective each round.
    /// Optional underdog system: from the end of round 3 only the player behind on VP gets the 15%.</item>
    /// </list>
    /// Judgement calls are in house rules C, "Meat Grinder".
    /// </summary>
    public static class MeatGrinder
    {
        public const int Rounds = 6;
        public const string WithdrawOption = "Withdraw (Meat Grinder)";
        private static readonly TextColor Red = new(230, 70, 60, 255);
        private static readonly TextColor Gold = new(255, 210, 80, 255);

        public static bool IsMeatGrinder(EExtraMission m) =>
            m is EExtraMission.MeatGrinderRespawning or EExtraMission.MeatGrinderRecruitment or EExtraMission.MeatGrinderRecruitmentUnderdog;

        public static bool IsRecruitment(EExtraMission m) =>
            m is EExtraMission.MeatGrinderRecruitment or EExtraMission.MeatGrinderRecruitmentUnderdog;

        public static bool IsOptionWithdraw(string option) => option == WithdrawOption;

        private static EExtraMission Current(IGameContext c) => ExtraMissions.Current(c);
        private static bool Respawning(IGameContext c) => Current(c) == EExtraMission.MeatGrinderRespawning;

        private static IEnumerable<IPlayerSlotInfo> Seats(IGameContext c) =>
            c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID);

        private static int Other(IGameContext c, int slot) => Seats(c).Select(p => p.SlotID).FirstOrDefault(s => s != slot, -1);

        private static int SlotOf(IGameContext c, PlayerID p) => MissionCardGame.SlotOf(c, p);

        private static MeatGrinderState Grinder(ExtraMissionState s) => s.Grinder ??= new MeatGrinderState();

        /// <summary>The army's points: what its units cost at the start of the game.</summary>
        private static int ArmyCost(IGameContext c, PlayerID player) =>
            c.TableState.Units.Objects.Where(u => u.PlayerID.Equals(player)).OfType<UnitData>().Sum(u => Math.Max(0, u.PointCost));

        private static int ArmyPoints(IGameContext c, MeatGrinderState g, int slot)
        {
            if (g.ArmyPoints.TryGetValue(slot, out int known) && known > 0) return known;
            IPlayerSlotInfo? seat = Seats(c).FirstOrDefault(p => p.SlotID == slot);
            int cost = seat == null ? 0 : ArmyCost(c, seat.PlayerID);
            return cost > 0 ? cost : Math.Max(1, c.Settings.ArmyPoints);
        }

        /// <summary>The game size recruitment works from (the lobby's points).</summary>
        public static int GameSize(GameSettings s) => Math.Max(1, s.ArmyPoints);

        private static int Percent(int size, int percent) => (int)Math.Ceiling(size * percent / 100.0);

        /// <summary>Recruitment: the starting purse - "players start the game with as many points as 50% of total game
        /// size to recruit units (rounding up)" - so the lobby holds each list to it.</summary>
        public static int StartingPoints(int gameSize) => Percent(gameSize, 50);

        /// <summary>"players may spend max. as many points as 30% of total game size each round (rounding up)".</summary>
        public static int MaxSpend(int gameSize) => Percent(gameSize, 30);

        /// <summary>"one for every full 5% of the total points of the army the unit cost" - full steps, as every book's
        /// example shows (the text's "(rounding up)" disagrees with its own examples; house rules C).</summary>
        public static int TokensFor(int unitCost, int armyPoints)
        {
            if (armyPoints <= 0 || unitCost <= 0) return 0;
            double step = armyPoints * 0.05;
            return (int)Math.Floor(unitCost / step + 1e-9);
        }

        /// <summary>"15% of total game size, plus X * 1% of total game size for each objective they control (rounding
        /// up)". <paramref name="baseShare"/> false = the underdog system took the 15% away this round.</summary>
        public static int Income(int gameSize, int round, int objectives, bool baseShare = true) =>
            (int)Math.Ceiling(gameSize * ((baseShare ? 15 : 0) + round * objectives) / 100.0);

        // ---------------------------------------------------------------- set-up

        /// <summary>The mission was picked (from <see cref="ExtraMissions.Begin"/>): note each army's points, open the
        /// recruitment purses.</summary>
        internal static void Begin(IGameContext c, ExtraMissionState s)
        {
            MeatGrinderState g = Grinder(s);
            int size = GameSize(c.Settings);
            foreach (IPlayerSlotInfo seat in Seats(c))
            {
                int cost = ArmyCost(c, seat.PlayerID);
                g.ArmyPoints[seat.SlotID] = cost > 0 ? cost : size;
                if (IsRecruitment(s.Mission))
                {
                    int start = StartingPoints(size);
                    g.Bank[seat.SlotID] = Math.Max(0, start - cost);
                    c.Log($"Meat Grinder: {seat.Name} starts with {start} pts to recruit with; the starting army cost {cost}, "
                          + $"{g.Bank[seat.SlotID]} pts are banked.");
                    if (cost > start)
                        c.Log($"  ({seat.Name}'s list is over the {start} pts recruitment allows - played as it is.)");
                }
                else
                    c.Log($"Meat Grinder: {seat.Name}'s army is {g.ArmyPoints[seat.SlotID]} pts - destroyed units come back with "
                          + $"one token per full {g.ArmyPoints[seat.SlotID] * 0.05:0.#} pts they cost.");
            }
        }

        // ---------------------------------------------------------------- destroyed units

        /// <summary>After every activation, at the end of each round and before the round's reserve rolls: newly destroyed
        /// units score the enemy their points (Respawning) and go into reserves with their tokens.</summary>
        public static void AfterActivation(IGameContext c)
        {
            if (!Respawning(c)) return;
            ExtraMissionState s = ExtraMissions.StateFor(c);
            if (SendDestroyedToReserves(c, s)) ExtraMissions.SaveState(c, s);
        }

        private static bool SendDestroyedToReserves(IGameContext c, ExtraMissionState s)
        {
            MeatGrinderState g = Grinder(s);
            bool changed = false;
            foreach (IUnit unit in c.TableState.Units.Objects.ToList())
            {
                if (unit.GetIsAlive() || unit is not UnitData data) continue;
                string id = unit.ID.ID.ToString();
                if (g.Gone.Contains(id) || unit.Tokens.HasToken(TokenType.Conceded)) continue;
                int slot = SlotOf(c, unit.PlayerID);
                int cost = Math.Max(0, data.PointCost);
                // "When an enemy unit is destroyed, players score X VPs, where X is that unit's point value".
                int scorer = Other(c, slot);
                if (scorer >= 0 && cost > 0)
                {
                    s.Vp[scorer] = s.Vp.GetValueOrDefault(scorer) + cost;
                    c.Log($"Meat Grinder: {unit.Name} is destroyed - {SeatName(c, scorer)} scores {cost} VP ({s.Vp[scorer]} in all).");
                }
                changed = true;
                if (cost <= 0)
                {
                    // A unit with no points value (summoned, made by a rule) was never part of the army: it stays gone.
                    g.Gone.Add(id);
                    continue;
                }
                HordePlacement.Revive(data);
                ReserveRules.PlaceInReserve(data);
                if (!data.Tokens.HasToken(TokenType.Respawning)) data.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Respawning));
                int tokens = TokensFor(cost, ArmyPoints(c, g, slot));
                g.Tokens[id] = tokens;
                c.Log($"Meat Grinder: {unit.Name} goes into reserves with {tokens} token{(tokens == 1 ? "" : "s")}.");
            }
            return changed;
        }

        private static string SeatName(IGameContext c, int slot) =>
            Seats(c).FirstOrDefault(p => p.SlotID == slot)?.Name ?? $"Player {slot + 1}";

        // ---------------------------------------------------------------- withdrawing a unit

        /// <summary>"Players may also voluntarily destroy any of their units that have half or less of their total size or
        /// Tough value (for single-model units) left when they are activated."</summary>
        public static bool CanWithdraw(IGameContext c, IUnit unit) =>
            Respawning(c) && unit.GetIsAlive() && unit.GetIsOnBattlefield() && AtHalfOrLess(unit);

        public static bool AtHalfOrLess(IUnit unit)
        {
            var models = unit.Models.ToList();
            if (models.Count == 0) return false;
            return HordeReinforce.IsAtHalfStrength(models.Count, models.Count(m => m.GetIsAlive()),
                models.Sum(m => m.TotalWounds), models.Sum(m => m.GetIsAlive() ? m.TotalWounds - m.WoundsDealt : 0f));
        }

        public static string WithdrawText(IGameContext c, IUnit unit)
        {
            ExtraMissionState s = ExtraMissions.StateFor(c);
            int slot = SlotOf(c, unit.PlayerID);
            int cost = Math.Max(0, (unit as UnitData)?.PointCost ?? 0);
            int tokens = TokensFor(cost, ArmyPoints(c, Grinder(s), slot));
            return $"Destroy this unit now (it is at half strength or less). The enemy scores its {cost} VP, and it goes into "
                   + $"reserves with {tokens} token{(tokens == 1 ? "" : "s")} - from the next round it comes back at full strength "
                   + $"on a roll higher than its tokens.";
        }

        public static async Task Withdraw(IGameContext c, UnitData unit)
        {
            foreach (DataBinding<ModelData> b in unit.ModelBindings)
            {
                ModelData model = b.GetValue();
                if (model.GetIsAlive()) model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }
            c.Log($"Meat Grinder: {c.GetPlayerName(unit.PlayerID)} withdraws {unit.Name} (destroyed, to come back from reserves).");
            await c.Announce($"{unit.Name} withdrawn", Red);
            AfterActivation(c);
        }

        // ---------------------------------------------------------------- the round

        /// <summary>The beginning of each round, starting with the side that activates next: Respawning rolls for every
        /// unit in reserves; Recruitment opens the shop.</summary>
        public static async Task RoundStart(IGameContext c, int round, IReadOnlyList<ITeam>? order)
        {
            EExtraMission m = Current(c);
            if (!IsMeatGrinder(m)) return;
            ExtraMissionState s = ExtraMissions.StateFor(c);
            List<IPlayerSlotInfo> seats = SeatsInOrder(c, order);
            if (m == EExtraMission.MeatGrinderRespawning)
            {
                SendDestroyedToReserves(c, s);
                ExtraMissions.SaveState(c, s);
                await RollReserves(c, s, seats);
            }
            else
                await Recruit(c, s, seats, round);
        }

        private static List<IPlayerSlotInfo> SeatsInOrder(IGameContext c, IReadOnlyList<ITeam>? order)
        {
            var seats = Seats(c).ToList();
            if (order == null || order.Count == 0) return seats;
            int Rank(IPlayerSlotInfo p)
            {
                for (int i = 0; i < order.Count; i++) if (order[i].IsPlayerOnTeam(p.PlayerID)) return i;
                return order.Count;
            }
            return seats.OrderBy(Rank).ThenBy(p => p.SlotID).ToList();
        }

        /// <summary>"players alternate in rolling one die for each unit in reserves, trying to roll higher than the total
        /// number of tokens on that unit. If successful, remove all tokens from the unit and deploy it following the
        /// standard deployment rules. If failed, remove one token from the unit instead."</summary>
        private static async Task RollReserves(IGameContext c, ExtraMissionState s, List<IPlayerSlotInfo> seats)
        {
            MeatGrinderState g = Grinder(s);
            // Units that left reserves some other way are no longer waiting.
            foreach (string id in g.Tokens.Keys.ToList())
                if (UnitById(c, id) is not UnitData u || !u.GetIsAlive() || !ReserveRules.IsInReserve(u)) g.Tokens.Remove(id);

            var queues = seats.Select(seat => new Queue<UnitData>(g.Tokens.Keys.Select(id => UnitById(c, id)).OfType<UnitData>()
                .Where(u => u.PlayerID.Equals(seat.PlayerID)).OrderByDescending(u => u.PointCost))).ToList();
            while (queues.Any(q => q.Count > 0))
            {
                foreach (Queue<UnitData> q in queues)
                {
                    if (q.Count == 0) continue;
                    UnitData unit = q.Dequeue();
                    string id = unit.ID.ID.ToString();
                    int tokens = g.Tokens.GetValueOrDefault(id);
                    int roll = HordePlacement.RollD6(c);
                    if (roll <= tokens)
                    {
                        g.Tokens[id] = Math.Max(0, tokens - 1);
                        c.Log($"Meat Grinder: {unit.Name} rolls {roll} against {tokens} token{(tokens == 1 ? "" : "s")} - stays in reserves "
                              + $"({g.Tokens[id]} left).");
                        continue;
                    }
                    c.Log($"Meat Grinder: {unit.Name} rolls {roll} against {tokens} token{(tokens == 1 ? "" : "s")} - deploys again.");
                    g.Tokens.Remove(id);
                    unit.Tokens.RemoveTokens(TokenType.Respawning);
                    ExtraMissions.SaveState(c, s);
                    await PlaceInZone(c, unit, "comes back from reserves");
                    await c.Announce($"{unit.Name} returns!", Gold);
                }
            }
            ExtraMissions.SaveState(c, s);
        }

        private static IUnit? UnitById(IGameContext c, string id) =>
            c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == id);

        /// <summary>"deploy it following the standard deployment rules": anywhere in its side's deployment zone.</summary>
        private static async Task PlaceInZone(IGameContext c, UnitData unit, string why)
        {
            ITeam? team = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(unit.PlayerID));
            IBoundedZone zone = team != null && GameProgressUtilities.DeploymentZoneOf(c.GameDataStore, team.TeamNumber) is { } z
                ? (z.Parts is { Count: > 0 } parts ? new PolygonZone(parts) : new RectangularZone(z.Left, z.Right, z.Bottom, z.Top))
                : new RectangularZone(0f, HordePlacement.TableWidth, 0f, 12f);
            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"Meat Grinder: deploy {unit.Name}", zone, unit.ModelBindings,
                displayName: $"Deploying {unit.Name} in your deployment zone");
            List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard.RequestClearPlacement(c, request);
            ReserveRules.ClearReserve(unit);
            foreach (PlacedObjectEntry<ModelData> p in placements)
            {
                p.Binding.GetValue().SetPosition(p.Position);
                FacingRules.ApplyPlaced(p.Binding.GetValue(), p.Facing, c.Settings);
            }
            c.Log($"Meat Grinder: {unit.Name} {why} in its deployment zone.");
        }

        /// <summary>"At the beginning of each round, starting with the player that activates next, players may alternate in
        /// spending their points to recruit units and deploy them following the standard deployment rules, however players
        /// may spend max. as many points as 30% of total game size each round (rounding up). Recruited units can't seize or
        /// contest objectives on the round they deploy." Each player visits the shop once, in turn (house rules C).</summary>
        private static async Task Recruit(IGameContext c, ExtraMissionState s, List<IPlayerSlotInfo> seats, int round)
        {
            MeatGrinderState g = Grinder(s);
            int size = GameSize(c.Settings);
            int cap = MaxSpend(size);
            foreach (IPlayerSlotInfo seat in seats)
            {
                int bank = g.Bank.GetValueOrDefault(seat.SlotID);
                int spendable = Math.Min(bank, cap);
                if (spendable <= 0)
                {
                    c.Log($"Meat Grinder: {seat.Name} has no points to recruit with.");
                    continue;
                }
                DataBinding<ArmyData>? armyBinding = c.GameDataStore.GetAllDataBindings<ArmyData>()
                    .FirstOrDefault(b => b.GetValue().PlayerID == seat.PlayerID);
                if (armyBinding == null) continue;
                ArmyData army = armyBinding.GetValue();
                ITeam? team = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(seat.PlayerID));
                ZoneRect? z = team != null ? GameProgressUtilities.DeploymentZoneOf(c.GameDataStore, team.TeamNumber) : null;
                float cx = z != null ? (z.Left + z.Right) / 2f : HordePlacement.TableWidth / 2f;
                float cz = z != null ? (z.Bottom + z.Top) / 2f : 6f;
                var spot = new List<HordeShopObjective> { new(0, cx, cz, false) };
                var request = new HordeShopRequest(seat.PlayerID, round, spendable, army.Faction, army.ArmyName, spot)
                {
                    AllowedGameSystems = c.Settings.AllowedGameSystems,
                    GameSystemSlug = GameSystemInfo.Slug(c.Settings.GameSystem),
                    PointsEarned = g.LastIncome.GetValueOrDefault(seat.SlotID),
                    PointsBanked = bank,
                    RecruitOnly = true,
                    BotsRecruit = true,
                    Title = "RECRUIT",
                    Subtitle = $"Meat Grinder - round {round}. You have {bank} pts; up to {cap} may be spent this round.",
                    DeployNote = "Recruits deploy in your deployment zone, and can't seize or contest objectives this round.",
                };
                HordeShopReply reply = await c.PlayerRequester.RequestDecision<HordeShopRequest, HordeShopReply>(request);
                await ApplyRecruits(c, s, seat, armyBinding, reply, spendable);
                ExtraMissions.SaveState(c, s);
            }
        }

        private static async Task ApplyRecruits(IGameContext c, ExtraMissionState s, IPlayerSlotInfo seat,
            DataBinding<ArmyData> armyBinding, HordeShopReply reply, int spendable)
        {
            ArmyListFile? recruits = HordeShop.ReadList(c, reply.RecruitsJson);
            if (recruits == null || recruits.Units.Count == 0)
            {
                c.Log($"Meat Grinder: {seat.Name} recruits nothing this round.");
                return;
            }
            int cost = recruits.Units.Sum(u => Math.Max(0, u.PointCost));
            if (cost > spendable)
            {
                c.Log($"Meat Grinder: the recruits cost {cost} pts but only {spendable} may be spent - nothing recruited.");
                return;
            }
            MeatGrinderState g = Grinder(s);
            g.Bank[seat.SlotID] = g.Bank.GetValueOrDefault(seat.SlotID) - cost;
            c.Log($"Meat Grinder: {seat.Name} spends {cost} pts ({g.Bank[seat.SlotID]} left).");
            IRuleResolver resolver = c.RuleEvaluator.RuleResolver ?? new RuleResolver();
            HordeShop.MergeRuleData(c, armyBinding.GetValue(), recruits, resolver);
            IReadOnlyList<UnitData> built = HordeShop.BuildRecruits(c, seat.PlayerID, armyBinding, recruits, reply, resolver);
            ExtraMissions.SaveState(c, s);
            foreach (UnitData unit in built)
            {
                await c.Announce($"{unit.Name} recruited!", Gold);
                await PlaceInZone(c, unit, "is recruited and deploys");
                unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve));
            }
        }

        /// <summary>The end of a round (after this round's VP): Respawning sends the round's dead to reserves; Recruitment
        /// pays each player and settles the underdog.</summary>
        internal static void RoundEnd(IGameContext c, ExtraMissionState s, int round)
        {
            EExtraMission m = s.Mission;
            if (m == EExtraMission.MeatGrinderRespawning)
            {
                SendDestroyedToReserves(c, s);
                return;
            }
            if (!IsRecruitment(m)) return;
            MeatGrinderState g = Grinder(s);
            int size = GameSize(c.Settings);
            g.Underdog = -1;
            var seats = Seats(c).ToList();
            bool underdogRule = m == EExtraMission.MeatGrinderRecruitmentUnderdog && round >= 3;
            if (underdogRule && seats.Count == 2)
            {
                // "if one player has fewer VPs than the other, then they count as the underdog for that round".
                int a = s.Vp.GetValueOrDefault(seats[0].SlotID), b = s.Vp.GetValueOrDefault(seats[1].SlotID);
                if (a != b) g.Underdog = a < b ? seats[0].SlotID : seats[1].SlotID;
                c.Log(g.Underdog < 0
                    ? "Meat Grinder: the players are level on VP - no underdog, so nobody gets the 15% this round."
                    : $"Meat Grinder: {SeatName(c, g.Underdog)} is the underdog - only they get the 15%.");
            }
            Dictionary<int, int> held = ExtraMissions.Controlled(c);
            foreach (IPlayerSlotInfo seat in seats)
            {
                bool baseShare = !underdogRule || g.Underdog == seat.SlotID;
                int income = Income(size, round, held.GetValueOrDefault(seat.SlotID), baseShare);
                g.LastIncome[seat.SlotID] = income;
                g.Bank[seat.SlotID] = g.Bank.GetValueOrDefault(seat.SlotID) + income;
                c.Log($"Meat Grinder: {seat.Name} earns {income} pts ({held.GetValueOrDefault(seat.SlotID)} objective"
                      + $"{(held.GetValueOrDefault(seat.SlotID) == 1 ? "" : "s")}) - {g.Bank[seat.SlotID]} banked.");
            }
        }

        /// <summary>Respawning's final tally: the VP from kills, plus "X/2 VPs (rounding up) for enemy units with half or
        /// less of their total size or Tough value on the table", plus "X VPs for each objective they control, where X equals
        /// 20% of the total points of each army (rounding up)".</summary>
        internal static (Dictionary<int, int> Score, string Unit) FinalScore(IGameContext c, ExtraMissionState s)
        {
            var score = new Dictionary<int, int>(s.Vp);
            if (s.Mission != EExtraMission.MeatGrinderRespawning) return (score, "VP");
            MeatGrinderState g = Grinder(s);
            Dictionary<int, int> held = ExtraMissions.Controlled(c);
            foreach (IPlayerSlotInfo seat in Seats(c))
            {
                int slot = seat.SlotID;
                int perObjective = Percent(ArmyPoints(c, g, slot), 20);
                int objectives = held.GetValueOrDefault(slot) * perObjective;
                int halves = c.TableState.Units.Objects.OfType<UnitData>()
                    .Where(u => !u.PlayerID.Equals(seat.PlayerID) && SlotOf(c, u.PlayerID) >= 0 && u.GetIsAlive()
                                && u.GetIsOnBattlefield() && !ReserveRules.IsInReserve(u) && AtHalfOrLess(u))
                    .Sum(u => (Math.Max(0, u.PointCost) + 1) / 2);
                score[slot] = score.GetValueOrDefault(slot) + objectives + halves;
                c.Log($"Meat Grinder: {seat.Name} - {s.Vp.GetValueOrDefault(slot)} VP from kills, {objectives} from "
                      + $"{held.GetValueOrDefault(slot)} objective(s) at {perObjective} each, {halves} from enemy units at half strength "
                      + $"= {score[slot]} VP.");
            }
            return (score, "VP");
        }

        // ---------------------------------------------------------------- panel

        /// <summary>The Mission panel's lines (seat name -> line), for the viewer of <paramref name="state"/>.</summary>
        public static IReadOnlyList<string> PanelLines(ExtraMissionState state, Func<int, string> seatName, IEnumerable<int> slots,
            Func<string, string?> unitName)
        {
            var lines = new List<string>();
            MeatGrinderState g = state.Grinder ?? new MeatGrinderState();
            foreach (int slot in slots)
            {
                if (state.Mission == EExtraMission.MeatGrinderRespawning)
                    lines.Add($"{seatName(slot)}: {state.Vp.GetValueOrDefault(slot)} VP from kills");
                else
                    lines.Add($"{seatName(slot)}: {state.Vp.GetValueOrDefault(slot)} VP, {g.Bank.GetValueOrDefault(slot)} pts banked"
                              + (g.LastIncome.TryGetValue(slot, out int inc) ? $" (+{inc} last round)" : ""));
            }
            if (state.Mission == EExtraMission.MeatGrinderRespawning && g.Tokens.Count > 0)
                lines.Add("In reserves: " + string.Join(", ", g.Tokens.Select(kv => $"{unitName(kv.Key) ?? "a unit"} ({kv.Value})")));
            if (state.Mission == EExtraMission.MeatGrinderRecruitmentUnderdog && g.Underdog >= 0)
                lines.Add($"Underdog: {seatName(g.Underdog)}");
            return lines;
        }
    }
}
