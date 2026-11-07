using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Ai;
using FDG.Data;
using FDG.Players;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// A bot unit that Advances to shoot must not walk past the Advance distance. The mover budgets the
    /// unit's CENTRE, but the engine measures the longest MODEL's walk, and a model that routes round an
    /// obstacle walks further than the centre does. Found playing a full AoF game (2026-11-06): the bot's
    /// Marksmen advanced 6.1" and 6.7" on a 6" Advance, the engine read both as Rushes, and the unit never
    /// fired.
    /// </summary>
    [TestFixture]
    public class AiAdvanceBudgetTests
    {
        private GameDataStore _store = null!;
        private PlayerID _ai;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _ai }));
            _store.Create(new TeamData(1, new List<PlayerID> { _player }));
        }

        [Test]
        public void AnAdvanceAroundAnObstacle_StaysWithinTheAdvanceDistance()
        {
            // Five models abreast, a wall straight ahead of the middle three, the goal far beyond it.
            DataBinding<UnitData> mover = Unit(_ai, "Marksmen", Enumerable.Range(0, 5)
                .Select(i => new Position(10f + i * 1.2f, 10f)).ToList());
            Unit(_player, "Target", new List<Position> { new(12.4f, 40f) });
            _store.Create(new TerrainData(ETerrainType.Impassible | ETerrainType.Blocking,
                new RectangularZone(10.8f, 14f, 12f, 13f)));

            var context = new TestGameContext(_store, new FixedDiceRoller(4));
            var request = new DefineMovementPathRequest(_ai, "Move", mover,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                mainActivationMove: true);

            List<ModelMoveEntry> move = AiMoveExecutor.Toward(request, context.TableState, _ai,
                new Position(12.4f, 40f), stopWithin: 0f, budgetCapInches: request.MaxAdvanceDistance);

            float walked = MovementUtilities.GetMaxMoveDistance(move);
            Assert.That(walked, Is.GreaterThan(1f), "test setup: the unit should actually move");
            Assert.That(walked, Is.LessThanOrEqualTo(6f),
                $"the longest model's walk ({walked:0.###}\") decides Advance vs Rush, so it must fit the 6\" Advance");
        }

        private DataBinding<UnitData> Unit(PlayerID owner, string name, List<Position> at)
        {
            var bindings = at.Select(p => _store.GetDataBinding<ModelData>(_store.Create(
                new ModelData(0.5f, new List<Weapon>(), p, _store)))).ToList();
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
