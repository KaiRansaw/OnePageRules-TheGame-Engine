using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-07: "deployment is ONLY picked in the lobby." Maps drawn by the old map maker can still
    /// carry deployment zones in their file, and until this change a map with a zone for every team took
    /// over deployment and silently overrode the lobby's Deployment pick. Those zones are now ignored.
    ///
    /// <para>
    /// A whole AI-vs-AI game rather than a call into the zone code: the zones are chosen by a stage deep
    /// inside deployment, and only a played game proves which ones the armies actually stood in. The map's
    /// zones are two narrow strips down the SHORT edges - a shape no lobby pick produces - so the recorded
    /// zones can only match the pick if the map was ignored.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MapZonesIgnoredTests
    {
        private const string OldMapJson = @"{
  ""Name"": ""Old map with zones"",
  ""Pieces"": [],
  ""Objectives"": [],
  ""DeploymentZones"": [
    { ""Left"": 0.0, ""Right"": 10.0, ""Bottom"": 0.0, ""Top"": 48.0 },
    { ""Left"": 62.0, ""Right"": 72.0, ""Bottom"": 0.0, ""Top"": 48.0 }
  ]
}";

        private static async Task<IReadOnlyDictionary<int, ZoneRect>?> PlayOn(string mapPath, EDeploymentStyle style)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            const int seed = 7311;

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
            settings.DeploymentStyle = style;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            return GameProgressUtilities.DeploymentZones(store);
        }

        private static string WriteOldMap()
        {
            string path = Path.Combine(Path.GetTempPath(), $"fdg-oldzones-{Guid.NewGuid():N}{TerrainLayoutFile.EXTENSION_WITH_PERIOD}");
            File.WriteAllText(path, OldMapJson);
            Assert.That(TerrainLayoutLoader.TryLoadFromFile(path, out string? error)?.DeploymentZones, Has.Count.EqualTo(2),
                "sanity: the old map must load WITH its two zones, or this test proves nothing. " + error);
            return path;
        }

        private static (float, float, float, float) Bounds(ZoneRect z) =>
            (MathF.Round(z.Left, 2), MathF.Round(z.Right, 2), MathF.Round(z.Bottom, 2), MathF.Round(z.Top, 2));

        [Test]
        [CancelAfter(180_000)]
        public async Task AMapsOldDeploymentZonesAreIgnored_TheLobbysPickIsUsed()
        {
            string path = WriteOldMap();
            try
            {
                const EDeploymentStyle pick = EDeploymentStyle.GroundWar;
                IReadOnlyDictionary<int, ZoneRect>? zones = await PlayOn(path, pick);

                Assert.That(zones, Is.Not.Null);
                (RectangularZone a, RectangularZone b) = AdvancedDeployment.Zones(pick, EGameSystem.GrimdarkFuture,
                    GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
                var expected = new[] { Bounds(ZoneRect.From(a)), Bounds(ZoneRect.From(b)) }.OrderBy(x => x).ToList();
                var actual = zones!.Values.Select(Bounds).OrderBy(x => x).ToList();
                Assert.That(actual, Is.EqualTo(expected),
                    "the armies must deploy in the lobby's Deployment pick, not the map's own zones.");
                Assert.That(actual, Does.Not.Contain((0f, 10f, 0f, 48f)), "the map's short-edge strip was used.");
            }
            finally { File.Delete(path); }
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task WithTheCorePick_AnOldMapStillDeploysAlongTheLongEdges()
        {
            string path = WriteOldMap();
            try
            {
                IReadOnlyDictionary<int, ZoneRect>? zones = await PlayOn(path, EDeploymentStyle.Core);

                Assert.That(zones, Is.Not.Null);
                float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
                float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
                var actual = zones!.Values.Select(Bounds).OrderBy(x => x).ToList();
                Assert.That(actual, Is.EqualTo(new[] { (0f, w, 0f, d), (0f, w, h - d, h) }.OrderBy(x => x).ToList()),
                    "Core is 12\" from each long table edge, whatever the map file says.");
            }
            finally { File.Delete(path); }
        }
    }
}
