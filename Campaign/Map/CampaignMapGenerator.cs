namespace FDG.Campaign.Map
{
    /// <summary>
    /// #467 - makes a fresh campaign map (Kai: "a new map generated for each campaign", size picked per campaign). The same
    /// seed always makes the same map. The graph is planar (no two roads cross), connected, and every region has two to
    /// four neighbours where the layout allows; the capitals are spread as far apart as the roads allow, each with one
    /// starting region beside it of the same type for everyone, so no army starts with a better perk.
    /// </summary>
    public static class CampaignMapGenerator
    {
        public static int RegionCount(EMapSize size) => size switch
        {
            EMapSize.Small => 8,
            EMapSize.Medium => 14,
            _ => 20,
        };

        /// <summary>Enough regions that every army has room: two each to start, plus four neutral.</summary>
        public static int RegionCount(EMapSize size, int armies) => Math.Max(RegionCount(size), armies * 2 + 4);

        public const int MaxDegree = 4;

        /// <summary>The type every army's starting region gets, so nobody starts ahead.</summary>
        public const ERegionType StartingRegionType = ERegionType.Forge;

        /// <summary>Capitals are all the same type too.</summary>
        public const ERegionType CapitalType = ERegionType.Hive;

        /// <summary>
        /// A new map. <paramref name="factions"/> are the armies in play, the player first; each gets a capital and one
        /// region beside it.
        /// </summary>
        public static CampaignMap Generate(EGameSystem system, EMapSize size, IReadOnlyList<MapFaction> factions, int seed)
        {
            if (factions.Count == 0 || !factions[0].IsPlayer) throw new ArgumentException("The player's army comes first.", nameof(factions));
            var rng = new Random(seed);
            EMapStyle style = CampaignMapRules.StyleFor(system);
            int count = RegionCount(size, factions.Count);

            var map = new CampaignMap { System = system, Style = style, Size = size, Seed = seed };
            foreach (MapFaction f in factions) map.Factions.Add(new MapFaction
            {
                Id = f.Id, Name = f.Name, IsPlayer = f.IsPlayer, ArmyFile = f.ArmyFile, Alive = true, Colour = f.Colour,
            });

            List<(float X, float Y)> points = Scatter(count, rng);
            var names = Shuffled(style == EMapStyle.Realm ? RealmNames : StarNames, rng);
            for (int i = 0; i < points.Count; i++)
                map.Regions.Add(new MapRegion { Id = "R" + (i + 1), Name = names[i % names.Count] + (i >= names.Count ? " " + (i / names.Count + 1) : ""), X = points[i].X, Y = points[i].Y });

            Connect(map, points);
            PlaceCapitals(map, rng);
            AssignTypes(map, rng);
            return map;
        }

        // ------------------------------------------------------------------ positions

        private static List<(float X, float Y)> Scatter(int count, Random rng)
        {
            const float left = 0.07f, right = 0.93f, top = 0.09f, bottom = 0.91f;
            float spacing = 0.8f / MathF.Sqrt(count);
            var pts = new List<(float X, float Y)>();
            for (int attempt = 0; pts.Count < count; attempt++)
            {
                if (attempt > 0 && attempt % 400 == 0) spacing *= 0.9f; // crowded: relax and keep going
                float x = left + (float)rng.NextDouble() * (right - left), y = top + (float)rng.NextDouble() * (bottom - top);
                if (pts.All(p => Dist(p, (x, y)) >= spacing)) pts.Add((x, y));
            }
            return pts;
        }

        private static float Dist((float X, float Y) a, (float X, float Y) b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        // ------------------------------------------------------------------ roads

        private static void Connect(CampaignMap map, List<(float X, float Y)> pts)
        {
            int n = pts.Count;
            var degree = new int[n];
            var edges = new List<(int A, int B)>();
            var pairs = new List<(int A, int B, float D)>();
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++) pairs.Add((a, b, Dist(pts[a], pts[b])));
            pairs.Sort((p, q) => p.D.CompareTo(q.D));

            float longest = 2.2f / MathF.Sqrt(n);
            foreach (var (a, b, d) in pairs)
            {
                if (d > longest || degree[a] >= MaxDegree || degree[b] >= MaxDegree) continue;
                if (edges.Any(e => Crosses(pts, e, (a, b)))) continue;
                edges.Add((a, b)); degree[a]++; degree[b]++;
            }
            // Anything still on its own (or a separate cluster): join it by the shortest road that crosses nothing, or the
            // shortest road at all as a last resort - a map you can't cross is worse than one crossed road.
            while (true)
            {
                int[] group = Components(n, edges);
                if (group.Distinct().Count() == 1) break;
                var join = pairs.Where(p => group[p.A] != group[p.B]).ToList();
                var clean = join.FirstOrDefault(p => !edges.Any(e => Crosses(pts, e, (p.A, p.B))));
                var pick = clean.D > 0 ? clean : join[0];
                edges.Add((pick.A, pick.B)); degree[pick.A]++; degree[pick.B]++;
            }
            foreach (var (a, b) in edges) map.Links.Add(new MapLink(map.Regions[a].Id, map.Regions[b].Id));
        }

        private static int[] Components(int n, List<(int A, int B)> edges)
        {
            var g = Enumerable.Range(0, n).ToArray();
            int Find(int x) => g[x] == x ? x : g[x] = Find(g[x]);
            foreach (var (a, b) in edges) g[Find(a)] = Find(b);
            return Enumerable.Range(0, n).Select(Find).ToArray();
        }

        /// <summary>Whether two roads cross (sharing an end is not crossing).</summary>
        private static bool Crosses(List<(float X, float Y)> p, (int A, int B) e, (int A, int B) f)
        {
            if (e.A == f.A || e.A == f.B || e.B == f.A || e.B == f.B) return false;
            static float Cross((float X, float Y) o, (float X, float Y) a, (float X, float Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            float d1 = Cross(p[f.A], p[f.B], p[e.A]), d2 = Cross(p[f.A], p[f.B], p[e.B]);
            float d3 = Cross(p[e.A], p[e.B], p[f.A]), d4 = Cross(p[e.A], p[e.B], p[f.B]);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        // ------------------------------------------------------------------ capitals

        /// <summary>Steps between two regions along the roads.</summary>
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
            var allDist = map.Regions.ToDictionary(r => r.Id, r => Distances(map, r.Id));
            foreach (MapFaction f in map.Factions)
            {
                MapRegion pick;
                if (capitals.Count == 0)
                {
                    // The player starts at an edge of the map with room to grow: a region with 2+ roads, far from the middle.
                    var edge = map.Regions.Where(r => CampaignMapRules.Neighbours(map, r.Id).Count() >= 2)
                        .OrderByDescending(r => MathF.Abs(r.X - 0.5f) + MathF.Abs(r.Y - 0.5f) * 0.6f).Take(4).ToList();
                    pick = (edge.Count > 0 ? edge : map.Regions)[rng.Next(Math.Min(edge.Count > 0 ? edge.Count : map.Regions.Count, 4))];
                }
                else
                {
                    // Every other capital: as far as the roads allow from the ones already placed (ties broken at random).
                    var free = map.Regions.Where(r => r.Owner == null && CampaignMapRules.Neighbours(map, r.Id).Any(n => map.Region(n)!.Owner == null)).ToList();
                    if (free.Count == 0) free = map.Regions.Where(r => r.Owner == null).ToList();
                    int Score(MapRegion r) => capitals.Min(c => allDist[c.Id].TryGetValue(r.Id, out int d) ? d : 99);
                    int best = free.Max(Score);
                    var tied = free.Where(r => Score(r) == best).ToList();
                    pick = tied[rng.Next(tied.Count)];
                }
                pick.Owner = f.Id;
                pick.Capital = true;
                pick.Starred = true;
                capitals.Add(pick);

                // One starting region beside it - preferably not next to anyone else's capital.
                var near = CampaignMapRules.Neighbours(map, pick.Id).Select(id => map.Region(id)!).Where(r => r.Owner == null).ToList();
                var calm = near.Where(r => !CampaignMapRules.Neighbours(map, r.Id).Any(x => map.Region(x)!.Capital && map.Region(x)!.Owner != f.Id)).ToList();
                var from = calm.Count > 0 ? calm : near;
                if (from.Count > 0) from[rng.Next(from.Count)].Owner = f.Id;
            }
        }

        // ------------------------------------------------------------------ types

        /// <summary>How often each type turns up among the neutral regions.</summary>
        private static readonly ERegionType[] Bag =
        {
            ERegionType.Hive, ERegionType.Hive, ERegionType.Forge, ERegionType.Shrine, ERegionType.Shrine,
            ERegionType.Fortress, ERegionType.Fortress, ERegionType.Medical, ERegionType.Port, ERegionType.Wild, ERegionType.Wild,
        };

        private static void AssignTypes(CampaignMap map, Random rng)
        {
            var bag = new List<ERegionType>();
            foreach (MapRegion r in map.Regions)
            {
                if (r.Capital) { r.Type = CapitalType; continue; }
                if (r.Owner != null) { r.Type = StartingRegionType; continue; }
                if (bag.Count == 0) bag = Shuffled(Bag, rng);
                r.Type = bag[^1];
                bag.RemoveAt(bag.Count - 1);
                r.Starred = r.Type == ERegionType.Fortress;
            }
        }

        private static List<T> Shuffled<T>(IReadOnlyList<T> items, Random rng)
        {
            var list = items.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        // ------------------------------------------------------------------ names (made up; ASCII - game text)

        private static readonly string[] StarNames =
        {
            "Varn Prime", "Kessel's Forge", "Saint Oriel", "Bastion IX", "Halcyon", "Grey Harbour", "Thornmaw", "Ashfall",
            "Choir Spire", "Lumen Gate", "Ossuary", "Redoubt Kol", "Cold Reach", "Tessar", "Mordax", "Vela Minor",
            "Iron Veil", "Sorrow's End", "Kharon Deep", "Pale Lantern", "Erebus Station", "Novagrad", "Cinder Moon",
            "Tyros", "Hollow Star", "Galen's Rest", "Obsidia", "Severance", "Ninefold", "Aurum Drift", "Black Reef",
            "Corvane", "Dust Halo", "Meridian", "Last Light", "Quorum", "Rime", "Stygos", "Umbral Gate", "Wrathholm",
        };

        private static readonly string[] RealmNames =
        {
            "Highmarch", "Ember Pits", "Willowmere", "Brackenford", "Old Barrow", "Saltreach", "Gloamwood", "Duskhold",
            "Thornkeep", "Cinder Vale", "Mourning Shrine", "Fenmoor", "Gallows Cross", "Rookery", "Ashenford", "Briarholt",
            "Coldwater", "Dunmere", "Eastwatch", "Foxhollow", "Greyhaven", "Hallow Hill", "Ironmoot", "Kettleby",
            "Larkspur", "Mirefield", "Northfold", "Oakenshaw", "Pikestead", "Ravensgate", "Stonebridge", "Tallowmere",
            "Underhollow", "Vexmoor", "Wolfsden", "Yarrowdale", "Blackwater", "Crowfoot", "Deepdell", "Elderglen",
        };
    }
}
