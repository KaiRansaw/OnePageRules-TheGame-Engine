using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Campaign;
using FDG.SaveLoad;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Homebrew (Kai, 2026-11-27): named veterans + memorial (house rule B26) and salvage (house rule B27).
    /// </summary>
    [TestFixture]
    public class CampaignVeteransAndSalvageTests
    {
        private static CampaignNumbers Gf => CampaignNumbers.For(EGameSystem.GrimdarkFuture);

        [SetUp]
        public void SeedNicknames() => CampaignVeterans.Rng = new Random(7);

        private static ArmyListFile Army(string name, params UnitFileEntry[] units)
        {
            var army = new ArmyListFile { Name = name, Faction = name + " Faction" };
            army.Units.AddRange(units);
            CampaignRules.Start(army, name, name + ".fdgarmy", Gf, EGameSystem.GrimdarkFuture,
                ECampaignType.Endless, vpTarget: 0);
            army.StrikeTeam!.BankedPoints = 500;
            return army;
        }

        private static UnitFileEntry Infantry(string name = "Warriors") =>
            new() { Name = name, ModelCount = 5, PointCost = 100 };

        private static UnitFileEntry Tank(string name = "Battle Tank", int tough = 9, int cost = 205) => new()
        {
            Name = name, ModelCount = 1, PointCost = cost,
            SpecialRules = { new SpecialRuleEntry_CoreNumeric("Tough", tough), new SpecialRuleEntry_Core("Fearless") },
        };

        private static UnitFileEntry HeroOnMonster() => new()
        {
            Name = "Beast Lord", ModelCount = 1, PointCost = 300,
            SpecialRules = { new SpecialRuleEntry_CoreNumeric("Tough", 12), new SpecialRuleEntry_Core("Hero") },
        };

        private static StrikeUnit SheetOf(ArmyListFile army, string name) =>
            CampaignRules.Sheet(army.Units.First(u => u.Name == name))!;

        private static CampaignUnitResult Result(ArmyListFile army, string name, bool casualty, int kills = 0,
                                                 int heroKills = 0, string? fellTo = null) =>
            new(SheetOf(army, name).Id, casualty, kills, heroKills, fellTo);

        // -- Service record ----------------------------------------------------------------------

        [Test]
        public void KillsAndSurvivalsAddUpAcrossMissions()
        {
            ArmyListFile army = Army("Reavers", Infantry());
            CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", false, kills: 2, heroKills: 1) }, true, Gf, () => 4);
            CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", false, kills: 1) }, true, Gf, () => 4);

            StrikeUnit sheet = SheetOf(army, "Warriors");
            Assert.That(sheet.CareerKills, Is.EqualTo(3));
            Assert.That(sheet.CareerHeroKills, Is.EqualTo(1));
            Assert.That(sheet.MissionsSurvived, Is.EqualTo(2));
            Assert.That(sheet.Kills, Is.Zero, "the per-game tally is still cleared");
        }

        [Test]
        public void ACasualtyThatRecoversIsCountedAsRecoveredNotSurvived()
        {
            ArmyListFile army = Army("Reavers", Infantry());
            CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", true) }, true, Gf, () => 3);

            StrikeUnit sheet = SheetOf(army, "Warriors");
            Assert.That(sheet.TimesRecovered, Is.EqualTo(1));
            Assert.That(sheet.MissionsSurvived, Is.Zero);
        }

        [Test]
        public void TheServiceRecordDoesNotTakeTheCampaignsDice()
        {
            // A suggestion must never shift the casualty or trait rolls: count the d6 calls with and without a level-up.
            int Calls(int xpBefore)
            {
                ArmyListFile army = Army("Reavers", Infantry());
                StrikeUnit s = SheetOf(army, "Warriors");
                s.Xp = xpBefore;
                CampaignRules.Save(army.Units[0], s);
                int calls = 0;
                CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", false) }, true, Gf, () => { calls++; return 2; });
                return calls;
            }
            Assert.That(Calls(4), Is.EqualTo(1), "a level-up rolls exactly one trait (Headstrong on a 2) and nothing else");
        }

        // -- Nicknames ---------------------------------------------------------------------------

        [Test]
        public void ALevelUpSuggestsANicknameButDoesNotApplyIt()
        {
            ArmyListFile army = Army("Reavers", Infantry());
            StrikeUnit s = SheetOf(army, "Warriors");
            s.Xp = 4;
            CampaignRules.Save(army.Units[0], s);

            List<string> log = CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", false) }, true, Gf, () => 2);

            StrikeUnit sheet = SheetOf(army, "Warriors");
            Assert.That(sheet.NicknameSuggestion, Is.Not.Empty);
            Assert.That(sheet.Nickname, Is.Empty, "Kai: the game suggests, he decides");
            Assert.That(log.Any(l => l.Contains(sheet.NicknameSuggestion)), Is.True);
        }

        [Test]
        public void TheSuggestionFollowsWhatTheUnitDid()
        {
            var shooter = new StrikeUnit { CareerKills = 4, Traits = { HordeExperience.ShootingSpecialist } };
            var survivor = new StrikeUnit { TimesRecovered = 3 };
            var hunter = new StrikeUnit { CareerHeroKills = 2 };
            for (int i = 0; i < 20; i++)
            {
                Assert.That(CampaignVeterans.ShootingNames.Concat(CampaignVeterans.PlainNames)
                    .Concat(CampaignVeterans.SlayerNames), Does.Contain(CampaignVeterans.Suggest(shooter, new Random(i))));
                Assert.That(CampaignVeterans.UnkillableNames.Concat(CampaignVeterans.PlainNames),
                    Does.Contain(CampaignVeterans.Suggest(survivor, new Random(i))));
                Assert.That(CampaignVeterans.KingslayerNames.Concat(CampaignVeterans.PlainNames),
                    Does.Contain(CampaignVeterans.Suggest(hunter, new Random(i))));
            }
        }

        [Test]
        public void ARerollNeverGivesTheSameSuggestionBack()
        {
            ArmyListFile army = Army("Reavers", Infantry());
            UnitFileEntry unit = army.Units[0];
            string first = CampaignVeterans.Reroll(unit, new Random(1));
            for (int i = 0; i < 30; i++)
            {
                string next = CampaignVeterans.Reroll(unit, new Random(i));
                Assert.That(next, Is.Not.EqualTo(first));
                first = next;
            }
        }

        [Test]
        public void ANicknameIsCleanedAndAnswersTheSuggestion()
        {
            ArmyListFile army = Army("Reavers", Infantry());
            UnitFileEntry unit = army.Units[0];
            CampaignVeterans.Reroll(unit, new Random(3));

            CampaignVeterans.SetNickname(unit, "  the \"Iron\"   Wall  ");

            StrikeUnit sheet = CampaignRules.Sheet(unit)!;
            Assert.That(sheet.Nickname, Is.EqualTo("the Iron Wall"));
            Assert.That(sheet.NicknameSuggestion, Is.Empty);
            Assert.That(CampaignVeterans.Title(unit), Is.EqualTo("Warriors \"the Iron Wall\""));

            CampaignVeterans.SetNickname(unit, "   ");
            Assert.That(CampaignRules.Sheet(unit)!.Nickname, Is.Empty);
        }

        // -- Memorial ----------------------------------------------------------------------------

        [Test]
        public void AUnitThatDiesGoesOnTheMemorialWithItsRecord()
        {
            ArmyListFile army = Army("Reavers", Infantry(), Infantry("Scouts"));
            CampaignVeterans.SetNickname(army.Units[0], "Old Guard");
            CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", false, kills: 3) }, true, Gf, () => 4);

            CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", true, kills: 1, fellTo: "Hive Tyrant") },
                false, Gf, () => 1);

            Assert.That(army.Units.Select(u => u.Name), Does.Not.Contain("Warriors"));
            FallenUnit f = army.StrikeTeam!.Memorial.Single();
            Assert.That(f.Title, Is.EqualTo("Warriors \"Old Guard\""));
            Assert.That(f.Kills, Is.EqualTo(4), "kills from the mission it fell in count too");
            Assert.That(f.Missions, Is.EqualTo(2));
            Assert.That(f.Mission, Is.EqualTo(2));
            Assert.That(f.FellTo, Is.EqualTo("Hive Tyrant"));
            Assert.That(CampaignVeterans.Epitaph(f), Does.Contain("destroyed by Hive Tyrant"));
        }

        [Test]
        public void NoPermadeathPutsNobodyOnTheMemorial()
        {
            ArmyListFile army = Army("Reavers", Infantry());
            army.StrikeTeam!.NoPermadeath = true;
            CampaignRules.AfterGame(army, new[] { Result(army, "Warriors", true) }, false, Gf, () => 1);

            Assert.That(army.StrikeTeam.Memorial, Is.Empty);
            Assert.That(SheetOf(army, "Warriors").TimesRecovered, Is.EqualTo(1));
        }

        [Test]
        public void TheDeadAreHandedBackForSalvage()
        {
            ArmyListFile army = Army("Reavers", Tank());
            var dead = new List<UnitFileEntry>();
            CampaignRules.AfterGame(army, new[] { Result(army, "Battle Tank", true) }, false, Gf, () => 1, dead);
            Assert.That(dead.Select(u => u.Name), Is.EqualTo(new[] { "Battle Tank" }));
        }

        // -- Salvage -----------------------------------------------------------------------------

        [Test]
        public void OnlyVehiclesAndMonstersCanBeSalvaged()
        {
            Assert.That(CampaignSalvage.IsSalvageable(Tank(), EGameSystem.GrimdarkFuture), Is.True);
            Assert.That(CampaignSalvage.IsSalvageable(Tank(tough: 6), EGameSystem.GrimdarkFuture), Is.True, "light vehicles count");
            Assert.That(CampaignSalvage.IsSalvageable(Tank(tough: 3), EGameSystem.GrimdarkFuture), Is.False, "Tough(3) is infantry-sized");
            Assert.That(CampaignSalvage.IsSalvageable(Infantry(), EGameSystem.GrimdarkFuture), Is.False);
            Assert.That(CampaignSalvage.IsSalvageable(HeroOnMonster(), EGameSystem.GrimdarkFuture), Is.False, "no Heroes");
            Assert.That(CampaignSalvage.IsSalvageable(Tank(tough: 9), EGameSystem.GFFirefight), Is.False, "Firefight triples the Tough line");
            Assert.That(CampaignSalvage.IsSalvageable(Tank(tough: 18), EGameSystem.GFFirefight), Is.True);
            var flyer = new UnitFileEntry { Name = "Jet", ModelCount = 2, SpecialRules = { new SpecialRuleEntry_Core("Aircraft") } };
            Assert.That(CampaignSalvage.IsSalvageable(flyer, EGameSystem.GrimdarkFuture), Is.True, "aircraft are vehicles");
        }

        [Test]
        public void ThePriceIsHalfRoundedUpToFive()
        {
            Assert.That(CampaignSalvage.PriceFor(205), Is.EqualTo(105));
            Assert.That(CampaignSalvage.PriceFor(200), Is.EqualTo(100));
            Assert.That(CampaignSalvage.PriceFor(210), Is.EqualTo(105));
            Assert.That(CampaignSalvage.PriceFor(0), Is.EqualTo(5));
        }

        private static (ArmyListFile Winner, ArmyListFile Loser, SalvageOffer Offer) Fight()
        {
            ArmyListFile loser = Army("Hive", Tank("Carnifex", cost: 205));
            // The dead tank had levelled up and picked up a trait - all of it has to come off.
            UnitFileEntry tank = loser.Units[0];
            StrikeUnit s = CampaignRules.Sheet(tank)!;
            s.Xp = 4;
            CampaignRules.Save(tank, s);
            CampaignRules.AfterGame(loser, new[] { Result(loser, "Carnifex", false) }, true, Gf, () => 4); // Resilient
            Assert.That(tank.PointCost, Is.EqualTo(205 + Gf.RegularLevelCost));
            Assert.That(tank.SpecialRules.OfType<SpecialRuleEntry_Core>().Any(r => r.Name == HordeExperience.Resilient));

            var dead = new List<UnitFileEntry>();
            CampaignRules.AfterGame(loser, new[] { Result(loser, "Carnifex", true) }, false, Gf, () => 1, dead);

            ArmyListFile winner = Army("Reavers", Infantry());
            CampaignRules.AfterGame(winner, new[] { Result(winner, "Warriors", false) }, true, Gf, () => 4);
            List<SalvageOffer> offers = CampaignSalvage.OffersFrom(loser, dead, EGameSystem.GrimdarkFuture, winner.StrikeTeam!.Matches);
            winner.StrikeTeam.Salvage.AddRange(offers);
            return (winner, loser, offers.Single());
        }

        [Test]
        public void ASalvagedUnitArrivesWithoutItsCampaignChanges()
        {
            (ArmyListFile winner, _, SalvageOffer offer) = Fight();
            Assert.That(offer.Price, Is.EqualTo(105), "half of the book's 205, not of the levelled-up cost");
            int bank = winner.StrikeTeam!.BankedPoints;

            CampaignPurchase bought = CampaignSalvage.Claim(winner, offer);

            Assert.That(bought.Ok, Is.True, bought.Message);
            UnitFileEntry unit = winner.Units.Single(u => u.Name == "Salvaged Carnifex");
            Assert.That(unit.PointCost, Is.EqualTo(205));
            Assert.That(unit.SpecialRules.OfType<SpecialRuleEntry_Core>().Any(r => r.Name == HordeExperience.Resilient), Is.False);
            StrikeUnit sheet = CampaignRules.Sheet(unit)!;
            Assert.That(sheet.Xp, Is.Zero);
            Assert.That(sheet.SalvagedFrom, Is.EqualTo("Hive"));
            Assert.That(sheet.UnitType, Is.EqualTo("Carnifex"));
            Assert.That(winner.StrikeTeam.BankedPoints, Is.EqualTo(bank - 105));
        }

        [Test]
        public void OnlyOneSalvagePerMission()
        {
            (ArmyListFile winner, _, SalvageOffer offer) = Fight();
            var second = new SalvageOffer { Name = "x", UnitType = "Carnifex", Price = 5, UnitJson = offer.UnitJson, RulesJson = "[]", SpellsJson = "[]" };
            winner.StrikeTeam!.Salvage.Add(second);

            Assert.That(CampaignSalvage.Claim(winner, offer).Ok, Is.True);
            Assert.That(CampaignSalvage.CannotClaim(winner, second), Does.Contain("one salvaged unit"));
        }

        [Test]
        public void SalvageNeedsTheTreasury()
        {
            (ArmyListFile winner, _, SalvageOffer offer) = Fight();
            winner.StrikeTeam!.BankedPoints = 100;
            Assert.That(CampaignSalvage.Claim(winner, offer).Ok, Is.False);
            Assert.That(winner.Units.Any(u => u.Name.StartsWith("Salvaged")), Is.False);
        }

        [Test]
        public void UnclaimedSalvageIsGoneAfterTheNextMission()
        {
            (ArmyListFile winner, _, _) = Fight();
            List<string> log = CampaignRules.AfterGame(winner, new[] { Result(winner, "Warriors", false) }, true, Gf, () => 4);
            Assert.That(winner.StrikeTeam!.Salvage, Is.Empty);
            Assert.That(log.Any(l => l.StartsWith("Salvage left on the field")), Is.True);
        }
    }
}
