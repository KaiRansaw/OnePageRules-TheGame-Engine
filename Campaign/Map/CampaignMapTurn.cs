namespace FDG.Campaign.Map
{
    /// <summary>What kind of battle a map turn produced.</summary>
    public enum EMapBattleKind
    {
        /// <summary>An army attacks a region another army holds.</summary>
        Attack = 0,
        /// <summary>An army fights a neutral region's local garrison (no treasury, no VP - house rule).</summary>
        Garrison = 1,
        /// <summary>Two armies went for the same region this turn and meet there first.</summary>
        Clash = 2,
    }

    /// <summary>One battle of a map turn. <see cref="Defender"/> is null for a garrison.</summary>
    public sealed record MapBattle(string Region, string Attacker, string? Defender, EMapBattleKind Kind, bool CapitalAssault)
    {
        public bool Involves(string faction) => Attacker == faction || Defender == faction;

        /// <summary>For a player battle: the army on the other side, or null for a garrison.</summary>
        public string? OpponentOf(string faction) => Attacker == faction ? Defender : Attacker;
    }

    /// <summary>Treasury and mission VP an army gained or spent off the table this turn (claims, rest, income, rolls).</summary>
    public sealed class MapArmyChange
    {
        public int Treasury { get; set; }
        public int MissionVp { get; set; }
    }

    /// <summary>Where a turn's resolution got to.</summary>
    public sealed class MapTurnResult
    {
        /// <summary>The map after the turn when <see cref="Finished"/>; otherwise the map as it was (nothing applied).</summary>
        public CampaignMap Map { get; init; } = null!;

        /// <summary>The player's next battle to play, or null when the turn is done.</summary>
        public MapBattle? WaitingFor { get; init; }

        public bool Finished => WaitingFor == null;

        /// <summary>
        /// Changes to apply to each army's campaign file - only when <see cref="Finished"/>. Battles the player played are
        /// NOT in here: the mission itself pays those (and a garrison mission pays nothing).
        /// </summary>
        public Dictionary<string, MapArmyChange> Changes { get; init; } = new();

        /// <summary>The turn report so far (ASCII - game text).</summary>
        public List<string> Lines { get; init; } = new();
    }

    /// <summary>
    /// #467 - one turn of the campaign map (Kai, 2026-11-14): every army gives one order (attack / claim / fight a garrison /
    /// rest), the player's first; then the turn resolves region by region. Battles between rivals are an off-screen roll
    /// weighted by army size; every battle the player is in is a real mission, so resolution stops at each one until its
    /// result is recorded.
    ///
    /// <para>Resolution is a pure replay: the orders, the armies' books as they stood when the turn began, and the player's
    /// results so far all live in <see cref="CampaignMap.Pending"/>, and the dice come from the map's seed and the turn
    /// number - so saving and reloading in the middle of a turn, or between its battles, always gives the same turn.</para>
    /// </summary>
    public static class CampaignMapTurn
    {
        /// <summary>
        /// Starts a turn: the player's order (checked - throws if it isn't allowed) and every living rival's, decided by
        /// <see cref="CampaignMapBot"/>. <paramref name="armies"/> is every army's books right now; the turn keeps a copy.
        /// </summary>
        public static void Begin(CampaignMap map, MapOrder playerOrder, IReadOnlyDictionary<string, MapArmyState> armies, CampaignNumbers n)
        {
            if (map.Pending != null) throw new InvalidOperationException("A turn is already under way.");
            if (playerOrder.Faction != CampaignMap.PlayerId) throw new ArgumentException("That isn't the player's order.", nameof(playerOrder));
            if (CampaignMapRules.WhyNot(map, playerOrder, State(armies, playerOrder.Faction), n) is { } why)
                throw new InvalidOperationException(why);

            var rng = new Random(unchecked(map.Seed * 31 + map.Turn * 7_368_787 + 11));
            var pending = new MapPendingTurn();
            pending.Orders.Add(playerOrder);
            foreach (MapFaction f in map.Alive.Where(f => !f.IsPlayer))
                pending.Orders.Add(CampaignMapBot.Order(map, f.Id, armies, n, rng));
            foreach (MapFaction f in map.Factions) pending.Armies[f.Id] = State(armies, f.Id);
            map.Pending = pending;
        }

        /// <summary>Records the result of the player's battle that <see cref="Resolve"/> is waiting for.</summary>
        public static void RecordPlayerResult(CampaignMap map, bool playerWon)
        {
            if (map.Pending == null) throw new InvalidOperationException("No turn is under way.");
            map.Pending.PlayerResults.Add(playerWon);
        }

        private static MapArmyState State(IReadOnlyDictionary<string, MapArmyState> armies, string id) =>
            armies.TryGetValue(id, out var s) ? s : default;

        /// <summary>
        /// Plays the turn under way as far as it can: to its end, or to the next player battle without a result. Never
        /// changes <paramref name="map"/>; the finished map comes back in the result.
        /// </summary>
        public static MapTurnResult Resolve(CampaignMap map, CampaignNumbers n)
        {
            MapPendingTurn pending = map.Pending ?? throw new InvalidOperationException("No turn is under way.");
            CampaignMap m = map.Clone();
            var rng = new Random(unchecked(map.Seed * 17 + map.Turn * 104_729 + 3));
            var lines = new List<string>();
            var changes = m.Factions.ToDictionary(f => f.Id, _ => new MapArmyChange());
            int playerResult = 0;
            string you = CampaignMap.PlayerId;

            string NameOf(string? id) => id == null ? "the local garrison" : m.Faction(id)?.Name ?? id;
            MapArmyState Snap(string id) => pending.Armies.TryGetValue(id, out var s) ? s : default;
            int Treasury(string id) => Snap(id).Treasury + changes[id].Treasury;

            // ---- rests first: they earn whatever happens elsewhere.
            foreach (MapOrder o in pending.Orders.Where(o => o.Kind == EOrderKind.Rest))
            {
                if (m.Faction(o.Faction) is not { Alive: true }) continue;
                changes[o.Faction].Treasury += CampaignMapRules.RestIncome(n);
                lines.Add($"{NameOf(o.Faction)} rested: units recover, +{CampaignMapRules.RestIncome(n)} pts.");
            }

            // ---- then every targeted region, in map order; armies that picked the same region meet there first.
            var groups = pending.Orders.Where(o => o.Kind != EOrderKind.Rest && o.Region != null)
                .GroupBy(o => o.Region!).OrderBy(g => m.Regions.FindIndex(r => r.Id == g.Key));
            foreach (var group in groups)
            {
                MapRegion region = m.Region(group.Key)!;
                var armiesHere = group.Where(o => m.Faction(o.Faction)?.Alive == true).ToList();
                if (armiesHere.Count == 0) continue;

                if (armiesHere.Count > 1)
                {
                    string winner = armiesHere[0].Faction;
                    foreach (MapOrder other in armiesHere.Skip(1))
                    {
                        var battle = new MapBattle(region.Id, winner, other.Faction, EMapBattleKind.Clash, false);
                        bool? won = Fight(battle);
                        if (won == null) return Waiting(battle);
                        winner = won.Value ? winner : other.Faction;
                    }
                    lines.Add($"{string.Join(" and ", armiesHere.Select(o => NameOf(o.Faction)))} all went for {region.Name} and met there - {NameOf(winner)} won.");
                    if (region.Owner == null)
                    {
                        region.Owner = winner;
                        lines.Add($"{NameOf(winner)} takes {region.Name}.");
                    }
                    else lines.Add($"{NameOf(region.Owner)} keeps {region.Name} this turn.");
                    continue;
                }

                MapOrder order = armiesHere[0];
                string attacker = order.Faction;
                EOrderKind kind = order.Kind;
                // The map may have changed since the orders were given (an army knocked out leaves its land neutral).
                if (kind == EOrderKind.Attack && region.Owner == null) kind = EOrderKind.Garrison;
                if (kind == EOrderKind.Garrison && region.Owner != null && region.Owner != attacker) kind = EOrderKind.Attack;
                if (region.Owner == attacker) continue;

                if (kind == EOrderKind.Claim)
                {
                    int cost = CampaignMapRules.ClaimCost(m, attacker, n);
                    if (region.Owner != null) { lines.Add($"{NameOf(attacker)} wanted to buy {region.Name}, but it was taken first."); continue; }
                    if (Treasury(attacker) < cost) { lines.Add($"{NameOf(attacker)} couldn't afford {region.Name} ({cost} pts)."); continue; }
                    changes[attacker].Treasury -= cost;
                    region.Owner = attacker;
                    lines.Add($"{NameOf(attacker)} paid {cost} pts and claimed {region.Name}.");
                    continue;
                }

                if (kind == EOrderKind.Garrison)
                {
                    var battle = new MapBattle(region.Id, attacker, null, EMapBattleKind.Garrison, false);
                    bool? won = Fight(battle);
                    if (won == null) return Waiting(battle);
                    if (won.Value)
                    {
                        region.Owner = attacker;
                        lines.Add($"{Tag(battle)}{NameOf(attacker)} beat the garrison of {region.Name} and takes it.");
                    }
                    else lines.Add($"{Tag(battle)}{NameOf(attacker)} was driven off by the garrison of {region.Name}.");
                    continue;
                }

                // An attack on an army.
                string defender = region.Owner!;
                bool assault = region.Capital;
                var fight = new MapBattle(region.Id, attacker, defender, EMapBattleKind.Attack, assault);
                bool? attackerWon = Fight(fight);
                if (attackerWon == null) return Waiting(fight);
                if (!attackerWon.Value)
                {
                    lines.Add($"{Tag(fight)}{NameOf(attacker)} attacked {NameOf(defender)} at {region.Name} and lost.");
                    if (region.Siege != null)
                    {
                        lines.Add($"The siege of {region.Name} is lifted.");
                        region.Siege = null;
                    }
                    continue;
                }
                if (assault && region.Siege != attacker)
                {
                    region.Siege = attacker;
                    lines.Add($"{Tag(fight)}{NameOf(attacker)} beat {NameOf(defender)} at {region.Name} - the capital is under siege. One more win takes it.");
                    continue;
                }
                region.Owner = attacker;
                region.Siege = null;
                lines.Add($"{Tag(fight)}{NameOf(attacker)} beat {NameOf(defender)} at {region.Name} and takes it.");
                if (assault)
                {
                    region.Capital = false; // a taken capital is an ordinary (still starred) region of its new holder
                    KnockOut(defender);
                }
            }

            // ---- income from Hive Worlds / Towns.
            foreach (MapFaction f in m.Alive)
            {
                int income = CampaignMapRules.TurnIncome(m, f.Id, n);
                if (income > 0) changes[f.Id].Treasury += income;
            }

            m.History.Add(new MapTurnRecord { Turn = m.Turn, Lines = lines.ToList() });
            m.Pending = null;
            m.Turn++;
            return new MapTurnResult { Map = m, Changes = changes, Lines = lines };

            // ---- helpers

            string Tag(MapBattle b) => b.Involves(you) ? "Your battle: " : "";

            // True = the attacker won; null = a player battle not played yet.
            bool? Fight(MapBattle b)
            {
                if (b.Involves(you))
                {
                    if (playerResult >= pending.PlayerResults.Count) return null;
                    bool playerWon = pending.PlayerResults[playerResult++];
                    return b.Attacker == you ? playerWon : !playerWon;
                }
                double a = Math.Max(1, Snap(b.Attacker).Points);
                double d = b.Defender == null ? a * CampaignMapRules.GarrisonShare : Math.Max(1, Snap(b.Defender).Points);
                bool attackerWins = rng.NextDouble() < a / (a + d);
                // Off-screen army battles pay the book's rewards (win: 2 VP + the win payout; loss: the loss payout). A
                // garrison pays nothing (house rule).
                if (b.Defender != null)
                {
                    string w = attackerWins ? b.Attacker : b.Defender, l = attackerWins ? b.Defender : b.Attacker;
                    changes[w].MissionVp += CampaignNumbers.WinVp;
                    changes[w].Treasury += n.WinPoints;
                    changes[l].Treasury += n.LosePoints;
                }
                return attackerWins;
            }

            void KnockOut(string id)
            {
                MapFaction f = m.Faction(id)!;
                f.Alive = false;
                foreach (MapRegion r in m.Regions)
                {
                    if (r.Owner == id) { r.Owner = null; r.Capital = false; r.Siege = null; }
                    if (r.Siege == id) r.Siege = null;
                }
                lines.Add(f.IsPlayer ? "You lost your capital - the campaign is lost." : $"{f.Name} lost its capital and is knocked out. Its land turns neutral.");
            }

            MapTurnResult Waiting(MapBattle b) => new() { Map = map, WaitingFor = b, Lines = lines };
        }
    }

    /// <summary>
    /// #467 - how a rival picks its order (Kai: "every army for itself" - it attacks whoever is weakest next to it, the player
    /// included). Finishes a siege it started; attacks a neighbour no stronger than itself; otherwise takes neutral land,
    /// buying it when the treasury allows and fighting the garrison when not; otherwise rests.
    /// </summary>
    public static class CampaignMapBot
    {
        public static MapOrder Order(CampaignMap map, string faction, IReadOnlyDictionary<string, MapArmyState> armies,
                                     CampaignNumbers n, Random rng)
        {
            MapArmyState me = armies.TryGetValue(faction, out var s) ? s : default;
            var reach = CampaignMapRules.Reachable(map, faction).ToList();
            if (reach.Count == 0) return new MapOrder(faction, EOrderKind.Rest);

            MapRegion? siege = reach.FirstOrDefault(r => r.Capital && r.Siege == faction && r.Owner != null);
            if (siege != null) return new MapOrder(faction, EOrderKind.Attack, siege.Id);

            double Strength(string? owner) => owner != null && armies.TryGetValue(owner, out var o) ? Math.Max(1, o.Points) : 1;
            var targets = reach.Where(r => r.Owner != null)
                .Select(r => (Region: r, Ratio: Math.Max(1, me.Points) / Strength(r.Owner)))
                .Where(t => t.Ratio >= 0.9)
                .OrderByDescending(t => t.Ratio + (t.Region.Starred ? 0.4 : 0) + (t.Region.Capital ? 0.3 : 0))
                .ToList();
            var neutral = reach.Where(r => r.Owner == null).OrderByDescending(Value).ThenBy(r => r.Id).ToList();

            if (targets.Count > 0 && (neutral.Count == 0 || rng.NextDouble() < 0.5))
                return new MapOrder(faction, EOrderKind.Attack, targets[0].Region.Id);
            if (neutral.Count > 0)
            {
                MapRegion pick = neutral.Count > 1 && Value(neutral[0]) == Value(neutral[1]) && rng.NextDouble() < 0.5 ? neutral[1] : neutral[0];
                bool canPay = me.Treasury >= CampaignMapRules.ClaimCost(map, faction, n);
                return new MapOrder(faction, canPay ? EOrderKind.Claim : EOrderKind.Garrison, pick.Id);
            }
            if (targets.Count == 0 && rng.NextDouble() < 0.3)
            {
                MapRegion any = reach.Where(r => r.Owner != null).OrderBy(r => Strength(r.Owner)).First();
                return new MapOrder(faction, EOrderKind.Attack, any.Id);
            }
            return new MapOrder(faction, EOrderKind.Rest);
        }

        /// <summary>What a neutral region is worth to a rival: stars, then income, then the rest.</summary>
        private static int Value(MapRegion r) => (r.Starred ? 3 : 0) + r.Type switch
        {
            ERegionType.Hive => 2,
            ERegionType.Wild => 0,
            _ => 1,
        };
    }
}
