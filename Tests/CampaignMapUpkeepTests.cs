using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Campaign.Map;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #467 step 2 - rivals on a campaign map run their own books between turns (Kai, 2026-11-14: "rivals run
    /// themselves"), keeping back enough to buy land while there is land to buy.
    /// </summary>
    [TestFixture]
    public class CampaignMapUpkeepTests
    {
        private static readonly CampaignNumbers GF = CampaignNumbers.GrimdarkFuture;

        private static BookFile Book() => new()
        {
            Name = "Test Legion",
            Faction = "Test Legion",
            Units =
            {
                new RosterUnit
                {
                    Id = "trooper", Name = "Troopers", Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
                new RosterUnit
                {
                    Id = "captain", Name = "Captain", Quality = 3, Defense = 4,
                    BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 50,
                    Weapons = { new WeaponFileEntry { Name = "Blade", Attacks = 3 } },
                    Rules = { new SpecialRuleEntry_Core("Hero") },
                },
            },
        };

        private static BuiltArmyFile Rival(int treasury, BookFile book)
        {
            var list = new BuilderList { Name = "Rival", BookName = book.Name };
            list.Units.Add(new BuilderUnit { RosterUnitId = "trooper", ModelCount = 5 });
            list.Units.Add(new BuilderUnit { RosterUnitId = "captain", ModelCount = 1 });
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            CampaignRules.Start(army, "Rival", "rival.fdgarmy", GF, EGameSystem.GrimdarkFuture, ECampaignType.Endless, isBot: true);
            army.StrikeTeam!.BankedPoints = treasury;
            return army;
        }

        private static CampaignMap Map()
        {
            var factions = new List<MapFaction>
            {
                new() { Id = CampaignMap.PlayerId, Name = "You", IsPlayer = true },
                new() { Id = "r1", Name = "Rival", Colour = 1 },
            };
            return CampaignMapGenerator.Generate(EGameSystem.GrimdarkFuture, EMapSize.Medium, factions, 4);
        }

        private static Func<int> Threes() => () => 3;

        [Test]
        public void Apply_AddsTheTurnsChange_NeverBelowZero()
        {
            var info = new StrikeTeamInfo { BankedPoints = 100, Vp = 2 };
            CampaignMapUpkeep.Apply(info, new MapArmyChange { Treasury = 150, MissionVp = 2 });
            Assert.That(info.BankedPoints, Is.EqualTo(250));
            Assert.That(info.Vp, Is.EqualTo(4));
            CampaignMapUpkeep.Apply(info, new MapArmyChange { Treasury = -400 });
            Assert.That(info.BankedPoints, Is.EqualTo(0));
        }

        [Test]
        public void LandReserve_IsTheNextClaimsPrice_WhileThereIsLandToBuy()
        {
            CampaignMap map = Map();
            int cost = CampaignMapRules.ClaimCost(map, "r1", GF);
            Assert.That(CampaignMapUpkeep.LandReserve(map, "r1", 1000, GF), Is.EqualTo(cost));
            Assert.That(CampaignMapUpkeep.LandReserve(map, "r1", 50, GF), Is.EqualTo(50), "never more than it has");
            foreach (MapRegion r in map.Regions.Where(r => r.Owner == null)) r.Owner = CampaignMap.PlayerId;
            Assert.That(CampaignMapUpkeep.LandReserve(map, "r1", 1000, GF), Is.EqualTo(0), "no neutral land left");
        }

        [Test]
        public void ARival_SpendsAllButItsLandReserve()
        {
            CampaignMap map = Map();
            BookFile book = Book();
            BuiltArmyFile army = Rival(600, book);
            int cost = CampaignMapRules.ClaimCost(map, "r1", GF);
            int unitsBefore = army.Units.Count;

            List<string> log = CampaignMapUpkeep.RivalBetweenTurns(map, "r1", army, book, GF, biggestOtherArmy: 2000, Threes());

            Assert.That(army.Units.Count, Is.GreaterThan(unitsBefore), "it recruited");
            Assert.That(army.StrikeTeam!.BankedPoints, Is.GreaterThanOrEqualTo(cost), "the land money was kept back");
            Assert.That(army.StrikeTeam.BankedPoints, Is.LessThan(cost + 100), "and the rest spent (its cheapest regular unit is 100)");
            Assert.That(log.Last(), Does.Contain("buy land"));
        }

        [Test]
        public void ThePlayersArmy_IsNeverSpentForThem()
        {
            CampaignMap map = Map();
            BookFile book = Book();
            BuiltArmyFile army = Rival(600, book);
            List<string> log = CampaignMapUpkeep.RivalBetweenTurns(map, CampaignMap.PlayerId, army, book, GF, 2000, Threes());
            Assert.That(log, Is.Empty);
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(600));
        }

        [Test]
        public void AKnockedOutRival_DoesNothing()
        {
            CampaignMap map = Map();
            map.Faction("r1")!.Alive = false;
            BookFile book = Book();
            BuiltArmyFile army = Rival(600, book);
            Assert.That(CampaignMapUpkeep.RivalBetweenTurns(map, "r1", army, book, GF, 2000, Threes()), Is.Empty);
        }
    }
}

