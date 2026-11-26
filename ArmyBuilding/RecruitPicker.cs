using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;

namespace FDG.ArmyBuilding
{
    /// <summary>
    /// Where the engine finds an army book when a bot has to buy units in the middle of a game (Meat Grinder
    /// recruitment). The books ship with the app, not the engine, so the app sets <see cref="Find"/> at start-up;
    /// with nothing set (unit tests) a bot simply buys nothing.
    /// </summary>
    public static class BookSource
    {
        /// <summary>(faction, army name, game book slug) -> the faction's book for that game, or null.</summary>
        public static Func<string, string, string?, BookFile?>? Find { get; set; }
    }

    /// <summary>
    /// A bot's shopping list for a mid-game recruitment (Meat Grinder): units from its own book at their default size
    /// and options, picked at random - new kinds before repeats - while they still fit the points it may spend.
    /// Heroes are left out (a bought hero would stand alone). Kept simple on purpose: the Solo &amp; Co-Op books say
    /// nothing about recruiting.
    /// </summary>
    public static class RecruitPicker
    {
        public static ArmyListFile? Pick(BookFile book, int points, Random rng)
        {
            if (points <= 0 || book.Units.Count == 0) return null;
            var list = new BuilderList { Name = "Recruits", BookName = book.Name, PointsLimit = points };
            int Cost() => ListCompiler.Compile(book, list).Units.Sum(u => Math.Max(0, u.PointCost));

            var pool = book.Units.Where(u => !Campaign.CampaignBot.IsHeroEntry(u)).ToList();
            var tried = new HashSet<string>();
            var full = new HashSet<string>();
            for (int guard = 0; guard < 60; guard++)
            {
                var candidates = pool.Where(u => !full.Contains(u.Id)).ToList();
                if (candidates.Count == 0) break;
                var fresh = candidates.Where(u => !tried.Contains(u.Id)).ToList();
                List<RosterUnit> from = fresh.Count > 0 ? fresh : candidates;
                RosterUnit pick = from[rng.Next(from.Count)];
                tried.Add(pick.Id);
                if (BuilderListEditing.AddUnit(book, list, pick.Id) < 0) { full.Add(pick.Id); continue; }
                int cost = Cost();
                if (cost > points || cost <= 0)
                {
                    list.Units.RemoveAt(list.Units.Count - 1);
                    full.Add(pick.Id);
                }
            }
            if (list.Units.Count == 0) return null;
            BuiltArmyFile built = ListCompiler.Compile(book, list);
            return built.Units.Count == 0 ? null : built;
        }

        /// <summary>The shop reply a bot sends for <paramref name="request"/> (nothing when it may not or cannot buy).</summary>
        public static HordeShopReply BotReply(HordeShopRequest request)
        {
            if (!request.BotsRecruit || request.PointsAvailable <= 0 || BookSource.Find is not { } find) return HordeShopReply.Nothing;
            BookFile? book;
            try { book = find(request.Faction, request.ArmyName, request.GameSystemSlug); }
            catch (Exception) { return HordeShopReply.Nothing; }
            if (book == null) return HordeShopReply.Nothing;
            var rng = new Random(HashCode.Combine(request.Wave, request.PointsAvailable, request.TargetPlayerID.ID.GetHashCode()));
            ArmyListFile? recruits = Pick(book, request.PointsAvailable, rng);
            if (recruits == null) return HordeShopReply.Nothing;
            string json = JsonSerializer.Serialize<ArmyListFile>(recruits, RuleJson.Options);
            int objective = request.SeizedObjectives.FirstOrDefault()?.Number ?? 0;
            return new HordeShopReply(json, recruits.Units.Select(_ => objective).ToList());
        }
    }
}
