using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Climbing (Advanced Rulebooks 3.6.0 Climbable / Steep; Kai 2026-11-22: climbing up and down both cost, anyone may
    /// climb where the base fits flat). A 2" tall wall stands across x 9..10; a 2" tall 4"x4" block at x 9..13.
    /// </summary>
    [TestFixture]
    public class ClimbingTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private DataBinding<ModelData> Model(Position at, float radius = 0.5f)
        {
            var data = new ModelData(baseRadiusInches: radius, weapons: new List<Weapon>(), initialPosition: at, gameDataStore: _store);
            return _store.GetDataBinding<ModelData>(_store.Create(data));
        }

        private static ITerrain Wall(float h = 2f) => new TerrainData(ETerrainType.Climbable, new RectangularZone(9, 10, -10, 10), h);
        private static ITerrain Block(float h = 2f) => new TerrainData(ETerrainType.Climbable, new RectangularZone(9, 13, -2, 2), h);

        private static List<ReasonForInvalidMove> Errors(DataBinding<ModelData> model, Position to, float max, params ITerrain[] terrain)
        {
            MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { new ModelMoveEntry(model, new List<Position> { to }) },
                max, terrain.ToList(), out List<ReasonForInvalidMove> errors);
            return errors;
        }

        [Test]
        public void ClimbingOverAWall_CostsUpAndDown()
        {
            // 13" flat + 2" up + 2" down = 17".
            Assert.That(Climbing.ClimbAlong(new[] { new Float2(3, 0), new Float2(16, 0) }, new[] { Wall() }), Is.EqualTo(4f).Within(1e-3));
            Assert.That(Errors(Model(new Position(3, 0)), new Position(16, 0), 17f, Wall()), Is.Empty);
            Assert.That(Errors(Model(new Position(3, 0)), new Position(16, 0), 16f, Wall()).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.OutOfMoveRange));
        }

        [Test]
        public void ShortTerrain_UpTo1Inch_IsIgnored()
        {
            Assert.That(Climbing.ClimbAlong(new[] { new Float2(3, 0), new Float2(16, 0) }, new[] { Wall(1f) }), Is.EqualTo(0f));
            Assert.That(Errors(Model(new Position(3, 0)), new Position(16, 0), 13f, Wall(1f)), Is.Empty);
        }

        [Test]
        public void EndingHalfOnAWall_IsMidClimb_EndingFlatOnATop_IsFine()
        {
            Assert.That(Errors(Model(new Position(3, 0)), new Position(9.2f, 0), 12f, Wall()).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.EndedMidClimb), "half on the wall, half off");
            Assert.That(Errors(Model(new Position(3, 0)), new Position(11, 0), 12f, Block()), Is.Empty, "whole base on the block's top");
            Assert.That(Errors(Model(new Position(3, 0)), new Position(8.7f, 0), 12f, Block()).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.EndedMidClimb), "hanging over the block's edge");
        }

        [Test]
        public void ABaseTooBigForTheTop_CannotStopThere()
        {
            Assert.That(Errors(Model(new Position(3, 0), radius: 2.5f), new Position(11, 0), 12f, Block()).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.EndedMidClimb));
        }

        [Test]
        public void ImpassableClimbable_OverThreeInches_IsNotClimbed()
        {
            var tall = new TerrainData(TerrainBook.WithClimbableHeight(ETerrainType.Climbable, 4f), new RectangularZone(9, 10, -10, 10), 4f);
            Assert.That(Errors(Model(new Position(3, 0)), new Position(16, 0), 30f, tall).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.MovingThroughImpassibleTerrain));
        }

        [Test]
        public void PartHeights_ClimbOnlyTheTallPart()
        {
            var ruin = new TerrainData(ETerrainType.Climbable, new CompositeZone(new List<IZone>
            {
                new RaisedRectZone(9, 10, -10, 0, 2.5f),
                new RaisedRectZone(9, 10, 0, 10, 0.5f),
            }), 2.5f);
            Assert.That(Climbing.ClimbAlong(new[] { new Float2(3, -5), new Float2(16, -5) }, new[] { ruin }), Is.EqualTo(5f).Within(1e-3));
            Assert.That(Climbing.ClimbAlong(new[] { new Float2(3, 5), new Float2(16, 5) }, new[] { ruin }), Is.EqualTo(0f), "the low part is ignored");
        }

        [Test]
        public void Steep_IsDifficultGoingOnto_OpenComingOff()
        {
            var hill = new TerrainData(ETerrainType.Steep, new RectangularZone(9, 15, -3, 3), 1.5f);
            Assert.That(Errors(Model(new Position(3, 0)), new Position(12, 0), 12f, hill).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.ExceededDifficultTerrainMoveLimit), "9\" onto a hill");
            Assert.That(Errors(Model(new Position(12, 0)), new Position(24, 0), 12f, hill), Is.Empty, "coming off is open ground");
            Assert.That(Climbing.StandingHeight(new[] { hill }, new Float2(12, 0)), Is.EqualTo(1.5f));
        }

        [Test]
        public void AModelOnTop_SeesOverTheWallInFront()
        {
            VolumetricSight.On = true;
            try
            {
                var block = Block(2f);
                var wall = new TerrainData(ETerrainType.Blocking, new RectangularZone(15, 16, -10, 10), 2f);
                var terrain = new List<ITerrain> { block, wall };
                SightEnd target = SightEnd.Point(new Position(20, 0));
                Assert.That(VolumetricSight.AnyClearLine(SightEnd.Point(new Position(11, 0)), target, terrain), Is.True, "from the block's top");
                Assert.That(VolumetricSight.AnyClearLine(SightEnd.Point(new Position(7, 0)), target, terrain), Is.False, "from the ground");
            }
            finally { GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture); }
        }

        [Test]
        public void Coherency_IsThreeInches_BetweenModelsAtDifferentHeights()
        {
            List<ReasonForInvalidMove> Coherency(params ITerrain[] terrain)
            {
                var a = Model(new Position(5, 0));
                var b = Model(new Position(4, 0));
                var moves = new List<ModelMoveEntry>
                {
                    new ModelMoveEntry(a, new List<Position> { new Position(11, 0) }),
                    new ModelMoveEntry(b, new List<Position> { new Position(7.2f, 0) }),
                };
                MovementUtilities.ValidatePaths(moves, 12f, terrain.ToList(), out List<ReasonForInvalidMove> errors);
                return errors;
            }
            Assert.That(Coherency(Block()).Select(e => e.ErrorReasonType), Does.Not.Contain(EErrorReasonType.TooFarFromAnyUnitModel),
                "1.8\" apart, one on the block's top and one below: within the book's 3\"");
            var flat = new TerrainData(ETerrainType.Cover, new RectangularZone(30, 31, 30, 31), 1f);
            Assert.That(Coherency(flat).Select(e => e.ErrorReasonType), Does.Contain(EErrorReasonType.TooFarFromAnyUnitModel),
                "the same gap on flat ground breaks the 1\" rule");
        }

        [Test]
        public void AModelOnAWallTop_DoesNotBlockSightUnderneath_AtGroundLevel()
        {
            var raised = new ModelSightBlocker(new CircularZone(10, 0, 0.6f), 1.25f, bottomInches: 3f);
            var onGround = new ModelSightBlocker(new CircularZone(10, 0, 0.6f), 1.25f);
            Float3 p = new Float3(0, 0.5f, 0), q = new Float3(20, 0.5f, 0);
            SightEnd a = SightEnd.Point(new Position(0, 0)), b = SightEnd.Point(new Position(20, 0));
            Assert.That(VolumetricSight.BlocksSegment(raised, p, q, a, b), Is.False);
            Assert.That(VolumetricSight.BlocksSegment(onGround, p, q, a, b), Is.True);
        }

        [Test]
        public void APush_GoesDownForFree_ButNotUp()
        {
            var block = Block(2.5f);
            var onTop = Model(new Position(11, 0));
            Assert.That(Errors(onTop, new Position(14.5f, 0), 3.5f, block).Select(e => e.ErrorReasonType),
                Does.Contain(EErrorReasonType.OutOfMoveRange), "walking off costs the climb down");
            using (Climbing.Pushed())
            {
                Assert.That(Errors(onTop, new Position(14.5f, 0), 3.5f, block), Is.Empty, "pushed off: it falls instead");
                Assert.That(Errors(Model(new Position(7, 0)), new Position(10.5f, 0), 3.5f, block).Select(e => e.ErrorReasonType),
                    Does.Contain(EErrorReasonType.OutOfMoveRange), "a push is no climb up");
            }
        }

        [Test]
        public void Falling_IsOneDiePerWholeInch_From2Inches()
        {
            var block = Block(2.5f);
            var landed = Model(new Position(15, 0));
            var model = landed.GetValue();
            var falls = Falling.Falls(new Dictionary<IModel, float> { [model] = 2.5f }, new[] { block });
            Assert.That(falls.Select(f => f.Dice), Is.EqualTo(new[] { 2 }));
            Assert.That(Falling.Falls(new Dictionary<IModel, float> { [model] = 1.5f }, new[] { block }), Is.Empty, "under 2\" is no fall");
        }
    }
}
