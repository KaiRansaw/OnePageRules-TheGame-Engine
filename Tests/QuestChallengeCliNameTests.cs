using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (run 4). The headless --challenges switch documents the short names
    // boss,injuries,alertness,time,weather,events, but only a prefix of the WHOLE name matched, so
    // "injuries" (Hero Injuries) and "events" (Random Events) stopped every scripted run with an exception -
    // 11 of 16 AoF: Quest Horde sweep games died before round 1.
    [TestFixture]
    public class QuestChallengeCliNameTests
    {
        [TestCase("boss", QuestChallenges.BossFights)]
        [TestCase("injuries", QuestChallenges.HeroInjuries)]
        [TestCase("alertness", QuestChallenges.IncreasedAlertness)]
        [TestCase("time", QuestChallenges.TimeOfDayName)]
        [TestCase("weather", QuestChallenges.WeatherName)]
        [TestCase("events", QuestChallenges.RandomEvents)]
        [TestCase("random-events", QuestChallenges.RandomEvents)]
        [TestCase("Hero Injuries", QuestChallenges.HeroInjuries)]
        public void DocumentedShortNames_AllMatch(string raw, string expected)
        {
            Assert.That(QuestChallenges.MatchCliName(raw), Is.EqualTo(expected));
        }

        [Test]
        public void UnknownName_MatchesNothing()
        {
            Assert.That(QuestChallenges.MatchCliName("dragons"), Is.Null);
            Assert.That(QuestChallenges.MatchCliName(""), Is.Null);
        }
    }
}
