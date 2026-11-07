using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Auto-assign takes wounds on the unit's plain models before the ones with an upgraded weapon. Found in
    /// a network play-through (2026-11-07): a Marksmen unit lost both Gatling Rifle models to the first two
    /// wounds because they happened to be listed first. The book leaves the choice to the defending player;
    /// this is the default any player would make.
    /// </summary>
    [TestFixture]
    public class AutoAssignPlainModelsFirstTests
    {
        [Test]
        public void AutoFill_RemovesPlainModelsBeforeSpecialWeapons()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            Weapon Gatling() => new Weapon("Gatling Rifle", rangeInches: 24f, attacks: 3, armorPenetration: 0);

            var kits = new List<List<Weapon>>
            {
                new() { Gatling() }, new() { Gatling() },
                new() { Rifle() }, new() { Rifle() }, new() { Rifle() },
            };
            List<DataBinding<ModelData>> models = kits.Select((kit, i) => store.GetDataBinding<ModelData>(
                store.Create(new ModelData(0.5f, kit, new Position(i, 0f), store)))).ToList();
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Marksmen", quality: 4, defense: 4, modelBindings: models);
            DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(store.Create(unit));

            var results = new AssignWoundsResults(binding, totalWoundsToAssign: 3);
            results.AutoFill();

            float On(DataBinding<ModelData> m) => results.PendingWounds.First(e => e.Model == m).Wounds;
            Assert.That(models.Skip(2).All(m => On(m) == 1f), Is.True, "the three rifle models take the wounds");
            Assert.That(On(models[0]) + On(models[1]), Is.EqualTo(0f), "both Gatling Rifles survive");
        }
    }
}
