using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Shooting over the network (Kai, 2026-11-07): not "does a networked game finish" - the playthrough
    /// fixture already proves that - but "does the joined player see the SAME table as the host". The melee
    /// pass found two ways the client's copy could drift silently (rules lost on a unit update, the final VP
    /// never sent), so this checks the whole store, every object, byte for byte.
    ///
    /// <para><b>The sync point.</b> Every time the joined player is asked a question, the host is blocked
    /// waiting for the answer, and everything it changed before asking has already been sent down the same
    /// ordered socket. So at that instant the two stores must agree exactly. The client's registry is
    /// wrapped to compare them on every request, which samples the table dozens of times per round:
    /// after every shot, save, spell, wound and morale test that preceded a client decision.</para>
    ///
    /// <para>Real army files with casters, Blast, AP, Deadly and the rest, so the shooting path is exercised
    /// with the special rules a real game has. The official bot plays both seats.</para>
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Two real networked games, ~9 min - run on demand with --filter NetworkedTableParity")]
    public class NetworkedTableParityTests
    {
        private readonly List<Action> _teardown = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Action a in _teardown) { try { a(); } catch { } }
            _teardown.Clear();
        }

        [TestCase(GameSystems.GrimdarkFuture, "GDF/1k - Alien Hives.fdgarmy", "GDF/1k - Human Defense Force.fdgarmy")]
        [TestCase(GameSystems.AgeOfFantasy, "AoF/1k - Duchies of Vinci.fdgarmy", "AoF/1k - Human Empire.fdgarmy")]
        [CancelAfter(600_000)]
        public async Task JoinedPlayer_SeesTheHostsTable_AtEveryDecision(string system, string armyA, string armyB)
        {
            int port = FreeLoopbackPort();
            var host = new FDGHost(port: port);
            _teardown.Add(host.Stop);
            Task listening = host.StartAsync();
            Assert.That(listening.IsFaulted, Is.False);

            var hostVm = new LobbyViewModel_Host("Host", "Parity", "", host);
            _teardown.Add(hostVm.Dispose);
            PlayerID hostId = hostVm.PlayerInfos.Single().PlayerID;

            var client = new FDGClient();
            _teardown.Add(client.Disconnect);
            Assert.That(await client.ConnectAsync(IPAddress.Loopback, port), Is.True);
            var clientVm = new LobbyViewModel_Client("Client", client, "");
            _teardown.Add(clientVm.Dispose);
            Assert.That(await clientVm.JoinResultTask.WaitAsync(TimeSpan.FromSeconds(20)), Is.Null);

            PlayerID clientId = default;
            for (int i = 0; i < 200 && clientId == default; i++)
            {
                clientId = hostVm.PlayerInfos.FirstOrDefault(p => p.PlayerID != hostId && p.PlayerType == EPlayerType.Network)?.PlayerID ?? default;
                if (clientId == default) await Task.Delay(50);
            }
            Assert.That(clientId, Is.Not.EqualTo(default(PlayerID)), "the client never reached the roster");

            var a = SoloAiAuditPlaythroughs.LoadArmy(armyA);
            var b = SoloAiAuditPlaythroughs.LoadArmy(armyB);
            a.GameSystem = system;
            b.GameSystem = system;
            hostVm.UpdateArmyListFile(hostId, a);
            hostVm.UpdateArmyListFile(clientId, b);
            hostVm.SetPlayerTeam(clientId, ETeamOption.Team2);
            Assert.That(hostVm.BlockingLaunchProblems(), Is.Empty);

            GameDataStore? hostStore = null;
            var mismatches = new List<string>();
            int checks = 0;
            var lastProgress = Stopwatch.StartNew();

            hostVm.OnLaunched += game =>
            {
                hostStore = (GameDataStore)game.TableState.DataStore;
                PlayerID me = game.LocalPlayerIDs.Single();
                game.AssignInterfaces(null, null,
                    AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, game.TableState, me, seed: 7101, slotID: 0), null, null);
            };
            clientVm.OnLaunched += game =>
            {
                var clientStore = (GameDataStore)game.TableState.DataStore;
                PlayerID me = game.LocalPlayerIDs.Single();
                IStageResolverRegistry inner = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, game.TableState, me, seed: 7102, slotID: 1);
                game.AssignInterfaces(null, null, new ParityCheckingRegistry(inner, typeName =>
                {
                    lastProgress.Restart();
                    if (hostStore == null) return;
                    Interlocked.Increment(ref checks);
                    string? diff;
                    try { diff = Diff(hostStore, clientStore); }
                    catch (Exception ex) { diff = "the check itself threw: " + ex.Message; }
                    if (diff != null) lock (mismatches) if (mismatches.Count < 10) mismatches.Add($"before {typeName}: {diff}");
                }), null, null);
            };

            var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hostVm.OnGameEnded += r => ended.TrySetResult(r);
            Assert.That(hostVm.TryLaunchGame(out string? why), Is.True, why);

            while (!ended.Task.IsCompleted)
            {
                await Task.WhenAny(ended.Task, Task.Delay(2000));
                if (lastProgress.Elapsed > TimeSpan.FromSeconds(90))
                    Assert.Fail($"the joined player was asked nothing for 90s ({checks} checks so far)");
            }

            TestContext.Out.WriteLine($"{checks} parity checks");
            Assert.That(checks, Is.GreaterThan(20), "the client must actually have been asked things");
            Assert.That(mismatches, Is.Empty, "the joined player's table drifted from the host's:\n" + string.Join("\n", mismatches));
        }

        /// <summary>The first object that differs between the two stores, or null when they agree.</summary>
        private static string? Diff(GameDataStore hostStore, GameDataStore clientStore)
        {
            Dictionary<string, string> Snapshot(GameDataStore s)
            {
                // A store being written on another thread can throw mid-enumeration; retry a few times.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return s.GetAllDataReferencesAsJson().ToDictionary(
                            p => $"{p.DataReference.TypeID.ID}:{p.DataReference.Index}:{p.DataReference.Generation}", p => p.JsonValue);
                    }
                    catch (InvalidOperationException) when (attempt < 5) { Thread.Sleep(20); }
                }
            }

            Dictionary<string, string> h = Snapshot(hostStore), c = Snapshot(clientStore);
            foreach (var (key, json) in h)
            {
                if (!c.TryGetValue(key, out string? other)) return $"{key} missing on the client ({Head(json)})";
                if (other != json) return $"{key} differs at char {FirstDiff(json, other)}:\n  host:   {Around(json, other)}\n  client: {Around(other, json)}";
            }
            foreach (string key in c.Keys.Except(h.Keys)) return $"{key} exists only on the client ({Head(c[key])})";

            // The JSON carries a unit's rules as a blob, so it can match while the client's LIVE rule list
            // is empty - exactly the B110 bug. Compare what each side's rule engine would actually see.
            static string Rules(IEnumerable<FDG.Rules.Dispatch.ResolvedRule>? rules) =>
                string.Join(",", (rules ?? Enumerable.Empty<FDG.Rules.Dispatch.ResolvedRule>()).Select(r => r.RequestedName));
            foreach (DataReference r in hostStore.GetAllDataReferences<UnitData>())
            {
                UnitData hu = hostStore.GetValue<UnitData>(r), cu = clientStore.GetValue<UnitData>(r);
                if (Rules(cu.RuleDefinitions) != Rules(hu.RuleDefinitions))
                    return $"{hu.Name}'s live rules: host [{Rules(hu.RuleDefinitions)}], client [{Rules(cu.RuleDefinitions)}]";
            }
            foreach (DataReference r in hostStore.GetAllDataReferences<ModelData>())
            {
                ModelData hm = hostStore.GetValue<ModelData>(r), cm = clientStore.GetValue<ModelData>(r);
                if (Rules(cm.RuleDefinitions) != Rules(hm.RuleDefinitions))
                    return $"model {r.Index}'s live rules: host [{Rules(hm.RuleDefinitions)}], client [{Rules(cm.RuleDefinitions)}]";
            }
            return null;
        }

        private static string Head(string s) => s.Length > 160 ? s[..160] + "..." : s;
        private static int FirstDiff(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return n;
        }
        private static string Around(string s, string other)
        {
            int i = FirstDiff(s, other);
            int from = Math.Max(0, i - 120);
            return s.Substring(from, Math.Min(s.Length - from, 240));
        }

        private sealed class ParityCheckingRegistry : IStageResolverRegistry
        {
            private readonly IStageResolverRegistry _inner;
            private readonly Action<string> _check;
            internal ParityCheckingRegistry(IStageResolverRegistry inner, Action<string> check) { _inner = inner; _check = check; }

            public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
                where TRequest : IStageTaskRequest<TReply> => _inner.RegisterResolver(resolver);

            public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                _check(typeof(TRequest).Name.Split('`')[0]);
                return _inner.ResolveRequest<TRequest, TReply>(request);
            }

            public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore gameDataStore)
            {
                _check(typeFullName[(typeFullName.LastIndexOf('.') + 1)..]);
                return _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
            }
        }

        private static int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
