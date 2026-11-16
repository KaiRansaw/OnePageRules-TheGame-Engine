using FDG.Data;
using FDG.Data.Containers;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.DataMessages;

namespace FDG.Network.Synchronization
{
    internal class GameDataUpdateSender
    {

        private IReadWriteableGameDataStore _gameDataStore;

        private IMessageBusHost _messageBusHost;

        public GameDataUpdateSender(IReadWriteableGameDataStore gameDataStore, IMessageBusHost messageBusHost)
        {
            _gameDataStore = gameDataStore;
            _messageBusHost = messageBusHost;

            // Performance sweep (2026-11-15): a simulated game (the Expert bot's look-ahead) has no one to send
            // updates to - its bus drops every message - but subscribing made the store turn every single change
            // into JSON first (~6% of the Expert bot's search time). Nothing is listening, so nothing is lost.
            if (messageBusHost is Simulation.SimulationMessageBus) return;

            _gameDataStore.OnDataAddedAsJson += SendDataAddedMessageToAll;
            _gameDataStore.OnDataUpdatedAsJson += SendDataUpdatedMessageToAll;
            _gameDataStore.OnDataRemoved += SendDataRemovedMessageToAll;

            _messageBusHost.RegisterForConnectionMessageEvent<RequestAllDataMessage>(OnReceivedRequestAllDataMessage);
        }

        private void SendDataAddedMessageToAll(DataReference data, string newObjectJson)
        {
            AddSingleDataMessage addMessage = new AddSingleDataMessage(data, newObjectJson);
            _messageBusHost.SendCommandToAllAsync(addMessage);
        }

        private void SendDataUpdatedMessageToAll(DataReference data, string newValueJson)
        {
            UpdateSingleDataMessage updateMessage = new UpdateSingleDataMessage(data, newValueJson);
            _messageBusHost.SendCommandToAllAsync(updateMessage);
        }

        private void SendDataRemovedMessageToAll(DataReference data)
        {
            RemoveSingleDataMessage removeMessage = new RemoveSingleDataMessage(data);
            _messageBusHost.SendCommandToAllAsync(removeMessage);
        }

        private void OnReceivedRequestAllDataMessage(RequestAllDataMessage _, ConnectionID connectionID)
        {
            List<ReferenceJsonValuePair> allData = _gameDataStore.GetAllDataReferencesAsJson();
            AddAllDataMessage allDataMessage = new AddAllDataMessage(allData);

            _messageBusHost.SendCommandToSingleAsync(allDataMessage, connectionID);
        }



    }
}
