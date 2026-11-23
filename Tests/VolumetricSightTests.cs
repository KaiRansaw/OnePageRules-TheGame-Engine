using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Volumetric Line of Sight (Advanced Rulebooks 3.6.0, Tournament Guidelines). Geometry: shooter at (0,5), target at
    /// (20,5), whatever stands between at x 9..11.
    /// </summary>
    [TestFixture]
    public class VolumetricSightTests
    {
        private static readonly Position A = new Position(0, 5);
        private static readonly Position B = new Position(20, 5);

        private static SightEnd Model(Position at, float baseMm)
            => new SightEnd(at, baseMm / VolumetricSight.MmPerInch / 2f, VolumetricSight.HeightForBaseMm(baseMm));

        private static TerrainData Wall(float height, ETerrainType type = ETerrainType.Blocking)
            => new TerrainData(type, new RectangularZone(9, 11, 0, 10), height);

        private static ITerrain ModelInTheWay(float baseMm)
            => new ModelSightBlocker(new CircularZone(10, 5, baseMm / VolumetricSight.MmPerInch / 2f),
                VolumetricSight.HeightForBaseMm(baseMm));

        [SetUp]
        public void On() => VolumetricSight.On = true;

        [TearDown]
        public void Restore() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        [TestCase(25f, 1f)]
        [TestCase(32f, 1.25f)]
        [TestCase(40f, 1.5f)]
        [TestCase(50f, 2f)]
        [TestCase(60f, 3f)]
        [TestCase(100f, 4f)]
        public void BookHeights_AreExact(float mm, float inches)
            => Assert.That(VolumetricSight.HeightForBaseMm(mm), Is.EqualTo(inches).Within(1e-4));

        [TestCase(20f, 1f, TestName = "Under 25mm counts as 25mm")]
        [TestCase(45f, 1.75f, TestName = "Between two rows is a straight line")]
        [TestCase(80f, 3.5f, TestName = "60 to 100mm")]
        [TestCase(160f, 5.5f, TestName = "Over 100mm keeps climbing 1 inch per 40mm")]
        public void OffTheList_Heights(float mm, float inches)
            => Assert.That(VolumetricSight.HeightForBaseMm(mm), Is.EqualTo(inches).Within(1e-4));

        [Test]
        public void Infantry_BehindATallerWall_IsHidden()
        {
            var terrain = new List<ITerrain> { Wall(2f) };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 32), Model(B, 32), terrain),
                Is.EqualTo(ESightLineEffect.Blocking));
        }

        [Test]
        public void Monsters_SeeEachOtherOverALowWall_AndTheTargetHasCover()
        {
            var terrain = new List<ITerrain> { Wall(2f) };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 100), Model(B, 100), terrain),
                Is.EqualTo(ESightLineEffect.Cover), "seen over the wall = behind a Sight Blocker");
        }

        [Test]
        public void OneTallEnd_IsEnough_ForAnyPointToAnyPoint()
        {
            // A 4" model looking down on a 1.25" one 10" past a 2" wall: the line from its top to the infantry's top
            // clears the wall's top at x 9..11 (it is 4 - 2.75 * 9..11 / 20 = 2.76..2.49 high there).
            var terrain = new List<ITerrain> { Wall(2f) };
            Assert.That(LineOfSightUtilities.HasLineOfSight(Model(A, 100), Model(B, 32), terrain), Is.True);
        }

        // Heights read from the models (Kai, 2026-11-22): a ruin with a tall back wall and low rubble in front.
        private static TerrainData Ruin(float lowTop, float tallTop) => new TerrainData(ETerrainType.Blocking,
            new CompositeZone(new List<IZone>
            {
                new RaisedRectZone(9, 11, 0, 4, lowTop),   // more rubble, away from the line
                new RaisedRectZone(9, 11, 4, 6, lowTop),   // low rubble right on the line
                new RaisedRectZone(9, 11, 6, 10, tallTop), // the tall wall, beside the line
            }), tallTop);

        [Test]
        public void PartHeights_SeeOverTheLowPart_BesideTheTallOne()
        {
            var terrain = new List<ITerrain> { Ruin(0.75f, 5f) };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 32), Model(B, 32), terrain),
                Is.Not.EqualTo(ESightLineEffect.Blocking), "1.25\" infantry see over 0.75\" rubble, past the 5\" wall beside it");
        }

        [Test]
        public void PartHeights_TheTallPartStillBlocks()
        {
            var terrain = new List<ITerrain> { Ruin(5f, 5f) };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 32), Model(B, 32), terrain),
                Is.EqualTo(ESightLineEffect.Blocking));
        }

        [Test]
        public void PartHeights_TopAt_IsThePartUnderThePoint()
        {
            var ruin = Ruin(0.75f, 5f);
            Assert.That(TerrainHeights.TopAt(ruin, new Float2(10, 5)), Is.EqualTo(0.75f));
            Assert.That(TerrainHeights.TopAt(ruin, new Float2(10, 8)), Is.EqualTo(5f));
            Assert.That(TerrainHeights.TopAt(ruin, new Float2(3, 5)), Is.Null);
        }

        [Test]
        public void ClassicMethod_StillBlocksEveryWall()
        {
            VolumetricSight.On = false;
            var terrain = new List<ITerrain> { Wall(2f) };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 100), Model(B, 100), terrain),
                Is.EqualTo(ESightLineEffect.Blocking));
        }

        [Test]
        public void APieceWithNoHeight_BlocksAsTheClassicMethodDoes()
        {
            var terrain = new List<ITerrain> { Wall(0f) };
            Assert.That(LineOfSightUtilities.HasLineOfSight(Model(A, 160), Model(B, 160), terrain), Is.False);
        }

        [Test]
        public void BigModels_SeeOverSmallUnits_WithNoCover()
        {
            var terrain = new List<ITerrain> { ModelInTheWay(25) };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 60), Model(B, 60), terrain),
                Is.EqualTo(ESightLineEffect.Clear), "another unit is not a Sight Blocker");
        }

        [Test]
        public void EqualModels_CannotSeeThroughAnotherUnitOfTheSameSize()
        {
            var terrain = new List<ITerrain> { ModelInTheWay(32) };
            Assert.That(LineOfSightUtilities.HasLineOfSight(Model(A, 32), Model(B, 32), terrain), Is.False);
        }

        [Test]
        public void SightPastTheEdgeOfABase_CountsAsAnyPointOfTheVolume()
        {
            // A model of the same size stands just off the centre line (0.3" off, under its 0.63" radius): the classic
            // centre line hits it and nobody sees over it, but a line from the side of one base to the same side of the
            // other passes beside it - "any point of the model's volume to any point of the target model's volume".
            var blocker = new ModelSightBlocker(new CircularZone(10, 5.3f, 32f / VolumetricSight.MmPerInch / 2f),
                VolumetricSight.HeightForBaseMm(32));
            var terrain = new List<ITerrain> { blocker };
            Assert.That(LineOfSightUtilities.HasLineOfSight(Model(A, 32), Model(B, 32), terrain), Is.True);
            VolumetricSight.On = false;
            Assert.That(LineOfSightUtilities.HasLineOfSight(Model(A, 32), Model(B, 32), terrain), Is.False, "classic");
        }

        [Test]
        public void DenseForest_LowerThanTheEyes_IsSeenOver()
        {
            var forest = new List<ITerrain> { Wall(3f, ETerrainType.Dense | ETerrainType.Cover) };
            Assert.That(LineOfSightUtilities.HasLineOfSight(Model(A, 32), Model(B, 32), forest), Is.False, "infantry");
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Model(A, 160), Model(B, 160), forest),
                Is.EqualTo(ESightLineEffect.Cover), "big models over it, through Cover terrain");
        }

        [Test]
        public void SettingOff_InAQuestGame_EvenWhenTicked()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.VolumetricLineOfSight = true;
            settings.GameSystem = EGameSystem.GFStarQuest;
            GameWideConstants.ConfigureTable(settings);
            Assert.That(VolumetricSight.On, Is.False);

            settings.GameSystem = EGameSystem.AgeOfFantasy;
            GameWideConstants.ConfigureTable(settings);
            Assert.That(VolumetricSight.On, Is.True);

            GameWideConstants.ConfigureTable(EGameSystem.AgeOfFantasy);
            Assert.That(VolumetricSight.On, Is.False, "the plain overload (tests' restore) goes back to classic");
        }

        [TestCase(EGameSystem.GrimdarkFuture, 4700)]
        [TestCase(EGameSystem.AgeOfFantasy, 4701)]
        public async Task AGameWithVolumetricSight_PlaysToTheEnd(EGameSystem system, int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs");
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(EAiProfile.SoloCoOp, $"AI {i}",
                    slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            settings.VolumetricLineOfSight = true;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Assert.That(VolumetricSight.On, Is.True, "the server turned the method on for this game");
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
        }
    }
}
