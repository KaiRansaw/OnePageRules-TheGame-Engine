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

        /// <summary>Answers the push the way the bot does (house rules C, "Bots and pushing").</summary>
        private sealed class BotPush : IPlayerRequestByID
        {
            public ITableState? Table;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is DefineMovementPathRequest r)
                {
                    List<ModelMoveEntry>? best = FDG.Ai.Solo.SoloAiPush.Best(r, Table!, new PlayerID(Guid.NewGuid()));
                    Assert.That(best, Is.Not.Null, "a push that gains something exists");
                    object reply = new Selected<List<ModelMoveEntry>>(best!);
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException($"Unexpected request {typeof(TRequest).Name}");
            }
        }

        private static (TestGameContext C, DataBinding<UnitData> Unit) Table(int die, Position to, IPlayerRequestByID? requester = null)
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
            return (new TestGameContext(store, new FixedDiceRoller(die), settings: settings, playerRequester: requester ?? new PushTo(to)), unit);
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
        public async Task TheBot_PushesAnEnemyOffTheLedge()
        {
            var bot = new BotPush();
            var (c, unit) = Table(die: 2, to: default, requester: bot);
            bot.Table = c.TableState;
            await new GameOperationServices(c).MoveUnit(unit.GetValue(), 2f, isOptional: true, controller: null, oncePerRound: false, pushed: true);
            IModel m = unit.GetValue().Models.First();
            Assert.That(Climbing.StandingHeight(c.TableState.Terrain.Objects, m.Position.Position2D), Is.LessThan(0.5f),
                "pushed off the 2.5\" top, not along it");
            Assert.That(m.GetIsAlive(), Is.False, "and it fell: 2 dice of 2 kill a 1-wound model");
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
