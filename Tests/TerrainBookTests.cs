using System;
using System.Linq;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// <see cref="TerrainBook"/> - the Map Maker's book data (Map Maker mockup, approved 2026-11-13). Pins the numbers each
    /// current book prints, so a later edit can't quietly swap one game's figures for another's.
    /// </summary>
    [TestFixture]
    public class TerrainBookTests
    {
        private static Func<int> Dice(int seed)
        {
            var r = new Random(seed);
            return () => r.Next(1, 7);
        }

        [Test]
        public void Kinds_CarryTheAdvancedRulebooksCommonTypes()
        {
            Assert.That(TerrainBook.Kind("Forest")!.Rules, Is.EqualTo(ETerrainType.Difficult | ETerrainType.Cover | ETerrainType.Dense),
                "\"Forests - Difficult + Cover + Dense\"");
            Assert.That(TerrainBook.Kind("Building")!.Rules, Is.EqualTo(ETerrainType.Impassible | ETerrainType.Blocking),
                "\"Buildings - Impassable + Sight Blocking\"");
            Assert.That(TerrainBook.Kind("Ruins")!.Rules, Is.EqualTo(ETerrainType.Cover | ETerrainType.Unstable), "\"Ruins - Cover + Unstable\"");
            Assert.That(TerrainBook.Kind("LakeDeep")!.Rules, Is.EqualTo(ETerrainType.Impassible), "\"Lakes - ... Impassable (if deep)\"");
            Assert.That(TerrainBook.Kind("RiverDeep")!.Rules, Is.EqualTo(ETerrainType.Unstable), "\"Rivers - ... Unstable (if deep)\"");
            Assert.That(TerrainBook.Kind("Wall")!.Rules, Is.EqualTo(ETerrainType.Climbable), "\"Walls - Climbable\"");
            Assert.That(TerrainBook.Kinds.Select(k => k.Id), Is.Unique);
        }

        [Test]
        public void EveryRule_HasTheBooksText_InAscii()
        {
            foreach (ETerrainType flag in TerrainBook.RuleFlags)
            {
                string text = TerrainBook.RuleText(flag);
                Assert.That(text, Is.Not.Empty, flag.ToString());
                Assert.That(text.All(c => c < 128), $"{flag}: game text is ASCII only (CLAUDE.md)");
            }
            Assert.That(TerrainBook.RuleText(ETerrainType.Dense),
                Does.Contain("Units may draw line of sight into and out of dense terrain, but not through it to targets on the other side."));
        }

        [Test]
        public void Guidance_IsEachGamesOwnBook()
        {
            Assert.That(TerrainBook.GuidanceFor(EGameSystem.GrimdarkFuture), Is.EqualTo(new TerrainBook.Guidance(15, 20, 25, 12, 6, "GF Advanced Rulebook v3.6.0")));
            Assert.That(TerrainBook.GuidanceFor(EGameSystem.GFFirefight)!.MinPieces, Is.EqualTo(20));
            Assert.That(TerrainBook.GuidanceFor(EGameSystem.AoFSkirmish)!.MaxGap, Is.EqualTo(6), "Skirmish: \"no gaps bigger than 6\"\"");
            Assert.That(TerrainBook.GuidanceFor(EGameSystem.AoFQuest)!.CoverTablePercent, Is.EqualTo(50));
            Assert.That(TerrainBook.GuidanceFor(EGameSystem.GFStarQuest), Is.Null, "Star Quest's core book has no placement guidance");
        }

        [TestCase(EGameSystem.GrimdarkFuture, 9, 4)]
        [TestCase(EGameSystem.AgeOfFantasy, 9, 4)]
        [TestCase(EGameSystem.GFFirefight, 10, 6)]
        [TestCase(EGameSystem.AoFSkirmish, 10, 6)]
        public void Generator_TakesTheBooksCount(EGameSystem system, int fixedPieces, int extraDice)
        {
            Assert.That(TerrainBook.FixedCount(system), Is.EqualTo(fixedPieces));
            foreach (TerrainBook.ETheme theme in Enum.GetValues<TerrainBook.ETheme>())
            {
                int dice = TerrainBook.ExtraDice(system, theme);
                if (!(system == EGameSystem.GFFirefight && theme == TerrainBook.ETheme.Death))
                    Assert.That(dice, Is.EqualTo(extraDice), $"{system} {theme}");
                for (int seed = 0; seed < 25; seed++)
                {
                    var pieces = TerrainBook.Generate(system, theme, Dice(seed));
                    Assert.That(pieces.Count, Is.InRange(fixedPieces + dice, fixedPieces + 3 * dice), $"{system} {theme}: {fixedPieces} + {dice}D3");
                    Assert.That(pieces.All(p => TerrainBook.Kind(p.KindId) != null && p.Width > 0 && p.Depth > 0));
                }
            }
        }

        [Test]
        public void Generator_FirefightDeath_IsAsPrinted()
        {
            Assert.That(TerrainBook.ExtraDice(EGameSystem.GFFirefight, TerrainBook.ETheme.Death), Is.EqualTo(4),
                "GFF Advanced v3.6.0 prints \"take 4D3 more\" for Death Planets (6D3 for its other themes) - as printed");
            Assert.That(TerrainBook.ExtraDice(EGameSystem.AoFSkirmish, TerrainBook.ETheme.Death), Is.EqualTo(6));
        }

        [Test]
        public void Generator_OnlyInGamesWithAnAdvancedRulebook()
        {
            Assert.That(TerrainBook.HasGenerator(EGameSystem.GFStarQuest), Is.False);
            Assert.That(TerrainBook.HasGenerator(EGameSystem.AoFQuest), Is.False);
            Assert.That(TerrainBook.Generate(EGameSystem.AoFQuest, TerrainBook.ETheme.Generic, Dice(1)), Is.Empty);
            Assert.That(TerrainBook.ThemeName(TerrainBook.ETheme.Death, EGameSystem.AgeOfFantasy), Is.EqualTo("Death Planes"));
            Assert.That(TerrainBook.ThemeName(TerrainBook.ETheme.Death, EGameSystem.GrimdarkFuture), Is.EqualTo("Death Planets"));
        }

        [Test]
        public void GrowToCover_ReachesTheBooksCover_WithinTwelveInches()
        {
            var pieces = TerrainBook.Generate(EGameSystem.GrimdarkFuture, TerrainBook.ETheme.Generic, Dice(2));
            var grown = TerrainBook.GrowToCover(pieces, 72, 48, EGameSystem.GrimdarkFuture);
            float area = grown.Sum(p => p.Round ? MathF.PI * p.Width * p.Width / 4f : p.Width * p.Depth);
            Assert.That(area / (72f * 48f), Is.GreaterThanOrEqualTo(0.22f), "about the GF book's 25% (whole inches round a little off)");
            Assert.That(grown.Where(p => p.Large && p.KindId is not ("RiverShallow" or "RiverDeep")).All(p => p.Width <= 12 && p.Depth <= 12), "\"can be as large as 12\"x12\"\"");
            Assert.That(grown.Where(p => !p.Large), Is.EqualTo(pieces.Where(p => !p.Large)), "scatter stays scatter-sized");
        }

        [Test]
        public void Spread_KeepsEveryPieceOnTheTable_AndApart()
        {
            var rng = new Random(4);
            var pieces = TerrainBook.Generate(EGameSystem.GrimdarkFuture, TerrainBook.ETheme.Generic, Dice(4));
            var placed = TerrainBook.Spread(pieces, 72, 48, rng.NextDouble, out int left);
            Assert.That(placed.Count + left, Is.EqualTo(pieces.Count));
            Assert.That(placed.Count, Is.GreaterThan(pieces.Count / 2), "a 6'x4' table takes most of a GF generator roll");
            foreach (var p in placed)
            {
                Assert.That(p.X - p.W / 2, Is.GreaterThanOrEqualTo(1f));
                Assert.That(p.X + p.W / 2, Is.LessThanOrEqualTo(71f));
                Assert.That(p.Z - p.D / 2, Is.GreaterThanOrEqualTo(1f));
                Assert.That(p.Z + p.D / 2, Is.LessThanOrEqualTo(47f));
                Assert.That(placed.Where(o => o != p).All(o => TerrainBook.Gap(o, p) >= 1f), "never closer than the book's 1\" step-across gap");
            }
        }
    }
}
