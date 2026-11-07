using FDG.Data;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The game-length setting (house rule when not 4) and Fast-Play Simplified Terrain's cover clause:
    /// "the only way for units to get Cover is by entering a piece of terrain" (GF Advanced 3.6.0, p17).
    /// </summary>
    [TestFixture]
    public class GameLengthAndSimplifiedTerrainTests
    {
        [Test]
        public void AStandardGame_TakesTheSetLength_HordeKeepsItsOwn()
        {
            GameSettings s = GameSettings.GetDefault();
            Assert.That(HordeRules.RoundLimit(s), Is.EqualTo(4), "the books' four by default");
            s.NumberOfRounds = 6;
            Assert.That(HordeRules.RoundLimit(s), Is.EqualTo(6));
            s.GameMode = EGameMode.Horde;
            Assert.That(HordeRules.RoundLimit(s), Is.EqualTo(GameWideConstants.HORDE_NUMBER_OF_ROUNDS),
                "Horde's twelve rounds are its own book's and do not move");
        }

        [Test]
        public void SimplifiedTerrain_CoverOnlyInside_NotBehind()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            IUnit Unit(float x, float z)
            {
                var m = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(x, z), store)));
                return store.GetDataBinding<UnitData>(store.Create(new UnitData(new PlayerID(Guid.NewGuid()), "U", 4, 4,
                    new List<DataBinding<ModelData>> { m }))).GetValue();
            }
            IUnit shooter = Unit(10f, 10f);
            IUnit behind = Unit(22f, 10f);
            IUnit inside = Unit(17f, 20f);
            var terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(17f, 10f), 2f)),
                new TerrainData(ETerrainType.Cover | ETerrainType.Difficult, new CircularZone(new Float2(17f, 20f), 2f)),
            };

            Assert.That(CoverMajority.Evaluate(shooter, behind, terrain, false).HasCover, Is.True,
                "test setup: under the core rules a unit behind cover is in cover");
            Assert.That(CoverMajority.Evaluate(shooter, behind, terrain, false, null, simplifiedTerrain: true).HasCover,
                Is.False, "Simplified Terrain: behind is not inside");
            Assert.That(CoverMajority.Evaluate(shooter, inside, terrain, false, null, simplifiedTerrain: true).HasCover,
                Is.True, "standing inside a piece is cover");
        }
    }
}