namespace FDG.Tests
{
    /// <summary>#467 step 3 - a rival's starting army, built for you from a random book.</summary>
    [TestFixture]
    public class MapRivalArmyTests
    {
        private static BookFile Book() => new()
        {
            Name = "Test Legion",
            Faction = "Test Legion",
            Units =
            {
                new RosterUnit { Id = "a", Name = "Troopers", Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } } },
                new RosterUnit { Id = "b", Name = "Scouts", Quality = 4, Defense = 5, BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 60,
                    Weapons = { new WeaponFileEntry { Name = "Carbine", RangeInches = 18, Attacks = 1 } } },
                new RosterUnit { Id = "c", Name = "Heavies", Quality = 4, Defense = 3, BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 150,
                    Weapons = { new WeaponFileEntry { Name = "Cannon", RangeInches = 30, Attacks = 2 } } },
                new RosterUnit { Id = "h", Name = "Captain", Quality = 3, Defense = 4, BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 50,
                    Weapons = { new WeaponFileEntry { Name = "Blade", Attacks = 3 } }, Rules = { new SpecialRuleEntry_Core("Hero") } },
                new RosterUnit { Id = "h2", Name = "Major", Quality = 3, Defense = 3, BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 80,
                    Weapons = { new WeaponFileEntry { Name = "Sword", Attacks = 4 } }, Rules = { new SpecialRuleEntry_Core("Hero") } },
            },
        };

        [Test]
        public void ARival_IsALegalStartingArmy_WithOneHero_AndVariety()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            for (int seed = 0; seed < 40; seed++)
            {
                var built = MapRivalArmy.Build(EGameSystem.GrimdarkFuture, new[] { Book() }, new Random(seed));
                Assert.That(built, Is.Not.Null);
                var army = built!.Value.Army;
                Assert.That(CampaignRules.CannotStart(army, n), Is.Null, $"seed {seed}");
                Assert.That(army.TotalPoints, Is.InRange(600, 750));
                Assert.That(army.Units.Count(u => u.Name is "Captain" or "Major"), Is.EqualTo(1), "exactly one Hero");
                Assert.That(army.Units.Select(u => u.Name).Distinct().Count(), Is.GreaterThanOrEqualTo(3), "not all one unit");
                Assert.That(army.StrikeTeam!.IsBot, Is.True);
                Assert.That(army.Book, Is.Not.Null, "it keeps its book, to recruit from later");
            }
        }

        [Test]
        public void Names_DontRepeat_WhileThereAreFreeOnes()
        {
            var rng = new Random(1);
            var taken = new List<string>();
            for (int i = 0; i < 10; i++) taken.Add(MapRivalArmy.PickName(EMapStyle.StarSystem, rng, taken));
            Assert.That(taken.Distinct().Count(), Is.EqualTo(10));
        }
    }
}
