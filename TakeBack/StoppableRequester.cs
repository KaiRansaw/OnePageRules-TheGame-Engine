using FDG.Players;
using FDG.StageResolution;

namespace FDG.TakeBackTools
{
    /// <summary>Thrown into a game that has been stopped (see <see cref="StoppableRequester"/>). The server
    /// catches it and ends the run silently - it is not a fault and not a game result.</summary>
    public sealed class GameStoppedSignal : Exception
    {
        public GameStoppedSignal() : base("The game was stopped.") { }
    }

    /// <summary>
    /// The outermost wrapper on the host's request channel. Once <see cref="Stop"/> is called, the decision
    /// being waited on (and every later one) fails with <see cref="GameStoppedSignal"/>, which unwinds the
    /// state machine. Used when Take Back replaces a running game, so the old one - bots included - stops
    /// instead of playing on unseen in the background.
    /// </summary>
    internal sealed class StoppableRequester : IPlayerRequestByID
    {
        private readonly IPlayerRequestByID _inner;
        private readonly TaskCompletionSource _stopped =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public StoppableRequester(IPlayerRequestByID inner) => _inner = inner;

        public bool IsStopped => _stopped.Task.IsCompleted;

        public void Stop() => _stopped.TrySetResult();

        public async Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (IsStopped) throw new GameStoppedSignal();

            Task<TReply> pending = _inner.RequestDecision<TRequest, TReply>(request);
            if (await Task.WhenAny(pending, _stopped.Task) != pending)
            {
                // The abandoned decision may still complete or fault later; observe it so it never
                // surfaces as an unobserved task exception.
                _ = pending.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                throw new GameStoppedSignal();
            }
            return await pending;
        }
    }
}
