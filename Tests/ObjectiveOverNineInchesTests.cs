using System;
using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-28 (AoF: Skirmish + Mission Cards): auto-placed markers landed exactly 9.0" apart (e.g. (20,28)
    // and (20,19)). Every 3.6.0 book says "over 9" away from other markers" (core rules, Advanced Rulebooks, Mission
    // Cards v3.6.0 "over 9" away from each other"), so exactly 9" is too close.
    [TestFixture]
    public class ObjectiveOverNineInchesTests
    {
        private GameDataStore _store = null!;
        private TableState _table = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _table = new TableState(_store);
        }

        private static RectangularZone Band() => new RectangularZone(left: 0f, right: 48f, bottom: 12f, top: 36f);

        [Test]
        public void ExactlyNineInchesAway_IsTooClose()
        {
            _store.Create(new ObjectiveData(new Position(20f, 28f), _store));
            var result = ObjectivePlacementValidator.Check(new Position(20f, 19f), Band(), 9f,
                _table.Objectives.Objects.ToList(), new List<ITerrain>());
            Assert.That(result, Is.EqualTo(ObjectivePlacementValidity.TooCloseToObjective));
        }

        [Test]
        public void JustOverNineInches_IsFine()
        {
            _store.Create(new ObjectiveData(new Position(20f, 28f), _store));
            var result = ObjectivePlacementValidator.Check(new Position(20f, 18.9f), Band(), 9f,
                _table.Objectives.Objects.ToList(), new List<ITerrain>());
            Assert.That(result, Is.EqualTo(ObjectivePlacementValidity.Valid));
        }

        [Test]
        public void AutoPlacedSixMarkers_AreAllOverNineInchesApart()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var store = GameDataStore.GameDataStoreBuilder.GetDefault();
                var table = new TableState(store);
                var rng = new Random(seed);
                for (int i = 1; i <= 6; i++)
                {
                    bool found = ObjectiveAutoPlacer.TryChoosePlacement(Band(), 9f, i, 6,
                        table.Objectives.Objects.ToList(), new List<ITerrain>(), rng, out Position p);
                    Assert.That(found, Is.True, $"seed {seed}: a spot for marker {i}");
                    foreach (var o in table.Objectives.Objects)
                    {
                        float dx = o.Position.x - p.x, dz = o.Position.z - p.z;
                        Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.GreaterThan(9f), $"seed {seed}: marker {i} vs {o.Position.x},{o.Position.z}");
                    }
                    store.Create(new ObjectiveData(p, store));
                }
            }
        }
    }
}
