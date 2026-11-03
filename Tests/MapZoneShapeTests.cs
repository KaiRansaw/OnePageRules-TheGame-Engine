using System;
using System.Collections.Generic;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The step that turns what the map maker SAVED into what the game DEPLOYS into.
    ///
    /// <para>
    /// Worth its own tests because it is the seam where an L-shaped zone could quietly become its
    /// bounding box - the deployment pipeline is typed to <c>RectangularZone</c>, and "just make a
    /// rectangle from the corners" compiles, runs, and hands both players the notch. Every other test
    /// in this area would still pass.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MapZoneShapeTests
    {
        [Test]
        public void AnOrdinaryZoneBecomesAPlainRectangle()
        {
            RectangularZone zone = ChooseMapSideStage.BuildMapZone(new MapDeploymentZoneEntry
            {
                Left = 0f, Right = 72f, Bottom = 0f, Top = 12f,
            });

            Assert.That(zone, Is.Not.InstanceOf<MultiRectZone>(),
                "a one-rectangle zone carries no extra machinery.");
            Assert.That(zone.Right, Is.EqualTo(72f));
        }

        [Test]
        public void AnLShapedZoneBecomesAnLShapedZone()
        {
            RectangularZone zone = ChooseMapSideStage.BuildMapZone(new MapDeploymentZoneEntry
            {
                Left = 0f, Right = 20f, Bottom = 0f, Top = 6f,
                ExtraParts = { new MapZoneRect { Left = 0f, Right = 6f, Bottom = 6f, Top = 20f } },
            });

            Assert.That(zone, Is.InstanceOf<MultiRectZone>());
            Assert.That(zone.IsPointWithinZone(new Float2(18f, 3f)), Is.True, "in the band.");
            Assert.That(zone.IsPointWithinZone(new Float2(3f, 18f)), Is.True, "up the arm.");
            Assert.That(zone.IsPointWithinZone(new Float2(18f, 18f)), Is.False,
                "the notch - if this is inside, the L became its bounding box somewhere.");
        }

        /// <summary>
        /// A rectangle given back to front still works. The map maker normalises as it saves, but the
        /// file is plain JSON somebody may hand-edit, and RectangularZone's constructor throws on an
        /// inverted rectangle - mid-deployment, which is the worst possible moment.
        /// </summary>
        [Test]
        public void ABackwardsRectangleIsNormalisedRatherThanThrowing()
        {
            RectangularZone zone = ChooseMapSideStage.BuildMapZone(new MapDeploymentZoneEntry
            {
                Left = 72f, Right = 0f, Bottom = 12f, Top = 0f,
            });

            Assert.That(zone.Left, Is.EqualTo(0f));
            Assert.That(zone.Right, Is.EqualTo(72f));
            Assert.That(zone.IsPointWithinZone(new Float2(36f, 6f)), Is.True);
        }

        /// <summary>
        /// A zero-area piece - something half-drawn, or a hand-edited file - is skipped rather than
        /// thrown on. The same input used to reach RectangularZone's constructor and take the game
        /// down in the middle of deployment.
        /// </summary>
        [Test]
        public void AZeroAreaPieceIsSkipped()
        {
            RectangularZone zone = ChooseMapSideStage.BuildMapZone(new MapDeploymentZoneEntry
            {
                Left = 0f, Right = 20f, Bottom = 0f, Top = 6f,
                ExtraParts = { new MapZoneRect { Left = 5f, Right = 5f, Bottom = 6f, Top = 20f } },
            });

            Assert.That(zone, Is.Not.InstanceOf<MultiRectZone>(),
                "the empty arm was dropped, leaving a plain rectangle.");
            Assert.That(zone.Top, Is.EqualTo(6f));
        }

        [Test]
        public void AZoneWithNothingUsableIsRefusedLoudly()
        {
            // Better than a silent zero-size zone, which would reach the players as "you may deploy
            // nowhere" with no explanation anywhere.
            Assert.Throws<InvalidOperationException>(() =>
                ChooseMapSideStage.BuildMapZone(new MapDeploymentZoneEntry
                {
                    Left = 5f, Right = 5f, Bottom = 5f, Top = 5f,
                }));
        }
    }
}
