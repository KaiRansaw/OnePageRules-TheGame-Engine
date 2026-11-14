using FDG.Rules.Dispatch;
using NUnit.Framework;
using static FDG.Rules.Dispatch.GameRulebook;

namespace FDG.Tests
{
    /// <summary>
    /// Each game's own core rulebook (Kai, 2026-11-14). These fail the build when a game's Army Forge wording for a
    /// core rule differs from the shared wording and nobody has said how that game plays it - the way differences
    /// used to hide (every game quietly played Grimdark Future's version) - or when the list and the engine disagree.
    /// </summary>
    [TestFixture]
    public class GameRulebookTests
    {
        private static IEnumerable<EGameSystem> AllGames() => GameRulebook.Games;

        [TestCaseSource(nameof(AllGames))]
        public void EveryDifferenceFromTheSharedWording_IsAccountedFor(EGameSystem game)
        {
            List<string> unreviewed = GameRulebook.For(game)
                .Where(e => e.Note.StartsWith("UNREVIEWED"))
                .Select(e => $"{e.Rule}:\n   shared: {e.SharedText}\n   {game}: {e.GameText}")
                .ToList();
            Assert.That(unreviewed, Is.Empty,
                "These rules are worded differently in this game and the rulebook doesn't say how it plays them. "
                + "Add each to GameRulebook's ledger (own definition / handled per game / same behaviour / known gap):\n"
                + string.Join("\n", unreviewed));
        }

        [TestCaseSource(nameof(AllGames))]
        public void TheRulebookListsEveryCoreRule(EGameSystem game)
        {
            Assert.That(GameRulebook.For(game).Select(e => e.Rule), Is.EquivalentTo(CoreRuleCatalog.All.Select(d => d.Name)));
        }

        [TestCaseSource(nameof(AllGames))]
        public void OwnDefinitions_AreExactlyTheGamesVariants_AndArePlayed(EGameSystem game)
        {
            var listed = GameRulebook.LedgerRows.Where(r => r.System == game && r.Reading == EReading.OwnDefinition)
                .Select(r => r.Rule).ToList();
            var variants = SystemRuleVariants.For(game).Select(v => v.Name).ToList();
            Assert.That(listed, Is.EquivalentTo(variants), "the ledger's own-definition rows and SystemRuleVariants must agree");

            foreach (Entry e in GameRulebook.For(game).Where(e => e.Reading == EReading.OwnDefinition))
                Assert.That(e.Definition, Is.Not.SameAs(CoreRuleCatalog.All.Single(d => d.Name == e.Rule)),
                    $"{e.Rule}: {game} must play its own definition, not the shared one");
        }

        [Test]
        public void NoLedgerRow_IsStale()
        {
            // A row for a game whose wording now matches the shared one (or no longer exists) should be removed -
            // or, for an own definition, re-checked: Army Forge may have changed the number.
            var stale = GameRulebook.LedgerRows
                .Select(r => (r.Rule, r.System, Own: OfficialRuleText.OwnText(r.Rule, GameSystemInfo.Slug(r.System))?.Trim(),
                    Shared: GameRulebook.SharedText(r.Rule)))
                .Where(x => string.IsNullOrEmpty(x.Own) || x.Own == x.Shared)
                .Select(x => $"{x.Rule} / {x.System}")
                .ToList();
            Assert.That(stale, Is.Empty, string.Join("\n", stale));
        }

        [Test]
        public void GrimdarkFuture_PlaysTheSharedRules()
        {
            Assert.That(GameRulebook.For(EGameSystem.GrimdarkFuture).Where(e => e.Reading is not (EReading.Same or EReading.NotInThisGame))
                .Select(e => e.Rule), Is.Empty, "the shared wording IS Grimdark Future's");
        }

        [Test]
        public void Example_FirefightSlayer_IsFirefightsOwnRule()
        {
            Entry slayer = GameRulebook.For(EGameSystem.GFFirefight).Single(e => e.Rule == "Slayer");
            Assert.That(slayer.Reading, Is.EqualTo(EReading.OwnDefinition));
            Assert.That(slayer.GameText, Does.Contain("Tough(9)"));
            Assert.That(GameRulebook.For(EGameSystem.GFFirefight).Count(e => e.Note.StartsWith("AuraConferral")), Is.GreaterThan(30),
                "Firefight's auras are read as the wide (up to 2 picked units) kind");
            Assert.That(GameRulebook.For(EGameSystem.GrimdarkFuture).Count(e => e.Reading == EReading.Same), Is.GreaterThan(60));
        }

        [Test, Explicit("prints each game's rulebook summary")]
        public void Summary()
        {
            foreach (EGameSystem g in GameRulebook.Games)
                TestContext.Progress.WriteLine(g + ": " + string.Join(", ", GameRulebook.For(g).GroupBy(e => e.Reading).Select(x => $"{x.Key} {x.Count()}")));
        }
    }
}
