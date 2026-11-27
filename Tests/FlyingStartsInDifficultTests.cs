using FDG.Data;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Flying and Difficult terrain it starts its move in (2026-11-14). Army Forge's Flying text: Grimdark Future and
    /// Age of Fantasy - "ignores the effects of Difficult and Dangerous terrain"; GF: Firefight, AoF: Skirmish and both
    /// Quest games - "...terrain it doesn't start its move in". So in those four, a flier standing in Difficult terrain
    /// at the start of its move is held to the Difficult cap like anyone else. Strider has no such clause.
    ///
    /// <para>2026-11-27: Army Forge's 3.6.1 books dropped that clause from all four - Flying now reads "ignores the
    /// effects of Difficult and Dangerous terrain" everywhere - so a flier starting in Difficult ignores it in every
    /// system. The cases below are the regression guard for that change.</para>
    /// </summary>
    [TestFixture]
    public class FlyingStartsInDifficultTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [TestCase(EGameSystem.GFFirefight, true)]
        [TestCase(EGameSystem.AoFSkirmish, true)]
        [TestCase(EGameSystem.GFStarQuest, true)]
        [TestCase(EGameSystem.AoFQuest, true)]
        [TestCase(EGameSystem.GrimdarkFuture, true)]
        [TestCase(EGameSystem.AgeOfFantasy, true)]
        public void AFlierStartingInDifficult_IgnoresItInEverySystem(EGameSystem system, bool ignores)
        {
            _store.Create(new TerrainData(ETerrainType.Difficult, new RectangularZone(-2, 2, -2, 2)));
            UnitData flier = MakeUnit(new Position(0, 0), "Flying", CoreRuleCatalog.Flying);

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(Ctx(system), flier), Is.EqualTo(ignores));
        }

        [Test]
        public void AFlierStartingOutsideDifficult_StillIgnoresIt()
        {
            _store.Create(new TerrainData(ETerrainType.Difficult, new RectangularZone(10, 14, -2, 2)));
            UnitData flier = MakeUnit(new Position(0, 0), "Flying", CoreRuleCatalog.Flying);

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(Ctx(EGameSystem.GFFirefight), flier), Is.True,
                "\"terrain it doesn't start its move in\" - flying over it from outside is still ignored");
        }

        [Test]
        public void AStriderStartingInDifficult_StillIgnoresIt()
        {
            _store.Create(new TerrainData(ETerrainType.Difficult, new RectangularZone(-2, 2, -2, 2)));
            UnitData strider = MakeUnit(new Position(0, 0), "Strider", CoreRuleCatalog.Strider);

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(Ctx(EGameSystem.GFFirefight), strider), Is.True);
        }

        private TestGameContext Ctx(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            return new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        private UnitData MakeUnit(Position at, string ruleName, Rules.Definitions.SpecialRuleDefinition rule)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: at, gameDataStore: _store);
            var mb = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { mb });
            unit.AttachRuleDefinition(new ResolvedRule(ruleName, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
