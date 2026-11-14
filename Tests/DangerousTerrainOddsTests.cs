using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The move preview's Dangerous-terrain dice (Build 2.07). It must say exactly what the roll will do: the 3.6.0
    /// pool (the unit's remaining wounds), only when a path crosses, nothing for a flier, and in the skirmish and
    /// Quest games a flier still pays for the minefield it starts in.
    /// </summary>
    [TestFixture]
    public class DangerousTerrainOddsTests
    {
        private GameDataStore _store = null!;
        private static readonly RectangularZone Minefield = new(3, 5, -2, 2);

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void AMoveAcross_CostsTheWholeUnitsRemainingWounds()
        {
            // Three models, one Tough(3) with a wound on it: 1 + 1 + 2 dice. Only the first model's path crosses.
            var a = Model(new Position(0, 0));
            var b = Model(new Position(0, 6));
            var c = Model(new Position(0, 9), tough: 3);
            c.GetValue().DealWounds(1);
            UnitData unit = Unit(null, a, b, c);
            var paths = new List<ModelMoveEntry>
            {
                new(a, new List<Position> { new(8, 0) }),
                new(b, new List<Position> { new(8, 6) }),
                new(c, new List<Position> { new(8, 9) }),
            };

            Assert.That(Odds(EGameSystem.GrimdarkFuture, unit).Dice(paths, Terrain(), unit), Is.EqualTo(4));
        }

        [Test]
        public void AMoveThatMisses_CostsNothing()
        {
            var a = Model(new Position(0, 6));
            UnitData unit = Unit(null, a);
            var paths = new List<ModelMoveEntry> { new(a, new List<Position> { new(8, 6) }) };

            Assert.That(Odds(EGameSystem.GrimdarkFuture, unit).Dice(paths, Terrain(), unit), Is.Zero);
        }

        [TestCase(EGameSystem.GrimdarkFuture, 0)]
        [TestCase(EGameSystem.AgeOfFantasy, 0)]
        [TestCase(EGameSystem.GFFirefight, 1)]
        [TestCase(EGameSystem.AoFSkirmish, 1)]
        [TestCase(EGameSystem.GFStarQuest, 1)]
        [TestCase(EGameSystem.AoFQuest, 1)]
        public void AFlierLeavingAMinefield_PaysOnlyInTheGamesThatSaySo(EGameSystem system, int dice)
        {
            var a = Model(new Position(4, 0));
            UnitData flier = Unit(CoreRuleCatalog.Flying, a);
            var paths = new List<ModelMoveEntry> { new(a, new List<Position> { new(12, 0) }) };

            Assert.That(Odds(system, flier).Dice(paths, Terrain(), flier), Is.EqualTo(dice));
        }

        [Test]
        public void AFlierCrossingAMinefieldFromOutside_NeverPays()
        {
            var a = Model(new Position(0, 0));
            UnitData flier = Unit(CoreRuleCatalog.Flying, a);
            var paths = new List<ModelMoveEntry> { new(a, new List<Position> { new(8, 0) }) };

            Assert.That(Odds(EGameSystem.GFFirefight, flier).Dice(paths, Terrain(), flier), Is.Zero);
        }

        [Test]
        public void ThePreview_TravelsOnTheMoveRequest_ThroughANetworkCopy()
        {
            var a = Model(new Position(0, 0));
            var unit = _store.GetDataBinding<UnitData>(_store.Create(new UnitData(new PlayerID(Guid.NewGuid()), "Unit",
                quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>> { a })));
            var request = new DefineMovementPathRequest(new PlayerID(Guid.NewGuid()), "Move", unit, 6, 12, 12)
            { DangerousTerrain = new DangerousTerrainOdds(false, true, true) };

            var settings = new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(request, settings);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<DefineMovementPathRequest>(json, settings)!;

            Assert.That(copy.DangerousTerrain, Is.EqualTo(request.DangerousTerrain));
        }

        private List<ITerrain> Terrain() => new() { new TerrainData(ETerrainType.Dangerous, Minefield) };

        private DangerousTerrainOdds Odds(EGameSystem system, UnitData unit)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            return DangerousTerrainOdds.For(new TestGameContext(_store, new FixedDiceRoller(4), settings: settings), unit);
        }

        private DataBinding<ModelData> Model(Position at, int tough = 1)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: at, gameDataStore: _store);
            if (tough > 1) model.SetMaxWounds(tough);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private UnitData Unit(Rules.Definitions.SpecialRuleDefinition? rule, params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: models.ToList());
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
