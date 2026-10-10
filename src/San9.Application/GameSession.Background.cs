using System.Collections.Immutable;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    /// <summary>
    /// Calculates against an isolated session on a worker. The caller keeps reading the last
    /// committed world and explicitly publishes the result on its own thread with Complete().
    /// No worker invokes Changed or touches the caller's mutable session state.
    /// </summary>
    public PendingSessionOperation StartOperation(SessionRequest request)
    {
        if (_executing) throw new InvalidOperationException("Session is executing.");
        var simulation = _simulation;
        var records = _records.ToArray();
        var chronicle = _chronicle.ToArray();
        var sequence = NextOperationSequence;
        var ending = _ending;
        _executing = true;
        return new(this, request, Task.Run(() =>
        {
            // Apply branches the shared committed runtime once, before making any changes.
            var worker = new GameSession(_definition, simulation)
            { NextOperationSequence = sequence, _ending = ending };
            worker._records.AddRange(records);
            worker._chronicle.AddRange(chronicle);
            var result = worker.Apply(request);
            // Projection is pure data work too; publish an already prepared view.
            _ = worker.View;
            return (worker, result);
        }));
    }

    public sealed class PendingSessionOperation
    {
        private readonly GameSession _owner;
        private readonly SessionRequest _request;
        private readonly Task<(GameSession Worker, SessionResult Result)> _work;
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private bool _completed;

        internal PendingSessionOperation(GameSession owner, SessionRequest request,
            Task<(GameSession, SessionResult)> work) => (_owner, _request, _work) = (owner, request, work);

        public bool IsCompleted => _work.IsCompleted;
        public Presentation.StrategySnapshot PreparedView => IsCompleted
            ? _work.GetAwaiter().GetResult().Worker.View
            : throw new InvalidOperationException("Operation is still running.");
        public IReadOnlyList<DomainEvent> PreparedEvents => IsCompleted
            ? _work.GetAwaiter().GetResult().Result.Events
            : throw new InvalidOperationException("Operation is still running.");
        public WorldSnapshot PreparedSnapshot => IsCompleted
            ? _work.GetAwaiter().GetResult().Result.Snapshot
            : throw new InvalidOperationException("Operation is still running.");

        public SessionResult Complete()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Publish on the session owner thread.");
            if (_completed || !IsCompleted) throw new InvalidOperationException("Operation is not ready for publication.");
            _completed = true;
            SessionResult result;
            try
            {
                var (worker, outcome) = _work.GetAwaiter().GetResult();
                result = outcome;
                if (result.Disposition == SessionDisposition.Accepted)
                {
                    _owner._simulation = worker._simulation;
                    _owner._view = worker._view;
                    _owner._ending = worker._ending;
                    _owner._records.Clear();
                    _owner._records.AddRange(worker._records);
                    _owner._chronicle.Clear();
                    _owner._chronicle.AddRange(worker._chronicle);
                    _owner.NextOperationSequence = worker.NextOperationSequence;
                    _owner._capture = null;
                    if (_owner.IsRecording)
                        _owner._recording.Add(new(_request, result.Events.ToImmutableArray()));
                }
            }
            finally { _owner._executing = false; }
            if (result.Disposition == SessionDisposition.Accepted) _owner.Changed?.Invoke();
            return result;
        }
    }
}
