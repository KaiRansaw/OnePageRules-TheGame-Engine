using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.TextInterface;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #425 - the one question none of the other networking tests answer: <b>can two copies of the game
    /// play a whole game against each other, start to finish, over a real socket?</b>
    ///
    /// <para>Everything else in the suite tests a slice. <see cref="NetworkedFullStateSyncTests"/> proves
    /// the join snapshot and the token stream are faithful. The lobby fixtures prove the roster, teams,
    /// colours and join gates sync. <see cref="ResumeRejoinNetworkTests"/> runs real sockets but stops at
    /// the lobby - its own summary says so: "What they do NOT cover: launching the resumed game and
    /// playing on." So the end-to-end claim rested on hand-verification, and nothing would have caught a
    /// regression in it.</para>
    ///
    /// <para><b>How it works.</b> A real <see cref="FDGHost"/> listens on loopback and a real
    /// <see cref="FDGClient"/> connects, so framing, per-connection PlayerID targeting, the request/reply
    /// round trip and the full-state catch-up are all genuinely in the path. Both seats are then driven by
    /// the <b>official AI</b>: the host's own slot by a registry over its local game, and the CLIENT's slot
    /// by a registry standing on the client's own synced store, in the client's own
    /// <see cref="FDGGame_AsClient"/>. That is exactly the production shape of a human client - the GUI
    /// does the same thing with ImGui resolvers in place of these - so every decision for slot 1 crosses
    /// the wire as a request and comes back as a reply.</para>
    ///
    /// <para><b>What a pass means.</b> Deployment, terrain placement, activations, shooting, melee, morale
    /// and the end-of-game check all completed with one of the two players answering remotely, and
    /// <see cref="ILobbyViewModel.OnGameEnded"/> produced a result. A failure here is a real multiplayer
    /// break, not a flaky double: the only doubles in the test are the dice.</para>
    /// </summary>
    // NonParallelizable, like the other networking fixture (FdgHostConnectionLimitTests). This is the
    // heaviest test in the suite - three in-process players driving a whole game over real sockets,
    // ~1m25s on its own - and the stall detector below fails it when the table makes no progress for
    // StallTimeout.
    //
    // HONEST STATUS (2026-11-02): this test is INTERMITTENT in the full suite and reliable on its own.
    // Observed twice red at ~48s and twice green in the same session, no code between them that it
    // touches. The attribute is the conventional first move and it is NOT PROVEN to fix this - do not
    // read it as a fix. The real cause is unidentified; the open suspicion is contention for threads or
    // sockets with whatever else the runner has in flight, which would make the stall the runner's
    // scheduling rather than a multiplayer break. Tracked in claude/fdg-todo.md.
    //
    // Raising StallTimeout is NOT the answer: that threshold exists to report a real hang quickly, and
    // padding it to cover a scheduling hiccup trades the one thing it is for. If this needs fixing
    // properly, the thing to measure is what the table's last progress was when it gave up - the failure
    // message already prints it, so capture a red run's message rather than theorising again.
    [TestFixture, NonParallelizable]
    public class NetworkedGamePlaythroughTests
    {
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// How long the game may go with <b>nothing happening at all</b> before this fixture calls it a
        /// hang. Note what it is measured against: not total runtime, but the gap since the last log line
        /// any seat produced.
        ///
        /// <para>
        /// <b>Why it is a stall detector and not a deadline.</b> These games are almost entirely
        /// <i>waiting</i> - about 8s of CPU for 2m12s of wall clock, because every decision for a remote
        /// seat is a real socket round trip. So their wall-clock runtime is a function of how busy the
        /// box is, not of whether the game works, and a flat deadline measures the wrong thing. This
        /// fixture proved that twice in one day (2026-11-01): a 180s deadline failed the three-player
        /// test under the full suite's parallel load, it was raised to 240s with the measurement written
        /// down, and <b>the 240s deadline then failed the same test the same way</b> - while the test
        /// passed in 1m32s when run on its own. Ratcheting the number up is a treadmill; the third false
        /// red would have cost another cycle.
        /// </para>
        ///
        /// <para>
        /// A progress tap removes the guesswork. <see cref="TableProgressWatch"/> samples each seat's
        /// <see cref="ITableState"/> and asks only "has anything on the table changed since last time" -
        /// the round, the activating unit, how many units are still to activate, where the models are,
        /// how wounded they are. A slow-but-healthy game simply takes as long as it takes, and a
        /// genuinely wedged one - a seat that stopped answering, a state machine blocked on a reply that
        /// never comes - stops changing and is caught in <see cref="StallTimeout"/> rather than in four
        /// minutes, with a message saying how far it got.
        /// </para>
        ///
        /// <para>
        /// <b>Why the table and not the log.</b> The obvious tap is the game's
        /// <see cref="ILogMessageUI"/>, which this test leaves null, and it was tried first: it receives
        /// <b>nothing</b>. Log lines are routed per player slot and an AI-driven seat discards them, so
        /// the sink stays silent through a perfectly good game. <see cref="ITableState.Progress"/>'s own
        /// summary says to read it "instead of parsing round info out of the log or banner stream" -
        /// which is exactly right, and is why the fingerprint is built from table state instead.
        /// </para>
        ///
        /// <para>
        /// 45s is many times the longest real gap between two table changes in these games. Every
        /// activation moves models and most deal wounds, so a 4-round game fingerprints dozens of times;
        /// even deployment changes it on every placement. <c>CancelAfter(420_000)</c> stays as the
        /// backstop for the case this cannot see: a hang with no game running yet, in the join handshake.
        /// </para>
        /// </summary>
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

        /// <summary>How often the stall detector looks; small enough to be prompt, large enough to be free.</summary>
        private static readonly TimeSpan StallPollInterval = TimeSpan.FromSeconds(2);

        private readonly List<IDisposable> _disposables = new();
        private readonly List<FDGHost> _hosts = new();
        private readonly List<FDGClient> _clients = new();

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable disposable in _disposables) TryQuietly(disposable.Dispose);
            foreach (FDGClient client in _clients) TryQuietly(client.Disconnect);
            foreach (FDGHost host in _hosts) TryQuietly(host.Stop);

            _disposables.Clear();
            _clients.Clear();
            _hosts.Clear();
        }

        [Test]
        [CancelAfter(420_000)]
        public async Task HostAndClient_PlayAWholeGame_ToAResult()
        {
            int port = FreeLoopbackPort();
            var host = new FDGHost(port: port);
            _hosts.Add(host);

            // StartAsync IS the accept loop - awaiting it would block until the host stops.
            Task listening = host.StartAsync();
            Assert.That(listening.IsFaulted, Is.False,
                $"host failed to bind: {listening.Exception?.GetBaseException().Message}");

            var hostVm = new LobbyViewModel_Host("Mr. Host", "Two Machines", "", host);
            _disposables.Add(hostVm);

            PlayerID hostPlayerID = hostVm.PlayerInfos.Single().PlayerID;

            // A real client, joining the way ClientModal does.
            LobbyViewModel_Client clientVm = await JoinAsync("Mrs. Client", port);
            PlayerID clientPlayerID = await WaitForNetworkSlot(hostVm, hostPlayerID);

            // Lobby setup, host-side, exactly as a human host would click it: an army each, opposing
            // teams (a single-team lobby is a hard launch block), and a small table so the game reaches a
            // decision rather than two gunlines missing each other for four rounds.
            hostVm.UpdateArmyListFile(hostPlayerID, MakeArmy("Host Guard"));
            hostVm.UpdateArmyListFile(clientPlayerID, MakeArmy("Client Guard"));
            hostVm.SetPlayerTeam(clientPlayerID, ETeamOption.Team2);

            Assert.That(hostVm.BlockingLaunchProblems(), Is.Empty,
                "the lobby must be launchable before this test means anything.");

            // Both seats get the official AI. The host's registry stands on its own local game; the
            // client's stands on the client's synced store inside its own FDGGame_AsClient, so every
            // decision it makes is a request that crossed the wire.
            var watch = new TableProgressWatch();
            hostVm.OnLaunched += game => AttachAi(game, slotID: 0, watch);
            clientVm.OnLaunched += game => AttachAi(game, slotID: 1, watch);

            var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hostVm.OnGameEnded += result => ended.TrySetResult(result);

            Assert.That(hostVm.TryLaunchGame(out string? failReason), Is.True,
                $"the host refused to launch: {failReason}");

            string gameResult = await AwaitGameEnd(ended.Task, watch,
                "the networked game never reached a result");
            Assert.That(gameResult, Is.Not.Null.And.Not.Empty,
                "the game ended without a result string.");
        }

        /// <summary>
        /// Kai, 2026-11-05: playing with friends over Tailscale. The features built since the last networked
        /// run - Mission Cards (fixed and custom decks, whose card choices are new request types), an
        /// Advanced deployment style (polygon zones), Fast-Play Leader-Based Melee and Movement - each played
        /// host vs client over a real socket to a result. Nothing here is Tailscale-specific: Tailscale only
        /// carries the same TCP connection.
        /// </summary>
        [TestCase("BattleOfWits")]
        [TestCase("TotalDomination")]
        [TestCase("DirectChallenge")]
        [TestCase("TacticalParagons")]
        [TestCase("Spearhead+FastPlay")]
        [CancelAfter(420_000)]
        public async Task HostAndClient_PlayTheNewFeatures_ToAResult(string feature)
        {
            int port = FreeLoopbackPort();
            var host = new FDGHost(port: port);
            _hosts.Add(host);
            Task listening = host.StartAsync();
            Assert.That(listening.IsFaulted, Is.False);

            var hostVm = new LobbyViewModel_Host("Mr. Host", "Two Machines", "", host);
            _disposables.Add(hostVm);
            PlayerID hostPlayerID = hostVm.PlayerInfos.Single().PlayerID;
            LobbyViewModel_Client clientVm = await JoinAsync("Mrs. Client", port);
            PlayerID clientPlayerID = await WaitForNetworkSlot(hostVm, hostPlayerID);

            hostVm.UpdateArmyListFile(hostPlayerID, MakeArmy("Host Guard"));
            hostVm.UpdateArmyListFile(clientPlayerID, MakeArmy("Client Guard"));
            hostVm.SetPlayerTeam(clientPlayerID, ETeamOption.Team2);
            if (feature == "Spearhead+FastPlay")
            {
                hostVm.SetDeploymentStyle(EDeploymentStyle.Spearhead);
                hostVm.SetFastPlay(movement: true, melee: true, shooting: false);
            }
            else
            {
                hostVm.SetMissionCardGame(Enum.Parse<FDG.Missions.EMissionCardGame>(feature));
            }
            Assert.That(hostVm.BlockingLaunchProblems(), Is.Empty);

            var watch = new TableProgressWatch();
            hostVm.OnLaunched += game => AttachAi(game, slotID: 0, watch);
            clientVm.OnLaunched += game => AttachAi(game, slotID: 1, watch);
            var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hostVm.OnGameEnded += result => ended.TrySetResult(result);

            Assert.That(hostVm.TryLaunchGame(out string? failReason), Is.True, $"the host refused to launch: {failReason}");
            string gameResult = await AwaitGameEnd(ended.Task, watch, $"the networked {feature} game never reached a result");
            Assert.That(gameResult, Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// The "more chairs" question, which decides how much of multiplayer is actually left: a host and
        /// TWO joined clients, three armies, three deployment zones, played to a result. Nothing in the
        /// engine is written for exactly two players - <see cref="LobbyViewModel_Host"/> builds a
        /// <see cref="PlayerSlot"/> per roster row and <c>FDGServer</c> takes the array - but the
        /// deployment and turn-order stages had never been run past two over a wire, so this is the test
        /// that says whether 3-4 players needs building or merely needs a lobby that offers it.
        /// </summary>
        [Test]
        [CancelAfter(420_000)]
        public async Task HostAndTwoClients_PlayAWholeGame_ToAResult()
        {
            int port = FreeLoopbackPort();
            var host = new FDGHost(port: port);
            _hosts.Add(host);

            Task listening = host.StartAsync();
            Assert.That(listening.IsFaulted, Is.False,
                $"host failed to bind: {listening.Exception?.GetBaseException().Message}");

            var hostVm = new LobbyViewModel_Host("Mr. Host", "Three Machines", "", host);
            _disposables.Add(hostVm);

            PlayerID hostPlayerID = hostVm.PlayerInfos.Single().PlayerID;

            LobbyViewModel_Client firstVm = await JoinAsync("Mrs. Client", port);
            PlayerID firstPlayerID = await WaitForNetworkSlot(hostVm, hostPlayerID);

            LobbyViewModel_Client secondVm = await JoinAsync("The Third Wheel", port);
            PlayerID secondPlayerID = await WaitForNetworkSlot(hostVm, hostPlayerID, besides: firstPlayerID);

            hostVm.UpdateArmyListFile(hostPlayerID, MakeArmy("Host Guard"));
            hostVm.UpdateArmyListFile(firstPlayerID, MakeArmy("First Guard"));
            hostVm.UpdateArmyListFile(secondPlayerID, MakeArmy("Second Guard"));

            // Three sides, three teams - a free-for-all, which is the harder case for deployment zones
            // and turn order than two clients sharing a team against the host.
            hostVm.SetPlayerTeam(firstPlayerID, ETeamOption.Team2);
            hostVm.SetPlayerTeam(secondPlayerID, ETeamOption.Team3);

            Assert.That(hostVm.BlockingLaunchProblems(), Is.Empty,
                "the three-player lobby must be launchable before this test means anything.");

            var watch = new TableProgressWatch();
            hostVm.OnLaunched += game => AttachAi(game, slotID: 0, watch);
            firstVm.OnLaunched += game => AttachAi(game, slotID: 1, watch);
            secondVm.OnLaunched += game => AttachAi(game, slotID: 2, watch);

            var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hostVm.OnGameEnded += result => ended.TrySetResult(result);

            Assert.That(hostVm.TryLaunchGame(out string? failReason), Is.True,
                $"the host refused to launch a three-player game: {failReason}");

            string gameResult = await AwaitGameEnd(ended.Task, watch,
                "the three-player networked game never reached a result");
            Assert.That(gameResult, Is.Not.Null.And.Not.Empty,
                "the game ended without a result string.");
        }

        /// <summary>
        /// Stands the official AI up over a launched game, which is all a front end does: build a resolver
        /// registry for the player(s) this process owns and hand it to the game. On a client this is also
        /// what sends <c>PostLaunchPlayerReadyMessage</c> and wires the networked request receiver, so the
        /// host's state machine unblocks on it.
        /// <para>
        /// Also hands this seat's table to <paramref name="watch"/>, which is the fixture's only window
        /// into "is anything still happening". Every seat shares one watch, so a change on ANY of them
        /// counts as progress. That is the permissive direction on purpose: the failure this guards
        /// against is the fixture calling a working game hung, so it should take the whole table's word
        /// for it.
        /// </para>
        /// </summary>
        private static void AttachAi(IFDGGame game, int slotID, TableProgressWatch watch)
        {
            PlayerID playerID = game.LocalPlayerIDs.Single();
            IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(EAiProfile.SoloRules,
                game.TableState, playerID, seed: 5150, slotID: slotID);
            game.AssignInterfaces(null, null, registry, null, null);
            watch.Watch(game.TableState);
        }

        /// <summary>
        /// Wait for <paramref name="ended"/>, failing only once the table has gone quiet for
        /// <see cref="StallTimeout"/>. See <see cref="StallTimeout"/> for why this is not a deadline.
        /// </summary>
        private static async Task<string> AwaitGameEnd(Task<string> ended, TableProgressWatch watch,
            string whatStalled)
        {
            while (true)
            {
                if (ended.IsCompleted) return await Finish(ended, watch);

                Task finished = await Task.WhenAny(ended, Task.Delay(StallPollInterval));
                if (finished == ended) return await Finish(ended, watch);

                TimeSpan quietFor = watch.SinceLastChange;
                if (quietFor < StallTimeout) continue;

                // Deliberately detailed: the whole point of this detector is that when it DOES fire, the
                // reader can tell a stall from a slow box without re-running anything.
                Assert.Fail($"{whatStalled}: nothing on any seat's table changed for "
                    + $"{quietFor.TotalSeconds:0}s (stall threshold {StallTimeout.TotalSeconds:0}s). "
                    + $"{watch.ChangeCount} table changes were seen before it went quiet"
                    + $"{(watch.ChangeCount == 0 ? " - the game never got as far as putting anything on the table" : $", the last state being: {watch.LastFingerprint}")}. "
                    + "A networked seat stopped answering, or the host state machine stalled waiting on one. "
                    + "This is NOT the old wall-clock timeout - a busy box no longer fails this test, so a "
                    + "failure here means the game really did stop.");
            }
        }

        /// <summary>
        /// The game reached a result - but first, prove the detector that was watching it could see.
        /// <para>
        /// A watch that never observed a change would make <see cref="AwaitGameEnd"/> vacuous in the
        /// dangerous direction: it would wait out <see cref="StallTimeout"/> and fail a perfectly good
        /// game, which is the exact failure mode this whole mechanism replaced. Worse, it would do it
        /// silently whenever the game happened to finish inside the window, so the fixture would look
        /// fine right up until a slow run made it look broken. This is not a hypothetical - the first
        /// version of this detector watched <see cref="ILogMessageUI"/> and received nothing at all, and
        /// this assertion is what caught it rather than a flake three weeks later.
        /// </para>
        /// </summary>
        private static async Task<string> Finish(Task<string> ended, TableProgressWatch watch)
        {
            Assert.That(watch.ChangeCount, Is.GreaterThan(0),
                "the game ended without the watch ever seeing the table change, so the stall detector was "
                + "blind for the whole run. Either AttachAi stopped handing it the game's ITableState, or "
                + "the fingerprint stopped reading anything that moves - and both turn this test back "
                + "into a coin flip on a busy box.");
            return await ended;
        }

        /// <summary>
        /// The progress tap: samples the watched tables and reports how long since any of them last
        /// looked different. Keeps no history beyond the latest fingerprint - the question is only
        /// whether the game is still moving, not what it did.
        /// </summary>
        private sealed class TableProgressWatch
        {
            private readonly object _gate = new();
            private readonly List<ITableState> _tables = new();
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private string _fingerprint = "";
            private TimeSpan _lastChange = TimeSpan.Zero;
            private int _changeCount;

            public void Watch(ITableState table)
            {
                lock (_gate) _tables.Add(table);
            }

            public int ChangeCount { get { lock (_gate) { Sample(); return _changeCount; } } }

            public string LastFingerprint { get { lock (_gate) { Sample(); return _fingerprint; } } }

            /// <summary>
            /// How long since the fingerprint last changed - or since the watch was created, when it
            /// never has, so a game that never starts still trips the detector.
            /// </summary>
            public TimeSpan SinceLastChange
            {
                get { lock (_gate) { Sample(); return _clock.Elapsed - _lastChange; } }
            }

            private void Sample()
            {
                string now = Fingerprint();
                if (now == _fingerprint) return;

                _fingerprint = now;
                _lastChange = _clock.Elapsed;
                _changeCount++;
            }

            /// <summary>
            /// Everything cheap that moves while a game is being played: the round, which unit is
            /// activating, how many are still to go, the objective scores, and - because deployment and
            /// movement happen with the round counter still null - how many models there are, where they
            /// are, and how wounded they are.
            /// <para>
            /// Read from the test thread while engine threads mutate the same state, so an enumeration
            /// can legitimately throw mid-walk. A throw is counted as PROGRESS, not ignored: something
            /// was actively changing the collection at that instant, which is the very thing being
            /// asked about. A wedged game mutates nothing and cannot throw here.
            /// </para>
            /// </summary>
            private string Fingerprint()
            {
                var parts = new List<string>();

                foreach (ITableState table in _tables)
                {
                    try
                    {
                        IGameProgress progress = table.Progress;
                        int models = 0;
                        var geometry = new HashCode();
                        float wounds = 0f;

                        foreach (IModel model in table.Models.Objects)
                        {
                            models++;
                            wounds += model.WoundsDealt;
                            geometry.Add(model.Position);
                        }

                        parts.Add($"r{progress.RoundCount?.ToString() ?? "-"}"
                            + $"/a{progress.ActivatingUnit?.Name ?? "-"}"
                            + $"/u{progress.UnactivatedUnits.Count}"
                            + $"/s{string.Join(",", progress.Scores.Select(s => s.ObjectiveCount))}"
                            + $"/m{models}/w{wounds:0.##}/g{geometry.ToHashCode()}");
                    }
                    catch (Exception ex)
                    {
                        // Mutation in flight - see the remarks above. Unique per sample so it always
                        // reads as a change.
                        parts.Add($"mutating:{ex.GetType().Name}:{_clock.Elapsed.Ticks}");
                    }
                }

                return string.Join(" | ", parts);
            }
        }

        // Connects a real client and waits out the greeting handshake, as ClientModal does.
        private async Task<LobbyViewModel_Client> JoinAsync(string playerName, int port)
        {
            var client = new FDGClient();
            _clients.Add(client);
            Assert.That(await client.ConnectAsync(IPAddress.Loopback, port), Is.True,
                $"{playerName} could not connect to the host.");

            var clientVm = new LobbyViewModel_Client(playerName, client, "");
            _disposables.Add(clientVm);

            Task<string?> joinTask = clientVm.JoinResultTask;
            Assert.That(await Task.WhenAny(joinTask, Task.Delay(JoinTimeout)), Is.SameAs(joinTask),
                $"{playerName}'s join handshake did not complete.");
            Assert.That(await joinTask, Is.Null, $"{playerName} was rejected by the host.");

            return clientVm;
        }

        // The roster arrives on the read-loop thread, so poll for the handed-over slot rather than assume
        // it has landed by the time the join task completes.
        private static async Task<PlayerID> WaitForNetworkSlot(LobbyViewModel_Host hostVm,
            PlayerID hostPlayerID, PlayerID? besides = null)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                LobbyPlayerInfoSummary? row = hostVm.PlayerInfos.FirstOrDefault(p =>
                    p.PlayerID != hostPlayerID && p.PlayerType == EPlayerType.Network
                    && (besides == null || p.PlayerID != besides.Value));
                if (row != null) return row.PlayerID;
                await Task.Delay(50);
            }

            Assert.Fail("the host never took the connected client onto the roster.");
            throw new InvalidOperationException("unreachable");
        }

        /// <summary>
        /// Two small squads: one that shoots and one that has to walk over and hit something, so the game
        /// exercises both attack paths without taking four rounds to resolve.
        /// </summary>
        private static ArmyListFile MakeArmy(string name) => new ArmyListFile
        {
            Name = name,
            Faction = name,
            GameSystem = GameSystems.GrimdarkFuture,
            PointsLimit = 2000,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Riflemen", ModelCount = 5, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
                new UnitFileEntry
                {
                    Name = "Brawlers", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Blade", RangeInches = 0, Attacks = 2 } },
                },
            },
        };

        // Ask the OS for a free port, then hand it to FDGHost. Same small release-to-bind window as
        // ResumeRejoinNetworkTests, and the same reasoning: a collision surfaces as an explicit bind
        // failure rather than a silent pass.
        private static int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void TryQuietly(Action action)
        {
            try { action(); } catch { /* teardown must not mask the real failure */ }
        }
    }
}
