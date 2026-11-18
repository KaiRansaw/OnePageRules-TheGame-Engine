using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FDG.Players;

namespace FDG.Presentation
{
    /// <summary>
    /// Server-side <see cref="IPresenter"/> assigned to the game context: fans each beat out to
    /// every player's <see cref="IPresentationSink"/> (local players render in-process; remote
    /// players receive a <c>PresentBeatMessage</c>), then awaits the beat's scaled nominal
    /// duration <em>once</em> on the host clock. The single host-side wait is what gives every
    /// front-end a shared tempo and what keeps remote clients from building a backlog. The
    /// presentation analogue of <c>TempVisualRelayer</c>.
    /// </summary>
    internal class PresentationRelayer : IPresenter
    {
        private readonly PlayerSlotManager _playerSlotManager;
        private readonly IPresentationClock _clock;

        public PresentationRelayer(PlayerSlotManager playerSlotManager, IPresentationClock clock)
        {
            _playerSlotManager = playerSlotManager;
            _clock = clock;
        }

        /// <summary>
        /// Watch-only screens (every seat a bot - Kai, 2026-11-18: the bot vs bot report showed only "Held, nothing
        /// else"): they sit in no player slot, so the per-slot fan-out below never reached them. Their sink is read
        /// at each beat, because a front end assigns it after the game has started.
        /// </summary>
        private readonly List<FDG.EngineInterface.IFDGGame> _spectators = new List<FDG.EngineInterface.IFDGGame>();
        private readonly object _gate = new object();

        public void AddSpectator(FDG.EngineInterface.IFDGGame game)
        {
            lock (_gate) _spectators.Add(game);
        }

        /// <summary>
        /// Pause (Kai, 2026-11-18, bot vs bot): while set, the next beat waits here before it is shown, and with it the
        /// engine thread that produced it - so the game stops at the next thing that would have appeared on screen.
        /// </summary>
        public bool Paused
        {
            get => _paused;
            set
            {
                lock (_gate)
                {
                    _paused = value;
                    if (!value) { _resume.TrySetResult(true); _resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); }
                }
            }
        }
        private volatile bool _paused;
        private TaskCompletionSource<bool> _resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task Present(PresentationBeat beat, CancellationToken ct = default)
        {
            while (_paused)
            {
                Task wait;
                lock (_gate) wait = _paused ? _resume.Task : Task.CompletedTask;
                await Task.WhenAny(wait, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            await PresentNow(beat, ct).ConfigureAwait(false);
        }

        private Task PresentNow(PresentationBeat beat, CancellationToken ct)
        {
            // Deliver to each DISTINCT sink once. Two local players on one machine share a single
            // on-screen sink (both LocalPlayerControllers resolve to the same PresentationPlayer), so
            // fanning per-slot would render — and sound — every beat twice. Remote players each have
            // their own NetworkedPresentationSink, so they're unaffected.
            HashSet<IPresentationSink> delivered = new HashSet<IPresentationSink>();
            for (int i = 0; i < _playerSlotManager.PlayerSlots.Length; i++)
            {
                IPresentationSink? sink = _playerSlotManager.PlayerSlots[i].Controller?.PresentationSink;
                if (sink != null && delivered.Add(sink))
                {
                    sink.OnBeat(beat);
                }
            }

            List<FDG.EngineInterface.IFDGGame> watching;
            lock (_gate) watching = _spectators.ToList();
            foreach (FDG.EngineInterface.IFDGGame game in watching)
            {
                IPresentationSink? sink = game.PresentationSink;
                if (sink != null && delivered.Add(sink)) sink.OnBeat(beat);
            }

            // A held beat only paces its lead-in (see PresentationBeat.Held).
            return _clock.Wait(beat.Held ? beat.HoldLeadIn : beat.NominalDuration, ct);
        }
    }
}
