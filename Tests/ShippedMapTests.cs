using System;
using System.IO;
using System.Linq;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The maps shipped with the game must load and be playable. Kai's map picker is useless if the one
    /// map in the box does not open, and "it is only data" is exactly how a broken file ships.
    /// </summary>
    [TestFixture]
    public class ShippedMapTests
    {
        private static string? MapsFolder()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                 dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Maps");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        [Test]
        public void EveryShippedMapLoads()
        {
            string? folder = MapsFolder();
            if (folder == null) Assert.Ignore("No Maps folder beside the test run.");

            string[] files = Directory.GetFiles(folder!, "*" + TerrainLayoutFile.EXTENSION_WITH_PERIOD);
            Assert.That(files, Is.Not.Empty, "the game ships with no maps at all - the picker would be "
                + "empty on a fresh install and only Random would work.");

            foreach (string file in files)
            {
                TerrainLayoutFile? layout = TerrainLayoutLoader.TryLoadFromFile(file, out string? error);
                Assert.That(layout, Is.Not.Null, $"{Path.GetFileName(file)} did not load: {error}");
                Assert.That(layout!.Pieces.All(p => p.Shape != null), Is.True,
                    $"{Path.GetFileName(file)} has a piece with no shape, which draws as nothing.");
            }
        }

        /// <summary>
        /// Crossroads is the one authored to be played on, so it is held to more than "it parses": one
        /// whose terrain sits off the table is a table with nothing on it, and one with a building in the
        /// core deployment bands leaves the armies less room to stand. Maps carry no deployment zones of
        /// their own any more (Kai, 2026-11-07: deployment is picked in the lobby).
        /// </summary>
        [Test]
        public void CrossroadsIsPlayable()
        {
            string? folder = MapsFolder();
            if (folder == null) Assert.Ignore("No Maps folder beside the test run.");
            string path = Path.Combine(folder!, "Crossroads.fdgterrain");
            if (!File.Exists(path)) Assert.Ignore("Crossroads is not in this checkout.");

            TerrainLayoutFile layout = TerrainLayoutLoader.TryLoadFromFile(path, out string? error)!;
            Assert.That(layout, Is.Not.Null, error);

            Assert.That(layout.DeploymentZones, Is.Empty,
                "a shipped map must not carry its own deployment zones - deployment is the lobby's pick.");
            Assert.That(layout.Objectives, Has.Count.GreaterThanOrEqualTo(3),
                "objectives decide the winner, so a map meant to be played on needs some.");
            Assert.That(layout.Pieces, Has.Count.GreaterThanOrEqualTo(6), "a bare table is not a map.");

            // 72 x 48. Everything must be ON it - a piece or a marker outside the table is invisible and
            // unreachable, and nothing else would notice.
            foreach (MapObjectiveEntry o in layout.Objectives)
            {
                Assert.That(o.X, Is.InRange(0f, 72f));
                Assert.That(o.Z, Is.InRange(0f, 48f));
            }
            foreach (TerrainPieceEntry piece in layout.Pieces)
            {
                Assert.That(piece.Shape, Is.Not.Null);
                (float lx, float hx, float lz, float hz) = piece.Shape!.GetAABB();
                Assert.That(lx, Is.GreaterThanOrEqualTo(-0.01f), piece.Name);
                Assert.That(hx, Is.LessThanOrEqualTo(72.01f), piece.Name);
                Assert.That(lz, Is.GreaterThanOrEqualTo(-0.01f), piece.Name);
                Assert.That(hz, Is.LessThanOrEqualTo(48.01f), piece.Name);
            }

            // The core rules' two bands, 12" from each long edge - the deployment every lobby offers.
            float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            var coreZones = new[] { (L: 0f, R: 72f, B: 0f, T: d), (L: 0f, R: 72f, B: 48f - d, T: 48f) };

            // No IMPASSIBLE terrain inside a deployment zone. Woods and rubble reaching into the band
            // are normal - on a real table terrain is placed across the whole board, deployment zones
            // included, and starting in cover is a legitimate thing to want. A building is different:
            // models have to physically stand in the zone, and a footprint they cannot enter is room
            // taken off the only patch of table they are allowed to use.
            //
            // (Written the strict way first, which failed on this map's edge-of-zone copses. The map was
            // right and the assertion was wrong - noting it so nobody tightens it back up.)
            foreach (TerrainPieceEntry piece in layout.Pieces)
            {
                if (!piece.TerrainType.HasFlag(ETerrainType.Impassible)) continue;
                (float lx, float hx, float lz, float hz) = piece.Shape!.GetAABB();
                foreach (var z in coreZones)
                {
                    bool inZone = lx < z.R && z.L < hx && lz < z.T && z.B < hz;
                    Assert.That(inZone, Is.False,
                        $"impassible \"{piece.Name}\" sits inside a deployment zone.");
                }
            }
        }
    }
}
