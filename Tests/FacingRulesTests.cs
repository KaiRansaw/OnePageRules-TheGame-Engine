using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Which way models look (Kai, 2026-11-12): round bases face the enemy side when placed, the way they moved, and
    /// what they attack; a non-round base's facing is part of its footprint, so it only turns when told to.
    /// </summary>
    [TestFixture]
    public class FacingRulesTests
    {
        private IReadWriteableGameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private ModelData Round(float x, float z) => new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);

        private UnitData Unit(params ModelData[] models)
        {
            var bindings = models.Select(m => _store.GetDataBinding<ModelData>(_store.Create(m))).ToList();
            return new UnitData(new PlayerID(System.Guid.NewGuid()), "U", quality: 4, defense: 4, modelBindings: bindings);
        }

        private static void Faces(IModel m, float x, float z) =>
            Assert.That((m.Facing.X, m.Facing.Y), Is.EqualTo((x, z)).Using<(float, float)>((a, b) =>
                MathF.Abs(a.Item1 - b.Item1) < 1e-4f && MathF.Abs(a.Item2 - b.Item2) < 1e-4f));

        [Test]
        public void Placed_RoundBaseFacesTheTableCentre_UnlessAFacingIsGiven()
        {
            var settings = new GameSettings { GameSystem = EGameSystem.GrimdarkFuture, GameMode = EGameMode.Standard, ArmyPoints = 2000 };
            (float w, float h) = TableSizing.Resolve(settings);

            ModelData near = Round(w / 2, 3), far = Round(w / 2, h - 3);
            FacingRules.ApplyPlaced(near, null, settings);
            FacingRules.ApplyPlaced(far, null, settings);
            Faces(near, 0, 1);
            Faces(far, 0, -1);

            ModelData told = Round(w / 2, 3);
            FacingRules.ApplyPlaced(told, new Float2(1, 0), settings);
            Faces(told, 1, 0);
        }

        [Test]
        public void Travel_RoundBaseFacesTheWayItWent_ARectangleKeepsItsFootprint()
        {
            ModelData round = Round(10, 10);
            FacingRules.ApplyTravel(round, round.Position, new Position(10, 4));
            Faces(round, 0, -1);

            var tank = new ModelData(new RectangleBase(3, 5), new List<Weapon>(), new Position(10, 10), _store);
            Float2 before = tank.Facing;
            FacingRules.ApplyTravel(tank, tank.Position, new Position(20, 10));
            Assert.That(tank.Facing, Is.EqualTo(before), "an oval/rectangle base only turns when its move says so");

            FacingRules.ApplyTravel(round, round.Position, round.Position);
            Faces(round, 0, -1); // standing still keeps the last facing
        }

        [Test]
        public void Attack_EachRoundModelFacesTheNearestModelItAttacks()
        {
            ModelData a = Round(0, 0), b = Round(10, 0);
            UnitData shooters = Unit(a, b);
            UnitData target = Unit(Round(0, 20), Round(10, -20));

            FacingRules.FaceTarget(shooters, target);
            Faces(a, 0, 1);  // (0,20) is nearer to a than (10,-20)
            Faces(b, 0, -1); // (10,-20) is nearer to b
        }
    }
}
