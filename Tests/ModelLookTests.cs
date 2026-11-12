using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-12: "choose each individual model's look from their multiple models". A per-model 3D model
    /// file picked in the army builder, carried builder list -> compiled army -> unit in play -> save/clone, and
    /// never touching rules or points. Unpicked models carry null (the table uses the unit's first version).
    /// </summary>
    [TestFixture]
    public class ModelLookTests
    {
        [Test]
        public void Compile_CarriesLooks_TrimmedToTheUnitsSize()
        {
            BookFile book = DemoBook.Build();
            var list = new BuilderList
            {
                Name = "Looks", PointsLimit = 1000,
                Units = { new BuilderUnit { RosterUnitId = "warriors", Id = "a",
                    ModelLooks = new List<string?> { "b.glb", null, "c.glb", null, null, "extra.glb", "x.glb" } } },
            };
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            UnitFileEntry u = army.Units.Single();
            Assert.That(u.ModelLooks, Is.EqualTo(new List<string?> { "b.glb", null, "c.glb", null, null }));
            int plainCost = ListCompiler.Compile(book, new BuilderList { Units = { new BuilderUnit { RosterUnitId = "warriors", Id = "a" } } })
                .Units.Single().PointCost;
            Assert.That(u.PointCost, Is.EqualTo(plainCost), "looks are free");
        }

        [Test]
        public void Compile_NothingPicked_LeavesLooksOff()
        {
            BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), new BuilderList
            {
                Units = { new BuilderUnit { RosterUnitId = "warriors", ModelLooks = new List<string?> { null, "" } } },
            });
            Assert.That(army.Units.Single().ModelLooks, Is.Null, "a plain army file stays plain");
        }

        [Test]
        public void CombinedPair_LooksFollowTheirModels()
        {
            var list = new BuilderList
            {
                Units =
                {
                    new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "a" },
                    new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "b", CombinedWithId = "a",
                        ModelLooks = new List<string?> { "z.glb" } },
                },
            };
            UnitFileEntry merged = ListCompiler.Compile(DemoBook.Build(), list).Units.Single();
            Assert.That(merged.ModelCount, Is.EqualTo(10));
            Assert.That(merged.ModelLooks, Has.Count.EqualTo(10));
            Assert.That(merged.ModelLooks![5], Is.EqualTo("z.glb"), "the absorbed copy's first model");
            Assert.That(merged.ModelLooks.Count(l => l != null), Is.EqualTo(1));
        }

        [Test]
        public void ArmyFile_RoundTripsLooks()
        {
            BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), new BuilderList
            {
                Units = { new BuilderUnit { RosterUnitId = "warriors", ModelLooks = new List<string?> { null, "v2.glb" } } },
            });
            string json = JsonSerializer.Serialize(army, RuleJson.Options);
            BuiltArmyFile back = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;
            Assert.That(back.Units.Single().ModelLooks![1], Is.EqualTo("v2.glb"));
            Assert.That(back.Selections!.Units.Single().ModelLooks![1], Is.EqualTo("v2.glb"), "the editable list keeps them too");
        }

        [Test]
        public void UnitInPlay_ModelsCarryTheirLook_ThroughSaveAndClone()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var entry = new UnitFileEntry
            {
                Name = "U", ModelCount = 3, Quality = 4, Defense = 4,
                ModelLooks = new List<string?> { "a.glb", null },
            };
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), entry, store);
            store.Create(unit);
            var looks = ((IUnit)unit).Models.Select(m => m.Look).ToList();
            Assert.That(looks, Is.EqualTo(new List<string?> { "a.glb", null, null }));

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(store));
            GameDataStore cloned = StoreClone.Clone(store);
            foreach (GameDataStore s in new[] { loaded, cloned })
            {
                var all = s.GetAllValues<ModelData>().Select(m => m.Look).ToList();
                Assert.That(all.Count(l => l == "a.glb"), Is.EqualTo(1));
                Assert.That(all.Count(l => l == null), Is.EqualTo(2));
            }
        }
    }
}
