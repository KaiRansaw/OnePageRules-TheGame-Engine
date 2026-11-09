using FDG.GameModel;
using FDG.Presentation;
using FDG.StageResolution;

namespace FDG.Players
{
    /// <summary>
    /// Marks a controller as a bot playing its seat - what "the AI" means to the Solo &amp; Co-Op Challenge
    /// Bonuses. <see cref="ComputerPlayerController"/> is the one real implementation; a test that wraps a bot
    /// controller (to capture its log, say) implements it too, so the game still knows the seat is a bot.
    /// </summary>
    public interface IComputerPlayer
    {
    }

    public class ComputerPlayerController : IPlayerController, IComputerPlayer
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady => true;

        public IPresentationSink? PresentationSink => null;

        public event Action<bool>? OnReadyStateChanged;
        public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;

        public ComputerPlayerController(string name, PlayerID id, FDGGame_AsLocal localGame,
            IStageResolverRegistry registry)
        {
            Name = name;
            ID = id;
            localGame.AddLocalPlayerID(id);
            localGame.AssignInterfaces(null, null, registry, null, null);
        }

        public Task WaitUntilReadyAsync() => Task.CompletedTask;

        public void SendLogMessage(string logMessage, TextColor color) { }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
    }
}
