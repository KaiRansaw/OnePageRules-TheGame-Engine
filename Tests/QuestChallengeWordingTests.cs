using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (run 3). The mission set-up menu offered "Increased Alertness" - the Horde Mode v2.0.0 name -
    // where both Campaign Builders v2.1.0 (GF: Star Quest, AoF: Quest) print the rule as "Awareness", and every
    // challenge's hover was our own summary rather than OPR's words.
    [TestFixture]
    public class QuestChallengeWordingTests
    {
        [Test]
        public void TheAlertnessChallenge_IsCalledAwarenessInMissions_AndIncreasedAlertnessInHorde()
        {
            Assert.That(QuestChallenges.Label(QuestChallenges.IncreasedAlertness, horde: false), Is.EqualTo("Awareness"));
            Assert.That(QuestChallenges.Label(QuestChallenges.IncreasedAlertness, horde: true), Is.EqualTo("Increased Alertness"));
            Assert.That(QuestChallenges.Label(QuestChallenges.WeatherName, horde: false), Is.EqualTo("Weather"));
        }

        [Test]
        public void Hovers_AreTheBooksOwnWords()
        {
            // Campaign Builder v2.1.0, "Awareness" and "Boss Fights".
            Assert.That(QuestChallenges.Describe(QuestChallenges.IncreasedAlertness, horde: false),
                Does.StartWith("Pick how aware the enemies are, or roll for it: 1 Full Assault - At the end of each round, roll one die. On a 1-2, increase Alertness by 1."));
            Assert.That(QuestChallenges.Describe(QuestChallenges.BossFights, horde: false),
                Does.StartWith("After completing all objectives, players may choose to trigger a boss fight. At the end of that round deploy a wave of Reinforcements of double point value"));
            // Horde Mode v2.0.0, "Increased Alertness" and "Random Events".
            Assert.That(QuestChallenges.Describe(QuestChallenges.IncreasedAlertness, horde: true),
                Does.StartWith("Pick how alert the enemies are, or roll for it:"));
            Assert.That(QuestChallenges.Describe(QuestChallenges.RandomEvents, horde: true),
                Does.StartWith("Whenever a new wave is deployed, a random event happens."));
            Assert.That(QuestChallenges.Describe(QuestChallenges.RandomEvents, horde: false),
                Does.StartWith("At the start of the first round, and whenever a new wave of Reinforcements is deployed, a random event happens."));
        }
    }
}
