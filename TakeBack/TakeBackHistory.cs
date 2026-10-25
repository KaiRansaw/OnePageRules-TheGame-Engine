using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;

namespace FDG.TakeBackTools
{
    /// <summary>
    /// "Take back" for local games against bots: a rolling list of save points, one at the start of every
    /// activation (the same boundary the engine's own rolling save point and simulations use - a game
    /// resumed from one re-plays that activation from its start). The front end rewinds by relaunching the
    /// game from one of these, so the rewind reuses the ordinary Load Game resume path rather than trying
    /// to unwind a running state machine.
    ///
    /// <para>Process-wide (<see cref="Shared"/>) because a rewind replaces the server: the history has to
    /// outlive the game instance it was recorded in. A fresh game clears it; a rewind launch keeps it
    /// (<see cref="MarkRewindLaunch"/>).</para>
    /// </summary>
    public sealed class TakeBackHistory
    {
        public static TakeBackHistory Shared { get; } = new TakeBackHistory();

        /// <summary>One activation-start save point: the whole game as a save string, the round it
        /// belongs to, and the player whose activation starts there.</summary>
        public sealed record Entry(string Json, int Round, PlayerID Player);

        /// <summary>How many activation starts are kept. Older ones fall off the front.</summary>
        public const int MaxEntries = 30;

        private readonly object _lock = new object();
        private readonly List<Entry> _entries = new List<Entry>();
        // Each frame is the run of save points one take-back removed; its last entry is where Redo resumes.
        private readonly List<List<Entry>> _redoFrames = new List<List<Entry>>();
        private bool _rewindLaunchPending;
        // A resumed game re-captures the save point it resumed from as its first boundary - that one must
        // not count as "the game moved on" (which is what discards the redo history).
        private int _recapturesToIgnore;
        // True while the game sits at the save point it was just rewound to (nothing new recorded since):
        // a second Take Back then goes one activation further back instead of restarting the same one.
        private bool _atRewindPoint;
        // Which game may record. Bumped by every game start and every take-back / redo, so a game that is
        // being replaced can never slip one last save point in before it notices it was stopped.
        private int _generation;

        /// <summary>The per-game recording handle a running game holds (see <see cref="OnGameStarting"/>).</summary>
        public sealed class Recorder
        {
            private readonly TakeBackHistory _history;
            private readonly int _generation;

            internal Recorder(TakeBackHistory history, int generation)
            {
                _history = history;
                _generation = generation;
            }

            /// <summary>Records an activation-start save point (engine thread).</summary>
            public void Capture(IReadableGameDataStore store, int round, PlayerID player)
                => _history.Capture(_generation, store, round, player);
        }

        /// <summary>The front end is about to relaunch from a take-back / redo save point: the next game
        /// start keeps the history instead of clearing it.</summary>
        public void MarkRewindLaunch()
        {
            lock (_lock) _rewindLaunchPending = true;
        }

        /// <summary>Called by the server as a real game starts. Clears the history unless this start is a
        /// rewind relaunch.</summary>
        public Recorder OnGameStarting()
        {
            lock (_lock)
            {
                _generation++;
                var recorder = new Recorder(this, _generation);
                if (_rewindLaunchPending)
                {
                    _rewindLaunchPending = false;
                    return recorder;
                }
                _entries.Clear();
                _redoFrames.Clear();
                _recapturesToIgnore = 0;
                _atRewindPoint = false;
                return recorder;
            }
        }

        private void Capture(int generation, IReadableGameDataStore store, int round, PlayerID player)
        {
            if (store is not GameDataStore concrete) return;
            lock (_lock) if (generation != _generation) return;

            string json;
            try
            {
                json = GameSaveSerializer.Save(concrete);
            }
            catch (Exception ex)
            {
                // A missed save point only means one fewer place to take back to - never a game fault.
                Console.WriteLine($"[Take Back] Could not record a save point: {ex.Message}");
                return;
            }

            lock (_lock)
            {
                if (generation != _generation) return;
                if (_recapturesToIgnore > 0)
                {
                    _recapturesToIgnore--;
                    _atRewindPoint = true;
                }
                else
                {
                    _redoFrames.Clear();
                    _atRewindPoint = false;
                }

                _entries.Add(new Entry(json, round, player));
                if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
        }

        /// <summary>The save point a take-back would go to: the start of the most recent activation by one
        /// of <paramref name="localPlayers"/>, or null.</summary>
        public Entry? PeekTakeBack(IReadOnlyCollection<PlayerID> localPlayers)
        {
            lock (_lock)
            {
                int index = TakeBackIndex(localPlayers);
                return index < 0 ? null : _entries[index];
            }
        }

        // Caller holds _lock.
        private int TakeBackIndex(IReadOnlyCollection<PlayerID> localPlayers)
        {
            int index = _entries.FindLastIndex(e => localPlayers.Contains(e.Player));
            if (index > 0 && _atRewindPoint && index == _entries.Count - 1)
            {
                int earlier = _entries.FindLastIndex(index - 1, e => localPlayers.Contains(e.Player));
                if (earlier >= 0) index = earlier;
            }
            return index;
        }

        /// <summary>How many save points are held.</summary>
        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        public bool CanRedo
        {
            get { lock (_lock) return _redoFrames.Count > 0; }
        }

        /// <summary>The save point Redo would go to, or null.</summary>
        public Entry? PeekRedo()
        {
            lock (_lock) return _redoFrames.Count == 0 ? null : _redoFrames[^1][^1];
        }

        /// <summary>
        /// Takes back to the start of the latest local player's activation: returns its save string and
        /// drops it and everything after it from the history (the relaunched game re-records it). The
        /// dropped run becomes a redo frame when it reached past the target. Null = nothing to take back.
        /// </summary>
        public string? TakeBack(IReadOnlyCollection<PlayerID> localPlayers)
        {
            lock (_lock)
            {
                int index = TakeBackIndex(localPlayers);
                if (index < 0) return null;

                Entry target = _entries[index];
                List<Entry> removed = _entries.GetRange(index, _entries.Count - index);
                _entries.RemoveRange(index, _entries.Count - index);

                // Only a run that got past the target's own start has somewhere to redo to: taking back
                // inside your own activation just restarts it.
                if (removed.Count > 1) _redoFrames.Add(removed.GetRange(1, removed.Count - 1));

                _recapturesToIgnore = 1;
                _generation++; // the game being taken back records nothing more
                return target.Json;
            }
        }

        /// <summary>
        /// Undoes the last take-back: restores the save points it dropped and returns the latest of them
        /// (the start of the activation that was under way when Take Back was pressed). Null = nothing.
        /// </summary>
        public string? Redo()
        {
            lock (_lock)
            {
                if (_redoFrames.Count == 0) return null;

                List<Entry> frame = _redoFrames[^1];
                _redoFrames.RemoveAt(_redoFrames.Count - 1);

                // The target itself was dropped by the take-back and is re-recorded by the resumed game;
                // the redo point is likewise re-recorded when the relaunched game reaches it.
                // Restore what lay between, then hand back the redo point.
                _entries.AddRange(frame.GetRange(0, frame.Count - 1));
                Entry resumeAt = frame[^1];

                _recapturesToIgnore = 1;
                _generation++;
                return resumeAt.Json;
            }
        }
    }
}
