using FDG.Ai;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-19: "if a wall is L shaped, JUST the L shaped building should be impassable, but it makes the ENTIRE
    /// square of the terrain's footprint impassable". A map piece drawn as a 3D model carries its solid parts
    /// (<see cref="TerrainPieceEntry.Solid"/>); the game plays the rules on those and keeps the footprint only to draw
    /// the model on. A whole game, because only a played game proves the map's file reaches the table that way.
    /// </summary>
    [TestFixture]
    public class TerrainSolidPartsTests
    {
        // An 8" x 8" footprint at (36, 24) whose model is an L: a 1" wall along its left side and one along its far side.
        private static TerrainPieceEntry LWall() => new()
        {
            Name = "L Wall", TerrainType = ETerrainType.Impassible | ETerrainType.Blocking, HeightInches = 3f, Look = "wh:1",
            Shape = new RectangularZone(32, 40, 20, 28),
            Solid = new CompositeZone(new IZone[] { new RectangularZone(32, 33, 20, 28), new RectangularZone(32, 40, 27, 28) }),
        };

        private static string WriteMap(TerrainPieceEntry piece)
        {
            var layout = new TerrainLayoutFile { Name = "L wall", Pieces = { piece } };
            string path = Path.Combine(Path.GetTempPath(), $"fdg-lwall-{Guid.NewGuid():N}{TerrainLayoutFile.EXTENSION_WITH_PERIOD}");
            File.WriteAllText(path, TerrainLayoutLoader.SerializeToJson(layout));
            return path;
        }

        [Test]
        public void TheSolidParts_SurviveTheMapFile()
        {
            string path = WriteMap(LWall());
            try
            {
                TerrainPieceEntry read = TerrainLayoutLoader.TryLoadFromFile(path, out _)!.Pieces.Single();
                Assert.That(read.Solid, Is.InstanceOf<CompositeZone>());
                Assert.That(read.Solid!.IsPointWithinZone(new Float2(32.5f, 24f)), Is.True, "on the wall");
                Assert.That(read.Solid.IsPointWithinZone(new Float2(37f, 23f)), Is.False, "inside the L");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public async Task InAGame_OnlyTheWallIsImpassable_AndTheModelIsDrawnOnTheFootprint()
        {
            string path = WriteMap(LWall());
            try
            {
                TerrainData placed = (await PlayOn(path)).OfType<TerrainData>().Single(t => t.Name == "L Wall");
                Assert.That(placed.IsPointWithinZone(new Float2(32.5f, 24f)), Is.True, "the wall stops models");
                Assert.That(placed.IsPointWithinZone(new Float2(37f, 23f)), Is.False, "the open corner of the L is open ground");
                Assert.That(placed.DrawShape, Is.InstanceOf<RectangularZone>(), "the model still stands on its whole footprint");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public async Task APieceWithoutSolidParts_PlaysItsWholeFootprint()
        {
            TerrainPieceEntry plain = LWall();
            plain.Solid = null;
            string path = WriteMap(plain);
            try
            {
                TerrainData placed = (await PlayOn(path)).OfType<TerrainData>().Single(t => t.Name == "L Wall");
                Assert.That(placed.IsPointWithinZone(new Float2(37f, 23f)), Is.True);
                Assert.That(placed.DrawShape, Is.Null);
            }
            finally { File.Delete(path); }
        }

        private static async Task<List<ITerrain>> PlayOn(string mapPath)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            const int seed = 4471;
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.TerrainPlacementMode = ETerrainPlacementMode.LoadFromFile;
            settings.TerrainLayoutPath = mapPath;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            return store.GetAllDataBindings<TerrainData>().Select(b => (ITerrain)b.GetValue()).ToList();
        }
    }
}
