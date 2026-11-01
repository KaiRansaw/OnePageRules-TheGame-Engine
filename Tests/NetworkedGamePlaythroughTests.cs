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
    [TestFixture]
    public class NetworkedGamePlaythroughTests
    {
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// How long the game itself gets. A 2-unit-a-side game on loopback finishes in a second or two;
        /// this is a hang detector, not a performance budget.
        /// </summary>
        private static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(180);

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
        [CancelAfter(300_000)]
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
            hostVm.OnLaunched += game => AttachAi(game, slotID: 0);
            clientVm.OnLaunched += game => AttachAi(game, slotID: 1);

            var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hostVm.OnGameEnded += result => ended.TrySetResult(result);

            Assert.That(hostVm.TryLaunchGame(out string? failReason), Is.True,
                $"the host refused to launch: {failReason}");

            Task winner = await Task.WhenAny(ended.Task, Task.Delay(GameTimeout));
            Assert.That(winner, Is.SameAs(ended.Task),
                "the networked game never reached a result - a networked seat stopped answering, or the "
                + "host state machine stalled waiting on one.");

            string gameResult = await ended.Task;
            Assert.That(gameResult, Is.Not.Null.And.Not.Empty,
                "the game ended without a result string.");
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
        [CancelAfter(300_000)]
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

            hostVm.OnLaunched += game => AttachAi(game, slotID: 0);
            firstVm.OnLaunched += game => AttachAi(game, slotID: 1);
            secondVm.OnLaunched += game => AttachAi(game, slotID: 2);

            var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hostVm.OnGameEnded += result => ended.TrySetResult(result);

            Assert.That(hostVm.TryLaunchGame(out string? failReason), Is.True,
                $"the host refused to launch a three-player game: {failReason}");

            Task winner = await Task.WhenAny(ended.Task, Task.Delay(GameTimeout));
            Assert.That(winner, Is.SameAs(ended.Task),
                "the three-player networked game never reached a result.");
            Assert.That(await ended.Task, Is.Not.Null.And.Not.Empty,
                "the game ended without a result string.");
        }

        /// <summary>
        /// Stands the official AI up over a launched game, which is all a front end does: build a resolver
        /// registry for the player(s) this process owns and hand it to the game. On a client this is also
        /// what sends <c>PostLaunchPlayerReadyMessage</c> and wires the networked request receiver, so the
        /// host's state machine unblocks on it.
        /// </summary>
        private static void AttachAi(IFDGGame game, int slotID)
        {
            PlayerID playerID = game.LocalPlayerIDs.Single();
            IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(EAiProfile.SoloRules,
                game.TableState, playerID, seed: 5150, slotID: slotID);
            game.AssignInterfaces(null, null, registry, null, null);
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
