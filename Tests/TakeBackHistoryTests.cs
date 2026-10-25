using FDG.Data;
using FDG.Players;
using FDG.TakeBackTools;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Take Back history: which save point a take-back goes to, how redo works, that pressing it again at
    /// the point just rewound to goes one activation further back, and that a replaced game can't record.
    /// Save points are told apart by the round number they were recorded with.
    /// </summary>
    [TestFixture]
    public class TakeBackHistoryTests
    {
        private static readonly PlayerID Me = new PlayerID(Guid.NewGuid());
        private static readonly PlayerID Bot = new PlayerID(Guid.NewGuid());
        private static readonly PlayerID[] Local = { Me };

        private GameDataStore _store = null!;
        private TakeBackHistory _history = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _history = new TakeBackHistory();
        }

        private TakeBackHistory.Recorder Relaunch()
        {
            _history.MarkRewindLaunch();
            return _history.OnGameStarting();
        }

        [Test]
        public void TakeBack_GoesToTheLatestLocalActivationStart()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Me);
            game.Capture(_store, 1, Bot);
            game.Capture(_store, 2, Me);
            game.Capture(_store, 2, Bot);

            Assert.That(_history.PeekTakeBack(Local)!.Round, Is.EqualTo(2));
            Assert.That(_history.TakeBack(Local), Is.Not.Null);
            Assert.That(_history.Count, Is.EqualTo(2), "the target and everything after it leave the history");
            Assert.That(_history.CanRedo, Is.True, "the bot's later activation is somewhere to redo to");
        }

        [Test]
        public void TakeBack_DuringYourOwnActivation_RestartsItWithNothingToRedo()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Bot);
            game.Capture(_store, 1, Me);

            Assert.That(_history.TakeBack(Local), Is.Not.Null);
            Assert.That(_history.CanRedo, Is.False);
        }

        [Test]
        public void NoLocalActivation_NothingToTakeBack()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Bot);

            Assert.That(_history.PeekTakeBack(Local), Is.Null);
            Assert.That(_history.TakeBack(Local), Is.Null);
        }

        [Test]
        public void PressingAgainAtTheRewindPoint_GoesOneActivationFurtherBack_AndRedoReturns()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Me);
            game.Capture(_store, 1, Bot);
            game.Capture(_store, 2, Me);
            game.Capture(_store, 2, Bot);

            _history.TakeBack(Local);              // -> round 2 start
            var second = Relaunch();
            second.Capture(_store, 2, Me);           // the relaunched game re-records where it resumed

            Assert.That(_history.PeekTakeBack(Local)!.Round, Is.EqualTo(1), "a second press goes further back");
            _history.TakeBack(Local);              // -> round 1 start
            var third = Relaunch();
            third.Capture(_store, 1, Me);

            Assert.That(_history.PeekRedo()!.Round, Is.EqualTo(2), "redo returns to where the second press left");
            Assert.That(_history.Redo(), Is.Not.Null);
            var fourth = Relaunch();
            fourth.Capture(_store, 2, Me);

            Assert.That(_history.CanRedo, Is.True, "the first take-back's redo is still there");
            Assert.That(_history.PeekRedo()!.Round, Is.EqualTo(2));
        }

        [Test]
        public void PlayingOn_DiscardsRedo()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Me);
            game.Capture(_store, 1, Bot);
            _history.TakeBack(Local);

            var relaunched = Relaunch();
            relaunched.Capture(_store, 1, Me);  // re-record of the resume point keeps redo
            Assert.That(_history.CanRedo, Is.True);
            relaunched.Capture(_store, 1, Bot); // a new activation started: the old future is gone
            Assert.That(_history.CanRedo, Is.False);
        }

        [Test]
        public void ReplacedGame_CannotRecord()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Me);
            game.Capture(_store, 1, Bot);
            _history.TakeBack(Local);

            game.Capture(_store, 9, Me);  // the old game's dying breath
            Assert.That(_history.Count, Is.EqualTo(0));
            Assert.That(_history.CanRedo, Is.True);
        }

        [Test]
        public void FreshGame_ClearsHistory_RewindLaunchKeepsIt()
        {
            var game = _history.OnGameStarting();
            game.Capture(_store, 1, Me);
            game.Capture(_store, 1, Bot);

            Relaunch();
            Assert.That(_history.Count, Is.EqualTo(2));

            _history.OnGameStarting();
            Assert.That(_history.Count, Is.EqualTo(0));
            Assert.That(_history.CanRedo, Is.False);
        }

        [Test]
        public void OldestSavePointsFallOff()
        {
            var game = _history.OnGameStarting();
            for (int i = 0; i < TakeBackHistory.MaxEntries + 5; i++)
                game.Capture(_store, i, Bot);
            Assert.That(_history.Count, Is.EqualTo(TakeBackHistory.MaxEntries));
        }
    }
}
