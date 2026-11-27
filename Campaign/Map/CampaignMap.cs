using System.Text.Json;
using System.Text.Json.Serialization;

namespace FDG.Campaign.Map
{
    // #467 - the campaign map (Kai, 2026-11-14; design: project doc claude/campaign-map-spec.md). The four Campaign
    // Rules v3.6.0 books have NO map rules, so everything in this namespace is a house rule (claude/house-rules.md).
    // The map is its own file beside the player's campaign army; the armies keep their books (VP, treasury, units)
    // in their own StrikeTeamInfo blocks, and the map never duplicates them - turn resolution reads them in as
    // MapArmyState and hands back what changed.

    /// <summary>How the map looks and is named: a star system for the sci-fi games, a realm of regions for fantasy.</summary>
    public enum EMapStyle
    {
        /// <summary>Planets, moons and stations joined by travel lanes (Grimdark Future, GF: Firefight).</summary>
        StarSystem = 0,
        /// <summary>Regions joined by roads (Age of Fantasy, AoF: Skirmish).</summary>
        Realm = 1,
    }

    /// <summary>How big a campaign's map is (Kai: picked per campaign). Since 2026-11-27 the map is a hex grid and the
    /// presets are its width x height (<see cref="CampaignMapGenerator.Dimensions"/>); Custom takes the player's own.</summary>
    public enum EMapSize
    {
        Small = 0,
        Medium = 1,
        Large = 2,
        Custom = 3,
    }

    /// <summary>A hex nobody can own or cross (Kai 2026-11-27: void/sea round the land, and mountains, lakes, seas,
    /// asteroid fields and nebulae sprinkled through it). Only drawn - the rules never see these.</summary>
    public enum EDeadHex
    {
        /// <summary>Open sea (fantasy) / deep space (sci-fi): the border round the land.</summary>
        Void = 0,
        /// <summary>An inland sea (fantasy) / nebula (sci-fi): water or gas cut off inside the land.</summary>
        Sea = 1,
        /// <summary>Mountains (fantasy) / an asteroid field (sci-fi).</summary>
        Rough = 2,
        /// <summary>A lake (fantasy) / a small nebula (sci-fi).</summary>
        Lake = 3,
    }

    /// <summary>One impassable hex of a hex map.</summary>
    public sealed record MapHex(int Col, int Row, EDeadHex Kind);

    /// <summary>
    /// What a region is. One set of types for both styles - only the names differ (<see cref="CampaignMapRules.TypeName"/>)
    /// - and each perk reuses a number the campaign books already have, so it scales with each game by itself.
    /// </summary>
    public enum ERegionType
    {
        /// <summary>Hive World / Town: + the special-objective bonus to the treasury every turn.</summary>
        Hive = 0,
        /// <summary>Forge World / Mine: one 5-XP recruit per turn without the surcharge.</summary>
        Forge = 1,
        /// <summary>Shrine World / Temple: +1 underdog point in every battle.</summary>
        Shrine = 2,
        /// <summary>Fortress World / Castle: starred; +1 underdog point when defending it.</summary>
        Fortress = 3,
        /// <summary>Medical Station / Abbey: units sitting out come back one turn sooner.</summary>
        Medical = 4,
        /// <summary>Spaceport / Harbour: may attack or claim up to two steps away.</summary>
        Port = 5,
        /// <summary>Death World / Deep Forest: no perk; a dangerous battlefield.</summary>
        Wild = 6,
    }

    /// <summary>One region (a planet, or a stretch of land).</summary>
    public sealed class MapRegion
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ERegionType Type { get; set; }

        /// <summary>Worth 1 VP to whoever holds it, for as long as they hold it.</summary>
        public bool Starred { get; set; }

        /// <summary>An army's capital: takes two wins to fall, and losing it knocks the army out.</summary>
        public bool Capital { get; set; }

        /// <summary>Where it is drawn: 0-1 across and down.</summary>
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>Its hex on a hex map (column and row, odd rows shifted half a hex right); -1 on an old region map.</summary>
        public int Col { get; set; } = -1;
        public int Row { get; set; } = -1;

        /// <summary>The faction holding it, or null for neutral.</summary>
        public string? Owner { get; set; }

