using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Bot banter: a few seconds after a bot is added to the lobby it posts exactly one line to chat,
    /// attributed to its own name so it reads like any other player, and no two bots ever pick the same
    /// line. The delay is collapsed to milliseconds here via the internal test seam so the suite does
    /// not sleep for the real 3-5 seconds.
    /// </summary>
    [TestFixture]
    public class LobbyBotGreetingTests
    {
        [Test]
        public async Task AddingABot_PostsExactlyOneLine_AttributedToThatBot()
        {
            LobbyViewModel_Host hostVm = MakeFastGreetingLobby();

            hostVm.AddAiPlayer(EAiProfile.Tactician);

            IReadOnlyList<LobbyChatMessage> greetings = await WaitForBotGreetings(hostVm, 1);

            Assert.That(greetings.Count, Is.EqualTo(1), "One bot must produce exactly one line.");
            Assert.That(greetings[0].SendingPlayerName, Is.EqualTo("Tactician Bot 1"),
                "The line must be attributed to the bot's lobby name, so it reads as a player talking " +
                "rather than a System notice.");
            Assert.That(greetings[0].Message, Is.Not.Empty);

            // A bot speaks once, not on a repeating timer - give it well past its delay to prove silence.
            await Task.Delay(200);
            Assert.That(BotGreetingsIn(hostVm).Count, Is.EqualTo(1),
                "A bot must greet once per add, never repeatedly.");
        }

        [Test]
        public async Task MultipleBots_NeverPickTheSameLine()
        {
            LobbyViewModel_Host hostVm = MakeFastGreetingLobby();

            // Eight bots is past the 8-player advertised cap, so this covers more bots than a real
            // lobby can hold against a 10-line pool.
            const int botCount = 8;
            for (int i = 0; i < botCount; i++)
            {
                hostVm.AddAiPlayer(i % 2 == 0 ? EAiProfile.Tactician : EAiProfile.SoloRules);
            }

            IReadOnlyList<LobbyChatMessage> greetings = await WaitForBotGreetings(hostVm, botCount);

            string[] lines = greetings.Select(msg => msg.Message).ToArray();
            Assert.That(lines.Distinct().Count(), Is.EqualTo(botCount),
                "Two bots picked the same line. The pick races on thread-pool continuations, so the " +
                "used-line set must be taken under a lock.");

            string[] speakers = greetings.Select(msg => msg.SendingPlayerName).ToArray();
            Assert.That(speakers.Distinct().Count(), Is.EqualTo(botCount),
                "Each bot should speak exactly once, so every line has a distinct speaker.");

            foreach (string line in lines)
            {
                Assert.That(line.All(character => character <= 0x00FF), Is.True,
                    $"Game text is ASCII-only - the ImGui atlas renders anything above U+00FF as '?': {line}");
            }
        }

        [Test]
        public async Task DisposedLobby_DoesNotPostAPendingGreeting()
        {
            LobbyViewModel_Host hostVm = MakeFastGreetingLobby();
            // Long enough that the lobby is certainly torn down before the bot would speak.
            hostVm.BotGreetingMinDelayMs = 250;
            hostVm.BotGreetingMaxDelayMs = 250;

            hostVm.AddAiPlayer(EAiProfile.Tactician);
            hostVm.Dispose();

            await Task.Delay(600);

            Assert.That(BotGreetingsIn(hostVm), Is.Empty,
                "A bot must not speak into a lobby that has already been torn down.");
        }

        private static LobbyViewModel_Host MakeFastGreetingLobby()
        {
            LobbyViewModel_Host hostVm =
                new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            // Set before any AddAiPlayer call - the greeting is scheduled at add-time.
            hostVm.BotGreetingMinDelayMs = 1;
            hostVm.BotGreetingMaxDelayMs = 10;
            return hostVm;
        }

        // Chat also carries host-side "System" notices (server start), which are not bot banter.
        //
        // #414: the enumeration is guarded because each bot's greeting is appended from its own timer
        // thread while this polls. Reading a List<T> mid-append throws IndexOutOfRangeException out of
        // LINQ, which is exactly what started happening once the poll below ran for 20s instead of 5 -
        // a torn read, not a missing greeting, so the right answer is "nothing to see yet, poll again".
        //
        // **Worth a real look separately:** the same race exists in the running game, where the UI
        // thread draws ChatMessages while the lobby appends to it, so a lobby with several bots could in
        // principle throw while drawing. The engine-side fix (a lock or an immutable snapshot on the
        // view model) is a lobby thread-safety job and is recorded in WorkItems/414 rather than done
        // here, because a test must not be the thing that makes a production race look handled.
        private static IReadOnlyList<LobbyChatMessage> BotGreetingsIn(LobbyViewModel_Host hostVm)
        {
            try
            {
                return hostVm.ChatMessages
                    .Where(msg => msg.SendingPlayerName.StartsWith("Tactician Bot")
                                  || msg.SendingPlayerName.StartsWith("DerpBot"))
                    .ToArray();
            }
            catch (Exception) // torn read while a bot thread appends; the caller polls again
            {
                return Array.Empty<LobbyChatMessage>();
            }
        }

        private static async Task<IReadOnlyList<LobbyChatMessage>> WaitForBotGreetings(
            LobbyViewModel_Host hostVm, int expected)
        {
            // 20s, not 5. This is wall-clock, and each bot's greeting is on its own timer, so eight of
            // them racing the rest of a 3,800-test suite on a 2-core box intermittently saw 7 of 8 and
            // failed the whole run - caught three times in a row of five while working on #414, and
            // nothing to do with what was being changed. A generous ceiling costs nothing on a healthy
            // run (the loop exits as soon as the greetings arrive) and only delays a run that was going
            // to fail anyway. A flaky suite is worse than a slow one: it trains everyone to re-run.
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 20000)
            {
                IReadOnlyList<LobbyChatMessage> found = BotGreetingsIn(hostVm);
                if (found.Count >= expected)
                {
                    return found;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"Timed out waiting for {expected} bot greeting(s); saw {BotGreetingsIn(hostVm).Count}.");
            return Array.Empty<LobbyChatMessage>();
        }

        // Host-only no-op network double, mirroring LobbyBotNamingTests: nothing joins or launches here,
        // so broadcasts go nowhere. The greeting still reaches ChatMessages because the message bus
        // dispatches locally before it hits the wire.
        private sealed class NullNetworkHost : INetworkHost
        {
            public event Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
            public event Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
            public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived { add { } remove { } }

            public Task StartAsync() => Task.CompletedTask;
            public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled) => Task.CompletedTask;
            public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled)
                => Task.CompletedTask;
            public void DisconnectClient(ConnectionID clientId) { }

            public void MarkClientAuthenticated(ConnectionID clientId) { }
            public void Stop() { }
        }
    }
}
