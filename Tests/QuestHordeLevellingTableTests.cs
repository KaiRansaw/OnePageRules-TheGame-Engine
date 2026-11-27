using FDG.Quest;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (run 4), AoF: Quest Horde. Horde Mode v2.0.0 (both Quest games, the current Horde books)
    // prints its own levelling table - level 2 at 2 XP ... level 15 at 122, with +2 Tou / +1 Qua at level 4 and
    // +2 Tou / +2 Pow at level 8 - while Quest Horde levelled by the Campaign Builder v2.1.0 table (3 ... 168, levels
    // 4 and 8 the other way round). In the sweep a hero with 2 XP after wave 1 stayed level 1.
    [TestFixture]
    public class QuestHordeLevellingTableTests
    {
        [Test]
        public void HordeTable_IsTheHordeBooks()
        {
            Assert.That(QuestProgression.HordeXpForLevel.Skip(1),
                Is.EqualTo(new[] { 0, 2, 4, 8, 12, 18, 24, 32, 40, 50, 60, 82, 94, 108, 122 }));
            Assert.That(QuestProgression.OptionsFor(4, horde: true), Is.EqualTo(new[] { "+2 Tou", "+1 Qua" }));
            Assert.That(QuestProgression.OptionsFor(8, horde: true), Is.EqualTo(new[] { "+2 Tou", "+2 Pow" }));
            Assert.That(QuestProgression.OptionsFor(5, horde: true), Is.EqualTo(new[] { "+2 Pow", "+2 Skill", "+1 Caster" }));
        }

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(4, 3)]
        [TestCase(7, 3)]
        [TestCase(8, 4)]
        [TestCase(122, 15)]
        public void HordeLevels(int xp, int level) => Assert.That(QuestProgression.LevelForXp(xp, horde: true), Is.EqualTo(level));

        [Test]
        public void MissionsAndCampaigns_KeepTheCampaignBuildersTable()
        {
            Assert.That(QuestProgression.LevelForXp(2), Is.EqualTo(1));
            Assert.That(QuestProgression.LevelForXp(3), Is.EqualTo(2));
            Assert.That(QuestProgression.OptionsFor(4, horde: false), Is.EqualTo(new[] { "+2 Tou", "+2 Pow" }));
            Assert.That(QuestProgression.OptionsFor(8, horde: false), Is.EqualTo(new[] { "+2 Tou", "+1 Qua" }));
        }
    }
}
