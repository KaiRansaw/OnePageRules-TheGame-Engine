using FDG.Ai.Solo;
using FDG.GameModel;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - the per-system table from the four <b>Solo &amp; Co-Op Rules v3.6.0</b> books, and the
    /// objective-placement rule that reads from it.
    ///
    /// <para>
    /// The books are word for word identical except in seven places, so the risk here is not complexity -
    /// it is assuming the two skirmish games match each other. They do on Blast and Deadly and they do NOT
    /// on deployment: AoF: Skirmish uses two table sections off a d6 and four objective squares where the
    /// other three use three sections off a D3 and six squares. That assumption is exactly what shipped six
    /// wrong rules for AoF: Quest two patches earlier, so every cell of the table is pinned here.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloCoOpPlacementTests
    {
        private static RectangularZone Band(float left = 0f, float right = 72f,
            float bottom = 18f, float top = 30f) => new(left, right, bottom, top);

        /// <summary>Dice that answer from a fixed script, so each clause can be tested on its own.</summary>
        private static Func<int, int> Rolls(params int[] script)
        {
            int index = 0;
            return _ => script[Math.Min(index++, script.Length - 1)];
        }

        // ── The per-system table ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SixSquaresEverywhereExceptSkirmish()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SoloCoOpProfile.GrimdarkFuture.ObjectiveSquares, Is.EqualTo(6));
                Assert.That(SoloCoOpProfile.Firefight.ObjectiveSquares, Is.EqualTo(6));
                Assert.That(SoloCoOpProfile.AgeOfFantasy.ObjectiveSquares, Is.EqualTo(6));
                Assert.That(SoloCoOpProfile.AoFSkirmish.ObjectiveSquares, Is.EqualTo(4),
                    "AoF: Skirmish's book says four, not six.");
            });
        }

        [Test]
        public void ThreeDeploymentSectionsExceptSkirmish_WhichUsesTwoOffAD6()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SoloCoOpProfile.GrimdarkFuture.DeploymentSections, Is.EqualTo(3));
                Assert.That(SoloCoOpProfile.Firefight.DeploymentSections, Is.EqualTo(3));
                Assert.That(SoloCoOpProfile.AgeOfFantasy.DeploymentSections, Is.EqualTo(3));
                Assert.That(SoloCoOpProfile.AoFSkirmish.DeploymentSections, Is.EqualTo(2));
                Assert.That(SoloCoOpProfile.AoFSkirmish.SectionRollIsD3, Is.False,
                    "Skirmish rolls a d6 and halves it, where the others roll a D3.");
            });
        }

        [Test]
        public void SkirmishSectionRoll_SplitsTheD6InHalf()
        {
            SoloCoOpProfile skirmish = SoloCoOpProfile.AoFSkirmish;
            Assert.Multiple(() =>
            {
                Assert.That(skirmish.SectionForRoll(1), Is.EqualTo(1));
                Assert.That(skirmish.SectionForRoll(3), Is.EqualTo(1));
                Assert.That(skirmish.SectionForRoll(4), Is.EqualTo(2));
                Assert.That(skirmish.SectionForRoll(6), Is.EqualTo(2));
            });
        }

        [Test]
        public void TheD3Books_MapEachRollToItsOwnSection()
        {
            SoloCoOpProfile gf = SoloCoOpProfile.GrimdarkFuture;
            Assert.Multiple(() =>
            {
                Assert.That(gf.SectionForRoll(1), Is.EqualTo(1));
                Assert.That(gf.SectionForRoll(2), Is.EqualTo(2));
                Assert.That(gf.SectionForRoll(3), Is.EqualTo(3));
            });
        }

        [Test]
        public void ArtilleryAndTransportClauses_ExistOnlyWhereTheBookPrintsThem()
        {
            Assert.Multiple(() =>
            {
                // Artillery: GF and AoF only - the skirmish games have no Artillery rule.
                Assert.That(SoloCoOpProfile.GrimdarkFuture.HasArtilleryClause, Is.True);
                Assert.That(SoloCoOpProfile.AgeOfFantasy.HasArtilleryClause, Is.True);
                Assert.That(SoloCoOpProfile.Firefight.HasArtilleryClause, Is.False);
                Assert.That(SoloCoOpProfile.AoFSkirmish.HasArtilleryClause, Is.False);

                // Transport: Grimdark Future alone, including against AoF.
                Assert.That(SoloCoOpProfile.GrimdarkFuture.HasTransportClause, Is.True);
                Assert.That(SoloCoOpProfile.AgeOfFantasy.HasTransportClause, Is.False);
                Assert.That(SoloCoOpProfile.Firefight.HasTransportClause, Is.False);
                Assert.That(SoloCoOpProfile.AoFSkirmish.HasTransportClause, Is.False);
            });
        }

        [Test]
        public void BlastAndDeadly_SplitAlongScale_NotAlongSetting()
        {
            // The one place the two skirmish games DO agree with each other rather than with their parents.
            Assert.Multiple(() =>
            {
                Assert.That(SoloCoOpProfile.GrimdarkFuture.Blast,
                    Is.EqualTo(EBlastPriority.MultiModelWithoutToughFirst));
                Assert.That(SoloCoOpProfile.AgeOfFantasy.Blast,
                    Is.EqualTo(EBlastPriority.MultiModelWithoutToughFirst));
                Assert.That(SoloCoOpProfile.Firefight.Blast, Is.EqualTo(EBlastPriority.MultiModelFirst));
                Assert.That(SoloCoOpProfile.AoFSkirmish.Blast, Is.EqualTo(EBlastPriority.MultiModelFirst));

                Assert.That(SoloCoOpProfile.GrimdarkFuture.Deadly,
                    Is.EqualTo(EDeadlyPriority.SingleModelWithToughFirst));
                Assert.That(SoloCoOpProfile.AgeOfFantasy.Deadly,
                    Is.EqualTo(EDeadlyPriority.SingleModelWithToughFirst));
                Assert.That(SoloCoOpProfile.Firefight.Deadly, Is.EqualTo(EDeadlyPriority.SingleModelFirst));
                Assert.That(SoloCoOpProfile.AoFSkirmish.Deadly, Is.EqualTo(EDeadlyPriority.SingleModelFirst));
            });
        }

        [Test]
        public void EachSystemGetsItsOwnBook()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SoloCoOpProfile.For(EGameSystem.GrimdarkFuture),
                    Is.SameAs(SoloCoOpProfile.GrimdarkFuture));
                Assert.That(SoloCoOpProfile.For(EGameSystem.GFFirefight),
                    Is.SameAs(SoloCoOpProfile.Firefight));
                Assert.That(SoloCoOpProfile.For(EGameSystem.AgeOfFantasy),
                    Is.SameAs(SoloCoOpProfile.AgeOfFantasy));
                Assert.That(SoloCoOpProfile.For(EGameSystem.AoFSkirmish),
                    Is.SameAs(SoloCoOpProfile.AoFSkirmish));
            });
        }

        [Test]
        public void TheQuestSystemsHaveNoSoloBook_AndAskingIsAWiringMistake()
        {
            // Their core rules carry their own flowchart, which StarQuestAiBrain implements. Returning
            // Grimdark Future's rather than throwing keeps a mis-wire from faulting a game, and the
            // profile's own docs record it as a mistake rather than a supported combination.
            Assert.That(SoloCoOpProfile.For(EGameSystem.GFStarQuest),
                Is.SameAs(SoloCoOpProfile.GrimdarkFuture));
            Assert.That(SoloCoOpProfile.For(EGameSystem.AoFQuest),
                Is.SameAs(SoloCoOpProfile.GrimdarkFuture));
        }

        // ── The squares rule ────────────────────────────────────────────────────────────────────────

        [Test]
        public void SixSquares_AreThreeAcrossTheLongAxisByTwo()
        {
            RectangularZone band = Band();   // 72 wide, 12 deep

            (int cols, int rows) = SoloCoOpObjectivePlacer.Grid(band, 6);

            Assert.That(cols, Is.EqualTo(3), "the wide axis takes the extra column.");
            Assert.That(rows, Is.EqualTo(2));
        }

        [Test]
        public void SixSquares_OnATallBand_TurnTheGridAround()
        {
            RectangularZone tall = Band(left: 18f, right: 30f, bottom: 0f, top: 72f);

            (int cols, int rows) = SoloCoOpObjectivePlacer.Grid(tall, 6);

            Assert.That(cols, Is.EqualTo(2));
            Assert.That(rows, Is.EqualTo(3));
        }

        [Test]
        public void FourSquares_AreTwoByTwo()
        {
            (int cols, int rows) = SoloCoOpObjectivePlacer.Grid(Band(), 4);

            Assert.That(cols, Is.EqualTo(2));
            Assert.That(rows, Is.EqualTo(2));
        }

        [Test]
        public void CellCentres_CoverTheWholeBand_AndSitInsideIt()
        {
            RectangularZone band = Band();
            List<Position> centres = SoloCoOpObjectivePlacer.CellCentres(band, 6);

            Assert.That(centres, Has.Count.EqualTo(6));
            foreach (Position centre in centres)
            {
                Assert.That(centre.x, Is.InRange(band.Left, band.Right));
                Assert.That(centre.z, Is.InRange(band.Bottom, band.Top));
            }

            // Three distinct columns, two distinct rows - a grid, not six points in a line.
            Assert.That(centres.Select(c => MathF.Round(c.x, 2)).Distinct().Count(), Is.EqualTo(3));
            Assert.That(centres.Select(c => MathF.Round(c.z, 2)).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void ARolledSquare_PutsTheMarkerAtItsCentre()
        {
            RectangularZone band = Band();
            List<Position> centres = SoloCoOpObjectivePlacer.CellCentres(band, 6);

            Position placed = SoloCoOpObjectivePlacer.Choose(band, 6, _ => true, Rolls(4));

            Assert.That(placed.x, Is.EqualTo(centres[3].x).Within(0.01f));
            Assert.That(placed.z, Is.EqualTo(centres[3].z).Within(0.01f));
        }

        [Test]
        public void AnIllegalSquare_MovesTheMarkerTowardAnotherOne_JustEnough()
        {
            // "If that's not possible roll for another random square and move the objective toward it just
            // enough to be in a valid position." So the result is neither the first centre (illegal) nor
            // necessarily the second (further than needed) - it is the first legal point on the way.
            RectangularZone band = Band();
            List<Position> centres = SoloCoOpObjectivePlacer.CellCentres(band, 6);
            Position blocked = centres[0];

            bool IsLegal(Position p) => Position.GetDistance2D(p, blocked) > 6f;

            Position placed = SoloCoOpObjectivePlacer.Choose(band, 6, IsLegal, Rolls(1, 3));

            Assert.That(IsLegal(placed), Is.True, "it has to end up somewhere legal.");
            Assert.That(Position.GetDistance2D(placed, blocked), Is.LessThan(12f),
                "and only just far enough - 'just enough to be in a valid position', not all the way.");
        }

        [Test]
        public void WithNowhereLegalAtAll_ItStillReturnsAPoint()
        {
            // The caller's validator is the authority and will fault with a message about the layout; the
            // placer must not throw from inside a loop and leave that message unwritten.
            RectangularZone band = Band();

            Position placed = SoloCoOpObjectivePlacer.Choose(band, 6, _ => false, Rolls(1));

            Assert.That(placed.x, Is.InRange(band.Left, band.Right));
            Assert.That(placed.z, Is.InRange(band.Bottom, band.Top));
        }

        [Test]
        public void SkirmishPlacesInFourSquares_NotSix()
        {
            // The end-to-end shape of the one difference most likely to be missed.
            RectangularZone band = Band();

            var sixes = SoloCoOpObjectivePlacer.CellCentres(band, SoloCoOpProfile.GrimdarkFuture.ObjectiveSquares);
            var fours = SoloCoOpObjectivePlacer.CellCentres(band, SoloCoOpProfile.AoFSkirmish.ObjectiveSquares);

            Assert.That(sixes, Has.Count.EqualTo(6));
            Assert.That(fours, Has.Count.EqualTo(4));
        }
    }
}
