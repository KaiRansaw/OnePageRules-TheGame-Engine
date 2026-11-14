using FDG.Ai;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// The Advanced Rulebooks' (3.6.0) Generic terrain generator ends: "After terrain has been placed, roll-off to
    /// see who goes first, and then players alternate in picking D3 pieces of terrain each, which count as Dangerous
    /// Terrain." A whole AI-vs-AI game on a small map: only a played game proves the step runs between terrain and
    /// objectives and that the picks stay on the table.
    /// </summary>
    [TestFixture]
    public class PickDangerousTerrainTests
    {
        private static TerrainPieceEntry Piece(string name, ETerrainType rules, float x, float z) => new()
        {
            Name = name, TerrainType = rules, HeightInches = 1f,
            Shape = new RectangularZone(x - 2, x + 2, z - 2, z + 2),
        };

        private static string WriteMap()
        {
            var layout = new TerrainLayoutFile
            {
                Name = "Dangerous picks",
                Pieces =
                {
                    Piece("Forest", ETerrainType.Difficult | ETerrainType.Cover, 12, 24),
                    Piece("Forest", ETerrainType.Difficult | ETerrainType.Cover, 24, 24),
                    Piece("Ruins", ETerrainType.Cover, 48, 24),
                    Piece("Field", ETerrainType.Difficult, 60, 24),
                    Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 36, 30),
                    Piece("Lava", ETerrainType.Dangerous, 36, 18),
                },
            };
            string path = Path.Combine(Path.GetTempPath(), $"fdg-danger-{Guid.NewGuid():N}{TerrainLayoutFile.EXTENSION_WITH_PERIOD}");
            File.WriteAllText(path, TerrainLayoutLoader.SerializeToJson(layout));
            return path;
        }

        private static async Task<List<ITerrain>> PlayOn(string mapPath, bool pick)
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
            settings.PickDangerousTerrain = pick;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            return store.GetAllDataBindings<TerrainData>().Select(b => (ITerrain)b.GetValue()).ToList();
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task PlayersPickD3PiecesEach_ToCountAsDangerous()
        {
            string path = WriteMap();
            try
            {
                List<ITerrain> terrain = await PlayOn(path, pick: true);

                Assert.That(terrain, Has.Count.EqualTo(6), "a pick changes a piece, it never adds or removes one");
                List<ITerrain> madeDangerous = terrain.Where(t => t.Name != "Lava" && t.TerrainType.HasFlag(ETerrainType.Dangerous)).ToList();
                // Two players, D3 each = 2, 4 or 6 picks - but only four pieces can be picked.
                Assert.That(madeDangerous.Count, Is.EqualTo(2).Or.EqualTo(4));
                Assert.That(terrain.Single(t => t.Name == "Building").TerrainType.HasFlag(ETerrainType.Dangerous), Is.False,
                    "Impassable pieces are never offered - nobody can move into them");
                Assert.That(madeDangerous.All(t => t.TerrainType.HasFlag(ETerrainType.Difficult) || t.TerrainType.HasFlag(ETerrainType.Cover)),
                    "a picked piece keeps its own rules and gains Dangerous");
            }
            finally { File.Delete(path); }
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task WithTheStepOff_NothingChanges()
        {
            string path = WriteMap();
            try
            {
                List<ITerrain> terrain = await PlayOn(path, pick: false);
                Assert.That(terrain.Count(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)), Is.EqualTo(1), "only the Lava");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void TheBot_PicksThePieceNearestTheMiddle()
        {
            var request = new PickDangerousTerrainRequest(new PlayerID(Guid.NewGuid()), "Pick", 1, 2, 72, 48, new[]
            {
                new PickDangerousTerrainRequest.TerrainOption("Forest", 10, 10, ETerrainType.Cover),
                new PickDangerousTerrainRequest.TerrainOption("Field", 40, 22, ETerrainType.Difficult),
                new PickDangerousTerrainRequest.TerrainOption("Hill", 60, 40, ETerrainType.Cover),
            });
            Assert.That(AiPickDangerousTerrainResolver.Pick(request), Is.EqualTo(1));
        }

        [Test]
        public void IndexOf_FindsAPieceByNameAndCentre()
        {
            var forest = new TerrainData(ETerrainType.Cover, new RectangularZone(8, 12, 8, 12), 1f, "Forest");
            var request = new PickDangerousTerrainRequest(new PlayerID(Guid.NewGuid()), "Pick", 1, 1, 72, 48, new[]
            {
                new PickDangerousTerrainRequest.TerrainOption("Forest", 30, 30, ETerrainType.Cover),
                new PickDangerousTerrainRequest.TerrainOption("Forest", 10, 10, ETerrainType.Cover),
            });
            Assert.That(request.IndexOf(forest), Is.EqualTo(1));
        }
    }
}
