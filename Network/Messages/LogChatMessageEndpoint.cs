using FDG.MessageBus;
using FDG.Players;
using FDG.TextInterface;

namespace FDG.Network.Messages
{
    internal class LogChatMessageEndpoint : IDisposable
    {
        private ILogMessageUI _logMessageUI;
        private IPlayerMessageUI _playerMessageUI;
        private ITableState _tableState;
        private List<PlayerID> _localPlayerIDs;
        private IMessageBusClient _messageBusClient;

        public LogChatMessageEndpoint(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI,
            ITableState tableState, List<PlayerID> localPlayerIDs, IMessageBusClient messageBusClient)
        {
            // An empty list is a spectator: every seat is a bot and this screen only watches (Kai, 2026-11-02 / 2026-11-17).
            // It still receives the log and the chat; it just has nobody to send chat as. This used to throw, and the Godot
            // front end lost the game silently - LAUNCH did nothing, then "The game has already been launched."

            _logMessageUI = logMessageUI;
            _playerMessageUI = playerMessageUI;
            _tableState = tableState;
            _localPlayerIDs = localPlayerIDs;
            _messageBusClient = messageBusClient;

            _messageBusClient.RegisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _messageBusClient.RegisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);

            if (_playerMessageUI != null) _playerMessageUI.OnMessageSentByPlayer += SendChatMessage;
        }

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _messageBusClient.DeregisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);
            if (_playerMessageUI != null) _playerMessageUI.OnMessageSentByPlayer -= SendChatMessage;
        }

        private void SendChatMessage(string message, EChatMessageType messageType)
        {
            //Just use the first player. If multiple people are playing on one device and you use the chat to send a message,
            //then assume it's the first person sending it.
            //TODO: This messes with sending to the same team, but that's kind of a messed up concept anyway if you have multiple
            //people on one device on different teams but also are connected to a game with other players.
            if (_localPlayerIDs.Count == 0) return; // a spectator has no seat to speak for
            PlayerID sendingPlayerID = _localPlayerIDs[0];
            NetworkPlayerSubmitChatMessage chatMessage = new NetworkPlayerSubmitChatMessage(sendingPlayerID, messageType, message);
            _messageBusClient.SendCommandToHostAsync(chatMessage);
        }

        private void OnLogMessageReceived(LogChatNetworkMessage message)
        {
            if (message.IsDebug)
                _logMessageUI?.DisplayDebugMessage(message.LogMessage, message.Color);
            else
                _logMessageUI?.DisplayLogMessage(message.LogMessage, message.Color);
        }

        private void OnPlayerMessageReceived(PlayerChatNetworkMessage message)
        {
            if (_playerMessageUI == null)
            {
                return;
            }

            switch (message.MessageType)
            {
                case EChatMessageType.Global:
                    _playerMessageUI?.DisplayPlayerMessage(message.SendingPlayerName, message.MessageType, message.Message);
                    break;
                case EChatMessageType.Team:
                    IPlayerSlotInfo sendingPlayerSlot = _tableState.Players.Objects
                        .FirstOrDefault(player => player.Name == message.SendingPlayerName);
                    ITeam sendingTeam = sendingPlayerSlot != null
                        ? _tableState.Teams.Objects.First(team => team.TeamNumber == sendingPlayerSlot.TeamNumber)
                        : null;

                    //Show the message if _any_ local players are on that team.
                    foreach(PlayerID localID in _localPlayerIDs)
                    {
                        if(sendingTeam != null && sendingTeam.IsPlayerOnTeam(localID))
                        {
                            _playerMessageUI?.DisplayPlayerMessage(message.SendingPlayerName, message.MessageType, message.Message);
                            break; //Don't show more than once.
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

        }
    }
}
