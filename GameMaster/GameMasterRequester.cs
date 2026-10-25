using FDG.Players;
using FDG.StageResolution;

namespace FDG.GameMasterTools
{
    /// <summary>
    /// Wraps the host's player-request channel in a Game Master game: every decision the game asks for is
    /// a safe point to apply queued GM changes (applied first), and while the game waits on a HUMAN's
    /// decision, changes are applied the moment they are queued - so the GM panel responds immediately
    /// while it is your turn. While a bot is deciding, changes wait for its answer (bots read the table on
    /// their own threads; editing it under them could corrupt their view).
    /// </summary>
    internal sealed class GameMasterRequester : IPlayerRequestByID
    {
        private readonly IPlayerRequestByID _inner;
        private readonly GameMasterHub _hub;
        private readonly HashSet<PlayerID> _botPlayers;

        public GameMasterRequester(IPlayerRequestByID inner, GameMasterHub hub, IEnumerable<PlayerID> botPlayers)
        {
            _inner = inner;
            _hub = hub;
            _botPlayers = botPlayers.ToHashSet();
        }

        public async Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            // A prompt raised BY a GM change goes straight through.
            if (_hub.IsDraining)
                return await _inner.RequestDecision<TRequest, TReply>(request);

            await _hub.DrainAsync();

            Task<TReply> pending = _inner.RequestDecision<TRequest, TReply>(request);
            if (_botPlayers.Contains(request.TargetPlayerID))
                return await pending;

            while (!pending.IsCompleted)
            {
                Task queued = _hub.WhenCommandQueued();
                if (await Task.WhenAny(pending, queued) == queued && !pending.IsCompleted)
                    await _hub.DrainAsync();
            }
            return await pending;
        }
    }
}
