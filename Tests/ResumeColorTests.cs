using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// A resumed game gives every seat back the colour it picked in the lobby (Kai's list, 2026-11-07). The
    /// picks ride in the saved settings; the resume lobby seeds them back onto its seats.
    /// </summary>
    [TestFixture]
    public class ResumeColorTests
    {
        [Test]
        public void TheResumeLobby_RestoresEachSeatsColour()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var a = new PlayerID(Guid.NewGuid());
            var b = new PlayerID(Guid.NewGuid());
            store.Create(new PlayerSlotInfo(a, slotID: 0, teamNumber: 0, name: "Kai", isFilled: true));
            store.Create(new PlayerSlotInfo(b, slotID: 1, teamNumber: 1, name: "Friend", isFilled: true));
            GameSettings settings = GameSettings.GetDefault();
            settings.SlotColors = new Dictionary<int, int> { [0] = 5, [1] = 2 };
            GameProgressUtilities.WriteProgress(store, new GameProgressData(EResumeStage.MainPhase, 2,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), settings));

            var lobby = new LobbyViewModel_Host("Kai", "Table", "", new NullNetworkHost(), store);

            Assert.That(lobby.PlayerInfos.Single(p => p.PlayerID == a).ColorIndex, Is.EqualTo(5));
            Assert.That(lobby.PlayerInfos.Single(p => p.PlayerID == b).ColorIndex, Is.EqualTo(2));
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
