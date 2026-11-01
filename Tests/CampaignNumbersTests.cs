using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #410 - the per-game-system campaign price list, and that one shared loop really does produce each
    /// book's numbers. Every figure asserted here comes straight from the four Campaign Rules v3.6.0
    /// books; see the project doc claude/campaign-rules-delta.md for the table they were read from.
    /// </summary>
    [TestFixture]
    public class CampaignNumbersTests
    {
        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        private static ArmyListFile Army(int points)
        {
            var army = new BuiltArmyFile { Name = "Test", Faction = "Test", GameSystem = GameSystems.GrimdarkFuture };
            army.Units.Add(new UnitFileEntry
            {
                Name = "Captain", ModelCount = 1, Quality = 4, Defense = 4, PointCost = points / 2,
                SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Hero") },
            });
            army.Units.Add(new UnitFileEntry
            {
                Name = "Squad", ModelCount = 5, Quality = 5, Defense = 5, PointCost = points - points / 2,
            });
            return army;
        }

        /// <summary>
        /// Every number, read off each book's Preparation and Mission Setup sections.
        /// <para>
        /// <b>Age of Fantasy's cap and underdog rate were wrong here until #433</b>, and this test is why
        /// they stayed wrong: it asserted 325 and 65 for Age of Fantasy, copied from Grimdark Future
        /// along with the production alias, so the suite agreed with the bug. The book prints
        /// <b>250pts</b> and <b>50pts</b>. If a future change makes these two match Grimdark Future again,
        /// it is a regression, not a simplification.
        /// </para>
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture, 750, 325, 65, 75, 150, 300, 25, 25, 55, 50, 25)]
        [TestCase(EGameSystem.GFFirefight, 400, 125, 25, 40, 80, 160, 15, 15, 25, 30, 25)]
        [TestCase(EGameSystem.AgeOfFantasy, 750, 250, 50, 75, 150, 300, 25, 25, 55, 50, 25)]
        [TestCase(EGameSystem.AoFSkirmish, 300, 100, 20, 30, 60, 120, 15, 15, 25, 30, 15)]
        public void EachBooksNumbers(EGameSystem system, int treasury, int cap, int underdog, int special,
                                     int win, int lose, int injury, int regular, int hero, int veteran,
                                     int eventCredits)
        {
            CampaignNumbers n = CampaignNumbers.For(system);
            Assert.Multiple(() =>
            {
                Assert.That(n.Treasury, Is.EqualTo(treasury));
                Assert.That(n.ArmyCapOverLowest, Is.EqualTo(cap));
                Assert.That(n.UnderdogPer, Is.EqualTo(underdog));
                Assert.That(n.SpecialObjectiveBonus, Is.EqualTo(special));
                Assert.That(n.WinPoints, Is.EqualTo(win));
                Assert.That(n.LosePoints, Is.EqualTo(lose));
                Assert.That(n.HeroInjuryTalentCost, Is.EqualTo(injury));
                Assert.That(n.RegularLevelCost, Is.EqualTo(regular));
                Assert.That(n.HeroLevelCost, Is.EqualTo(hero));
                Assert.That(n.VeteranRecruitCost, Is.EqualTo(veteran));
                Assert.That(n.EventCredits, Is.EqualTo(eventCredits));
            });
        }

        /// <summary>
        /// The cross-check that makes the cap and the underdog rate trustworthy as a PAIR: every book's
        /// cap is exactly five underdog points wide (325/65, 125/25, 250/50, 100/20). That is almost
        /// certainly deliberate on OPR's part, and it is the arithmetic that would have caught #433's
        /// Age of Fantasy mix-up on its own - 250 with 65, or 325 with 50, fails this.
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void EveryBooksCapIsExactlyFiveUnderdogPointsWide(EGameSystem system)
        {
            CampaignNumbers n = CampaignNumbers.For(system);

            Assert.That(n.ArmyCapOverLowest, Is.EqualTo(n.UnderdogPer * CampaignNumbers.UnderdogPointsAcrossTheCap),
                $"{system}: a cap of {n.ArmyCapOverLowest} against {n.UnderdogPer} pts per underdog point "
                + "breaks the ratio every book shares, so one of the two numbers is a typo.");
        }

        /// <summary>
        /// GFF, AoF and AoFS all print a minimum spend that is exactly 80% of their treasury. GF prints
        /// 800 against a 750pt treasury, which cannot be met - Kai's call on 2026-10-30 was to treat it
        /// as an OPR typo and derive every minimum from the pattern, which gives GF 600.
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture, 600)]
        [TestCase(EGameSystem.GFFirefight, 320)]
        [TestCase(EGameSystem.AgeOfFantasy, 600)]
        [TestCase(EGameSystem.AoFSkirmish, 240)]
        public void MinimumSpendIsEightyPercentOfTreasury(EGameSystem system, int expected) =>
            Assert.That(CampaignNumbers.For(system).MinimumSpend, Is.EqualTo(expected));

        [Test]
        public void TheFourBooksShareTheirConstants()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CampaignNumbers.WinVp, Is.EqualTo(2));
                Assert.That(CampaignNumbers.XpPerLevel, Is.EqualTo(5));
                Assert.That(CampaignNumbers.MaxXp, Is.EqualTo(30), "six levels at most");
                Assert.That(CampaignNumbers.MaxHeroes, Is.EqualTo(3));
            });
        }

        /// <summary>
        /// Age of Fantasy against Grimdark Future, stated as the two differences rather than as equality.
        /// The old version of this asserted the two were IDENTICAL, which is what let #433's mix-up
        /// through: every other number does match, so the claim looked right and the two that matter most
        /// to a mission went unchecked. Written as a difference list so adding a number to one book and
        /// not the other fails here.
        /// </summary>
        [Test]
        public void AgeOfFantasyMatchesGrimdarkFuture_ExceptItsCapAndUnderdogRate()
        {
            CampaignNumbers gf = CampaignNumbers.GrimdarkFuture;
            CampaignNumbers aof = CampaignNumbers.AgeOfFantasy;

            Assert.Multiple(() =>
            {
                Assert.That(aof.ArmyCapOverLowest, Is.EqualTo(250), "the AoF book prints 250, GF prints 325.");
                Assert.That(aof.UnderdogPer, Is.EqualTo(50), "the AoF book prints 50, GF prints 65.");
                Assert.That(aof, Is.Not.EqualTo(gf),
                    "these two books are NOT the same price list - that assumption was #433's bug.");
                Assert.That(aof with { ArmyCapOverLowest = gf.ArmyCapOverLowest, UnderdogPer = gf.UnderdogPer },
                    Is.EqualTo(gf),
                    "and apart from those two, every other number is shared - so if this fails, a third "
                    + "number has diverged and belongs in the list above.");
            });
        }

        [Test]
        public void SkirmishBooksHeroSkillsCoverOnlyTheHero()
        {
            Assert.That(CampaignNumbers.GrimdarkFuture.HeroSkillsCoverUnit, Is.True);
            Assert.That(CampaignNumbers.AgeOfFantasy.HeroSkillsCoverUnit, Is.True);
            Assert.That(CampaignNumbers.Firefight.HeroSkillsCoverUnit, Is.False);
            Assert.That(CampaignNumbers.Skirmish.HeroSkillsCoverUnit, Is.False);
        }

        [Test]
        public void OnlyTheFourBigBooksHaveACampaignBook()
        {
            Assert.That(CampaignNumbers.HasCampaignBook(EGameSystem.GrimdarkFuture), Is.True);
            Assert.That(CampaignNumbers.HasCampaignBook(EGameSystem.GFFirefight), Is.True);
            Assert.That(CampaignNumbers.HasCampaignBook(EGameSystem.AgeOfFantasy), Is.True);
            Assert.That(CampaignNumbers.HasCampaignBook(EGameSystem.AoFSkirmish), Is.True);
            Assert.That(CampaignNumbers.HasCampaignBook(EGameSystem.GFStarQuest), Is.False,
                "Star Quest has the GFSQ Campaign Builder, not this campaign");
            Assert.That(CampaignNumbers.HasCampaignBook(EGameSystem.AoFQuest), Is.False);
        }

        /// <summary>The same loop, run twice, pays out each book's own mission reward.</summary>
        [TestCase(EGameSystem.GrimdarkFuture, 150, 300)]
        [TestCase(EGameSystem.GFFirefight, 80, 160)]
        [TestCase(EGameSystem.AoFSkirmish, 60, 120)]
        public void OneLoopPaysEachBooksMissionReward(EGameSystem system, int win, int lose)
        {
            CampaignNumbers n = CampaignNumbers.For(system);
            foreach (bool won in new[] { true, false })
            {
                ArmyListFile army = Army(n.MinimumSpend);
                CampaignRules.Start(army, "Test", "test", n, system);
                int before = army.StrikeTeam!.BankedPoints;
                CampaignRules.AfterGame(army, Array.Empty<CampaignUnitResult>(), won, n, Dice(3));
                Assert.That(army.StrikeTeam.BankedPoints - before, Is.EqualTo(won ? win : lose));
                Assert.That(army.StrikeTeam.Vp, Is.EqualTo(won ? CampaignNumbers.WinVp : 0));
            }
        }

        /// <summary>And each book's own level cost, from the same code path.</summary>
        [TestCase(EGameSystem.GrimdarkFuture, 25, 55)]
        [TestCase(EGameSystem.GFFirefight, 15, 25)]
        [TestCase(EGameSystem.AoFSkirmish, 15, 25)]
        public void OneLoopChargesEachBooksLevelCost(EGameSystem system, int regular, int hero)
        {
            CampaignNumbers n = CampaignNumbers.For(system);
            ArmyListFile army = Army(n.MinimumSpend);
            CampaignRules.Start(army, "Test", "test", n, system);

            UnitFileEntry squad = army.Units.First(u => u.Name == "Squad");
            int squadCost = squad.PointCost;
            CampaignRules.AddXp(squad, CampaignRules.Sheet(squad)!, 5, n, Dice(1));
            Assert.That(squad.PointCost, Is.EqualTo(squadCost + regular));

            UnitFileEntry captain = army.Units.First(u => u.Name == "Captain");
            int captainCost = captain.PointCost;
            CampaignRules.AddXp(captain, CampaignRules.Sheet(captain)!, 5, n, Dice(1, 1));
            Assert.That(captain.PointCost, Is.EqualTo(captainCost + hero));
        }

        [Test]
        public void StartKeepsTheWholeRemainingTreasury()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            ArmyListFile army = Army(600);
            Assert.That(CampaignRules.CannotStart(army, n), Is.Null);
            CampaignRules.Start(army, "Test", "test", n, EGameSystem.GrimdarkFuture);
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(150),
                "750pt treasury - a 600pt army; there is no cap on what carries over");
        }

        [Test]
        public void XpIsCappedAtThirty()
        {
            CampaignNumbers n = CampaignNumbers.Firefight;
            ArmyListFile army = Army(n.MinimumSpend);
            CampaignRules.Start(army, "Test", "test", n, EGameSystem.GFFirefight);
            UnitFileEntry squad = army.Units.First(u => u.Name == "Squad");
            StrikeUnit sheet = CampaignRules.Sheet(squad)!;
            CampaignRules.AddXp(squad, sheet, 100, n, Dice(1, 2, 4, 5, 3, 6));
            Assert.That(sheet.Xp, Is.EqualTo(30));
            Assert.That(sheet.Level, Is.EqualTo(6), "30 XP is six levels, the book's maximum");
        }

        [Test]
        public void SkillSetPairsAreTheThreeFromTheBook()
        {
            Assert.That(CampaignTables.SkillSetPairs, Is.EqualTo(new[]
            {
                ("Pathfinder", "Captain"), ("Warrior", "Hunter"), ("Warden", "Overseer"),
            }));
            Assert.That(CampaignTables.SkillSets.Values.SelectMany(v => v).Count(), Is.EqualTo(18));
        }

        [Test]
        public void SpecialObjectivesAreTheSixFromTheBook()
        {
            Assert.That(CampaignTables.SpecialObjectives.Select(o => o.Name), Is.EqualTo(new[]
            {
                "Hexer", "Annihilator", "Headhunter", "Dominator", "Intimidator", "Strategist",
            }));
        }
    }
}
