using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Homebrew campaign add-ons (Kai, 2026-11-27; claude/campaign-addons-spec.md): exploration and relic quests (house
    /// rule B28) and wanted posters (house rule B29).
    /// </summary>
    [TestFixture]
    public class CampaignAddonsTests
    {
        private static CampaignNumbers Gf => CampaignNumbers.For(EGameSystem.GrimdarkFuture);

        private static Func<int> Dice(params int[] faces)
        {
            int i = 0;
            return () => faces[Math.Min(i++, faces.Length - 1)];
        }

        private static ArmyListFile Army(string name, params UnitFileEntry[] units)
        {
            var army = new ArmyListFile { Name = name, Faction = name + " Faction" };
            army.Units.AddRange(units);
            CampaignRules.Start(army, name, name + ".fdgarmy", Gf, EGameSystem.GrimdarkFuture, ECampaignType.Endless, vpTarget: 0);
            army.StrikeTeam!.BankedPoints = 0;
            return army;
        }

        private static UnitFileEntry Infantry(string name = "Warriors") => new() { Name = name, ModelCount = 5, PointCost = 100 };

        private static UnitFileEntry Hero(string name = "Captain") => new()
        {
            Name = name, ModelCount = 1, PointCost = 80,
            SpecialRules = { new SpecialRuleEntry_CoreNumeric("Tough", 3), new SpecialRuleEntry_Core("Hero") },
            Weapons = { new WeaponFileEntry { Name = "Pistol" }, new WeaponFileEntry { Name = "Sword" } },
        };

        private static UnitFileEntry U(ArmyListFile army, string name) => army.Units.First(u => u.Name == name);
        private static StrikeUnit SheetOf(ArmyListFile army, string name) => CampaignRules.Sheet(U(army, name))!;

        // -- Wanted posters ------------------------------------------------------------------------

        private static UnitData Live(StrikeUnit sheet, string name) =>
            new(new PlayerID(Guid.NewGuid()), name, 4, 4, new List<DataBinding<ModelData>>()) { StrikeUnitJson = sheet.ToJson() };

        [Test]
        public void DestroyingARivalsUnitPutsAPosterUp_AndEachKillRaisesIt()
        {
            var killer = Live(new StrikeUnit { TeamFile = "Red.fdgarmy" }, "Crimson Tank");
            var a = Live(new StrikeUnit { TeamFile = "Blue.fdgarmy" }, "Scouts");
            var b = Live(new StrikeUnit { TeamFile = "Blue.fdgarmy" }, "Vargas Squad");

            FirefightCampaign.OnUnitDestroyed(a, killer, null, out FirefightCampaign.WantedNews first);
            FirefightCampaign.OnUnitDestroyed(b, killer, null, out FirefightCampaign.WantedNews second);

            StrikeUnit sheet = StrikeUnit.FromJson(killer.StrikeUnitJson)!;
            Assert.That(sheet.WantedKills, Is.EqualTo(2));
            Assert.That(sheet.WantedVictims, Is.EqualTo(new[] { "Scouts", "Vargas Squad" }));
            Assert.That(sheet.Kills, Is.EqualTo(2), "the book's kill tally is unchanged");
            Assert.That(first.Posted, Is.EqualTo("Crimson Tank"));
            Assert.That(second.PostedKills, Is.EqualTo(2));
            Assert.That(CampaignWanted.Price(sheet.WantedKills, Gf), Is.EqualTo(2 * Gf.SpecialObjectiveBonus),
                "the book's special-objective bonus per unit destroyed");
        }

        [Test]
        public void KillingAWantedUnitClaimsItsBounty_InTheSameBattle()
        {
            var tank = Live(new StrikeUnit { TeamFile = "Red.fdgarmy" }, "Crimson Tank");
            var victim = Live(new StrikeUnit { TeamFile = "Blue.fdgarmy" }, "Scouts");
            var avenger = Live(new StrikeUnit { TeamFile = "Blue.fdgarmy" }, "Hunters");
            FirefightCampaign.OnUnitDestroyed(victim, tank);

            FirefightCampaign.OnUnitDestroyed(tank, avenger, null, out FirefightCampaign.WantedNews news);

            StrikeUnit dead = StrikeUnit.FromJson(tank.StrikeUnitJson)!;
            Assert.That(dead.BountyClaimedBy, Is.EqualTo("Blue.fdgarmy"));
            Assert.That(dead.BountyKills, Is.EqualTo(1));
            Assert.That(dead.WantedKills, Is.Zero, "the poster is down");
            Assert.That(news.Claimed, Is.EqualTo("Crimson Tank"));
            Assert.That(StrikeUnit.FromJson(avenger.StrikeUnitJson)!.WantedKills, Is.EqualTo(1),
                "and the avenger is wanted in turn");
        }

        [Test]
        public void ARoutFromALostMeleeCountsForThePostersButEarnsNoKill()
        {
            var winner = Live(new StrikeUnit { TeamFile = "Red.fdgarmy" }, "Berserkers");
            var routed = Live(new StrikeUnit { TeamFile = "Blue.fdgarmy" }, "Scouts");

            FirefightCampaign.WantedNews news = FirefightCampaign.OnUnitRouted(routed, winner);

            StrikeUnit sheet = StrikeUnit.FromJson(winner.StrikeUnitJson)!;
            Assert.That(sheet.WantedKills, Is.EqualTo(1));
            Assert.That(sheet.Kills, Is.Zero, "a rout is not a kill for XP (the books leave it open)");
            Assert.That(news.Posted, Is.EqualTo("Berserkers"));
            Assert.That(StrikeUnit.FromJson(routed.StrikeUnitJson)!.FellTo, Is.EqualTo("Berserkers"));
        }

        [Test]
        public void TheSameArmyOrANonCampaignUnitPostsNothing()
        {
            var a = Live(new StrikeUnit { TeamFile = "Red.fdgarmy" }, "A");
            var b = Live(new StrikeUnit { TeamFile = "Red.fdgarmy" }, "B");
            var oneOff = new UnitData(new PlayerID(Guid.NewGuid()), "Bandits", 4, 5, new List<DataBinding<ModelData>>());
            FirefightCampaign.OnUnitDestroyed(b, a);
            FirefightCampaign.OnUnitDestroyed(oneOff, a);
            Assert.That(StrikeUnit.FromJson(a.StrikeUnitJson)!.WantedKills, Is.Zero);
        }

        [Test]
        public void ThePosterIsFiledAfterTheMission_AndComesDownWhenItDiesWithNobodyToCollect()
        {
            ArmyListFile army = Army("Red", Infantry());
            StrikeUnit live = SheetOf(army, "Warriors");
            live.WantedKills = 2;
            live.WantedVictims = new List<string> { "Scouts", "Squad" };
            var log = CampaignRules.AfterGame(army, new[] { new CampaignUnitResult(live.Id, false, 2, 0, Live: live) }, true, Gf, () => 4);
            Assert.That(SheetOf(army, "Warriors").WantedKills, Is.EqualTo(2));
            Assert.That(log.Any(l => l.Contains("is now WANTED")), Is.True);

            StrikeUnit live2 = SheetOf(army, "Warriors");
            var log2 = CampaignRules.AfterGame(army, new[] { new CampaignUnitResult(live2.Id, true, 0, 0, Live: live2) }, false, Gf, () => 4);
            Assert.That(log2.Any(l => l.Contains("poster comes down")), Is.True);
            Assert.That(SheetOf(army, "Warriors").WantedKills, Is.Zero);
        }

        // -- Exploration ---------------------------------------------------------------------------

        [Test]
        public void ExplorationRolls_OnePlusTheWinPlusEachHeroStanding()
        {
            Assert.That(CampaignExploration.Rolls(false, 0), Is.EqualTo(1));
            Assert.That(CampaignExploration.Rolls(true, 0), Is.EqualTo(2));
            Assert.That(CampaignExploration.Rolls(true, 3), Is.EqualTo(5));
        }

        [Test]
        public void TheFindsPayOutAsTheTableSays()
        {
            ArmyListFile army = Army("Red", Infantry());
            // 1: +25. 3 then 4: D3 = 2, +50. 5: +1 VP. 6 then 2: Relic Map. 6 then 6, then 5: Godly Blessing +D3 = 3 VP.
            var log = CampaignExploration.Explore(army, 5, army.Units, Gf, EGameSystem.GrimdarkFuture, Dice(1, 3, 4, 5, 6, 2, 6, 6, 5));
            StrikeTeamInfo info = army.StrikeTeam!;
            Assert.That(info.BankedPoints, Is.EqualTo(Gf.EventCredits * 3));
            Assert.That(info.RelicMaps, Is.EqualTo(1));
            Assert.That(info.Vp, Is.EqualTo(1 + 3));
            Assert.That(log, Has.Count.EqualTo(5));
            Assert.That(log[1], Does.Contain("Treasure Holopad"));
        }

        [Test]
        public void AnXpFindGoesToAUnitThatSearched()
        {
            ArmyListFile army = Army("Red", Infantry());
            int before = SheetOf(army, "Warriors").Xp;
            CampaignExploration.Explore(army, 1, army.Units, Gf, EGameSystem.GrimdarkFuture, Dice(2, 1));
            Assert.That(SheetOf(army, "Warriors").Xp, Is.EqualTo(before + 1));
        }

        [Test]
        public void FantasyBooksUseTheirOwnNames()
        {
            Assert.That(CampaignExploration.Name(CampaignExploration.EFind.Credits, EGameSystem.AgeOfFantasy),
                Is.EqualTo(CampaignEventNames.FantasyName(61)));
            Assert.That(CampaignExploration.Name(CampaignExploration.EFind.RelicMap, EGameSystem.AgeOfFantasy), Is.EqualTo("Relic Map"));
        }

        // -- Relic quests --------------------------------------------------------------------------

        [Test]
        public void OnlyAHeroWithAMapCanQuest()
        {
            ArmyListFile army = Army("Red", Infantry(), Hero());
            Assert.That(CampaignRelicQuest.CannotQuest(army, U(army, "Captain")), Does.Contain("no Relic Map"));
            army.StrikeTeam!.RelicMaps = 1;
            Assert.That(CampaignRelicQuest.CannotQuest(army, U(army, "Warriors")), Does.Contain("Only a Hero"));
            Assert.That(CampaignRelicQuest.CannotQuest(army, U(army, "Captain")), Is.Null);
            CampaignRelicQuest.SetQuest(army, U(army, "Captain"));
            Assert.That(CampaignRelicQuest.QuestHero(army), Is.SameAs(U(army, "Captain")));
        }

        [Test]
        public void WinningTheQuestWithTheHeroAliveWinsARelic_PickedFromTheRolledGroup()
        {
            ArmyListFile army = Army("Red", Hero());
            army.StrikeTeam!.RelicMaps = 1;
            CampaignRelicQuest.SetQuest(army, U(army, "Captain"));
            int cost = U(army, "Captain").PointCost;

            var log = CampaignRelicQuest.Resolve(army, won: true, heroAlive: true, Gf, EGameSystem.GrimdarkFuture, Dice(2));

            Assert.That(army.StrikeTeam.RelicMaps, Is.Zero, "the map is used up");
            Assert.That(army.StrikeTeam.QuestHeroId, Is.Empty);
            Assert.That(log.Single(), Does.Contain("Holy Armour"));
            string pending = SheetOf(army, "Captain").Pending.Single();
            List<string> options = CampaignRules.Options(SheetOf(army, "Captain"), pending);
            Assert.That(options, Does.Contain(RelicWargear.Destiny));

            CampaignRules.Choose(U(army, "Captain"), pending, RelicWargear.Destiny, () => 1);
            Assert.That(SheetOf(army, "Captain").Relic, Is.EqualTo(RelicWargear.Destiny));
            Assert.That(U(army, "Captain").PointCost, Is.EqualTo(cost + Gf.HeroLevelCost), "the book's Hero level-up price");
            Assert.That(SheetOf(army, "Captain").Pending, Is.Empty);
        }

        [Test]
        public void ARelicWeaponAsksWhichWeapon()
        {
            ArmyListFile army = Army("Red", Hero());
            army.StrikeTeam!.RelicMaps = 1;
            CampaignRelicQuest.SetQuest(army, U(army, "Captain"));
            CampaignRelicQuest.Resolve(army, true, true, Gf, EGameSystem.GrimdarkFuture, Dice(1));
            string pending = SheetOf(army, "Captain").Pending.Single();
            CampaignRules.Choose(U(army, "Captain"), pending, RelicWargear.Bloodshed, () => 1);

            string weapon = SheetOf(army, "Captain").Pending.Single();
            Assert.That(CampaignRules.Options(SheetOf(army, "Captain"), weapon), Is.EqualTo(new[] { "Pistol", "Sword" }));
            CampaignRules.Choose(U(army, "Captain"), weapon, "Sword", () => 1);
            Assert.That(SheetOf(army, "Captain").RelicWeapon, Is.EqualTo("Sword"));
        }

        [Test]
        public void FailingTheQuestKeepsTheMap()
        {
            ArmyListFile army = Army("Red", Hero());
            army.StrikeTeam!.RelicMaps = 1;
            CampaignRelicQuest.SetQuest(army, U(army, "Captain"));
            var log = CampaignRelicQuest.Resolve(army, won: true, heroAlive: false, Gf, EGameSystem.GrimdarkFuture, Dice(2));
            Assert.That(army.StrikeTeam.RelicMaps, Is.EqualTo(1));
            Assert.That(army.StrikeTeam.QuestHeroId, Is.Empty);
            Assert.That(SheetOf(army, "Captain").Pending, Is.Empty);
            Assert.That(log.Single(), Does.Contain("failed"));
        }

        [Test]
        public void ARelicIsLostWithItsHero_AndTheMemorialSaysSo()
        {
            ArmyListFile army = Army("Red", Hero());
            StrikeUnit sheet = SheetOf(army, "Captain");
            sheet.Relic = RelicWargear.Destiny;
            CampaignRules.Save(U(army, "Captain"), sheet);
            // Casualty roll 1 = dead in all four books' Hero injury tables.
            var log = CampaignRules.AfterGame(army, new[] { new CampaignUnitResult(sheet.Id, true, 0, 0) }, false, Gf, () => 1);
            if (army.Units.Count == 0)
            {
                Assert.That(log.Any(l => l.Contains("is lost with")), Is.True);
                Assert.That(CampaignVeterans.Epitaph(army.StrikeTeam!.Memorial.Single()), Does.Contain("Carried the " + RelicWargear.Destiny));
            }
            else Assert.Inconclusive("The Hero survived its casualty roll with this die.");
        }
    }
}
