using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #329 — points and army identity carried from the army FILE into the GAME, for the in-game army
    // list display. Before this, PointCost lived only on UnitFileEntry and the file's Name/Faction/
    // PointsLimit died at load, so a client (which never sees other players' files) had nothing to
    // print. The plumb: UnitData.PointCost set by the file-entry ctor, a joined hero's cost folded
    // into its host at merge (matching the Forge's combined-cost precedent in ListCompiler), and
    // ArmyData.ArmyName/Faction/PointsLimit set in CreateArmy — all public settable, so they ride
    // the store to clients and survive a save/load resume (proved via GameSaveSerializer, the same
    // path ArmyRuleDataResumeTests drives).
    [TestFixture]
    public class ArmyPointsCarryTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        [Test]
        public void UnitBuiltFromFileEntry_CarriesItsPointCost()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            UnitData unit = new UnitData(Player, Entry("Grunts", modelCount: 5, pointCost: 210), store);

            Assert.That(unit.PointCost, Is.EqualTo(210));
        }

        [Test]
        public void CreateArmy_SetsArmyIdentity_AndFoldsJoinedHeroCostIntoHost()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            GameBootstrap.CreateArmy(Player, ArmyFile(), store, CoreRuleCatalog.CreateResolver());

            ArmyData army = store.GetAllValues<ArmyData>().Single();
            Assert.That(army.ArmyName, Is.EqualTo("Hive Fleet"));
            Assert.That(army.Faction, Is.EqualTo("Alien Hives"));
            Assert.That(army.PointsLimit, Is.EqualTo(2000));

            // The hero merged into its host (2 survivors), and its 55 points folded in, so per-unit
            // costs still sum to the army total after the merge.
            Assert.That(army.Units.Count, Is.EqualTo(2));
            IUnit host = army.Units.Single(u => u.Name == "Grunts");
            IUnit solo = army.Units.Single(u => u.Name == "Spores");
            Assert.That(((UnitData)host).PointCost, Is.EqualTo(155), "host 100 + joined hero 55");
            Assert.That(((UnitData)solo).PointCost, Is.EqualTo(30));
        }

        [Test]
        public void PointsAndArmyIdentity_SurviveASaveLoadRoundTrip()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(Player, ArmyFile(), store, CoreRuleCatalog.CreateResolver());

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(store));

            ArmyData army = loaded.GetAllValues<ArmyData>().Single();
            Assert.That(army.ArmyName, Is.EqualTo("Hive Fleet"));
            Assert.That(army.Faction, Is.EqualTo("Alien Hives"));
            Assert.That(army.PointsLimit, Is.EqualTo(2000));

            List<UnitData> units = loaded.GetAllValues<UnitData>().ToList();
            Assert.That(units.Single(u => u.Name == "Grunts").PointCost, Is.EqualTo(155));
            Assert.That(units.Single(u => u.Name == "Spores").PointCost, Is.EqualTo(30));
        }

        // Game Over showed "POINTS LEFT 1005" for an untouched 1000-pt imported list: Army Forge's total sat in the
        // file as units 1005 + UnattributedPoints -5, and the game only sees units. CreateArmy now shares the
        // residual over the units by cost, so a full army in play totals exactly what the lobby shows.
        [TestCase(-5)]
        [TestCase(15)]
        [TestCase(-70)]
        public void CreateArmy_SharesUnattributedPoints_SoTheArmyTotalsWhatTheLobbyShows(int residual)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ArmyListFile file = ArmyFile();
            file.UnattributedPoints = residual;

            GameBootstrap.CreateArmy(Player, file, store, CoreRuleCatalog.CreateResolver());

            ArmyData army = store.GetAllValues<ArmyData>().Single();
            Assert.That(army.Units.Sum(u => ((UnitData)u).PointCost), Is.EqualTo(file.TotalPoints));
        }

        [Test]
        public void SpreadUnattributedPoints_IsProportional_AndExact()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var units = new[] { 220, 230, 125, 200, 95, 135 }
                .Select(p => new UnitData(Player, Entry("U", modelCount: 1, pointCost: p), store)).ToList();

            GameBootstrap.SpreadUnattributedPoints(units, -5);

            Assert.That(units.Sum(u => u.PointCost), Is.EqualTo(1000));
            Assert.That(units.Select(u => u.PointCost), Is.EqualTo(new[] { 219, 229, 124, 199, 95, 134 }),
                "the biggest leftovers lose a point first");
        }

        // A three-entry list: a multi-model host, a hero that joins it, and a standalone unit —
        // the smallest file that exercises both the plain carry and the hero fold.
        private static ArmyListFile ArmyFile() => new ArmyListFile
        {
            Name = "Hive Fleet",
            Faction = "Alien Hives",
            PointsLimit = 2000,
            Units =
            {
                Entry("Grunts", modelCount: 3, pointCost: 100, id: "host"),
                Entry("Hive Lord", modelCount: 1, pointCost: 55, joins: "host", rules: new[] { "Hero" }),
                Entry("Spores", modelCount: 2, pointCost: 30),
            },
        };

        private static UnitFileEntry Entry(string name, int modelCount, int pointCost,
            string? id = null, string? joins = null, string[]? rules = null)
        {
            var entry = new UnitFileEntry
            {
                Name = name,
                ModelCount = modelCount,
                Quality = 4,
                Defense = 4,
                PointCost = pointCost,
                Id = id,
                JoinsUnitId = joins,
            };
            foreach (string rule in rules ?? Array.Empty<string>())
                entry.SpecialRules.Add(new SpecialRuleEntry_Core(rule));
            return entry;
        }
    }
}
