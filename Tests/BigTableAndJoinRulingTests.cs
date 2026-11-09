using System.Linq;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai's rulings of 2026-11-08: bigger tables for bigger games ("even up to 10k points ... want the maps
    /// to reflect their size"), and Heroes joining units in Firefight and Skirmish but never in the Quest
    /// games. Both are house rules (see claude/house-rules.md); the book numbers they lean on are quoted.
    /// </summary>
    [TestFixture]
    public class BigTableAndJoinRulingTests
    {
        // ── Table size ─────────────────────────────────────────────────────────────────────────────────

        [TestCase(EGameSystem.GrimdarkFuture, 2000, 72f, 48f)]   // the book: 6'x4' for a full 2000pt match
        [TestCase(EGameSystem.GrimdarkFuture, 1000, 72f, 48f)]   // never smaller than the book's table
        [TestCase(EGameSystem.GrimdarkFuture, 3000, 96f, 54f)]   // 1.5x the room -> 8' x 4.5'
        [TestCase(EGameSystem.GrimdarkFuture, 4000, 120f, 60f)]  // 2x -> 10' x 5'
        [TestCase(EGameSystem.GrimdarkFuture, 10000, 240f, 72f)] // 5x -> 20' x 6'
        [TestCase(EGameSystem.AgeOfFantasy, 1500, 72f, 48f)]     // AoF's full match is 1500
        [TestCase(EGameSystem.AgeOfFantasy, 3000, 120f, 60f)]
        [TestCase(EGameSystem.GFFirefight, 800, 48f, 48f)]       // Firefight's full match is 800 on 4'x4'
        [TestCase(EGameSystem.GFFirefight, 1600, 96f, 54f)]
        [TestCase(EGameSystem.AoFSkirmish, 600, 48f, 48f)]
        public void TheTableGrowsWithTheArmyPoints(EGameSystem system, int points, float w, float h)
        {
            Assert.That(TableSizing.AutoSize(system, EGameMode.Standard, points), Is.EqualTo((w, h)));
        }

        [Test]
        public void EveryAutomaticTableGivesAtLeastTheBooksRoomPerPoint()
        {
            foreach (EGameSystem system in new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy,
                         EGameSystem.GFFirefight, EGameSystem.AoFSkirmish })
            {
                (float bw, float bh) = GameWideConstants.TableSizeFor(system);
                int full = TableSizing.FullMatchPoints(system)!.Value;
                for (int points = full; points <= 10000; points += 250)
                {
                    (float w, float h) = TableSizing.AutoSize(system, EGameMode.Standard, points);
                    // Past the biggest table offered (a 10k Skirmish game) it stays on the biggest.
                    if (TableSizing.Same((w, h), TableSizing.SizesFor(system)[^1])) continue;
                    Assert.That(w * h, Is.GreaterThanOrEqualTo(bw * bh * points / full - 0.01f),
                        $"{system} at {points} pts");
                    Assert.That(h, Is.LessThanOrEqualTo(72f), "depth is capped at 6' so armies still meet");
                }
            }
        }

        [Test]
        public void HordeAndTheQuestGamesKeepTheBookTable()
        {
            Assert.That(TableSizing.AutoSize(EGameSystem.GrimdarkFuture, EGameMode.Horde, 6000), Is.EqualTo((72f, 48f)));
            Assert.That(TableSizing.AutoSize(EGameSystem.GFStarQuest, EGameMode.StarQuestMission, 6000), Is.EqualTo((48f, 48f)));
            Assert.That(TableSizing.CanGrow(EGameSystem.AoFQuest, EGameMode.StarQuestHorde), Is.False);
        }

        [Test]
        public void TheMapsTableWinsThenThePickedSizeThenAutomatic()
        {
            var settings = new GameSettings
            {
                GameSystem = EGameSystem.GrimdarkFuture, GameMode = EGameMode.Standard, ArmyPoints = 4000,
                TerrainPlacementMode = ETerrainPlacementMode.AutoFromLayout,
            };
            Assert.That(TableSizing.Resolve(settings), Is.EqualTo((120f, 60f)));

            settings.TableWidthInches = 144f; settings.TableHeightInches = 72f;
            Assert.That(TableSizing.Resolve(settings), Is.EqualTo((144f, 72f)));

            settings.MapTableWidthInches = 108f; settings.MapTableHeightInches = 60f;
            Assert.That(TableSizing.Resolve(settings), Is.EqualTo((144f, 72f)), "a map's size only counts when the map is used");
            settings.TerrainPlacementMode = ETerrainPlacementMode.LoadFromFile;
            Assert.That(TableSizing.Resolve(settings), Is.EqualTo((108f, 60f)));
        }

        [Test, NonParallelizable] // GameWideConstants is global: no other test may measure a table meanwhile
        public void ConfigureTableUsesTheResolvedSize()
        {
            try
            {
                GameWideConstants.ConfigureTable(new GameSettings
                {
                    GameSystem = EGameSystem.GrimdarkFuture, GameMode = EGameMode.Standard, ArmyPoints = 4000,
                });
                Assert.That(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, Is.EqualTo(120f));
                Assert.That(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, Is.EqualTo(60f));
            }
            finally
            {
                GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            }
        }

        [Test]
        public void TheAutoTerrainIsUnchangedOnTheBookTables()
        {
            int authored = DefaultTerrainPool.Get().Pieces.Count;
            Assert.That(DefaultTerrainPool.ForTable(72f, 48f, out float keep).Pieces.Count, Is.EqualTo(authored));
            Assert.That(keep, Is.EqualTo(1f));
            Assert.That(DefaultTerrainPool.ForTable(48f, 48f, out _).Pieces.Count, Is.EqualTo(authored));
        }

        [Test]
        public void ABigTableGetsTerrainAcrossTheWholeTableAtTheBooksDensity()
        {
            int authored = DefaultTerrainPool.Get().Pieces.Count;
            var layout = DefaultTerrainPool.ForTable(240f, 72f, out float keep); // 20' x 6': a 3 x 2 grid
            Assert.That(layout.Pieces.Count, Is.EqualTo(authored * 6));
            Assert.That(keep, Is.EqualTo(240f * 72f / 6f / (72f * 48f)).Within(0.001f));
            float maxX = layout.Pieces.Max(p => TerrainTemplateUtilities.GetCenter(p.Shape).X);
            float minX = layout.Pieces.Min(p => TerrainTemplateUtilities.GetCenter(p.Shape).X);
            Assert.That(maxX, Is.GreaterThan(200f), "terrain reaches the far end of the table");
            Assert.That(minX, Is.LessThan(40f));
        }

        // ── Heroes joining units ───────────────────────────────────────────────────────────────────────

        [Test]
        public void HeroesJoinOnTheFullScaleTermsScaledToEachGame()
        {
            Assert.That(CoreRulesProfile.For(EGameSystem.GrimdarkFuture).HeroJoinToughCap, Is.EqualTo(6),
                "GF Core Rules 3.6.0: \"Heroes with up to Tough(6) may deploy as part of one multi-model unit\"");
            Assert.That(CoreRulesProfile.For(EGameSystem.AgeOfFantasy).HeroJoinToughCap, Is.EqualTo(6));
            // The skirmish books print no join clause; Kai allows joining, on the x3 Tough scale those books
            // use for Transport (Heroes up to Tough(18) where GF says Tough(6)).
            Assert.That(CoreRulesProfile.For(EGameSystem.GFFirefight).HeroJoinToughCap, Is.EqualTo(18));
            Assert.That(CoreRulesProfile.For(EGameSystem.AoFSkirmish).HeroJoinToughCap, Is.EqualTo(18));
            Assert.That(CoreRulesProfile.For(EGameSystem.GFStarQuest).HeroesMayJoinUnits, Is.False);
            Assert.That(CoreRulesProfile.For(EGameSystem.AoFQuest).HeroesMayJoinUnits, Is.False);
        }

        [Test]
        public void TheJoinCapIsReadOffTheArmysOwnSystem()
        {
            Assert.That(HeroJoinResolver.JoinCapFor(ArmyBuilding.GameSystems.GFFirefight), Is.EqualTo(18));
            Assert.That(HeroJoinResolver.JoinCapFor(ArmyBuilding.GameSystems.GFStarQuest), Is.EqualTo(0));
            Assert.That(HeroJoinResolver.JoinCapFor(null), Is.EqualTo(6), "an old file with no system is GF");
        }
    }
}
