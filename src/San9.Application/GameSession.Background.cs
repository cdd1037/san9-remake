using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    /// <summary>
    /// Calculates against an isolated engine branch on a worker. The caller keeps reading the last
    /// committed world and explicitly publishes the result on its own thread with Complete().
    /// No worker invokes Changed or touches the caller's mutable session state.
    /// </summary>
    public PendingSessionOperation StartOperation(SessionRequest request)
    {
        if (_executing) throw new InvalidOperationException("Session is executing.");
        var input = CaptureOperationInput();
        _executing = true;
        return new(this, request, Task.Run(() => CalculateOperation(input, request, capturePresentation: false)));
    }

    public sealed class PendingSessionOperation
    {
        private readonly GameSession _owner;
        private readonly SessionRequest _request;
        private readonly Task<PreparedOperation> _work;
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private bool _completed;

        internal PendingSessionOperation(GameSession owner, SessionRequest request,
            Task<PreparedOperation> work) => (_owner, _request, _work) = (owner, request, work);

        public bool IsCompleted => _work.IsCompleted;
        private PreparedOperation Prepared => IsCompleted
            ? _work.GetAwaiter().GetResult()
            : throw new InvalidOperationException("Operation is still running.");
        public Presentation.StrategySnapshot PreparedView => Prepared.View;
        public IReadOnlyList<DomainEvent> PreparedEvents => Prepared.Result.Events;
        public WorldSnapshot PreparedSnapshot => Prepared.Result.Snapshot;

        public SessionResult Complete()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Publish on the session owner thread.");
            if (_completed || !IsCompleted) throw new InvalidOperationException("Operation is not ready for publication.");
            _completed = true;
            PreparedOperation prepared;
            try { prepared = _work.GetAwaiter().GetResult(); }
            catch
            {
                _owner._executing = false;
                throw;
            }
            return _owner.PublishOperation(_request, prepared);
        }
    }
}
