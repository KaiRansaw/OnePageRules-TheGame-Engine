using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #427 - the books' <b>No Permadeath</b> option, which Kai asked for as "a toggle when creating a
    /// campaign" (2026-11-01).
    ///
    /// <para>The rule, identical in all four Campaign Rules v3.6.0 books: a unit that would be killed
    /// "is not killed, but instead loses all of the XP it earned for that match, and must sit out of
    /// the next D3 matches". So three things have to hold, and each is a separate way to get it wrong:
    /// the unit stays in the army; it banks <b>nothing</b> from that match, not even for what it killed
    /// before it fell; and it is unavailable for a counted number of missions rather than permanently
    /// or not at all.</para>
    ///
    /// <para>Everything already-earned survives - levels, traits, injuries, talents and the points they
    /// cost. Only this match's earnings are lost, which is the difference between the option being a
    /// safety net and it being an undo button.</para>
    /// </summary>
    [TestFixture]
    public class CampaignNoPermadeathTests
    {
        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        /// <summary>A started campaign army, with the option on or off.</summary>
        private static ArmyListFile Army(bool noPermadeath)
        {
            var army = new BuiltArmyFile { Name = "Raiders", Faction = "Test", GameSystem = GameSystems.GrimdarkFuture };
            army.Units.Add(new UnitFileEntry
            {
                Name = "Captain", ModelCount = 1, Quality = 4, Defense = 4, PointCost = 100,
                SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Hero") },
            });
            army.Units.Add(new UnitFileEntry { Name = "Squad", ModelCount = 5, Quality = 5, Defense = 5, PointCost = 500 });
            CampaignRules.Start(army, "Raiders", "raiders.fdgarmy", CampaignNumbers.GrimdarkFuture,
                EGameSystem.GrimdarkFuture, ECampaignType.Endless, noPermadeath);
            return army;
        }

        private static (UnitFileEntry Unit, StrikeUnit Sheet) U(ArmyListFile army, string name)
        {
            UnitFileEntry u = army.Units.First(x => x.Name == name);
            return (u, CampaignRules.Sheet(u)!);
        }

        private static CampaignUnitResult Casualty(ArmyListFile army, string name, int kills = 0, int heroKills = 0) =>
            new(U(army, name).Sheet.Id, Casualty: true, Kills: kills, HeroKills: heroKills);

        // The baseline this option changes: with permadeath on (the books' default), a regular unit that
        // rolls a 1 is gone. Stated here so the two cases sit side by side.
        [Test]
        public void WithPermadeath_ARegularRollingOne_LeavesTheArmy()
        {
            ArmyListFile army = Army(noPermadeath: false);

            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1));

            Assert.That(army.Units.Any(u => u.Name == "Squad"), Is.False,
                "with permadeath on, a casualty that rolls 1 is removed from the army.");
        }

        [Test]
        public void NoPermadeath_ARegularRollingOne_SurvivesAndSitsOut()
        {
            ArmyListFile army = Army(noPermadeath: true);

            // 1 = dead, then the D3: a 5 reads as 3 missions out.
            List<string> log = CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1, 5));

            (UnitFileEntry unit, StrikeUnit sheet) = U(army, "Squad");
            Assert.That(unit, Is.Not.Null, "the unit must still be in the army.");
            Assert.That(sheet.MissionsOut, Is.EqualTo(3), "a D3 of 5 is three missions out.");
            Assert.That(CampaignRules.CanBringToMission(unit), Is.False,
                "a recovering unit may not be taken to the next mission.");
            Assert.That(log.Any(l => l.Contains("No Permadeath")), Is.True,
                "the log must say why the unit survived.");
        }

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(6, 3)]
        public void NoPermadeath_SitsOutADThree_RolledAsHalfADSix(int roll, int expected)
        {
            ArmyListFile army = Army(noPermadeath: true);

            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1, roll));

            Assert.That(U(army, "Squad").Sheet.MissionsOut, Is.EqualTo(expected));
        }

        /// <summary>
        /// "Loses all of the XP it earned for that match" - all of it. A unit that killed two enemy units
        /// and a Hero before it fell would otherwise bank 4 XP and level up on the way out, which would
        /// make being killed under this option a net gain.
        /// </summary>
        [Test]
        public void NoPermadeath_LosesEveryXpFromThatMatch_IncludingItsKills()
        {
            ArmyListFile army = Army(noPermadeath: true);

            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad", kills: 2, heroKills: 1) },
                won: true, CampaignNumbers.GrimdarkFuture, Dice(1, 3));

            StrikeUnit sheet = U(army, "Squad").Sheet;
            Assert.That(sheet.Xp, Is.Zero, "no XP at all for the match it was killed in.");
            Assert.That(sheet.Level, Is.Zero);
        }

        /// <summary>
        /// Only THIS match's earnings are lost. A unit that was already levelled keeps its levels, its
        /// traits and the points they added - the option is a safety net, not a reset.
        /// </summary>
        [Test]
        public void NoPermadeath_KeepsWhatItHadAlreadyEarned()
        {
            ArmyListFile army = Army(noPermadeath: true);
            (UnitFileEntry unit, StrikeUnit sheet) = U(army, "Squad");
            CampaignRules.AddXp(unit, sheet, 10, CampaignNumbers.GrimdarkFuture, Dice(1));
            CampaignRules.Save(unit, sheet);
            int costAfterLevels = unit.PointCost;
            int traitsAfterLevels = CampaignRules.Sheet(unit)!.Traits.Count;

            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1, 3));

            StrikeUnit after = CampaignRules.Sheet(unit)!;
            Assert.That(after.Xp, Is.EqualTo(10), "XP earned in earlier matches stays.");
            Assert.That(after.Level, Is.EqualTo(2));
            Assert.That(after.Traits.Count, Is.EqualTo(traitsAfterLevels), "its traits stay.");
            Assert.That(unit.PointCost, Is.EqualTo(costAfterLevels), "and so do the points they cost.");
        }

        /// <summary>
        /// A Hero reaches death through "Dead" on the injury table, and the knock-out replaces that
        /// result rather than stacking an injury on top of it. Recorded as a judgement call in
        /// claude/house-rules.md: the book replaces the death, and Dead is the death.
        /// </summary>
        [Test]
        public void NoPermadeath_AHeroRollingDead_SurvivesWithNoInjury()
        {
            ArmyListFile army = Army(noPermadeath: true);
            int costBefore = U(army, "Captain").Unit.PointCost;

            // 1 = casualty roll, then the injury table's Dead, then the D3.
            int dead = Array.IndexOf(CampaignTables.Injuries, "Dead") + 1;
            CampaignRules.AfterGame(army, new[] { Casualty(army, "Captain") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1, dead, 3));

            (UnitFileEntry hero, StrikeUnit sheet) = U(army, "Captain");
            Assert.That(hero, Is.Not.Null, "the hero must still be in the army.");
            Assert.That(sheet.MissionsOut, Is.GreaterThan(0), "and sitting out.");
            Assert.That(sheet.Injuries, Is.Empty, "the knock-out replaces the Dead result; it is not an injury.");
            Assert.That(hero.PointCost, Is.EqualTo(costBefore), "so it costs no less than before.");
        }

        /// <summary>
        /// The count-down. A unit sitting out is not on the table, so it never appears in a match's
        /// results - the only place it can move is the top of the next after-game, and it must move
        /// there or the unit would be out for ever.
        /// </summary>
        [Test]
        public void SittingOut_CountsDownOneMissionPerMatchPlayed()
        {
            ArmyListFile army = Army(noPermadeath: true);
            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1, 5));
            Assert.That(U(army, "Squad").Sheet.MissionsOut, Is.EqualTo(3), "precondition: three out.");

            // Three more matches, in each of which only the Captain plays.
            var captainOnly = new[] { new CampaignUnitResult(U(army, "Captain").Sheet.Id, false, 0, 0) };
            for (int expected = 2; expected >= 0; expected--)
            {
                CampaignRules.AfterGame(army, captainOnly, won: true, CampaignNumbers.GrimdarkFuture, Dice(3));
                Assert.That(U(army, "Squad").Sheet.MissionsOut, Is.EqualTo(expected));
            }

            Assert.That(CampaignRules.CanBringToMission(U(army, "Squad").Unit), Is.True,
                "after sitting out its D3 it is available again.");
        }

        /// <summary>
        /// The count-down runs BEFORE this match's casualties, so a unit knocked out today gets its full
        /// D3 from today. Rolled the other way round, a D3 of 1 would expire the same evening it was
        /// earned and the unit would never actually miss a mission.
        /// </summary>
        [Test]
        public void AUnitKnockedOutToday_DoesNotLoseAMissionOfItsRestImmediately()
        {
            ArmyListFile army = Army(noPermadeath: true);

            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1, 1));

            Assert.That(U(army, "Squad").Sheet.MissionsOut, Is.EqualTo(1),
                "a D3 of 1 must leave one whole mission to sit out, not zero.");
            Assert.That(CampaignRules.CanBringToMission(U(army, "Squad").Unit), Is.False);
        }

        // Permadeath on is the default, and nothing in an ordinary campaign should ever set MissionsOut -
        // the unit is simply gone, so there is nothing to recover.
        [Test]
        public void WithPermadeath_NothingEverSitsOut()
        {
            ArmyListFile army = Army(noPermadeath: false);

            CampaignRules.AfterGame(army, new[] { Casualty(army, "Squad") }, won: true,
                CampaignNumbers.GrimdarkFuture, Dice(1));

            Assert.That(army.Units.All(u => (CampaignRules.Sheet(u)?.MissionsOut ?? 0) == 0), Is.True);
            Assert.That(army.StrikeTeam!.NoPermadeath, Is.False, "and the option is off by default.");
        }
    }
}
