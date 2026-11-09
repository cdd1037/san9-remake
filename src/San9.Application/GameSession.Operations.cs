using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    // Captured on the owner thread. Calculation forks the engine and never accesses the live session.
    internal sealed record OperationInput(SimulationEngine Simulation, ScenarioSessionDefinition Definition,
        StrategySnapshot View, long Sequence, OriginalAiState OriginalAi,
        EndingSelection? Ending, ImmutableDictionary<int, StrategicContextState> StrategicContexts, ImmutableList<ChronicleFact> History,
        ImmutableArray<DeploymentPreset> DeploymentPresets, SettlementTransitNetwork? TransitNetwork);

    internal sealed record OperationCommit(SimulationEngine Simulation, OriginalAiState OriginalAi,
        ImmutableArray<OriginalAiWorkReport> AiReports, EndingSelection? Ending, ImmutableDictionary<int, StrategicContextState> StrategicContexts,
        ImmutableList<ChronicleFact> History, SessionRecord[] Records, ChronicleEntry? Note, ImmutableArray<DeploymentPreset> DeploymentPresets,
        SettlementTransitNetwork? TransitNetwork);

    internal sealed record PreparedOperation(SessionResult Result, StrategySnapshot View,
        OperationCommit? Commit = null)
    {
        public IReadOnlyList<Presentation.ResolutionPresentationTransition> Transitions { get; init; } = [];
    }

    private OperationInput CaptureOperationInput() => new(_simulation, _definition, View,
        NextOperationSequence, _originalAi, _ending, _strategicContexts, History, DeploymentPresets, _transitNetwork);

    public SessionResult Apply(SessionRequest request)
    {
        if (_executing) return Rejected(SessionRejection.NotStable);
        var input = CaptureOperationInput();
        _executing = true;
        PreparedOperation prepared;
        try { prepared = CalculateOperation(input, request); }
        catch
        {
            _executing = false;
            throw;
        }
        return PublishOperation(request, prepared);
    }

    private static PreparedOperation CalculateOperation(OperationInput input, SessionRequest request, bool captureTransitions = false)
    {
        PreparedOperation Rejected(SessionRejection reason, CommandRejection command = CommandRejection.None,
            DevelopmentOutcome? development = null) =>
            new(new(SessionDisposition.Rejected, reason, [], input.View.World, command, development), input.View);

        if (request is null || request.Id == Guid.Empty || request.Sequence < 0 ||
            string.IsNullOrWhiteSpace(request.ControllerId) || request.Operation is null)
            return Rejected(SessionRejection.InvalidRequest);
        if (request.Sequence < input.Sequence)
            return new(new(SessionDisposition.AlreadyProcessed, SessionRejection.None, [], input.View.World), input.View);
        if (request.Sequence != input.Sequence || input.Sequence == long.MaxValue)
            return Rejected(SessionRejection.OutOfOrder);
        if (input.View.World.Simulation.Boundary == SimulationBoundary.Terminal && request.Operation is not EndingOperation)
            return Rejected(SessionRejection.Terminal);
        try
        {
            // One operation can close several controls and advance. Publish only after all validation succeeds.
            var candidate = input.Simulation.Fork();
            var originalAi = input.OriginalAi;
            List<OriginalAiWorkReport> aiReports = [];
            var terrainRoutes = new OriginalAiTerrainRoutes(input.TransitNetwork);
            List<DomainEvent> events = [];
            List<ResolutionPresentationTransition> transitions = [];
            using var observation = captureTransitions ? candidate.ObserveResolution(transition => transitions.Add(new(transition.Phase,
                events.Count + transition.EventIndex, input.View.ForWorld(transition.Snapshot)))) : null;
            DevelopmentOutcome? development = null;
            ChronicleEntry? note = null;
            var ending = input.Ending;
            var deploymentPresets = input.DeploymentPresets;
            switch (request.Operation)
            {
                case SaveDeploymentPresetOperation savePreset:
                    if (request.ControllerId != "player" || savePreset.Preset is null ||
                        input.View.PlayerForceId != savePreset.Preset.ForceId)
                        return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable || originalAi.Continuation is not null)
                        return Rejected(SessionRejection.NotStable);
                    try { savePreset.Preset.Validate(input.View); }
                    catch (ArgumentException) { return Rejected(SessionRejection.InvalidRequest); }
                    var previousPreset = deploymentPresets.FirstOrDefault(p => p.Id == savePreset.Preset.Id);
                    if (previousPreset is not null && previousPreset.ForceId != savePreset.Preset.ForceId)
                        return Rejected(SessionRejection.InvalidRequest);
                    deploymentPresets = previousPreset is null ? deploymentPresets.Add(savePreset.Preset)
                        : deploymentPresets.Replace(previousPreset, savePreset.Preset);
                    break;
                case DeleteDeploymentPresetOperation deletePreset:
                    var removedPreset = deploymentPresets.FirstOrDefault(p => p.Id == deletePreset.PresetId);
                    if (request.ControllerId != "player" || removedPreset is null || input.View.PlayerForceId != removedPreset.ForceId)
                        return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable || originalAi.Continuation is not null)
                        return Rejected(SessionRejection.NotStable);
                    deploymentPresets = deploymentPresets.Remove(removedPreset);
                    break;
                case WorldEditOperation edit:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable || originalAi.Continuation is not null)
                        return Rejected(SessionRejection.NotStable);
                    try { events.AddRange(candidate.EditWorld(edit.Kind, edit.Edits)); }
                    catch (ArgumentException) { return Rejected(SessionRejection.InvalidRequest); }
                    break;
                case EndingOperation selection:
                    if (request.ControllerId != "player" || ending is not null) return Rejected(SessionRejection.InvalidRequest);
                    try { _ = EndingSession.Resolve(input.Definition, candidate.Snapshot, selection.Selection); }
                    catch (ArgumentException) { return Rejected(SessionRejection.InvalidRequest); }
                    ending = selection.Selection;
                    break;
                case ForceControlOperation:
                case HandOverAllForcesOperation:
                    if (request.ControllerId != "player")
                        return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable || originalAi.Continuation is not null)
                        return Rejected(SessionRejection.NotStable);
                    try
                    {
                        if (request.Operation is ForceControlOperation control)
                            candidate.SetForceControl(control.ForceId, control.Kind);
                        else
                            candidate.HandOverAllForces();
                    }
                    catch (ArgumentException) { return Rejected(SessionRejection.InvalidRequest); }
                    break;
                case PkSettingsOperation pk:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable)
                        return Rejected(SessionRejection.NotStable);
                    candidate.SetPkSettings(pk.OtherForceOrdersVisible, pk.OtherForceTacticsVisible, pk.BattleDeathsEnabled);
                    break;
                case CommandOperation execute:
                    if (execute.Command is null || execute.Command.CommandId != request.Id)
                        return Rejected(SessionRejection.InvalidRequest);
                    var command = candidate.Execute(new(request.ControllerId), execute.Command);
                    if (!command.Submission.Accepted)
                        return Rejected(SessionRejection.CommandRejected, command.Submission.Rejection, command.Development);
                    development = command.Development;
                    events.AddRange(command.Events);
                    break;
                case EndControlOperation:
                    var controlState = candidate.Snapshot.Simulation;
                    if (controlState.Boundary != SimulationBoundary.Stable ||
                        controlState.ActiveControl >= controlState.Controls.Length ||
                        controlState.Controls[controlState.ActiveControl].ControllerId != request.ControllerId)
                        return Rejected(SessionRejection.NotStable);
                    originalAi = AutomaticTurnRunner.EndControl(candidate, new(request.ControllerId), events, input.Definition, originalAi, aiReports, terrainRoutes);
                    break;
                case PrepareAdvanceOperation:
                case AdvanceOperation:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable)
                        return Rejected(SessionRejection.NotStable);
                    bool closedPlayer = false;
                    while (candidate.Snapshot.Simulation.Boundary == SimulationBoundary.Stable &&
                        candidate.Snapshot.Simulation.ActiveControl < candidate.Snapshot.Simulation.Controls.Length)
                    {
                        var seat = candidate.Snapshot.Simulation.Controls[candidate.Snapshot.Simulation.ActiveControl];
                        if (request.Operation is PrepareAdvanceOperation && closedPlayer && seat.Kind == ControllerKind.Player) break;
                        closedPlayer |= seat.Kind == ControllerKind.Player;
                        originalAi = AutomaticTurnRunner.EndControl(candidate, new(seat.ControllerId), events, input.Definition, originalAi, aiReports, terrainRoutes);
                    }
                    if (request.Operation is AdvanceOperation && candidate.Snapshot.Simulation.Boundary == SimulationBoundary.Stable)
                    {
                        var advance = candidate.AdvanceXun();
                        events.AddRange(advance.Events);
                    }
                    break;
                case AdvanceDayOperation:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    var dayState = candidate.Snapshot.Simulation;
                    if (dayState.Boundary != (dayState.DailyAdvanceStarted is null ? SimulationBoundary.Stable : SimulationBoundary.Advancing) ||
                        dayState.ActiveControl != dayState.Controls.Length)
                        return Rejected(SessionRejection.NotStable);
                    events.AddRange(candidate.AdvanceDay().Events);
                    break;
                case ChronicleOperation chronicle:
                    if (request.ControllerId != "player" || chronicle.Text is null || chronicle.Text.Trim().Length is < 1 or > 240)
                        return Rejected(SessionRejection.InvalidRequest);
                    if (input.View.PlayerForceId is not int noteForce) return Rejected(SessionRejection.InvalidRequest);
                    var clock = candidate.Snapshot.Clock;
                    note = new(clock.Year, clock.Month, clock.Day, noteForce, chronicle.Text.Trim());
                    break;
                default:
                    return Rejected(SessionRejection.InvalidRequest);
            }
            AutomaticResponseRunner.Run(candidate, events);
            originalAi = AutomaticTurnRunner.Resume(candidate, events, input.Definition, originalAi, aiReports, terrainRoutes);
            var completed = events.OfType<DevelopmentCompletedEvent>().Select(e =>
                new SessionRecord(e.Clock, e.Command.ForceId, e.Command.SettlementId, e.Command.OfficerIds[0],
                    e.Command.Activity, e.Increment, e.Cost)).ToArray();
            var snapshot = candidate.Snapshot;
            var view = input.View.ForWorld(snapshot);
            var history = ChronicleHistory.Append(input.History, ChronicleHistory.Read(input.View, view, events, input.Definition.Content.Terminal));
            var strategicContexts = input.StrategicContexts.Where(p => snapshot.Simulation.Controls.Any(c =>
                c.ForceId == p.Key && c.Kind == ControllerKind.Player)).ToImmutableDictionary();
            if (view.PlayerForceId is int force)
                strategicContexts = strategicContexts.SetItem(force, Presentation.StrategicContext.Prepare(view,
                    strategicContexts.GetValueOrDefault(force, StrategicContextState.Empty)));
            var publishedEvents = events.ToImmutableArray();
            var commit = new OperationCommit(candidate, originalAi.Reconcile(snapshot), aiReports.ToImmutableArray(),
                ending, strategicContexts, history, completed, note, deploymentPresets,
                terrainRoutes.CachedTransitNetwork);
            return new(new(SessionDisposition.Accepted, SessionRejection.None, publishedEvents, snapshot, Development: development),
                view, commit) { Transitions = transitions.ToArray() };
        }
        catch (AutomaticResponseException failure)
        {
            var rejected = Rejected(SessionRejection.AutomaticResponseFailed);
            return rejected with { Result = rejected.Result with { AutomaticResponseFailure = failure.Failure } };
        }
    }

    // Both synchronous and background operations commit here, exactly once on the owner thread.
    private SessionResult PublishOperation(SessionRequest request, PreparedOperation prepared)
    {
        try
        {
            if (prepared.Commit is { } commit)
            {
                _simulation = commit.Simulation;
                _transitNetwork = commit.TransitNetwork;
                _view = prepared.View;
                _originalAi = commit.OriginalAi;
                LastOriginalAiWork = commit.AiReports;
                _ending = commit.Ending;
                _strategicContexts = commit.StrategicContexts;
                History = commit.History;
                DeploymentPresets = commit.DeploymentPresets;
                _records.AddRange(commit.Records);
                if (_records.Count > RecentRecordLimit) _records.RemoveRange(0, _records.Count - RecentRecordLimit);
                if (commit.Note is not null) _chronicle.Add(commit.Note);
                NextOperationSequence++;
                HasUnsavedChanges = true;
                _capture = null;
            }
        }
        finally { _executing = false; }
        if (prepared.Commit is not null)
        {
            PrepareXunStartCheckpoint();
        }
        return prepared.Result;
    }
}
