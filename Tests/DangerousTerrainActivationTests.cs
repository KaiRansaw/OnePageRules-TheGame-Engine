using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #414 - the clause OPR 3.6.0 ADDED to dangerous terrain: *"Units with models moving across Dangerous
    /// terrain, **or that activate in it**, must roll as many dice as the number of wounds it would take to
    /// fully destroy them, and for each roll of 1 the unit takes one wound."* Word for word in GF, AoF and
    /// AoFS v3.6.0 and GFSQ v2.1.0, so it is a game-wide rule, not a skirmish one.
    ///
    /// <para>
    /// Before this, the engine tested only on a crossing, which made standing still in a minefield
    /// strictly safer than walking out of one - a unit could Hold in dangerous terrain for twelve rounds
    /// and never roll a die. It also leaves this repo's "what should holding in dangerous terrain do?"
    /// design fork answered by the book rather than by us.
    /// </para>
    ///
    /// <para>
    /// These drive <see cref="MovementExecutor.RollActivationDangerousTerrain"/> directly rather than
    /// <c>ActivationStartStage</c>, deliberately: the stage gathers rule offers and can raise player
    /// requests, and a unit test that drives a stage which asks a question just hangs with nobody to
    /// answer it (the lesson from Q6's first push test, which took the engine suite from 29 seconds to
    /// over ten minutes). The gate is what has rules in it; the stage only decides when to call it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DangerousTerrainActivationTests
    {
        // A band from x=3 to x=5, z=-2 to z=2. Models at x=4 are inside it; models at x=0 are not.
        private static readonly RectangularZone DangerZone = new RectangularZone(3, 5, -2, 2);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1)); // every die a 1 = a wound each
        }

        private static List<ITerrain> Minefield() =>
            new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) };

        // ── The new trigger ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void AUnitStandingInDangerousTerrain_TestsWhenItActivates()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0), new Position(4, 1));

            MovementExecutor.DangerousTerrainResult rolled =
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), Minefield());

            Assert.That(rolled.AnyTested, Is.True, "THE case: it activated in a minefield, so it tests.");
            Assert.That(rolled.Roll!.TotalRolls, Is.EqualTo(2f).Within(0.001f),
                "two one-wound models = two wounds to destroy the unit = two dice");
            Assert.That(rolled.Wounds, Is.EqualTo(2f).Within(0.001f), "both dice came up 1");
        }

        [Test]
        public void AUnitNowhereNearIt_DoesNotTest()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0, 0), new Position(0, 1));

            Assert.That(
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), Minefield()).AnyTested,
                Is.False, "the rule is about activating IN it, not about it being on the table");
        }

        [Test]
        public void OneModelInIt_IsEnough_AndTheWholeUnitRolls()
        {
            // The same unit-wide pool the movement trigger now uses. One model in the minefield, three in
            // the open, and the unit puts all four of its remaining wounds on the dice.
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0), new Position(0, 10),
                new Position(0, 11), new Position(0, 12));

            MovementExecutor.DangerousTerrainResult rolled =
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), Minefield());

            Assert.That(rolled.Roll!.TotalRolls, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void ModelMembershipIsBaseAware_NotCentreOnly()
        {
            // A 0.5" base whose CENTRE sits at x=2.7, just outside the band's x=3 edge, but whose base
            // overlaps it. A model with a foot in the minefield is in the minefield; a centre-point test
            // would wrongly let it stand on the edge for free, which is the kind of gap players find
            // immediately and then rely on.
            DataBinding<UnitData> unit = MakeUnit(new Position(2.7f, 0));

            Assert.That(
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), Minefield()).AnyTested,
                Is.True);
        }

        [Test]
        public void ADeadModelLyingInIt_DoesNotMakeTheUnitTest()
        {
            // A casualty in the minefield is scenery. If this counted, a unit would keep testing every
            // activation for the rest of the game because of where one of its dead fell.
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0), new Position(0, 10));
            unit.GetValue().Models[0].DealWounds(1f);

            Assert.That(
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), Minefield()).AnyTested,
                Is.False);
        }

        // ── What must NOT trigger it ────────────────────────────────────────────────────────────────────

        [Test]
        public void OtherTerrainTypesDoNotTrigger()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0));
            var harmless = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, DangerZone),
                new TerrainData(ETerrainType.Cover, DangerZone),
                new TerrainData(ETerrainType.Blocking, DangerZone),
            };

            Assert.That(
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), harmless).AnyTested,
                Is.False, "standing in a wood is not standing in a minefield");
        }

        [Test]
        public void AnEmptyTableDoesNotTrigger()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0));

            Assert.That(
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(),
                    new List<ITerrain>()).AnyTested,
                Is.False);
        }

        [Test]
        public void TheTerrainWaiverSkipsTheRollEntirely()
        {
            // Flying (the AllTerrain ignore scope) and the campaign Explorer skill both waive dangerous
            // terrain, and they must waive THIS test too - Explorer is offered at the same hook, so a hero
            // that spends its one use must not then be wounded by the thing it spent it on.
            //
            // Skipped, not rolled-and-discarded: the dice stream stays untouched so a seeded realistic-mode
            // replay is identical, which is the same contract RollForcedDangerousTerrain honours.
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0), new Position(4, 1));

            MovementExecutor.DangerousTerrainResult rolled = MovementExecutor.RollActivationDangerousTerrain(
                _ctx, unit.GetValue(), Minefield(), ignoresDangerousTerrain: true);

            Assert.That(rolled.AnyTested, Is.False);
            Assert.That(rolled.Roll, Is.Null, "no dice were drawn at all");
        }

        // ── Pool sizing, on this trigger too ────────────────────────────────────────────────────────────

        [Test]
        public void TheDicePoolIsRemainingWounds_NotToughAtFullStrength()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(4, 0));
            unit.GetValue().Models[0].SetMaxWounds(6);
            unit.GetValue().Models[0].DealWounds(4f);

            MovementExecutor.DangerousTerrainResult rolled =
                MovementExecutor.RollActivationDangerousTerrain(_ctx, unit.GetValue(), Minefield());

            Assert.That(rolled.Roll!.TotalRolls, Is.EqualTo(2f).Within(0.001f),
                "2 wounds left, 2 dice - same reading as the movement trigger");
        }

        // --- helpers ------------------------------------------------------------------------------------

        private DataBinding<UnitData> MakeUnit(params Position[] positions)
        {
            var bindings = new List<DataBinding<ModelData>>();
            foreach (Position position in positions)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: position, gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
