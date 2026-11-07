using FDG.Data;
using FDG.GameMasterTools;
using FDG.Players;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Game Master hub: changes only apply when the engine drains the queue, units move by the offset and
    /// stay on the table, objective damage at 3 destroys the marker, and round-control flags behave.
    /// </summary>
    [TestFixture]
    public class GameMasterHubTests
    {
        private GameDataStore _store = null!;
        private GameMasterHub _hub = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(_player, slotID: 0, teamNumber: 0, name: "Host", isFilled: true));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.Horde;
            settings.PlayStyle = EPlayStyle.GameMaster;
            _hub = new GameMasterHub();
            _hub.Attach(new TestGameContext(_store, new FixedDiceRoller(4), settings: settings));
        }

        private UnitData MakeUnit(Position at)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: at, gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_player, "Squad", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }

        [Test]
        public async Task Changes_WaitForTheEngine_ThenMoveTheUnitByTheOffset_ClampedToTheTable()
        {
            UnitData unit = MakeUnit(new Position(70f, 10f));
            _hub.MoveUnit(unit.ID, 5f, 2f);
            Assert.That(unit.Models[0].Position.x, Is.EqualTo(70f), "queued, not applied yet");
            Assert.That(_hub.PendingCount, Is.EqualTo(1));

            await _hub.DrainAsync();
            Assert.That(unit.Models[0].Position.x, Is.EqualTo(GameWideConstants.STANDARD_TABLE_WIDTH_INCHES), "kept on the table");
            Assert.That(unit.Models[0].Position.z, Is.EqualTo(12f));
            Assert.That(_hub.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public async Task WoundKillReviveAndShaken()
        {
            UnitData unit = MakeUnit(new Position(10f, 10f));
            _hub.ToggleShaken(unit.ID);
            _hub.KillUnit(unit.ID);
            await _hub.DrainAsync();
            Assert.That(unit.GetIsAlive(), Is.False);
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.True);

            _hub.ReviveUnit(unit.ID);
            await _hub.DrainAsync();
            Assert.That(unit.GetIsAlive(), Is.True);
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False, "revive clears Shaken");
        }

        [Test]
        public async Task ObjectiveDamage_AtThree_DestroysTheMarker()
        {
            var objective = new ObjectiveData(new Position(36f, 24f), _store, hordeNumber: 1);
            _store.Create(objective);

            _hub.AdjustObjectiveDamage(1, 2);
            await _hub.DrainAsync();
            Assert.That(objective.HordeDamage, Is.EqualTo(2));

            _hub.AdjustObjectiveDamage(1, -5);
            await _hub.DrainAsync();
            Assert.That(objective.HordeDamage, Is.EqualTo(0), "repair floors at 0");

            _hub.AdjustObjectiveDamage(1, 3);
            await _hub.DrainAsync();
            Assert.That(_store.GetAllValues<ObjectiveData>(), Is.Empty, "three damage destroys it");
        }

        /// <summary>
        /// "Not activated yet" (2026-11-07): queued for the round stage to apply between activations, and -
        /// like an end-round request - dropped if the round finishes first.
        /// </summary>
        [Test]
        public async Task NotActivatedYet_IsHandedToTheRound_AndDroppedAtANewRound()
        {
            UnitData unit = MakeUnit(new Position(10f, 10f));
            _hub.MarkNotActivated(unit.ID);
            await _hub.DrainAsync();
            Assert.That(_hub.ConsumeReactivations(), Is.EqualTo(new[] { unit.ID }));
            Assert.That(_hub.ConsumeReactivations(), Is.Empty, "consumed once");

            _hub.MarkNotActivated(unit.ID);
            _hub.OnRoundStart();
            Assert.That(_hub.ConsumeReactivations(), Is.Empty, "a request from a finished round never carries over");
        }

        [Test]
        public void RoundControl_EndRequestIsDroppedWhenANewRoundStarts_JumpIsConsumedOnce()
        {
            _hub.EndRoundNow();
            _hub.OnRoundStart();
            Assert.That(_hub.ConsumeEndRound(), Is.False, "a stale end-round never ends the fresh round");

            _hub.JumpToRound(7);
            Assert.That(_hub.ConsumeEndRound(), Is.True, "a jump ends the round in progress");
            Assert.That(_hub.ConsumeJumpToRound(), Is.EqualTo(7));
            Assert.That(_hub.ConsumeJumpToRound(), Is.Null);
        }

        [Test]
        public async Task ABadEdit_IsLogged_NotThrown()
        {
            _hub.MoveUnit(new UnitID(Guid.NewGuid()), 1f, 1f);   // no such unit - silently skipped
            Assert.DoesNotThrowAsync(async () => await _hub.DrainAsync());
            await Task.CompletedTask;
        }
    }
}
