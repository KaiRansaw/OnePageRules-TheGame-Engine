using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 (GF Core 3.6.0): the AI flies the 30" minimum along the way it faces when that fits on the table, and
    // turns to the nearest line that fits when it doesn't - it never flies off the table.
    [TestFixture]
    public class AiAircraftAdvanceResolverTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task PicksTheShortestOnTableDistance()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 5)); // facing +z: 30" → (10, 35), on-table
            var request = new AircraftAdvanceRequest(new PlayerID(Guid.NewGuid()), "test", unit,
                new Float2(0f, 1f), 30f, 36f);

            AircraftAdvanceResult result = await new AiAircraftAdvanceResolver().Resolve(request);

            Assert.That(result.FliesOffTable, Is.False);
            Assert.That(result.DistanceInches, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public async Task TurnsAwayFromTheEdge_WhenTheLineAheadIsTooShort()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 40)); // facing +z: under 8" to the top edge
            var request = new AircraftAdvanceRequest(new PlayerID(Guid.NewGuid()), "test", unit,
                new Float2(0f, 1f), 30f, 36f);

            AircraftAdvanceResult result = await new AiAircraftAdvanceResolver().Resolve(request);

            Assert.That(result.FliesOffTable, Is.False, "3.6.0 has no flying off the table.");
            Assert.That(result.Heading, Is.Not.Null);
            var paths = ForcedAircraftMove.BuildPaths(unit, result.Heading!.Value, result.DistanceInches);
            Assert.That(ForcedAircraftMove.WouldLeaveTable(paths), Is.False);
            Assert.That(result.DistanceInches, Is.EqualTo(30f).Within(0.001f));
        }

        private DataBinding<UnitData> MakeUnit(Position pos)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), pos, _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Jet", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
