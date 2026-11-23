using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Falling through the real push path (GameOperationServices.MoveUnit with pushed: true, Kai 2026-11-22 "make sure
    /// the climbing/jump/etc rules work in game"): a model pushed off a 2.5" top falls, rolls 2 dice, and is wounded on
    /// 1-3; the same move as an ordinary triggered move is refused (it can't afford the climb down).
    /// </summary>
    [TestFixture]
    public class FallingInPlayTests
    {
        private sealed class PushTo : IPlayerRequestByID
        {
            private readonly Position _to;
            public PushTo(Position to) => _to = to;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is DefineMovementPathRequest r)
                {
                    var moves = r.UnitDataBinding.GetValue().ModelBindings
                        .Select(m => new ModelMoveEntry(m, new List<Position> { _to })).ToList();
                    object reply = new Selected<List<ModelMoveEntry>>(moves);
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException($"Unexpected request {typeof(TRequest).Name}");
            }
        }

        private static (TestGameContext C, DataBinding<UnitData> Unit) Table(int die, Position to)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { me }));
            store.Create(new TerrainData(ETerrainType.Climbable, new RectangularZone(9, 13, -2, 2), 2.5f));
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(12.2f, 0), gameDataStore: store);
            var mb = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = store.GetDataBinding<UnitData>(store.Create(new UnitData(me, "Pushed", quality: 4, defense: 5,
                modelBindings: new List<DataBinding<ModelData>> { mb })));
            store.Create(new ArmyData(me, new List<DataBinding<UnitData>> { unit }));
            var settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFFirefight;
            return (new TestGameContext(store, new FixedDiceRoller(die), settings: settings, playerRequester: new PushTo(to)), unit);
        }

        [Test]
        public async Task PushedOffATop_Falls_AndIsWoundedOn1To3()
        {
            var (c, unit) = Table(die: 2, to: new Position(14f, 0));
            await new GameOperationServices(c).MoveUnit(unit.GetValue(), 2f, isOptional: true, controller: null, oncePerRound: false, pushed: true);
            IModel m = unit.GetValue().Models.First();
            Assert.That(m.Position.x, Is.EqualTo(14f).Within(1e-3), "pushed 1.8\" off the top");
            Assert.That(m.GetIsAlive(), Is.False, "2 dice of 2 = two wounds on a 1-wound model");
        }

        [Test]
        public async Task PushedOffATop_RollingHigh_LandsSafely()
        {
            var (c, unit) = Table(die: 5, to: new Position(14f, 0));
            await new GameOperationServices(c).MoveUnit(unit.GetValue(), 2f, isOptional: true, controller: null, oncePerRound: false, pushed: true);
            Assert.That(unit.GetValue().Models.First().GetIsAlive(), Is.True);
        }

        [Test]
        public async Task AnOrdinaryTriggeredMove_CannotStepOffForFree()
        {
            var (c, unit) = Table(die: 2, to: new Position(14f, 0));
            await new GameOperationServices(c).MoveUnit(unit.GetValue(), 2f, isOptional: true);
            IModel m = unit.GetValue().Models.First();
            Assert.That(m.Position.x, Is.EqualTo(12.2f).Within(1e-3), "1.8\" flat + 2.5\" down is over a 2\" move: declined");
            Assert.That(m.GetIsAlive(), Is.True);
        }
    }
}
