using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Horde Mode v3.6.0 (GF / GFF / AoF / AoFS), The Mission: "The mission ends when any of these conditions have been
    // met: All defending units are either Shaken or destroyed. ..." The engine only looked at the end of the round, so
    // a defender knocked Shaken before its activation stayed idle, recovered, and the mission went on. Now the round
    // closes the moment it happens, and that round's End of Round rewards never come.
    [TestFixture]
    public class HordeMissionEndsMidRoundTests
    {
        private GameDataStore _store = null!;
        private PlayerID _defender, _enemy;

        private sealed class RecordingLayer : IStateMachineLayer<ISingleRoundContext>
        {
            public readonly List<string> Events = new();
            public Task ExecuteTransition(string eventName, StageBase<ISingleRoundContext> leaving, ISingleRoundContext context)
            {
                Events.Add(eventName);
                return Task.CompletedTask;
            }
            public void NotifyChildEntered(IStage stage) { }
            public void NotifyChildExited(IStage stage) { }
        }

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _defender = new PlayerID(Guid.NewGuid());
            _enemy = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(_defender, slotID: 0, teamNumber: 1, name: "Defender", isFilled: true));
            _store.Create(new PlayerSlotInfo(_enemy, slotID: 1, teamNumber: 2, name: "Horde", isFilled: true));
        }

        private DataBinding<UnitData> Unit(PlayerID player, string name, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
            var models = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(
                _store.Create(new UnitData(player, name, quality: 4, defense: 4, modelBindings: models)));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private async Task<(RecordingLayer Layer, SingleRoundContext Round)> EnterTurnStage(EGameMode mode)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            GameSettings settings = ctx.Settings;
            settings.GameMode = mode;
            ctx.Settings = settings;
            var teamA = new TeamData(1, new List<PlayerID> { _defender });
            var teamB = new TeamData(2, new List<PlayerID> { _enemy });
            _store.Create(teamA);
            _store.Create(teamB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 2);
            var layer = new RecordingLayer();
            var stage = new DeterminePlayerTurnStage(ctx, layer);
            stage.OnDeterminedPlayerTurn.Bind("determined");
            stage.OnNoPlayersLeft.Bind("none");
            await stage.Enter(round);
            return (layer, round);
        }

        [Test]
        public async Task EveryDefenderShaken_EndsTheRoundBeforeTheNextActivation()
        {
            DataBinding<UnitData> a = Unit(_defender, "Defenders A", new Position(10f, 10f));
            DataBinding<UnitData> b = Unit(_defender, "Defenders B", new Position(12f, 10f));
            Unit(_enemy, "Horde", new Position(30f, 10f));
            a.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            b.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            (RecordingLayer layer, SingleRoundContext round) = await EnterTurnStage(EGameMode.Horde);

            Assert.That(layer.Events, Is.EqualTo(new[] { "none" }), "the mission is over - nobody activates");
            Assert.That(round.UnactivatedUnits.Values.All(p => p.Count == 0), Is.True);
        }

        [Test]
        public async Task OneDefenderStillStanding_TheRoundGoesOn()
        {
            DataBinding<UnitData> a = Unit(_defender, "Defenders A", new Position(10f, 10f));
            Unit(_defender, "Defenders B", new Position(12f, 10f));
            Unit(_enemy, "Horde", new Position(30f, 10f));
            a.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            (RecordingLayer layer, _) = await EnterTurnStage(EGameMode.Horde);

            Assert.That(layer.Events, Is.EqualTo(new[] { "determined" }));
        }

        [Test]
        public async Task OutsideHorde_ShakenUnitsDontEndTheRound()
        {
            DataBinding<UnitData> a = Unit(_defender, "Defenders A", new Position(10f, 10f));
            Unit(_enemy, "Horde", new Position(30f, 10f));
            a.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            (RecordingLayer layer, _) = await EnterTurnStage(EGameMode.Standard);

            Assert.That(layer.Events, Is.EqualTo(new[] { "determined" }));
        }
    }
}
