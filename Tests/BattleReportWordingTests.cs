using FDG.Presentation.Beats;
using FDG.Stages;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #464 - the Battle Report rework: lines say what happened, and the words that need explaining carry
    /// their own hover (items, events) while the line carries its breakdown (rolls, cause, models lost).
    /// </summary>
    [TestFixture]
    public class BattleReportWordingTests
    {
        [Test]
        public void ABannerBeat_CarriesItsHoverWordsAndBreakdown_AcrossTheWire()
        {
            var beat = new BannerBeat("Kara throws Smoke Grenade!", TextColor.White, EBannerTier.Toast, null,
                new[] { new ReportTerm("Smoke Grenade", "Place a smoke marker within 12\".") },
                new[] { "Rolled 5." });
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var back = JsonConvert.DeserializeObject<BannerBeat>(JsonConvert.SerializeObject(beat, settings), settings)!;
            Assert.That(back.Terms.Single(), Is.EqualTo(new ReportTerm("Smoke Grenade", "Place a smoke marker within 12\".")));
            Assert.That(back.Details, Is.EqualTo(new[] { "Rolled 5." }));
        }

        [Test]
        public void AnOldBeat_WithoutTheNewFields_StillReads()
        {
            var beat = new BannerBeat("Round 2", TextColor.White);
            Assert.That(beat.Terms, Is.Empty);
            Assert.That(beat.Details, Is.Empty);
        }

        [Test]
        public void RandomEventFlavours_AreAsciiAndFixedPerEvent()
        {
            for (int tens = 1; tens <= 6; tens++)
            for (int ones = 1; ones <= 6; ones++)
            {
                string f = QuestChallenges.EventFlavour(tens * 10 + ones);
                Assert.That(f.All(c => c < 128), Is.True, f);
                Assert.That(QuestChallenges.EventFlavour(tens * 10 + ones), Is.EqualTo(f));
            }
        }

        [Test]
        public void ItemTerms_GiveEachKnownItemItsDescription()
        {
            var terms = QuestTreasure.ItemTerms(new[] { "Smoke Grenade", "Smoke Grenade", "not an item" });
            Assert.That(terms.Count, Is.EqualTo(1), "duplicates folded, unknown names dropped");
            Assert.That(terms[0].Text, Does.Contain("smoke marker"));
        }
    }
}
