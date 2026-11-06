using System;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.EngineInterface;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The lobby gives a solo host a bot opponent on its own (LobbyScreen.EnsureTwoSeats). When a friend
    /// joins, they take that bot's seat and team - otherwise the friend landed in Seat 3 / Team 3 and the
    /// game became a three-way free-for-all with a bot nobody asked for (found in the GUI pass, 2026-11-05).
    /// A bot the host added or changed by hand is the host's choice and stays.
    /// </summary>
    [TestFixture]
    public class LobbySeatFillerTests
    {
        [Test]
        public async Task Friend_TakesTheSeatFillerBotsSeatAndTeam()
        {
            var (host, client) = await HostWithBotThenJoin(seatFiller: true, touch: null);

            Assert.That(host.PlayerInfos.Select(p => p.PlayerName), Is.EquivalentTo(new[] { "Host", "Client" }));
            Assert.That(TeamOf(host, "Client"), Is.EqualTo(ETeamOption.Team2), "the bot's team");
            Assert.That(client.PlayerInfos.Select(p => p.PlayerName), Is.EquivalentTo(new[] { "Host", "Client" }),
                "the client's roster agrees");
        }

        [Test]
        public async Task ABotTheHostAdded_Stays()
        {
            var (host, _) = await HostWithBotThenJoin(seatFiller: false, touch: null);
            Assert.That(host.PlayerInfos.Count, Is.EqualTo(3));
            Assert.That(TeamOf(host, "Client"), Is.EqualTo(ETeamOption.Team3));
        }

        [Test]
        public async Task ASeatFillerTheHostChanged_Stays()
        {
            var (host, _) = await HostWithBotThenJoin(seatFiller: true,
                touch: (h, bot) => h.SetPlayerTeam(bot, ETeamOption.Team1));
            Assert.That(host.PlayerInfos.Any(p => p.PlayerName == "DerpBot 1"), Is.True);
            Assert.That(host.PlayerInfos.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task OnlyTheFirstFriend_TakesIt()
        {
            var loopbackHost = new LoopbackNetworkHost();
            var host = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            host.AddAiPlayer(EAiProfile.SoloCoOp, seatFiller: true);
            await Join(loopbackHost, "A");
            await Join(loopbackHost, "B");

            Assert.That(host.PlayerInfos.Select(p => p.PlayerName), Is.EquivalentTo(new[] { "Host", "A", "B" }));
            Assert.That(TeamOf(host, "A"), Is.EqualTo(ETeamOption.Team2));
            Assert.That(TeamOf(host, "B"), Is.EqualTo(ETeamOption.Team3));
        }

        private static ETeamOption TeamOf(ILobbyViewModel vm, string playerName) =>
            vm.PlayerInfos.Single(p => p.PlayerName == playerName).TeamNumber;

        private static async Task<(LobbyViewModel_Host, LobbyViewModel_Client)> HostWithBotThenJoin(
            bool seatFiller, Action<LobbyViewModel_Host, PlayerID>? touch)
        {
            var loopbackHost = new LoopbackNetworkHost();
            var host = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            host.AddAiPlayer(EAiProfile.SoloCoOp, seatFiller);
            touch?.Invoke(host, host.PlayerInfos.Single(p => p.PlayerName == "DerpBot 1").PlayerID);
            LobbyViewModel_Client client = await Join(loopbackHost, "Client");
            return (host, client);
        }

        private static async Task<LobbyViewModel_Client> Join(LoopbackNetworkHost loopbackHost, string name)
        {
            var clientVm = new LobbyViewModel_Client(name, loopbackHost.Connect(name), "");
            Task<string?> joinTask = clientVm.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            Assert.That(await joinTask, Is.Null);
            return clientVm;
        }
    }
}
