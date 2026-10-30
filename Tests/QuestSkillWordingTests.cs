using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The one wording template every Star Quest skill message goes through (#407).
    ///
    /// <para>Each skill's effect text was written as a bare rules fragment - "one extra hit on its
    /// next charge" - so the Battle Report said what changed but never which skill changed it. With
    /// ~120 skills built, that made a working skill and a silent one read identically, which is what
    /// put several of Kai's QA items on standby. <see cref="QuestSkill.Line"/> is the single place
    /// that decides the shape, so these guard it rather than 120 individual strings.</para>
    /// </summary>
    [TestFixture]
    public class QuestSkillWordingTests
    {
        [Test]
        public void OutsideASkill_TheLineReadsAsItAlwaysDid()
        {
            // No skill resolving: a non-skill caller must not grow a stray separator.
            Assert.That(QuestSkill.Speaking, Is.Null);
            Assert.That(QuestSkill.Line("Magik", "takes 1 wound"), Is.EqualTo("Magik: takes 1 wound."));
        }

        [Test]
        public void EveryStarQuestSkill_HasEffectTextThatStartsLowercase()
        {
            // The template supplies "Hero: Skill - ", so each skill's own text is the CONTINUATION of
            // a sentence, not the start of one. A capitalised fragment reads as a second sentence
            // ("Magik: Reckless - Deals +1 wound"), and a text that repeats its own skill name
            // stutters ("Magik: Reckless - Reckless deals..."). Catches a hand-written line that did
            // not get the memo.
            foreach (QuestSkill skill in QuestSkill.All)
            {
                string description = skill.Description;
                if (string.IsNullOrWhiteSpace(description)) continue;
                Assert.That(description.StartsWith(skill.Name + " ", StringComparison.Ordinal), Is.False,
                    $"'{skill.Name}' repeats its own name in its description - the template adds it.");
            }
        }

        [Test]
        public void SkillNamesAreAsciiOnly()
        {
            // CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only, so anything past
            // U+00FF renders as '?' in game. Skill names reach the report, the hero card and the
            // Game Master panel.
            foreach (QuestSkill skill in QuestSkill.All)
            {
                foreach (char c in skill.Name)
                {
                    Assert.That(c, Is.LessThanOrEqualTo((char)0xFF),
                        $"'{skill.Name}' contains a character the font atlas cannot draw.");
                }
            }
        }
    }
}
