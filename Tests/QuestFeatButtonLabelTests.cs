using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (run 3): a hero's class feature is offered as "<feature> (once per game)", and the battle
    // screen printed that whole string on a 74px action square - "SENTINEL MEDITATION (ONCE PER GAME)" wrapped over
    // four lines and ran into the button's key number. The button now shows the feature's name with "Once per game"
    // as its small second line; the option the engine matches on is unchanged.
    [TestFixture]
    public class QuestFeatButtonLabelTests
    {
        [Test]
        public void ClassFeature_ShowsItsNameAndAOncePerGameNote()
        {
            var (name, note) = QuestActions.ButtonLabel("Sentinel Meditation" + QuestActions.FeatSuffix);
            Assert.That(name, Is.EqualTo("Sentinel Meditation"));
            Assert.That(note, Is.EqualTo("Once per game"));
        }

        [TestCase(QuestActions.SkillOption)]
        [TestCase(QuestActions.RestOption)]
        [TestCase(QuestActions.HoldWithoutShootingOption)]
        public void OtherOptions_AreShownAsTheyAre(string option)
        {
            var (name, note) = QuestActions.ButtonLabel(option);
            Assert.That(name, Is.EqualTo(option));
            Assert.That(note, Is.Null);
        }
    }
}
