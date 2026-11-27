namespace FDG.Campaign.Map
{
    /// <summary>
    /// An army's books as the map needs them for one turn - read from its campaign file (StrikeTeamInfo) by the app,
    /// never stored on the map, so the map can't drift from the army.
    /// </summary>
    /// <param name="Treasury">Unspent points (StrikeTeamInfo.BankedPoints).</param>
    /// <param name="Points">The army's value - what off-screen battles weigh.</param>
    /// <param name="MissionVp">VP earned in missions (StrikeTeamInfo.Vp); starred regions are added on top.</param>
    public readonly record struct MapArmyState(int Treasury, int Points, int MissionVp);

    /// <summary>
    /// #467 - the campaign map's house rules (Kai, 2026-11-14; claude/campaign-map-spec.md): who can reach what, what
    /// land costs, what regions give, VP and how the campaign ends. Pure - no files, no randomness.
    /// </summary>
    public static class CampaignMapRules
    {
        /// <summary>Defending your own capital: +2 underdog points.</summary>
        public const int CapitalDefenceUnderdog = 2;

        /// <summary>Holding a Fortress / Castle and defending it: +1 underdog point.</summary>
        public const int FortressDefenceUnderdog = 1;

        /// <summary>A garrison is about half the attacking force, so an off-screen attacker beats it 2 times in 3.</summary>
        public const double GarrisonShare = 0.5;

        // ------------------------------------------------------------------ names

        public static EMapStyle StyleFor(EGameSystem system) =>
            system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish or EGameSystem.AoFQuest ? EMapStyle.Realm : EMapStyle.StarSystem;

        /// <summary>The type's name in this style: "Hive World" / "Town".</summary>
        public static string TypeName(ERegionType t, EMapStyle s) => (t, s == EMapStyle.Realm) switch
        {
            (ERegionType.Hive, false) => "Hive World",
            (ERegionType.Hive, true) => "Town",
            (ERegionType.Forge, false) => "Forge World",
            (ERegionType.Forge, true) => "Mine",
            (ERegionType.Shrine, false) => "Shrine World",
            (ERegionType.Shrine, true) => "Temple",
            (ERegionType.Fortress, false) => "Fortress World",
            (ERegionType.Fortress, true) => "Castle",
            (ERegionType.Medical, false) => "Medical Station",
            (ERegionType.Medical, true) => "Abbey",
            (ERegionType.Port, false) => "Spaceport",
            (ERegionType.Port, true) => "Harbour",
            (ERegionType.Wild, false) => "Death World",
            (ERegionType.Wild, true) => "Deep Forest",
            _ => t.ToString(),
        };

        /// <summary>What holding the type gives, in plain words with this game's numbers. ASCII (game text).</summary>
        public static string PerkText(ERegionType t, EMapStyle s, CampaignNumbers n) => t switch
        {
            ERegionType.Hive => $"+{n.SpecialObjectiveBonus} pts to your treasury every turn.",
            ERegionType.Forge => $"Your 5-XP recruit costs no surcharge (saves {n.VeteranRecruitCost} pts; the book allows one per mission).",
            ERegionType.Shrine => "+1 underdog point in every battle.",
            ERegionType.Fortress => "Starred (1 VP while held). +1 underdog point when defending it.",
            ERegionType.Medical => "Units sitting out come back one turn sooner.",
            ERegionType.Port => "You may attack or claim up to 2 hexes away.",
            _ => "No perk - but the battle has a lot of dangerous terrain.",
        };

        /// <summary>
        /// The Advanced Rulebook terrain-generator theme a region's battle is rolled with (#467 step 4). The four books
        /// print four themes (Generic, Hospitable, Wasteland, Death Planets / Planes); each region type takes the one that
        /// reads closest - a judgement call (house rule B24), but the terrain itself is the book's own roll.
        /// </summary>
        public static TerrainBook.ETheme ThemeFor(ERegionType t) => t switch
        {
            ERegionType.Hive => TerrainBook.ETheme.Hospitable,     // cities and towns: buildings, walls, fields
            ERegionType.Forge => TerrainBook.ETheme.Wasteland,     // "devastated by years of industry"
            ERegionType.Shrine => TerrainBook.ETheme.Generic,      // ruins, barricades, sight blockers
            ERegionType.Fortress => TerrainBook.ETheme.Wasteland,  // a front line: craters, ruins, bunkers
            ERegionType.Medical => TerrainBook.ETheme.Hospitable,
            ERegionType.Port => TerrainBook.ETheme.Hospitable,     // rivers, lakes, open ground
            _ => TerrainBook.ETheme.Death,                         // deadly jungles and swamps
        };

        /// <summary>The battlefield a region's battle is fought on: its theme, in the game's own words. ASCII.</summary>
        public static string BattlefieldText(ERegionType t, EGameSystem system) =>
            TerrainBook.ThemeName(ThemeFor(t), system) + " - rolled on the Advanced Rulebook's terrain generator";

        // ------------------------------------------------------------------ the graph

        public static IEnumerable<string> Neighbours(CampaignMap map, string id) => map.NeighbourIds(id);

        public static bool Adjacent(CampaignMap map, string a, string b) => map.NeighbourIds(a).Contains(b);

        public static IEnumerable<MapRegion> Held(CampaignMap map, string faction) => map.Regions.Where(r => r.Owner == faction);

        public static int HeldCount(CampaignMap map, string faction) => map.Regions.Count(r => r.Owner == faction);

        /// <summary>
        /// Whether an army can act on a region: next to its land, or within two steps of a Spaceport / Harbour it holds.
        /// Never its own region.
        /// </summary>
        public static bool CanReach(CampaignMap map, string faction, MapRegion target)
        {
            if (target.Owner == faction) return false;
            // Walked from the target's side (same answer as walking out from every held region, but a few dozen lookups
            // instead of one per region on the map - a Large hex map has ~700).
            foreach (string step in Neighbours(map, target.Id))
            {
                if (map.Region(step)?.Owner == faction) return true;
                foreach (string far in Neighbours(map, step))
                    if (map.Region(far) is { Type: ERegionType.Port } port && port.Owner == faction) return true;
            }
            return false;
        }

        /// <summary>Every region the army can act on, in map order.</summary>
        public static IEnumerable<MapRegion> Reachable(CampaignMap map, string faction)
        {
            var near = new HashSet<string>();
            foreach (MapRegion mine in Held(map, faction))
                foreach (string step in Neighbours(map, mine.Id))
                {
                    near.Add(step);
                    if (mine.Type == ERegionType.Port) foreach (string far in Neighbours(map, step)) near.Add(far);
                }
            return map.Regions.Where(r => near.Contains(r.Id) && CanReach(map, faction, r)).ToList();
        }

        /// <summary>Fog (Kai): a rival's numbers are hidden until it borders the player's land.</summary>
        public static bool HasMet(CampaignMap map, string viewer, string other)
        {
            if (viewer == other) return true;
            foreach (MapRegion mine in Held(map, viewer))
                if (Neighbours(map, mine.Id).Any(n => map.Region(n)?.Owner == other)) return true;
            return false;
        }

        // ------------------------------------------------------------------ money and perks

        /// <summary>Paying for a neutral region: the special-objective bonus times the regions you already hold.</summary>
        public static int ClaimCost(CampaignMap map, string faction, CampaignNumbers n) =>
            n.SpecialObjectiveBonus * Math.Max(1, HeldCount(map, faction));

        /// <summary>What resting earns: the special-objective bonus.</summary>
        public static int RestIncome(CampaignNumbers n) => n.SpecialObjectiveBonus;

        /// <summary>Hive World / Town income: the special-objective bonus per one held, every turn.</summary>
        public static int TurnIncome(CampaignMap map, string faction, CampaignNumbers n) =>
            Held(map, faction).Count(r => r.Type == ERegionType.Hive) * n.SpecialObjectiveBonus;

        /// <summary>Underdog points an army's land gives it in a battle at <paramref name="where"/>.</summary>
        public static int UnderdogBonus(CampaignMap map, string faction, MapRegion where)
        {
            int points = Held(map, faction).Count(r => r.Type == ERegionType.Shrine);
            if (where.Owner == faction)
            {
                if (where.Capital) points += CapitalDefenceUnderdog;
                if (where.Type == ERegionType.Fortress) points += FortressDefenceUnderdog;
            }
            return points;
        }

        /// <summary>Forge World / Mine: how many recruits this turn skip the 5-XP surcharge.</summary>
        public static int FreeVeteranRecruits(CampaignMap map, string faction) =>
            Held(map, faction).Count(r => r.Type == ERegionType.Forge);

        /// <summary>Medical Station / Abbey: whether this army's sitting-out units come back a turn sooner.</summary>
        public static bool FasterRecovery(CampaignMap map, string faction) =>
            Held(map, faction).Any(r => r.Type == ERegionType.Medical);

        // ------------------------------------------------------------------ VP and the end

        public static int StarredHeld(CampaignMap map, string faction) => Held(map, faction).Count(r => r.Starred);

        /// <summary>An army's VP: its mission VP plus 1 per starred region it holds right now.</summary>
        public static int Vp(CampaignMap map, string faction, int missionVp) => missionVp + StarredHeld(map, faction);

        public static EMapOutcome Outcome(CampaignMap map, IReadOnlyDictionary<string, MapArmyState> armies)
        {
            if (!map.Player.Alive) return EMapOutcome.YouLost;
            if (map.Factions.Where(f => !f.IsPlayer).All(f => !f.Alive)) return EMapOutcome.YouWon;
            if (map.VpTarget <= 0) return EMapOutcome.InProgress;
            var scores = map.Alive.Select(f => (f.Id, Vp: Vp(map, f.Id, armies.TryGetValue(f.Id, out var s) ? s.MissionVp : 0)))
                .Where(x => x.Vp >= map.VpTarget).OrderByDescending(x => x.Vp).ToList();
            if (scores.Count == 0) return EMapOutcome.InProgress;
            if (scores.Count > 1 && scores[0].Vp == scores[1].Vp) return EMapOutcome.Draw;
            return scores[0].Id == CampaignMap.PlayerId ? EMapOutcome.YouWon : EMapOutcome.RivalWon;
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Why an order can't be given, or null when it can. ASCII (shown on the map screen).</summary>
        public static string? WhyNot(CampaignMap map, MapOrder order, MapArmyState army, CampaignNumbers n)
        {
            MapFaction? f = map.Faction(order.Faction);
            if (f is not { Alive: true }) return "That army is out of the campaign.";
            if (order.Kind == EOrderKind.Rest) return null;
            MapRegion? r = map.Region(order.Region);
            if (r == null) return "No such region.";
            if (r.Owner == order.Faction) return "You already hold it.";
            if (!CanReach(map, order.Faction, r)) return "It isn't next to your land.";
            switch (order.Kind)
            {
                case EOrderKind.Attack:
                    return r.Owner == null ? "Nobody holds it - claim it or fight its garrison instead." : null;
                case EOrderKind.Claim:
                    if (r.Owner != null) return "Only neutral regions can be bought.";
                    int cost = ClaimCost(map, order.Faction, n);
                    return army.Treasury < cost ? $"Claiming costs {cost} pts; the treasury has {army.Treasury}." : null;
                case EOrderKind.Garrison:
                    return r.Owner != null ? "It has no garrison - attack its holder instead." : null;
                default:
                    return "Unknown order.";
            }
        }
    }
}
