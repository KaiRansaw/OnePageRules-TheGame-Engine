using FDG.Campaign;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #433 - the 36 campaign random events in the right system's words, for the right money.
    ///
    /// <para>
    /// Two things vary between the four Campaign Rules v3.6.0 books and neither was handled: the fantasy
    /// books rename 21 of the 36 events, and AoF: Skirmish's two treasury events pay <b>15pts</b> where
    /// the others pay 25. #432 started announcing these in play the same morning, so an Age of Fantasy
    /// campaign was being told a Solar Flare had dimmed its knights' armour, and a Skirmish campaign was
    /// being overpaid.
    /// </para>
    ///
    /// <para>
    /// The renames are typed from the books, so the tests that matter here are the ones that would catch
    /// a typo or a missing entry rather than agreeing with whatever the map happens to say: the two sets
    /// must differ on exactly the codes the map claims, every name must be ASCII and non-empty, and no
    /// rename may collide with another event's canonical name.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignEventNameTests
    {
        private static readonly EGameSystem[] SciFi =
            { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight };

        private static readonly EGameSystem[] Fantasy =
            { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish };

        private static CampaignEvent Event(int code) => CampaignEvents.ByCode(code);

        // -- The split ---------------------------------------------------------------------------------

        [Test]
        public void TheFantasyBooksRenameTwentyOneOfTheThirtySix()
        {
            Assert.That(CampaignEventNames.RenamedCodes.Count, Is.EqualTo(21));
            Assert.That(CampaignEvents.All.Count(), Is.EqualTo(36));
        }

        /// <summary>
        /// The claim the map makes, checked against the map's own behaviour: a renamed code comes back
        /// different in a fantasy game, and an un-renamed one comes back identical. This is what fails if
        /// an entry is added, removed, or keyed to a code that does not exist.
        /// </summary>
        [Test]
        public void EveryEventIsRenamedInTheFantasyBooks_OrDeliberatelyNot()
        {
            foreach (CampaignEvent fired in CampaignEvents.All)
            {
                string fantasy = CampaignEventNames.Name(fired, EGameSystem.AgeOfFantasy);
                bool renamed = CampaignEventNames.RenamedCodes.Contains(fired.Code);

                Assert.That(fantasy != fired.Name, Is.EqualTo(renamed),
                    $"event {fired.Code} ({fired.Name}): the rename map says renamed={renamed}, "
                    + $"but the fantasy name came back \"{fantasy}\".");
            }
        }

        [Test]
        public void AgeOfFantasyAndSkirmishReadTheSameNames()
        {
            foreach (CampaignEvent fired in CampaignEvents.All)
                Assert.That(CampaignEventNames.Name(fired, EGameSystem.AoFSkirmish),
                    Is.EqualTo(CampaignEventNames.Name(fired, EGameSystem.AgeOfFantasy)),
                    $"event {fired.Code}: the two fantasy books print the same name for every event.");
        }

        [Test]
        public void GrimdarkFutureAndFirefightReadTheCanonicalNames()
        {
            foreach (EGameSystem system in SciFi)
                foreach (CampaignEvent fired in CampaignEvents.All)
                    Assert.That(CampaignEventNames.Name(fired, system), Is.EqualTo(fired.Name),
                        $"{system} event {fired.Code} should be the table's own name.");
        }

        /// <summary>A handful typed straight out of the AoF book, as a spot-check on the transcription.</summary>
        [TestCase(12, "Magic Trap")]
        [TestCase(32, "Melting Curse")]
        [TestCase(35, "Animal Guide")]
        [TestCase(61, "Gold Stash")]
        [TestCase(64, "Treasure Map")]
        [TestCase(66, "Godly Sunrays")]
        public void TheFantasyNamesMatchTheBook(int code, string expected)
        {
            Assert.That(CampaignEventNames.Name(Event(code), EGameSystem.AgeOfFantasy), Is.EqualTo(expected));
        }

        /// <summary>And a handful the books agree on, so "renamed" is not quietly applied to everything.</summary>
        [TestCase(11, "Falling Debris")]
        [TestCase(31, "Sinkhole")]
        [TestCase(43, "Thick Clouds")]
        [TestCase(52, "Prophetic Vision")]
        public void TheSharedNamesAreLeftAlone(int code, string expected)
        {
            Assert.That(CampaignEventNames.Name(Event(code), EGameSystem.AgeOfFantasy), Is.EqualTo(expected));
            Assert.That(CampaignEventNames.Name(Event(code), EGameSystem.GrimdarkFuture), Is.EqualTo(expected));
        }

        // -- Hygiene -----------------------------------------------------------------------------------

        /// <summary>
        /// Game text is ASCII-only: the ImGui font atlas bakes Basic Latin plus Latin-1, so anything past
        /// U+00FF renders as "?" in play. A name typed out of a PDF is exactly where a curly apostrophe or
        /// an en dash sneaks in.
        /// </summary>
        [Test]
        public void EveryNameAndEveryWordingIsAscii()
        {
            foreach (EGameSystem system in SciFi.Concat(Fantasy))
                foreach (CampaignEvent fired in CampaignEvents.All)
                {
                    string name = CampaignEventNames.Name(fired, system);
                    string text = CampaignEventNames.Text(fired, system);

                    Assert.That(name, Is.Not.Empty, $"event {fired.Code} has no name in {system}.");
                    Assert.That(text, Is.Not.Empty, $"event {fired.Code} has no wording in {system}.");
                    Assert.That(name.All(c => c <= 0x7F), Is.True, $"\"{name}\" ({system}) is not ASCII.");
                    Assert.That(text.All(c => c <= 0x7F), Is.True, $"\"{text}\" ({system}) is not ASCII.");
                }
        }

        /// <summary>
        /// No fantasy rename may collide with a DIFFERENT event's canonical name. A collision would make
        /// two events indistinguishable in the log, and it is the likeliest shape for a transcription
        /// slip, since the two tables share a lot of vocabulary.
        /// </summary>
        [Test]
        public void NoRenameCollidesWithAnotherEventsName()
        {
            foreach (int code in CampaignEventNames.RenamedCodes)
            {
                string fantasy = CampaignEventNames.FantasyName(code);
                CampaignEvent? clash = CampaignEvents.All
                    .FirstOrDefault(e => e.Code != code && e.Name == fantasy);

                Assert.That(clash?.Name, Is.Null,
                    $"event {code}'s fantasy name \"{fantasy}\" is already event {clash?.Code}'s name.");
            }
        }

        [Test]
        public void EveryNameIsUniqueWithinEachSystem()
        {
            foreach (EGameSystem system in SciFi.Concat(Fantasy))
            {
                var names = CampaignEvents.All.Select(e => CampaignEventNames.Name(e, system)).ToList();
                Assert.That(names.Distinct().Count(), Is.EqualTo(36),
                    $"{system} has two events sharing a name, so the log cannot tell them apart.");
            }
        }

        // -- The money ---------------------------------------------------------------------------------

        /// <summary>
        /// The one number in the table that is not shared. AoF: Skirmish's Gold Stash is 15pts and its
        /// Treasure Map D3 x 15; the other three books print 25.
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture, 25)]
        [TestCase(EGameSystem.GFFirefight, 25)]
        [TestCase(EGameSystem.AgeOfFantasy, 25)]
        [TestCase(EGameSystem.AoFSkirmish, 15)]
        public void TheTreasuryEventsPayTheirOwnBooksFigure(EGameSystem system, int expected)
        {
            Assert.That(CampaignEventNames.Credits(Event(61), system), Is.EqualTo(expected));
            Assert.That(CampaignEventNames.Credits(Event(64), system), Is.EqualTo(expected));
        }

        /// <summary>And the wording a player reads carries that same figure, not a baked-in 25.</summary>
        [Test]
        public void TheWordingCarriesTheSystemsOwnFigure()
        {
            Assert.That(CampaignEventNames.Text(Event(61), EGameSystem.AoFSkirmish),
                Is.EqualTo("One random army gets 15pts."));
            Assert.That(CampaignEventNames.Text(Event(64), EGameSystem.AoFSkirmish),
                Is.EqualTo("One random army gets D3 * 15pts."));
            Assert.That(CampaignEventNames.Text(Event(61), EGameSystem.GrimdarkFuture),
                Is.EqualTo("One random army gets 25pts."));
        }

        /// <summary>
        /// And no wording still has a placeholder in it. The token is only meant to survive as far as the
        /// fill, so one left over would be printed at a player verbatim.
        /// </summary>
        [Test]
        public void NoWordingLeavesAPlaceholderBehind()
        {
            foreach (EGameSystem system in SciFi.Concat(Fantasy))
                foreach (CampaignEvent fired in CampaignEvents.All)
                    Assert.That(CampaignEventNames.Text(fired, system),
                        Does.Not.Contain("{").And.Not.Contain("}"),
                        $"event {fired.Code} ({system}) still has a placeholder in its wording.");
        }

        /// <summary>
        /// Non-treasury events keep their own amount. Credits() is asked about every event, so it must
        /// not re-price a wound count or an XP award with a points figure.
        /// </summary>
        [Test]
        public void CreditsLeavesEveryOtherEventsAmountAlone()
        {
            foreach (CampaignEvent fired in CampaignEvents.All.Where(e => e.Kind != ECampaignEventKind.GainPoints))
                Assert.That(CampaignEventNames.Credits(fired, EGameSystem.AoFSkirmish), Is.EqualTo(fired.Amount),
                    $"event {fired.Code} is a {fired.Kind}, not a treasury reward.");
        }
    }
}
