using FDG.Missions;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Extra Missions (Advanced Rulebooks 3.6.0 p24-26, Kai 2026-11-22): the catalogue, the random roll and when
    /// a mission applies. Played-game checks: the bot sweep (every mission, GF / GFF / AoF / AoFS).</summary>
    [TestFixture]
    public class ExtraMissionsTests
    {
        [Test]
        public void EveryMission_HasTheBooksWords_AndAsciiOnly()
        {
            Assert.That(ExtraMissionCatalog.All.Count, Is.EqualTo(12), "Duel + 5 Face-Off + 6 Progressive");
            foreach (ExtraMissionInfo m in ExtraMissionCatalog.All)
            {
                Assert.That(m.BookText, Does.Contain("After 4 rounds"), m.Name);
                Assert.That(m.BookText.All(ch => ch < 128) && m.Name.All(ch => ch < 128), Is.True, m.Name);
            }
        }

        [Test]
        public void TheRandomRoll_FollowsTheBooksTables_AndRerollsAttackAndDefend()
        {
            int[] Faces(params int[] f) => f;
            EExtraMission Roll(params int[] faces) { int i = 0; return ExtraMissionCatalog.Roll(() => faces[i++]); }
            Assert.That(Roll(1, 1), Is.EqualTo(EExtraMission.None), "Face-Off 1 = Duel");
            Assert.That(Roll(2, 3), Is.EqualTo(EExtraMission.RelicHunt));
            Assert.That(Roll(1, 6), Is.EqualTo(EExtraMission.KingOfTheHill));
            Assert.That(Roll(3, 1), Is.EqualTo(EExtraMission.PitchedBattle));
            Assert.That(Roll(4, 4), Is.EqualTo(EExtraMission.Demolition));
            Assert.That(Roll(4, 6), Is.EqualTo(EExtraMission.MoshPit));
            Assert.That(Roll(6, 5, 2, 2), Is.EqualTo(EExtraMission.SeizeGround), "5-6 (Attack & Defend) is rolled again");
            _ = Faces();
        }

        [Test]
        public void OnlyInStandardGames_OfTheAdvancedRulebooksGames_WithoutMissionCards()
        {
            GameSettings s = GameSettings.GetDefault();
            s.ExtraMission = EExtraMission.Domination;
            Assert.That(ExtraMissions.Of(s), Is.EqualTo(EExtraMission.Domination));
            s.MissionCardGame = EMissionCardGame.BattleOfWits;
            Assert.That(ExtraMissions.Of(s), Is.EqualTo(EExtraMission.None), "Mission Cards bring their own scoring");
            s.MissionCardGame = EMissionCardGame.Off;
            s.GameSystem = EGameSystem.GFStarQuest;
            Assert.That(ExtraMissions.Of(s), Is.EqualTo(EExtraMission.None), "no Advanced Rulebook");
            s.GameSystem = EGameSystem.AoFSkirmish;
            s.GameMode = EGameMode.Horde;
            Assert.That(ExtraMissions.Of(s), Is.EqualTo(EExtraMission.None));
        }

        [Test]
        public void MapMarkers_AreUsedOnlyWhereTheMissionLeavesTheirNumberOpen()
        {
            GameSettings s = GameSettings.GetDefault();
            s.ExtraMission = EExtraMission.PitchedBattle;
            Assert.That(ExtraMissions.MapMarkersAllowed(s, 5), Is.True);
            s.ExtraMission = EExtraMission.RelicHunt;
            Assert.That(ExtraMissions.MapMarkersAllowed(s, 3), Is.True);
            Assert.That(ExtraMissions.MapMarkersAllowed(s, 4), Is.False);
            s.ExtraMission = EExtraMission.SeizeGround;
            Assert.That(ExtraMissions.MapMarkersAllowed(s, 4), Is.False, "the quarters' centres, not the map's");
        }
    }
}
