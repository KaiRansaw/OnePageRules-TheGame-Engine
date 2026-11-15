using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Each player's unit pictures, framing and model picks reach the other players (Kai, 2026-11-14: "does the game on
    /// multiplayer matches use the players' selected unit portraits for the other players? if not, can we make that
    /// work?" / "could you also do the same for the unit models in game?"). End to end through the real host and client
    /// view models over the loopback network, as <see cref="LobbySeatArmySyncTests"/> does.
    /// </summary>
    [TestFixture]
    public class LobbySeatPicturesSyncTests
    {
        private static SeatPictures Pics(string tag) => new()
        {
            Pictures = new() { [SeatPictures.Key("Riflemen", null)] = tag, [SeatPictures.Key("Riflemen", "v2.glb")] = tag + "2" },
            ModelMade = new() { SeatPictures.Key("Riflemen", "v2.glb") },
            Framing = new() { ["Riflemen"] = new[] { 0.5f, 0.3f, 1.5f } },
            Models = new() { [SeatPictures.Key("Riflemen", null)] = "Guard/Riflemen/v1.glb" },
        };

        [Test]
        public async Task TheHostsPictures_ReachTheClient()
        {
            var (host, _, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");

            host.SetMyPictures(hostSeat, Pics("AAAA"));

            SeatPictures seen = clients[0].SeatPictures[hostSeat];
            Assert.That(seen.Pictures[SeatPictures.Key("Riflemen", null)], Is.EqualTo("AAAA"));
            Assert.That(seen.ModelMade, Is.EqualTo(new[] { SeatPictures.Key("Riflemen", "v2.glb") }));
            Assert.That(seen.Framing["Riflemen"], Is.EqualTo(new[] { 0.5f, 0.3f, 1.5f }));
            Assert.That(seen.Models[SeatPictures.Key("Riflemen", null)], Is.EqualTo("Guard/Riflemen/v1.glb"));
        }

        [Test]
        public async Task AClientsPictures_GoRoundTheHost_ToTheOtherClient()
        {
            var (host, _, clients) = await StandUpLobby(2);
            PlayerID alpha = SeatOf(host, "Alpha");

            clients[0].SetMyPictures(alpha, Pics("ALPHA"));

            Assert.That(clients[1].SeatPictures[alpha].Pictures.Values, Does.Contain("ALPHA"));
            Assert.That(host.SeatPictures[alpha].Pictures.Values, Does.Contain("ALPHA"));
        }

        [Test]
        public async Task NewerPictures_ReplaceOlder()
        {
            var (host, _, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            host.SetMyPictures(hostSeat, Pics("OLD"));
            host.SetMyPictures(hostSeat, Pics("NEW"));

            Assert.That(clients[0].SeatPictures[hostSeat].Pictures[SeatPictures.Key("Riflemen", null)], Is.EqualTo("NEW"));
        }

        [Test]
        public async Task ALateJoiner_GetsEverySeatsPictures()
        {
            var (host, loopback, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            PlayerID alpha = SeatOf(host, "Alpha");
            host.AddAiPlayer(EAiProfile.SoloRules);
            PlayerID bot = SeatOf(host, "DerpBot 1");
            host.SetMyPictures(hostSeat, Pics("HOST"));
            clients[0].SetMyPictures(alpha, Pics("ALPHA"));
            host.SetMyPictures(bot, Pics("BOT"));

            LobbyViewModel_Client late = await Join(loopback, "Bravo");

            Assert.That(late.SeatPictures[hostSeat].Pictures.Values, Does.Contain("HOST"));
            Assert.That(late.SeatPictures[alpha].Pictures.Values, Does.Contain("ALPHA"));
            Assert.That(late.SeatPictures[bot].Pictures.Values, Does.Contain("BOT"), "bot seats too");
        }

        [Test]
        public async Task ARemovedSeat_TakesItsPicturesWithIt()
        {
            var (host, _, clients) = await StandUpLobby(1);
            host.AddAiPlayer(EAiProfile.SoloRules);
            PlayerID bot = SeatOf(host, "DerpBot 1");
            host.SetMyPictures(bot, Pics("BOT"));
            Assert.That(clients[0].SeatPictures.ContainsKey(bot), Is.True);

            host.RemovePlayer(bot);

            Assert.That(clients[0].SeatPictures.ContainsKey(bot), Is.False);
        }

        [Test]
        public async Task PicturesForASeatNotInTheLobby_AreIgnored()
        {
            var (host, _, clients) = await StandUpLobby(1);
            var stranger = new PlayerID(Guid.NewGuid());

            clients[0].SetMyPictures(stranger, Pics("X"));

            Assert.That(host.SeatPictures.ContainsKey(stranger), Is.False);
            Assert.That(clients[0].SeatPictures.ContainsKey(stranger), Is.False);
        }

        [Test]
        public void Trimmed_DropsOversizedAndMalformedEntries()
        {
            var p = new SeatPictures
            {
                Pictures = new() { ["ok|"] = "abc", ["big|"] = new string('a', SeatPictures.MaxPictureChars + 1) },
                ModelMade = new() { "ok|", "big|", "missing|" },
                Framing = new() { ["ok"] = new[] { 0.5f, 0.5f, 2f }, ["bad"] = new[] { 1f } },
                Models = new() { ["ok|"] = "A/B/c.glb", ["empty|"] = "" },
            }.Trimmed();

            Assert.That(p.Pictures.Keys, Is.EqualTo(new[] { "ok|" }));
            Assert.That(p.ModelMade, Is.EqualTo(new[] { "ok|" }));
            Assert.That(p.Framing.Keys, Is.EqualTo(new[] { "ok" }));
            Assert.That(p.Models.Keys, Is.EqualTo(new[] { "ok|" }));
        }

        // ── Helpers (as LobbySeatArmySyncTests) ──

        private static readonly string[] ClientNames = { "Alpha", "Bravo", "Charlie" };

        private static PlayerID SeatOf(ILobbyViewModel vm, string name) =>
            vm.PlayerInfos.Single(p => p.PlayerName == name).PlayerID;

        private static async Task<(LobbyViewModel_Host, LoopbackNetworkHost, IReadOnlyList<LobbyViewModel_Client>)> StandUpLobby(int clientCount)
        {
            var loopback = new LoopbackNetworkHost();
            var host = new LobbyViewModel_Host("Host", "The Table", "", loopback);
            var clients = new List<LobbyViewModel_Client>();
            for (int i = 0; i < clientCount; i++) clients.Add(await Join(loopback, ClientNames[i]));
            return (host, loopback, clients);
        }

        private static async Task<LobbyViewModel_Client> Join(LoopbackNetworkHost loopback, string name)
        {
            var client = new LobbyViewModel_Client(name, loopback.Connect(name), "");
            Task<string?> joinTask = client.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            Assert.That(await joinTask, Is.Null);
            return client;
        }
    }
}
