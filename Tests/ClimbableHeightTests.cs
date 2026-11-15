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
    /// The Advanced Rulebooks (3.6.0, Terrain): "Terrain features that are over 3" tall count as Impassable terrain, but
    /// any terrain piece that is up to 3" tall may be climbed as part of a unit's move." The first half needs no heights,
    /// so it is in the game (Kai, 2026-11-14: Warhammer walls didn't stop anyone). Star Quest's book has no Climbable.
    /// </summary>
    [TestFixture]
    public class ClimbableHeightTests
    {
        [Test]
        public void OverThreeInches_CountsAsImpassable_UpToThree_DoesNot()
        {
            Assert.That(TerrainBook.WithClimbableHeight(ETerrainType.Climbable, 3.5f).HasFlag(ETerrainType.Impassible), Is.True);
            Assert.That(TerrainBook.WithClimbableHeight(ETerrainType.Climbable, 3f).HasFlag(ETerrainType.Impassible), Is.False, "exactly 3\" can be climbed");
            Assert.That(TerrainBook.WithClimbableHeight(ETerrainType.Cover, 9f), Is.EqualTo(ETerrainType.Cover), "only Climbable pieces");
            Assert.That(TerrainBook.Plays(ETerrainType.Climbable, EGameSystem.GrimdarkFuture), Is.True);
            Assert.That(TerrainBook.Plays(ETerrainType.Climbable, EGameSystem.GFStarQuest), Is.False);
            Assert.That(TerrainBook.NeedsHeights(ETerrainType.Climbable), Is.False);
            Assert.That(TerrainBook.NeedsHeights(ETerrainType.Steep), Is.True);
        }

        private static TerrainPieceEntry Wall(string name, float height, float x) => new()
        {
            Name = name, TerrainType = ETerrainType.Climbable, HeightInches = height,
            Shape = new RectangularZone(x - 4, x + 4, 23.5f, 24.5f),
        };

        [Test]
        [CancelAfter(180_000)]
        public async Task ATallWall_IsImpassableOnTheTable_ALowOneIsNot()
        {
            var layout = new TerrainLayoutFile { Name = "Walls", Pieces = { Wall("Tall wall", 5f, 20), Wall("Low wall", 2f, 52) } };
            string path = Path.Combine(Path.GetTempPath(), $"fdg-walls-{Guid.NewGuid():N}{TerrainLayoutFile.EXTENSION_WITH_PERIOD}");
            File.WriteAllText(path, TerrainLayoutLoader.SerializeToJson(layout));
            try
            {
                var store = GameDataStore.GameDataStoreBuilder.GetDefault();
                var bus = new InProcessBus();
                const int seed = 911;
                var slots = new PlayerSlot[2];
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                    var aiGame = new FDGGame_AsLocal(store, bus);
                    slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController($"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
                }
                GameSettings settings = GameSettings.GetDefault();
                settings.RandomnessType = ERandomnessType.Probabilistic;
                settings.DiceSeed = seed;
                settings.TerrainPlacementMode = ETerrainPlacementMode.LoadFromFile;
                settings.TerrainLayoutPath = path;
                var completed = new TaskCompletionSource<GameResult>();
                var server = new FDGServer(store, bus, settings, slots);
                server.OnGameCompleted += r => completed.TrySetResult(r);
                Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
                Assert.That(finished, Is.SameAs(completed.Task));
                var terrain = store.GetAllDataBindings<TerrainData>().Select(b => (ITerrain)b.GetValue()).ToList();
                Assert.That(terrain.Single(t => t.Name == "Tall wall").TerrainType.HasFlag(ETerrainType.Impassible), Is.True);
                Assert.That(terrain.Single(t => t.Name == "Low wall").TerrainType.HasFlag(ETerrainType.Impassible), Is.False);
            }
            finally { File.Delete(path); }
        }
    }
}
