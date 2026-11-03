using System.Collections.Generic;
using FDG.Campaign;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #446 - the campaign win/loss record (campaign-flow-spec section 2's right-hand panel).
    ///
    /// <para>
    /// Wins were not recorded anywhere before this: <see cref="StrikeTeamInfo.Matches"/> counted missions
    /// played and nothing counted how many were won. So the record needed a new field, and a new field
    /// means every campaign already on disk reads back claiming zero wins - including one that has won
    /// five.
    /// </para>
    ///
    /// <para>
    /// <see cref="CampaignRules.RecordOf"/> therefore recovers the wins from the history lines in that
    /// one case. The test that makes that safe is
    /// <see cref="AWinWrittenByAfterGameIsFoundByTheBackfill"/>: it drives a real
    /// <see cref="CampaignRules.AfterGame"/> and asserts the backfill finds what it wrote, so rewording
    /// that log line fails here instead of silently zeroing everybody's record.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignRecordTests
    {
        private static CampaignNumbers Gf => CampaignNumbers.For(EGameSystem.GrimdarkFuture);

        private static ArmyListFile Army(string name = "Iron Reavers")
        {
            var army = new ArmyListFile { Name = name };
            army.Units.Add(new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 });
            CampaignRules.Start(army, name, name + ".fdgarmy", Gf, EGameSystem.GrimdarkFuture,
                ECampaignType.PointLimited, vpTarget: 7);
            return army;
        }

        /// <summary>Plays one mission through the real after-game write-back.</summary>
        private static void PlayMission(ArmyListFile army, bool won)
        {
            var results = new List<CampaignUnitResult>();
            foreach (UnitFileEntry unit in army.Units)
                if (CampaignRules.Sheet(unit) is { } sheet)
                    results.Add(new CampaignUnitResult { Id = sheet.Id, Casualty = false });

            List<string> log = CampaignRules.AfterGame(army, results, won, Gf, () => 4);
            army.StrikeTeam!.History.AddRange(log);
        }

        [Test]
        public void ANewCampaignHasNoRecord()
        {
            (int wins, int losses) = CampaignRules.RecordOf(Army().StrikeTeam!);

            Assert.That(wins, Is.Zero);
            Assert.That(losses, Is.Zero, "a campaign that has played nothing has lost nothing.");
        }

        [Test]
        public void AWinIsCounted()
        {
            ArmyListFile army = Army();
            PlayMission(army, won: true);

            Assert.That(army.StrikeTeam!.Wins, Is.EqualTo(1));
            Assert.That(CampaignRules.RecordOf(army.StrikeTeam), Is.EqualTo((1, 0)));
        }

        [Test]
        public void ALossIsCountedAsALossAndNotAsAWin()
        {
            ArmyListFile army = Army();
            PlayMission(army, won: false);

            Assert.That(army.StrikeTeam!.Wins, Is.Zero);
            Assert.That(CampaignRules.RecordOf(army.StrikeTeam), Is.EqualTo((0, 1)));
        }

        [Test]
        public void AMixedRunAddsUp()
        {
            ArmyListFile army = Army();
            PlayMission(army, won: true);
            PlayMission(army, won: false);
            PlayMission(army, won: true);
            PlayMission(army, won: false);
            PlayMission(army, won: true);

            Assert.That(army.StrikeTeam!.Matches, Is.EqualTo(5));
            Assert.That(CampaignRules.RecordOf(army.StrikeTeam), Is.EqualTo((3, 2)));
        }

        /// <summary>
        /// The one that keeps the migration honest. <see cref="CampaignRules.AfterGame"/> writes the
        /// winning history line and <see cref="CampaignRules.RecordOf"/> reads it back for older
        /// campaigns; if the two ever stop agreeing, every pre-#446 campaign silently shows 0 wins.
        /// Driving the real writer is the only way to catch that - a test that hand-wrote the line would
        /// pass while the real wording drifted.
        /// </summary>
        [Test]
        public void AWinWrittenByAfterGameIsFoundByTheBackfill()
        {
            ArmyListFile army = Army();
            PlayMission(army, won: true);
            PlayMission(army, won: false);
            PlayMission(army, won: true);

            StrikeTeamInfo info = army.StrikeTeam!;
            Assert.That(info.Wins, Is.EqualTo(2), "the stored count, for reference.");

            // Now make it look like a campaign saved before the field existed: the history is intact,
            // the counter is not.
            info.Wins = 0;

            Assert.That(CampaignRules.RecordOf(info), Is.EqualTo((2, 1)),
                "the backfill must recover what AfterGame wrote. If this fails, the winning history "
                + "line was reworded without updating CampaignRules.WonPrefix.");
        }

        /// <summary>
        /// An army that has genuinely lost every mission also has Wins at zero, so the backfill runs for
        /// it too. It must find nothing rather than inventing something.
        /// </summary>
        [Test]
        public void AnArmyThatLostEveryMissionReadsAsNoWins()
        {
            ArmyListFile army = Army();
            PlayMission(army, won: false);
            PlayMission(army, won: false);

            army.StrikeTeam!.Wins = 0;   // as an older file would be

            Assert.That(CampaignRules.RecordOf(army.StrikeTeam), Is.EqualTo((0, 2)));
        }

        /// <summary>
        /// A campaign file can be edited by hand, and the record is drawn on screen - it must not be
        /// possible to make it read as a negative number of losses.
        /// </summary>
        [Test]
        public void NonsenseCountsAreClampedRatherThanDrawn()
        {
            var info = new StrikeTeamInfo { Name = "Hand edited", Matches = 2, Wins = 99 };
            Assert.That(CampaignRules.RecordOf(info), Is.EqualTo((2, 0)),
                "more wins than missions clamps to the missions played.");

            var negative = new StrikeTeamInfo { Name = "Hand edited", Matches = -3, Wins = 0 };
            (int wins, int losses) = CampaignRules.RecordOf(negative);
            Assert.That(wins, Is.Zero);
            Assert.That(losses, Is.Zero, "a negative mission count cannot become negative losses.");
        }

        /// <summary>
        /// The bot's campaign army is paid, levelled and injured by the same write-back, so it keeps a
        /// record too - which is what lets the panel show both sides' results rather than only yours.
        /// </summary>
        [Test]
        public void TheRivalKeepsItsOwnRecord()
        {
            ArmyListFile mine = Army("Iron Reavers");
            ArmyListFile theirs = Army("The Hollow Choir");
            theirs.StrikeTeam!.IsBot = true;

            // One mission: you win it, they lose it.
            PlayMission(mine, won: true);
            PlayMission(theirs, won: false);

            Assert.That(CampaignRules.RecordOf(mine.StrikeTeam!), Is.EqualTo((1, 0)));
            Assert.That(CampaignRules.RecordOf(theirs.StrikeTeam!), Is.EqualTo((0, 1)));
        }
    }
}
