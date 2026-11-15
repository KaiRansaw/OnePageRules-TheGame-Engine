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
