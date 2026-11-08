using System;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The Game Over card's Rematch (Kai, 2026-11-07: "back to the same lobby, same armies") re-seats a
    /// fresh lobby from <see cref="LobbyViewModel_Host.GetSeats"/>. These pin what it hands back.
    /// </summary>
    [TestFixture]
    public class LobbySeatSnapshotTests
    {
        [Test]
        public void GetSeats_ListsEverySeatInRosterOrder_WithTheHostFirst()
        {
            var host = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            host.AddAiPlayer(EAiProfile.Tactician);
            host.AddLocalPlayer();

            var seats = host.GetSeats();

            Assert.That(seats.Select(s => s.PlayerName), Is.EqualTo(new[] { "Host", "Tactician Bot 1", "Local Player 3" }));
            Assert.That(seats[0].PlayerType, Is.EqualTo(EPlayerType.Local));
            Assert.That(seats[1].PlayerType, Is.EqualTo(EPlayerType.AI));
            Assert.That(seats[1].AiProfile, Is.EqualTo(EAiProfile.Tactician));
        }

        [Test]
        public void GetSeats_CarriesTeamsAndArmies()
        {
            var host = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            host.AddAiPlayer(EAiProfile.SoloRules);
            PlayerID bot = host.PlayerInfos[1].PlayerID;
            var army = new FDG.ArmyBuilding.BuiltArmyFile();
            host.UpdateArmyListFile(bot, army);
            host.SetPlayerTeam(bot, ETeamOption.Team1);

            var seat = host.GetSeats().Single(s => s.PlayerID == bot);

            Assert.That(seat.Army, Is.SameAs(army));
            Assert.That(seat.TeamNumber, Is.EqualTo(ETeamOption.Team1));
        }

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
