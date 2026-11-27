namespace FDG.Campaign.Map
{
    /// <summary>
    /// #467 - makes a fresh campaign map (Kai: "a new map generated for each campaign", size picked per campaign). The same
    /// seed always makes the same map.
    ///
    /// Since 2026-11-27 the map is a HEX GRID (Kai; mockup "Hex Campaign Map" v3, project doc claude/campaign-map-spec.md):
    /// every land hex is one region and its links are the land hexes touching it. The land comes in several chunks of
    /// random sizes scattered over the map, joined by thin land bridges so every army can reach every hex; void / sea runs
    /// round the edge, and clusters of mountains, lakes and inland seas (asteroid fields and nebulae in sci-fi) are
    /// sprinkled through the land without ever cutting it in two. Hex types clump into areas; starred Fortress / Castle
    /// hexes stay scattered. The capitals are spread as far apart as the land allows, each with one starting hex beside
    /// it of the same type for everyone, so no army starts with a better perk.
    /// </summary>
    public static class CampaignMapGenerator
    {
        /// <summary>The preset sizes, in hexes (Kai 2026-11-27: Small / Medium / Large plus his own width x height).</summary>
        public static (int Width, int Height) Dimensions(EMapSize size) => size switch
        {
            EMapSize.Small => (14, 10),
            EMapSize.Large => (32, 22),
            _ => (22, 16),
        };

        /// <summary>The smallest and largest a custom map may be, each way.</summary>
        public const int MinSide = 8, MaxSide = 60;

        public static int ClampSide(int side) => Math.Clamp(side, MinSide, MaxSide);

        /// <summary>The type every army's starting region gets, so nobody starts ahead.</summary>
        public const ERegionType StartingRegionType = ERegionType.Forge;

        /// <summary>Capitals are all the same type too.</summary>
        public const ERegionType CapitalType = ERegionType.Hive;

        /// <summary>The share of the land turned into sprinkled mountains / lakes / seas (Kai kept ~1 in 6, mockup v3).</summary>
        public const double BlockedShare = 0.17;

        /// <summary>A preset-sized map. <paramref name="factions"/> are the armies in play, the player first.</summary>
        public static CampaignMap Generate(EGameSystem system, EMapSize size, IReadOnlyList<MapFaction> factions, int seed)
        {
            var (w, h) = Dimensions(size);
            return Generate(system, w, h, factions, seed, size);
        }

        /// <summary>
        /// A map of <paramref name="width"/> x <paramref name="height"/> hexes (clamped to <see cref="MinSide"/> -
        /// <see cref="MaxSide"/>). <paramref name="factions"/> are the armies in play, the player first; each gets a
        /// capital and one hex beside it.
        /// </summary>
        public static CampaignMap Generate(EGameSystem system, int width, int height, IReadOnlyList<MapFaction> factions, int seed,
            EMapSize size = EMapSize.Custom)
        {
            if (factions.Count == 0 || !factions[0].IsPlayer) throw new ArgumentException("The player's army comes first.", nameof(factions));
            width = ClampSide(width);
            height = ClampSide(height);
            var rng = new Random(seed);
            // A layout where two armies border each other from turn 1 skips the land grab the rivals are there for; try a
            // few layouts for one where everyone starts apart (still one map per seed).
            CampaignMap map = Layout(system, width, height, size, factions, seed, rng);
            for (int attempt = 0; attempt < 12 && (AnyContact(map) || TooSmall(map)); attempt++)
                map = Layout(system, width, height, size, factions, seed, rng);
            return map;
        }

        /// <summary>Whether any two armies border each other.</summary>
        public static bool AnyContact(CampaignMap map) =>
            map.Factions.Any(f => map.Factions.Any(o => o != f && CampaignMapRules.HasMet(map, f.Id, o.Id)));

        /// <summary>Not enough land for every army to start with two hexes and room to grow.</summary>
        private static bool TooSmall(CampaignMap map) =>
            map.Regions.Count < map.Factions.Count * 2 + 4 || map.Factions.Any(f => CampaignMapRules.HeldCount(map, f.Id) != 2);

        // ------------------------------------------------------------------ the grid

        /// <summary>The (up to six) hexes touching a hex, in an odd-rows-shifted-right grid.</summary>
        public static IEnumerable<(int Col, int Row)> HexNeighbours(int col, int row, int width, int height)
        {
            (int, int)[] steps = (row & 1) == 1
                ? new[] { (1, 0), (-1, 0), (1, -1), (0, -1), (1, 1), (0, 1) }
                : new[] { (1, 0), (-1, 0), (0, -1), (-1, -1), (0, 1), (-1, 1) };
            foreach (var (dc, dr) in steps)
            {
                int c = col + dc, r = row + dr;
                if (c >= 0 && r >= 0 && c < width && r < height) yield return (c, r);
            }
        }

        /// <summary>Hexes between two hexes, as the crow flies.</summary>
        public static int HexDistance(int c1, int r1, int c2, int r2)
        {
            int x1 = c1 - (r1 - (r1 & 1)) / 2, x2 = c2 - (r2 - (r2 & 1)) / 2;
            int z1 = r1, z2 = r2, y1 = -x1 - z1, y2 = -x2 - z2;
            return Math.Max(Math.Abs(x1 - x2), Math.Max(Math.Abs(y1 - y2), Math.Abs(z1 - z2)));
        }

        /// <summary>A hex's centre, 0-1 across and down (what the region's X / Y hold).</summary>
        public static (float X, float Y) HexCentre(int col, int row, int width, int height) =>
            (((col + 0.5f * (row & 1)) + 0.5f) / (width + 0.5f), (row * 0.75f + 0.5f) / ((height - 1) * 0.75f + 1f));

        private enum ECell { Void, Land, Sea, Rough, Lake }

        private sealed class Grid
        {
            public readonly int W, H;
            public readonly ECell[] Cell;
            public Grid(int w, int h) { W = w; H = h; Cell = new ECell[w * h]; }
            public int I(int c, int r) => r * W + c;
            public (int C, int R) At(int i) => (i % W, i / W);
            public bool Edge(int i) { var (c, r) = At(i); return c == 0 || r == 0 || c == W - 1 || r == H - 1; }
            public IEnumerable<int> Next(int i) { var (c, r) = At(i); return HexNeighbours(c, r, W, H).Select(p => I(p.Col, p.Row)); }

            /// <summary>The cells joined to <paramref name="start"/> through cells that pass <paramref name="ok"/>.</summary>
            public HashSet<int> Flood(int start, Func<int, bool> ok)
            {
                var seen = new HashSet<int> { start };
                var stack = new Stack<int>();
                stack.Push(start);
                while (stack.Count > 0)
                    foreach (int n in Next(stack.Pop()))
                        if (ok(n) && seen.Add(n)) stack.Push(n);
                return seen;
            }

            public List<HashSet<int>> Pieces(ECell kind)
            {
                var done = new HashSet<int>();
                var pieces = new List<HashSet<int>>();
                for (int i = 0; i < Cell.Length; i++)
                    if (Cell[i] == kind && !done.Contains(i))
                    {
                        var p = Flood(i, x => Cell[x] == kind);
                        done.UnionWith(p);
                        pieces.Add(p);
                    }
                return pieces.OrderByDescending(p => p.Count).ToList();
            }

            public bool LandConnected()
            {
                int first = Array.IndexOf(Cell, ECell.Land);
                if (first < 0) return false;
                return Flood(first, x => Cell[x] == ECell.Land).Count == Cell.Count(c => c == ECell.Land);
            }
        }

        /// <summary>Smooth random values on a coarse lattice, so land and holes come in lumps rather than speckle.</summary>
        private static Func<double, double, double> Noise(int w, int h, double cell, Random rng)
        {
            int gw = (int)Math.Ceiling(w / cell) + 2, gh = (int)Math.Ceiling(h / cell) + 2;
            var v = new double[gw * gh];
            for (int i = 0; i < v.Length; i++) v[i] = rng.NextDouble();
            static double S(double t) => t * t * (3 - 2 * t);
            return (x, y) =>
            {
                double gx = Math.Clamp(x / cell, 0, gw - 1.001), gy = Math.Clamp(y / cell, 0, gh - 1.001);
                int x0 = (int)gx, y0 = (int)gy;
                double fx = S(gx - x0), fy = S(gy - y0);
                double G(int a, int b) => v[Math.Min(b, gh - 1) * gw + Math.Min(a, gw - 1)];
                double top = G(x0, y0) + (G(x0 + 1, y0) - G(x0, y0)) * fx;
                double bottom = G(x0, y0 + 1) + (G(x0 + 1, y0 + 1) - G(x0, y0 + 1)) * fx;
                return top + (bottom - top) * fy;
            };
        }

        private static Grid Land(int w, int h, Random rng)
        {
            var g = new Grid(w, h);
            var n1 = Noise(w, h, 3, rng);
            var n2 = Noise(w * 2, h * 2, 3, rng);

            // 1. Chunks of land of random sizes scattered over the whole map.
            int chunks = Math.Max(3, (int)Math.Round(w * h / (55.0 + rng.Next(31))));
            var blobs = Enumerable.Range(0, chunks)
                .Select(_ => (C: rng.Next(1, w - 1), R: rng.Next(1, h - 1), Rad: 1.5 + rng.NextDouble() * (Math.Min(w, h) / 5.0 + 1.5)))
                .ToList();
            var score = new double[w * h];
            for (int i = 0; i < score.Length; i++)
            {
                if (g.Edge(i)) { score[i] = double.MinValue; continue; }
                var (c, r) = g.At(i);
                double near = blobs.Max(b => 1 - HexDistance(b.C, b.R, c, r) / b.Rad);
                score[i] = near + (n1(c, r) - 0.5) * 0.7 + (n2(c * 2, r * 2) - 0.5) * 0.35;
            }
            double share = 0.5 + rng.NextDouble() * 0.08;
            double cut = score.OrderByDescending(s => s).ElementAt((int)(score.Length * share));
            for (int i = 0; i < score.Length; i++) if (score[i] > cut) g.Cell[i] = ECell.Land;

            // 2. Drop specks; join every chunk to the biggest by the shortest thin land bridge (Kai: keep the bridges).
            foreach (var piece in g.Pieces(ECell.Land).Where(p => p.Count < 4))
                foreach (int i in piece) g.Cell[i] = ECell.Void;
            var bridges = new HashSet<int>();
            for (int guard = 0; guard < 80; guard++)
            {
                var pieces = g.Pieces(ECell.Land);
                if (pieces.Count <= 1) break;
                var main = pieces[0];
                var from = new Dictionary<int, int>();
                var queue = new Queue<int>();
                foreach (int i in main) { from[i] = -1; queue.Enqueue(i); }
                int hit = -1;
                while (queue.Count > 0 && hit < 0)
                {
                    int at = queue.Dequeue();
                    foreach (int n in g.Next(at))
                    {
                        if (from.ContainsKey(n) || g.Edge(n)) continue;
                        from[n] = at;
                        if (g.Cell[n] == ECell.Land && !main.Contains(n)) { hit = n; break; }
                        queue.Enqueue(n);
                    }
                }
                if (hit < 0) break;
                for (int p = from[hit]; p >= 0 && !main.Contains(p); p = from[p]) { g.Cell[p] = ECell.Land; bridges.Add(p); }
            }
            var rest = g.Pieces(ECell.Land);
            foreach (var piece in rest.Skip(1)) foreach (int i in piece) g.Cell[i] = ECell.Void;

            // 3. Water cut off from the open sea is an inland sea (big) or a lake (small).
            var outside = g.Flood(0, x => g.Cell[x] == ECell.Void);
            foreach (var pocket in g.Pieces(ECell.Void).Where(p => !p.Contains(0) && !p.Overlaps(outside)))
                foreach (int i in pocket) g.Cell[i] = pocket.Count > 3 ? ECell.Sea : ECell.Lake;

            // 4. Sprinkle clusters of mountains / lakes / seas through the land, never cutting it in two or sitting on a bridge.
            var land = Enumerable.Range(0, g.Cell.Length).Where(i => g.Cell[i] == ECell.Land).ToList();
            int want = (int)Math.Round(land.Count * BlockedShare), made = 0;
            for (int tries = 0; made < want && tries < 300; tries++)
            {
                int seed = land[rng.Next(land.Count)];
                if (g.Cell[seed] != ECell.Land || bridges.Contains(seed)) continue;
                bool water = rng.NextDouble() < 0.4;
                int size = rng.Next(1, 6);
                var cluster = new List<int> { seed };
                while (cluster.Count < size)
                {
                    int b = cluster[rng.Next(cluster.Count)];
                    var options = g.Next(b).Where(n => g.Cell[n] == ECell.Land && !cluster.Contains(n) && !bridges.Contains(n)).ToList();
                    if (options.Count == 0) break;
                    cluster.Add(options[rng.Next(options.Count)]);
                }
                ECell kind = !water ? ECell.Rough : cluster.Count > 2 ? ECell.Sea : ECell.Lake;
                foreach (int i in cluster) g.Cell[i] = kind;
                if (!g.LandConnected()) { foreach (int i in cluster) g.Cell[i] = ECell.Land; continue; }
                made += cluster.Count;
            }
            return g;
        }

        private static CampaignMap Layout(EGameSystem system, int w, int h, EMapSize size, IReadOnlyList<MapFaction> factions, int seed, Random rng)
        {
            EMapStyle style = CampaignMapRules.StyleFor(system);
            var map = new CampaignMap { System = system, Style = style, Size = size, Seed = seed, Width = w, Height = h };
            foreach (MapFaction f in factions) map.Factions.Add(new MapFaction
            {
                Id = f.Id, Name = f.Name, IsPlayer = f.IsPlayer, ArmyFile = f.ArmyFile, Alive = true, Colour = f.Colour,
            });

            Grid g = Land(w, h, rng);
            var idOf = new Dictionary<int, string>();
            for (int i = 0; i < g.Cell.Length; i++)
            {
                var (c, r) = g.At(i);
                if (g.Cell[i] != ECell.Land)
                {
                    map.Dead.Add(new MapHex(c, r, g.Cell[i] switch
                    {
                        ECell.Sea => EDeadHex.Sea,
                        ECell.Rough => EDeadHex.Rough,
                        ECell.Lake => EDeadHex.Lake,
                        _ => EDeadHex.Void,
                    }));
                    continue;
                }
                var (x, y) = HexCentre(c, r, w, h);
                string id = "H" + c + "-" + r;
                idOf[i] = id;
                map.Regions.Add(new MapRegion { Id = id, Col = c, Row = r, X = x, Y = y });
            }
            foreach (var (i, id) in idOf)
                foreach (int n in g.Next(i))
                    if (n > i && idOf.TryGetValue(n, out string? other)) map.Links.Add(new MapLink(id, other));

            if (map.Regions.Count >= factions.Count * 2) PlaceCapitals(map, rng);
            AssignTypes(map, rng);
            Name(map, rng);
            return map;
        }

        // ------------------------------------------------------------------ capitals

        /// <summary>Steps between two regions over the land.</summary>
        public static Dictionary<string, int> Distances(CampaignMap map, string from)
        {
            var dist = new Dictionary<string, int> { [from] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                foreach (string next in CampaignMapRules.Neighbours(map, at))
                    if (dist.TryAdd(next, dist[at] + 1)) queue.Enqueue(next);
            }
            return dist;
        }

        private static void PlaceCapitals(CampaignMap map, Random rng)
        {
            var capitals = new List<MapRegion>();
            var fromCapital = new List<Dictionary<string, int>>();
            foreach (MapFaction f in map.Factions)
            {
                // Room to grow: a hex with 3+ land neighbours that are all still free.
                bool Roomy(MapRegion r) => r.Owner == null && CampaignMapRules.Neighbours(map, r.Id).Count(n => map.Region(n)!.Owner == null) >= 3;
                var free = map.Regions.Where(Roomy).ToList();
                if (free.Count == 0) free = map.Regions.Where(r => r.Owner == null).ToList();
                MapRegion pick;
                if (capitals.Count == 0)
                {
                    // The player starts near an edge of the land: one of the hexes furthest from the middle.
                    var edge = free.OrderByDescending(r => MathF.Abs(r.X - 0.5f) + MathF.Abs(r.Y - 0.5f) * 0.6f).Take(6).ToList();
                    pick = edge[rng.Next(edge.Count)];
                }
                else
                {
                    // Every other capital: as far over the land as it can get from the ones already placed.
                    int Score(MapRegion r) => fromCapital.Min(d => d.TryGetValue(r.Id, out int s) ? s : 999);
                    int best = free.Max(Score);
                    var tied = free.Where(r => Score(r) == best).ToList();
                    pick = tied[rng.Next(tied.Count)];
                }
                pick.Owner = f.Id;
                pick.Capital = true;
                pick.Starred = true;
                capitals.Add(pick);
                fromCapital.Add(Distances(map, pick.Id));
            }

            // Then one starting hex beside each capital, away from everyone else's land where the map allows.
            foreach (MapRegion cap in capitals)
            {
                string f = cap.Owner!;
                var near = CampaignMapRules.Neighbours(map, cap.Id).Select(id => map.Region(id)!).Where(r => r.Owner == null).ToList();
                bool Others(MapRegion r) => CampaignMapRules.Neighbours(map, r.Id).Any(x => map.Region(x)!.Owner is { } o && o != f);
                var calm = near.Where(r => !Others(r)).ToList();
                var from = calm.Count > 0 ? calm : near;
                if (from.Count > 0) from[rng.Next(from.Count)].Owner = f;
            }
        }

        // ------------------------------------------------------------------ types

        /// <summary>How often each type turns up among the neutral hexes (Fortress / Castle ~1 in 13, mockup v3).</summary>
        private static readonly (ERegionType Type, int Weight)[] Weights =
        {
            (ERegionType.Hive, 12), (ERegionType.Forge, 14), (ERegionType.Shrine, 11), (ERegionType.Fortress, 6),
            (ERegionType.Medical, 10), (ERegionType.Port, 10), (ERegionType.Wild, 16),
        };

        private static void AssignTypes(CampaignMap map, Random rng)
        {
            int total = Weights.Sum(w => w.Weight);
            ERegionType Roll()
            {
                int x = rng.Next(total);
                foreach (var (t, wt) in Weights) { if (x < wt) return t; x -= wt; }
                return ERegionType.Wild;
            }
            foreach (MapRegion r in map.Regions)
                r.Type = r.Capital ? CapitalType : r.Owner != null ? StartingRegionType : Roll();

            // Clump into areas - jungle belts, hive clusters - so the map reads as land rather than confetti. Fortresses,
            // capitals and starting hexes keep their type.
            bool Fixed(MapRegion r) => r.Owner != null || r.Type == ERegionType.Fortress;
            for (int pass = 0; pass < 2; pass++)
                foreach (MapRegion r in map.Regions)
                {
                    if (Fixed(r) || rng.NextDouble() < 0.45) continue;
                    var around = CampaignMapRules.Neighbours(map, r.Id).Select(id => map.Region(id)!)
                        .Where(n => n.Type != ERegionType.Fortress && !n.Capital).GroupBy(n => n.Type)
                        .OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key).FirstOrDefault();
                    if (around != null && around.Count() >= 2) r.Type = around.Key;
                }

            foreach (MapRegion r in map.Regions)
                if (!r.Capital) r.Starred = r.Owner == null && r.Type == ERegionType.Fortress;
        }

        // ------------------------------------------------------------------ names (made up; ASCII - game text)

        private static void Name(CampaignMap map, Random rng)
        {
            bool realm = map.Style == EMapStyle.Realm;
            string[] first = realm ? RealmFirst : StarFirst;
            var used = new HashSet<string>();
            foreach (MapRegion r in map.Regions)
            {
                string[] second = (realm ? RealmSecond : StarSecond)[r.Type];
                string name = "";
                for (int tries = 0; tries < 30; tries++)
                {
                    name = first[rng.Next(first.Length)] + " " + second[rng.Next(second.Length)];
                    if (!used.Contains(name)) break;
                }
                for (int n = 2; used.Contains(name); n++) name = name.Split(" (")[0] + " (" + n + ")";
                used.Add(name);
                r.Name = name;
            }
        }

        private static readonly string[] StarFirst =
        {
            "Varn", "Kessel", "Oriel", "Halcyon", "Thornmaw", "Ashfall", "Lumen", "Kol", "Tessar", "Mordax", "Vela", "Kharon",
            "Erebus", "Novagrad", "Cinder", "Tyros", "Galen", "Obsidia", "Corvane", "Meridian", "Quorum", "Rime", "Stygos",
            "Umbral", "Wrath", "Ixion", "Cael", "Drevos", "Hadris", "Solun", "Varkath", "Nyx", "Thessa", "Ostrak", "Zerran",
            "Pyra", "Askel", "Vhal", "Orrin", "Tauros", "Sorrow", "Pale", "Severan", "Aurum", "Dust", "Black Reef", "Iron",
        };

        private static readonly Dictionary<ERegionType, string[]> StarSecond = new()
        {
            [ERegionType.Hive] = new[] { "Prime", "Spire", "Metropolis", "Hab-Sprawl", "Hive", "Arcology" },
            [ERegionType.Forge] = new[] { "Forge", "Foundry", "Anvil", "Smeltworks", "Manufactorum" },
            [ERegionType.Shrine] = new[] { "Sanctum", "Reliquary", "Cathedra", "Choir", "Ossuary" },
            [ERegionType.Fortress] = new[] { "Bastion", "Citadel", "Bulwark", "Redoubt", "Keep" },
            [ERegionType.Medical] = new[] { "Medica", "Infirmary", "Hospice", "Sanatorium" },
            [ERegionType.Port] = new[] { "Dock", "Starport", "Anchorage", "Gate", "Station" },
            [ERegionType.Wild] = new[] { "Wilds", "Thornworld", "Miasma", "Verdance", "Deep" },
        };

        private static readonly string[] RealmFirst =
        {
            "Blackroot", "Ashford", "Greywater", "Highmoor", "Kingsreach", "Raven", "Stonebrook", "Thornwall", "Elder",
            "Hollow", "Iron", "Mirk", "Wolfs", "Gallow", "Bright", "Cold", "Dun", "Fen", "Ember", "Willow", "Bracken",
            "Barrow", "Salt", "Gloam", "Dusk", "Cinder", "Mourn", "Rook", "Briar", "East", "Fox", "Hallow", "Kettle", "Lark",
            "Mire", "North", "Oaken", "Pike", "Tallow", "Under", "Vex", "Yarrow", "Crow", "Deep", "Brine", "Hare", "Moss",
        };

        private static readonly Dictionary<ERegionType, string[]> RealmSecond = new()
        {
            [ERegionType.Hive] = new[] { "Market", "Crossing", "Burgh", "Hold", "Town", "Stead" },
            [ERegionType.Forge] = new[] { "Delve", "Pits", "Quarry", "Deep", "Mine" },
            [ERegionType.Shrine] = new[] { "Chapel", "Shrine", "Sanctum", "Temple", "Barrow" },
            [ERegionType.Fortress] = new[] { "Keep", "Castle", "Tower", "Watch", "Fort" },
            [ERegionType.Medical] = new[] { "Abbey", "Priory", "Cloister", "Hospice" },
            [ERegionType.Port] = new[] { "Harbour", "Wharf", "Haven", "Quay", "Landing" },
            [ERegionType.Wild] = new[] { "Wood", "Weald", "Thicket", "Mire", "Forest" },
        };
    }
}
