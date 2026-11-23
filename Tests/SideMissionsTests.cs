using FDG.Missions;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Side-Missions (Advanced Rulebooks 3.6.0 p27-28 / p25-26). Played-game checks: the bot sweep with
    /// --side-missions (every playstyle and Mad Dash variant).</summary>
    [TestFixture]
    public class SideMissionsTests
    {
        [Test]
        public void EveryGame_HasThirtySixSideMissions_InSixCategories_AsciiOnly()
        {
            foreach (EGameSystem system in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
            {
                var all = SideMissionCatalog.For(system);
                Assert.That(all.Count, Is.EqualTo(36), system.ToString());
                Assert.That(all.Select(i => i.Id).Distinct().Count(), Is.EqualTo(36));
                foreach (ESideCategory cat in Enum.GetValues<ESideCategory>())
                    Assert.That(all.Count(i => i.Category == cat), Is.EqualTo(6), $"{system} {cat}");
                Assert.That(all.All(i => i.Text.All(ch => ch < 128) && i.Name.All(ch => ch < 128)), Is.True);
            }
        }

        [Test]
        public void FirefightAndSkirmish_PrintTheSmallerNumbers()
        {
            Assert.That(SideMissionCatalog.Of(EGameSystem.GrimdarkFuture, 41).Text, Does.Contain("three or more enemy units with 5+"));
            Assert.That(SideMissionCatalog.Of(EGameSystem.GFFirefight, 41).Text, Does.Contain("two or more enemy units with 3+"));
            Assert.That(SideMissionCatalog.Of(EGameSystem.AgeOfFantasy, 46).Text, Does.Contain("twenty"));
            Assert.That(SideMissionCatalog.Of(EGameSystem.AoFSkirmish, 46).Text, Does.Contain("ten"));
            Assert.That(SideMissionCatalog.Of(EGameSystem.GrimdarkFuture, 66).Text, Does.Contain("and its unit"));
            Assert.That(SideMissionCatalog.Of(EGameSystem.AoFSkirmish, 66).Text, Does.Contain("six additional models"));
            Assert.That(SideMissionCatalog.SquadSize(EGameSystem.AgeOfFantasy), Is.EqualTo(5));
            Assert.That(SideMissionCatalog.SquadSize(EGameSystem.GFFirefight), Is.EqualTo(3));
        }

        [Test]
        public void MarkersScore_ThreeForOne_TwoEachForTwo_OneEachForMore()
        {
            Assert.That(SideMissions.PerMarker(1), Is.EqualTo(3));
            Assert.That(SideMissions.PerMarker(2), Is.EqualTo(2));
            Assert.That(SideMissions.PerMarker(3), Is.EqualTo(1));
            Assert.That(SideMissions.PerMarker(5), Is.EqualTo(1));
        }

        [Test]
        public void OnlyInStandardGames_WithoutMissionCards_OrAttackAndDefend()
        {
            GameSettings s = GameSettings.GetDefault();
            s.SideMissions = ESideMissionPlay.Shared;
            Assert.That(SideMissions.Of(s), Is.EqualTo(ESideMissionPlay.Shared));
            s.ExtraMission = EExtraMission.TheRaid;
            Assert.That(SideMissions.Of(s), Is.EqualTo(ESideMissionPlay.Off), "Attack & Defend is won on its own terms");
            s.ExtraMission = EExtraMission.Domination;
            Assert.That(SideMissions.Of(s), Is.EqualTo(ESideMissionPlay.Shared));
            s.MissionCardGame = EMissionCardGame.BattleOfWits;
            Assert.That(SideMissions.Of(s), Is.EqualTo(ESideMissionPlay.Off));
            s.MissionCardGame = EMissionCardGame.Off;
            s.GameSystem = EGameSystem.GFStarQuest;
            Assert.That(SideMissions.Of(s), Is.EqualTo(ESideMissionPlay.Off), "no Advanced Rulebook");
            s.GameSystem = EGameSystem.GrimdarkFuture;
            s.GameMode = EGameMode.Horde;
            Assert.That(SideMissions.Of(s), Is.EqualTo(ESideMissionPlay.Off));
        }

        [Test]
        public void MadDash_IsProgressive_TheOthersAreNot()
        {
            Assert.That(SideMissionCatalog.IsDash(ESideMissionPlay.SharedDash), Is.True);
            Assert.That(SideMissionCatalog.IsDash(ESideMissionPlay.RandomDash), Is.True);
            Assert.That(SideMissionCatalog.IsDash(ESideMissionPlay.SharedAndHidden), Is.False);
            Assert.That(SideMissionCatalog.PlayText(ESideMissionPlay.HiddenDash), Does.Contain("progressive"));
        }
    }
}
