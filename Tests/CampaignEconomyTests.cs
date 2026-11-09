using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #410 - steps 5 and 6 of the campaign loop (Upgrade Armies, Recruit Units) plus disbanding, against
    /// Campaign Rules v3.6.0. Prices come from re-compiling a unit against a book, so these run on a small
    /// hand-built book rather than a bundled one.
    /// </summary>
    [TestFixture]
    public class CampaignEconomyTests
    {
        private const string TrooperId = "trooper";
        private const string CaptainId = "captain";

        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        /// <summary>A two-entry book: a 100pt squad with a 20pt gun upgrade, and a 50pt Hero.</summary>
        private static BookFile Book() => new()
        {
            Name = "Test Legion",
            Faction = "Test Legion",
            Units =
            {
                new RosterUnit
                {
                    Id = TrooperId, Name = "Troopers",
                    Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5,
                    BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "gun", Label = "Upgrade one model's weapon",
                            Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "heavy", Label = "Heavy Rifle", Cost = 20,
                                    WeaponsGained = { new WeaponFileEntry { Name = "Heavy Rifle", RangeInches = 30, Attacks = 2 } },
                                },
                                new UpgradeOption
                                {
                                    Id = "launcher", Label = "Launcher", Cost = 45,
                                    WeaponsGained = { new WeaponFileEntry { Name = "Launcher", RangeInches = 36, Attacks = 1 } },
                                },
                            },
                        },
                    },
                },
                new RosterUnit
                {
                    Id = CaptainId, Name = "Captain",
                    Quality = 3, Defense = 4,
                    BaseModelCount = 1, MinModels = 1, MaxModels = 1,
                    BasePointCost = 50,
                    Weapons = { new WeaponFileEntry { Name = "Blade", Attacks = 3 } },
                    Rules = { new SpecialRuleEntry_Core("Hero"), new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                },
            },
        };

        private static BuilderUnit Plain(string rosterId, int models) =>
            new() { RosterUnitId = rosterId, ModelCount = models };

        private static BuilderUnit WithChoice(string rosterId, int models, string sectionId, string optionId) =>
            new()
            {
                RosterUnitId = rosterId, ModelCount = models,
                Choices = { new UpgradeChoice { SectionId = sectionId, OptionId = optionId, Count = 1 } },
            };

        /// <summary>A campaign army of one 100pt squad, built from the book so it carries selections.</summary>
        private static (BuiltArmyFile Army, BookFile Book) Started(int treasuryLeft = 0)
        {
            BookFile book = Book();
            var list = new BuilderList { Name = "Test", BookName = book.Name };
            list.Units.Add(Plain(TrooperId, 5));
            BuiltArmyFile army = ListCompiler.Compile(book, list);

            CampaignNumbers n = CampaignNumbers.Firefight;
            CampaignRules.Start(army, "Test", "test.fdgarmy", n, EGameSystem.GFFirefight);
            army.StrikeTeam!.BankedPoints = treasuryLeft;
            return (army, book);
        }

        private static UnitFileEntry Troopers(ArmyListFile army) => army.Units.First(u => u.Name.StartsWith("Troopers"));

        // -- Loadouts carry over from the Army Forge --------------------------------------------------

        [Test]
        public void StartingACampaignKeepsEachUnitsUpgradeSelections()
        {
            (BuiltArmyFile army, _) = Started();
            BuilderUnit? loadout = CampaignEconomy.Loadout(Troopers(army));
            Assert.That(loadout, Is.Not.Null, "a unit built from a book must stay upgradeable");
            Assert.That(loadout!.RosterUnitId, Is.EqualTo(TrooperId));
            Assert.That(loadout.ModelCount, Is.EqualTo(5));
        }

        [Test]
        public void AUnitWithNoLoadoutCannotBeUpgraded()
        {
            var army = new BuiltArmyFile { Name = "Hand-written", Faction = "Test", GameSystem = GameSystems.GrimdarkFuture };
            army.Units.Add(new UnitFileEntry { Name = "Mob", ModelCount = 5, Quality = 5, Defense = 5, PointCost = 100 });
            CampaignRules.Start(army, "Hand-written", "x", CampaignNumbers.Firefight, EGameSystem.GFFirefight);
            UnitFileEntry mob = army.Units[0];
            Assert.That(CampaignEconomy.Loadout(mob), Is.Null);
            CampaignPurchase result = CampaignEconomy.ChangeUpgrades(army, mob, Book(), Plain(TrooperId, 5));
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Message, Does.Contain("wasn't built from a book"));
        }

        // -- Buying and selling upgrades --------------------------------------------------------------

        [Test]
        public void BuyingAnUpgradeChargesTheDifferenceAndChangesTheUnit()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 100);
            UnitFileEntry unit = Troopers(army);
            int before = unit.PointCost;

            CampaignPurchase result = CampaignEconomy.ChangeUpgrades(army, unit, book,
                WithChoice(TrooperId, 5, "gun", "heavy"));

            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(result.Points, Is.EqualTo(20));
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(80));
            Assert.That(unit.PointCost, Is.EqualTo(before + 20));
            Assert.That(unit.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.True);
        }

        [Test]
        public void SellingBackRefundsTheDifference()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 100);
            UnitFileEntry unit = Troopers(army);
            CampaignEconomy.ChangeUpgrades(army, unit, book, WithChoice(TrooperId, 5, "gun", "heavy"));
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(80));

            CampaignPurchase back = CampaignEconomy.ChangeUpgrades(army, unit, book, Plain(TrooperId, 5));
            Assert.That(back.Ok, Is.True, back.Message);
            Assert.That(back.Points, Is.EqualTo(-20));
            Assert.That(army.StrikeTeam.BankedPoints, Is.EqualTo(100));
            Assert.That(unit.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.False);
        }

        [Test]
        public void AnUpgradeYouCannotAffordIsRefused()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 10);
            CampaignPurchase result = CampaignEconomy.ChangeUpgrades(army, Troopers(army), book,
                WithChoice(TrooperId, 5, "gun", "launcher"));
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Message, Does.Contain("45").And.Contain("10"));
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(10), "nothing is spent on a refusal");
        }

        /// <summary>
        /// The whole reason a campaign unit keeps its own loadout instead of sharing the army's builder
        /// list: re-compiling it must not throw away what the campaign gave it.
        /// </summary>
        [Test]
        public void UpgradingKeepsTheUnitsCampaignChanges()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 100);
            UnitFileEntry unit = Troopers(army);
            StrikeUnit sheet = CampaignRules.Sheet(unit)!;

            // Two levels' worth of campaign history: a trait, its cost, and a hero-style talent.
            CampaignRules.AddXp(unit, sheet, 5, CampaignNumbers.Firefight, Dice(1));
            sheet.Talents.Add("Toughened");
            CampaignRules.AddRule(unit, "Talent: Toughened");
            CampaignRules.Save(unit, sheet);
            int costWithHistory = unit.PointCost;
            string name = unit.Name;

            CampaignEconomy.ChangeUpgrades(army, unit, book, WithChoice(TrooperId, 5, "gun", "heavy"));

            sheet = CampaignRules.Sheet(unit)!;
            Assert.Multiple(() =>
            {
                Assert.That(unit.Name, Is.EqualTo(name), "the unit keeps its campaign name");
                Assert.That(sheet.Xp, Is.EqualTo(5), "and its XP");
                Assert.That(unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core { Name: "Trait: Agile" }), Is.True,
                    "and the rule its trait granted");
                Assert.That(unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core { Name: "Talent: Toughened" }), Is.True);
                Assert.That(unit.SpecialRules.Any(r => r is SpecialRuleEntry_CoreNumeric { Name: "Tough", NumericValue: 4 }), Is.True,
                    "Toughened's Tough(+3) is put back on a unit the book gives no Tough");
                Assert.That(unit.PointCost, Is.EqualTo(costWithHistory + 20),
                    "the level's points survive, and the upgrade is added on top");
                Assert.That(unit.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.True, "and it actually got the gun");
            });
        }

        [Test]
        public void SellValueIsHalfRoundedDownToAFive()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CampaignEconomy.SellValue(20), Is.EqualTo(10));
                Assert.That(CampaignEconomy.SellValue(45), Is.EqualTo(20), "22 rounds down to 20");
                Assert.That(CampaignEconomy.SellValue(30), Is.EqualTo(15));
                Assert.That(CampaignEconomy.SellValue(5), Is.EqualTo(5), "the book's 5pt special case");
                Assert.That(CampaignEconomy.SellValue(0), Is.EqualTo(0));
            });
            Assert.That(CampaignEconomy.SellsFivePointItem(Dice(3)), Is.False, "a 5pt item needs 4+");
            Assert.That(CampaignEconomy.SellsFivePointItem(Dice(4)), Is.True);
        }

        // -- Recruiting -------------------------------------------------------------------------------

        [Test]
        public void RecruitingAddsAUnitPaysForItAndGivesItASheet()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 200);
            CampaignPurchase result = CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), CampaignNumbers.Firefight);

            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(result.Points, Is.EqualTo(100));
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(100));
            Assert.That(army.Units.Count, Is.EqualTo(2));
            UnitFileEntry recruit = army.Units[1];
            Assert.That(recruit.Name, Is.EqualTo("Troopers 2"), "names stay unique so the sheets can be told apart");
            Assert.That(CampaignRules.Sheet(recruit), Is.Not.Null);
            Assert.That(CampaignEconomy.Loadout(recruit), Is.Not.Null, "a recruit is upgradeable straight away");
        }

        [Test]
        public void AVeteranRecruitCostsTheSurchargeAndArrivesAtFiveXp()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 200);
            CampaignNumbers n = CampaignNumbers.Firefight;
            CampaignPurchase result = CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n,
                veteran: true, d6: Dice(1));

            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(result.Points, Is.EqualTo(100 + n.VeteranRecruitCost), "100 for the unit, 30 for the 5 XP");
            UnitFileEntry recruit = army.Units[1];
            StrikeUnit sheet = CampaignRules.Sheet(recruit)!;
            Assert.That(sheet.Xp, Is.EqualTo(5));
            Assert.That(sheet.Level, Is.EqualTo(1));
            Assert.That(sheet.Traits, Is.Not.Empty, "5 XP is a level, so it rolls its trait on arrival");
        }

        [Test]
        public void TheOneHeroAllowanceIsKeptOnTheArmyFile()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 1000);
            CampaignNumbers n = CampaignNumbers.Firefight;
            Assert.That(CampaignEconomy.HeroRecruitUsed(army), Is.False);

            Assert.That(CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n).Ok, Is.True);
            Assert.That(army.StrikeTeam!.HeroRecruitedAfterMission, Is.EqualTo(army.StrikeTeam.Matches));
            Assert.That(CampaignEconomy.HeroRecruitUsed(army), Is.True);

            // No flag passed in - the file alone says the allowance is spent (it used to be screen memory,
            // so restarting the game gave it back).
            // (Still before mission 1, so the book's creation clause is the reason: "may buy max. one Hero".)
            CampaignPurchase second = CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n);
            Assert.That(second.Ok, Is.False);
            Assert.That(second.Message, Does.Contain("only one Hero"));
            Assert.That(CampaignEconomy.CannotRecruit(army, book, Plain(CaptainId, 1), false), Does.Contain("only one Hero"));

            // Regular units are unlimited.
            Assert.That(CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n).Ok, Is.True);

            // After the next mission it comes back.
            army.StrikeTeam.Matches++;
            Assert.That(CampaignEconomy.HeroRecruitUsed(army), Is.False);
            Assert.That(CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n).Ok, Is.True);
        }

        /// <summary>
        /// Campaign Rules v3.6.0, all four books, Preparation: "Before the first mission, players must spend
        /// at least [X]pts to build their army, and may buy max. one Hero with those points." The "one new
        /// Hero after each mission" allowance opens only after a mission, so an army that starts with its
        /// Hero cannot recruit a second before playing (Kai's question, 2026-11-08 - the books answer it).
        /// </summary>
        [Test]
        public void BeforeTheFirstMissionAnArmyMayHaveOnlyOneHero()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 1000);
            CampaignNumbers n = CampaignNumbers.Firefight;
            // The army came out of creation with its one Hero (added straight to the file, as the locked
            // Forge does), and no Hero was RECRUITED - so only the creation clause can stop the next one.
            army.Units.Add(ListCompiler.Compile(book, new BuilderList { Units = { Plain(CaptainId, 1) } }).Units[0]);
            Assert.That(army.StrikeTeam!.Matches, Is.EqualTo(0));
            Assert.That(army.StrikeTeam.HeroRecruitedAfterMission, Is.Not.EqualTo(0));

            Assert.That(CampaignEconomy.BeforeFirstMissionHeroTaken(army), Is.True);
            Assert.That(CampaignEconomy.HeroRecruitBlockedReason(army), Does.Contain("Before the first mission"));
            CampaignPurchase second = CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n);
            Assert.That(second.Ok, Is.False);
            Assert.That(CampaignEconomy.HeroCount(army), Is.EqualTo(1));
            Assert.That(CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n).Ok, Is.True,
                "regular units are still unlimited");

            // After mission 1 the ordinary one-per-mission allowance applies.
            army.StrikeTeam.Matches++;
            Assert.That(CampaignEconomy.HeroRecruitBlockedReason(army), Is.Null);
            Assert.That(CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n).Ok, Is.True);
            Assert.That(CampaignEconomy.HeroRecruitBlockedReason(army), Does.Contain("one new Hero"));
        }

        [Test]
        public void OnlyOneVeteranRecruitAfterEachMission()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 1000);
            CampaignNumbers n = CampaignNumbers.Firefight;

            Assert.That(CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n, veteran: true, d6: Dice(1)).Ok, Is.True);
            Assert.That(CampaignEconomy.VeteranRecruitUsed(army), Is.True);
            CampaignPurchase again = CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n, veteran: true, d6: Dice(1));
            Assert.That(again.Ok, Is.False);
            Assert.That(again.Message, Does.Contain("5 XP"));

            army.StrikeTeam!.Matches++;
            Assert.That(CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n, veteran: true, d6: Dice(1)).Ok, Is.True);
        }

        [Test]
        public void AnOldSaveHasNotUsedItsRecruitAllowances()
        {
            // A campaign file written before Batch F has neither field; it must read as "not used".
            var info = System.Text.Json.JsonSerializer.Deserialize<StrikeTeamInfo>("{\"Name\":\"Old\",\"Matches\":0}")!;
            Assert.That(info.HeroRecruitedAfterMission, Is.EqualTo(-1));
            Assert.That(info.VeteranRecruitedAfterMission, Is.EqualTo(-1));
            var army = new BuiltArmyFile { StrikeTeam = info };
            Assert.That(CampaignEconomy.HeroRecruitUsed(army), Is.False);
            Assert.That(CampaignEconomy.VeteranRecruitUsed(army), Is.False);
        }

        [Test]
        public void OnlyOneHeroPerMissionAndNeverMoreThanThree()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 1000);
            CampaignNumbers n = CampaignNumbers.Firefight;

            Assert.That(CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n).Ok, Is.True);
            Assert.That(CampaignEconomy.HeroCount(army), Is.EqualTo(1));

            CampaignPurchase second = CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n,
                heroAlreadyRecruitedThisMission: true);
            Assert.That(second.Ok, Is.False);
            Assert.That(second.Message, Does.Contain("only one Hero"), "before mission 1 the creation cap is the reason");

            // Fill up to the cap, one Hero after each mission, then one more. Batch F: the allowance is
            // on the army file now, so each further Hero needs a mission played in between (this test
            // used to recruit all three back to back, which the book never allowed).
            army.StrikeTeam!.Matches++;
            CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n);
            army.StrikeTeam.Matches++;
            CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n);
            Assert.That(CampaignEconomy.HeroCount(army), Is.EqualTo(CampaignNumbers.MaxHeroes));
            army.StrikeTeam.Matches++;
            CampaignPurchase tooMany = CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n);
            Assert.That(tooMany.Ok, Is.False);
            Assert.That(tooMany.Message, Does.Contain("3 Heroes"));
        }

        [Test]
        public void ARecruitYouCannotAffordIsRefused()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 30);
            CampaignPurchase result = CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), CampaignNumbers.Firefight);
            Assert.That(result.Ok, Is.False);
            Assert.That(army.Units.Count, Is.EqualTo(1), "nothing joins the army on a refusal");
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(30));
        }

        // -- Disbanding -------------------------------------------------------------------------------

        [Test]
        public void DisbandingRemovesTheUnitAndRefundsNothing()
        {
            (BuiltArmyFile army, _) = Started(treasuryLeft: 50);
            UnitFileEntry unit = Troopers(army);
            CampaignPurchase result = CampaignEconomy.Disband(army, unit);

            Assert.That(result.Ok, Is.True);
            Assert.That(army.Units, Is.Empty);
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(50), "the book is explicit: no benefit");
        }

        [Test]
        public void DisbandingAUnitDetachesAnyHeroThatJoinedIt()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 200);
            UnitFileEntry squad = Troopers(army);
            squad.Id ??= "squad-1";
            CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), CampaignNumbers.Firefight);
            UnitFileEntry hero = army.Units.First(CampaignRules.IsHero);
            hero.JoinsUnitId = squad.Id;

            CampaignEconomy.Disband(army, squad);
            Assert.That(hero.JoinsUnitId, Is.Null, "no dangling link is left in the army file");
        }

        // -- Taking only part of the army to a mission (#410) -----------------------------------------

        /// <summary>
        /// The campaign screen plays a subset by writing a "this mission only" copy of the army file
        /// holding just the units that are coming. That only works because the write-back matches each
        /// result to a unit by its sheet id, not by position - so the results that come back from the
        /// copy still land on the right units of the real army, and the ones left at home are untouched.
        /// This is the invariant that breaks silently if a copy ever regenerates sheet ids.
        /// </summary>
        [Test]
        public void ResultsFromAPartialMissionLandOnTheRealArmyByIdAndSkipTheUnitsLeftBehind()
        {
            (BuiltArmyFile army, BookFile book) = Started(treasuryLeft: 500);
            CampaignNumbers n = CampaignNumbers.Firefight;
            CampaignEconomy.Recruit(army, book, Plain(TrooperId, 5), n);
            CampaignEconomy.Recruit(army, book, Plain(CaptainId, 1), n);
            Assert.That(army.Units.Count, Is.EqualTo(3));

            UnitFileEntry stayedHome = army.Units[1];
            string homeId = CampaignRules.Sheet(stayedHome)!.Id;
            var went = army.Units.Where(u => !ReferenceEquals(u, stayedHome)).ToList();

            // What the mission copy would be: the same file minus the unit left behind. Sheets, and so
            // their ids and TeamFile, ride along untouched.
            var copy = new BuiltArmyFile { Name = army.Name, Faction = army.Faction, StrikeTeam = army.StrikeTeam };
            copy.Units.AddRange(went);
            Assert.That(copy.Units.Select(u => CampaignRules.Sheet(u)!.Id),
                Is.EqualTo(went.Select(u => CampaignRules.Sheet(u)!.Id)), "a copy must not re-id the sheets");

            // The results come back keyed by the copy's sheet ids, and are applied to the REAL army.
            var results = copy.Units
                .Select(u => new CampaignUnitResult(CampaignRules.Sheet(u)!.Id, Casualty: false, Kills: 1, HeroKills: 0))
                .ToList();
            CampaignRules.AfterGame(army, results, won: true, n, Dice(3));

            Assert.Multiple(() =>
            {
                foreach (UnitFileEntry u in went)
                    Assert.That(CampaignRules.Sheet(u)!.Xp, Is.EqualTo(2), $"{u.Name}: +1 survived, +1 kill");
                Assert.That(CampaignRules.Sheet(stayedHome)!.Xp, Is.Zero, "the unit left at home earns nothing");
                Assert.That(CampaignRules.Sheet(stayedHome)!.Games, Is.Zero, "and plays no game");
                Assert.That(CampaignRules.Sheet(stayedHome)!.Id, Is.EqualTo(homeId));
                Assert.That(army.Units.Count, Is.EqualTo(3), "nobody is lost by playing a subset");
                Assert.That(army.StrikeTeam!.Matches, Is.EqualTo(1), "the army still counts the mission once");
            });
        }

        [Test]
        public void DisbandingTheWholeArmyClearsEverything()
        {
            (BuiltArmyFile army, _) = Started(treasuryLeft: 500);
            army.StrikeTeam!.Vp = 6;
            army.StrikeTeam.Matches = 3;
            CampaignEconomy.DisbandArmy(army);

            Assert.Multiple(() =>
            {
                Assert.That(army.Units, Is.Empty);
                Assert.That(army.StrikeTeam.Vp, Is.Zero);
                Assert.That(army.StrikeTeam.BankedPoints, Is.Zero);
                Assert.That(army.StrikeTeam.Matches, Is.Zero);
                Assert.That(army.StrikeTeam.History, Has.Some.Contains("disbanded"));
            });
        }
    }
}