        /// <summary>A capital's besieger: the army whose next win there takes it. Null = not under siege.</summary>
        public string? Siege { get; set; }
    }

    /// <summary>A road / travel lane between two regions.</summary>
    public sealed record MapLink(string A, string B)
    {
        public bool Touches(string id) => A == id || B == id;
        public string Other(string id) => A == id ? B : A;
    }

    /// <summary>One army on the map: you, or a rival.</summary>
    public sealed class MapFaction
    {
        /// <summary>"you", "r1", "r2", "r3".</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsPlayer { get; set; }

        /// <summary>The army's campaign file (its units, VP and treasury live there).</summary>
        public string? ArmyFile { get; set; }

        /// <summary>False once it has lost its capital.</summary>
        public bool Alive { get; set; } = true;

        /// <summary>Which of the map colours it is drawn in (0 = the player's).</summary>
        public int Colour { get; set; }
    }

    /// <summary>What an army does this turn.</summary>
    public enum EOrderKind
    {
        /// <summary>Recover, and earn the special-objective bonus.</summary>
        Rest = 0,
        /// <summary>Attack a region another army holds.</summary>
        Attack = 1,
        /// <summary>Pay to take a neutral region (no battle).</summary>
        Claim = 2,
        /// <summary>Fight a neutral region's garrison for it (free if won).</summary>
        Garrison = 3,
    }

    /// <summary>One army's order for one turn.</summary>
    public sealed record MapOrder(string Faction, EOrderKind Kind, string? Region = null);

    /// <summary>The turn being played: every army's order and the results of the player's battles so far.</summary>
    public sealed class MapPendingTurn
    {
        public List<MapOrder> Orders { get; set; } = new();

        /// <summary>One entry per player battle already played, in order: true = the player won it.</summary>
        public List<bool> PlayerResults { get; set; } = new();

        /// <summary>Every army's books when the turn began - what the turn's claims and rolls go by, so a reload in the
        /// middle of a turn replays it exactly even though the player's battles have changed the armies since.</summary>
        public Dictionary<string, MapArmyState> Armies { get; set; } = new();

        /// <summary>The player's campaign Matches count when the battle being played was set up; null when none is.
        /// The mission is over once Matches has moved past it.</summary>
        public int? BattleAtMatch { get; set; }

        /// <summary>The garrison army file made for the battle being played, deleted once it is recorded.</summary>
        public string? GarrisonFile { get; set; }

        /// <summary>The battlefield made for the battle being played (#467 step 4), deleted once it is recorded.</summary>
        public string? BattlefieldFile { get; set; }
    }

    /// <summary>A finished turn's report.</summary>
    public sealed class MapTurnRecord
    {
        public int Turn { get; set; }
        public List<string> Lines { get; set; } = new();
    }

    /// <summary>How a map campaign stands.</summary>
    public enum EMapOutcome
    {
        InProgress = 0,
        YouWon = 1,
        YouLost = 2,
        /// <summary>A rival reached the VP target first.</summary>
        RivalWon = 3,
        Draw = 4,
    }

    /// <summary>The whole map campaign: the world, the armies on it, and its turn.</summary>
    public sealed class CampaignMap
    {
        public const string Extension = ".fdgmap";
        public const string PlayerId = "you";
        public const int DefaultVpTarget = 10;

        public int Version { get; set; } = 1;
        public EGameSystem System { get; set; } = EGameSystem.GrimdarkFuture;
        public EMapStyle Style { get; set; }
        public EMapSize Size { get; set; }
        public int Seed { get; set; }
        public int Turn { get; set; } = 1;
        public int VpTarget { get; set; } = DefaultVpTarget;

        /// <summary>A hex map's width and height in hexes; 0 = an old region-and-road map (saves from before 2026-11-27).</summary>
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>Whether this is a hex map: every region is one hex and its links are the hexes touching it.</summary>
        [JsonIgnore] public bool IsHex => Width > 0 && Height > 0;

        public List<MapRegion> Regions { get; set; } = new();
        public List<MapLink> Links { get; set; } = new();

        /// <summary>A hex map's impassable hexes (every hex that is not a region).</summary>
        public List<MapHex> Dead { get; set; } = new();
        public List<MapFaction> Factions { get; set; } = new();

        /// <summary>The turn under way, or null between turns.</summary>
        public MapPendingTurn? Pending { get; set; }

        public List<MapTurnRecord> History { get; set; } = new();

        public MapRegion? Region(string? id)
        {
            if (id == null) return null;
            if (_byId == null || _byIdCount != Regions.Count)
            {
                _byId = new Dictionary<string, MapRegion>();
                foreach (MapRegion r in Regions) _byId.TryAdd(r.Id, r);
                _byIdCount = Regions.Count;
            }
            return _byId.TryGetValue(id, out MapRegion? hit) ? hit : null;
        }

        /// <summary>The regions linked to <paramref name="id"/>. Cached - a Large hex map has ~700 regions and ~1,900 links,
        /// and the rules ask this thousands of times a turn. Rebuilt whenever the link count changes.</summary>
        public IReadOnlyList<string> NeighbourIds(string id)
        {
            if (_adj == null || _adjCount != Links.Count)
            {
                _adj = new Dictionary<string, List<string>>();
                foreach (MapLink l in Links)
                {
                    if (!_adj.TryGetValue(l.A, out var a)) _adj[l.A] = a = new List<string>();
                    if (!_adj.TryGetValue(l.B, out var b)) _adj[l.B] = b = new List<string>();
                    if (!a.Contains(l.B)) a.Add(l.B);
                    if (!b.Contains(l.A)) b.Add(l.A);
                }
                _adjCount = Links.Count;
            }
            return _adj.TryGetValue(id, out var list) ? list : Array.Empty<string>();
        }

        private Dictionary<string, MapRegion>? _byId;
        private int _byIdCount = -1;
        private Dictionary<string, List<string>>? _adj;
        private int _adjCount = -1;
        public MapFaction? Faction(string? id) => id == null ? null : Factions.FirstOrDefault(f => f.Id == id);
        public MapFaction Player => Factions.First(f => f.IsPlayer);
        public IEnumerable<MapFaction> Alive => Factions.Where(f => f.Alive);

        // ------------------------------------------------------------------ files

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static CampaignMap? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<CampaignMap>(json, Options); }
            catch (JsonException) { return null; }
        }

        public CampaignMap Clone() => FromJson(ToJson())!;

        public bool Save(string path)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, ToJson());
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }

        public static CampaignMap? Load(string path)
        {
            try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
    }
}
