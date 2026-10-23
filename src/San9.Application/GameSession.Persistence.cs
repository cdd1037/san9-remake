using System.Collections.Immutable;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    public bool HasUnsavedChanges { get; private set; } = true;
    public ImmutableArray<RulesVersionWarning> RulesVersionWarnings { get; private set; } = [];

    internal RulesVersionWarning? ReportRulesVersion(string savedVersion)
    {
        if (savedVersion == SessionProtocol.RulesVersion) return null;
        var warning = new RulesVersionWarning(savedVersion, SessionProtocol.RulesVersion);
        if (!RulesVersionWarnings.Contains(warning)) RulesVersionWarnings = RulesVersionWarnings.Add(warning);
        return warning;
    }

    /// <summary>A completed write only covers the operation sequence captured for that write.</summary>
    public void MarkSaved(long savedOperationSequence)
    {
        if (savedOperationSequence == NextOperationSequence) HasUnsavedChanges = false;
    }
    private SessionSnapshot? _capture;
    private SessionSnapshot? _xunStartCheckpoint;
    private bool _xunStartCheckpointsEnabled;
    private long _lastXunStartDay;

    /// <summary>Enables future checkpoints after loading; it never creates a checkpoint for the loaded xun.</summary>
    public void EnableXunStartCheckpoints()
    {
        if (_xunStartCheckpointsEnabled) return;
        _lastXunStartDay = Snapshot.Clock.DayNumber;
        _xunStartCheckpointsEnabled = true;
    }

    /// <summary>Claims one pre-order checkpoint. Loading and UI reconstruction do not create checkpoints.</summary>
    public SessionSnapshot? TakeXunStartCheckpoint()
    {
        if (_executing) return null;
        var checkpoint = _xunStartCheckpoint;
        _xunStartCheckpoint = null;
        return checkpoint;
    }

    /// <summary>A failed consumer may retry the same checkpoint while its xun is still current.</summary>
    public void RetryXunStartCheckpoint(SessionSnapshot checkpoint)
    {
        if (_xunStartCheckpointsEnabled && _xunStartCheckpoint is null && Snapshot.Clock == checkpoint.World.Clock &&
            Snapshot.Simulation.Boundary == SimulationBoundary.Stable)
            _xunStartCheckpoint = checkpoint;
    }

    private void PrepareXunStartCheckpoint()
    {
        var world = Snapshot;
        if (!_xunStartCheckpointsEnabled || !world.Clock.IsXunBoundary || world.Simulation.Boundary != SimulationBoundary.Stable ||
            world.Clock.DayNumber <= _lastXunStartDay) return;
        _lastXunStartDay = world.Clock.DayNumber;
        _xunStartCheckpoint = CaptureCurrentState();
    }
    public WorldSnapshot CaptureStableState()
    {
        if (_executing) throw new InvalidOperationException("Session is executing.");
        return _simulation.CaptureStableState();
    }

    public SessionSnapshot Capture()
    {
        CaptureStableState();
        return CaptureCurrentState();
    }

    internal SessionSnapshot CaptureCurrentState()
    {
        if (_executing) throw new InvalidOperationException("Session is executing.");
        var world = Snapshot;
        if (_capture is not null && ReferenceEquals(_capture.World, world)) return _capture;
        var records = _records.ToImmutableArray();
        var chronicle = _chronicle.ToImmutableArray();
        return _capture = new(world, records, chronicle, NextOperationSequence)
            { InitialPlayerForceIds = InitialPlayerForceIds, Ending = _ending, StrategicContexts = _strategicContexts, PlayerForceController = PlayerForceController, OriginalAi = _originalAi, History = History };
    }
}
