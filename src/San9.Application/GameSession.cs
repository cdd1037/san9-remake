using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    private SimulationEngine _simulation;
    private readonly ScenarioSessionDefinition _definition;
    private readonly StrategySnapshotSource _snapshotSource;
    private StrategySnapshot _view;
    private readonly StrategySnapshot _initialView;
    private readonly IReadOnlyList<DomainEvent> _initialEvents;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<SessionRecord> _readOnlyRecords;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<ChronicleEntry> _readOnlyChronicle;
    private readonly List<SessionRecord> _records = [];
    private readonly List<ChronicleEntry> _chronicle = [];
    public const int RecentRecordLimit = 128;
    public long NextOperationSequence { get; private set; }
    private bool _executing;
    internal bool IsExecuting => _executing;
    public bool CanEditWorld => !_executing && Snapshot.Simulation.Boundary == SimulationBoundary.Stable && _originalAi.Continuation is null;
    private EndingSelection? _ending;
    /// <summary>Initial controller configuration, used to reconstruct command logs.</summary>
    public ControllerKind PlayerForceController { get; private set; } = ControllerKind.Player;
    private OriginalAiState _originalAi;
    public OriginalAiState OriginalAi => _originalAi;
    public ImmutableArray<OriginalAiWorkReport> LastOriginalAiWork { get; private set; } = [];
    private ImmutableDictionary<int, StrategicContextState> _strategicContexts = ImmutableDictionary<int, StrategicContextState>.Empty;
    public StrategicContextState StrategicContext => View.PlayerForceId is int force
        ? _strategicContexts.GetValueOrDefault(force, StrategicContextState.Empty) : StrategicContextState.Empty;
    public event Action? Changed;

    public string InitialDate { get; }
    internal MilitaryRulebook? MilitaryRules => _definition.MilitaryRules;
    public IReadOnlyList<DomainEvent> InitialEvents => _initialEvents;
    public IReadOnlyList<SessionRecord> Records => _readOnlyRecords;
    public IReadOnlyList<ChronicleEntry> Chronicle => _readOnlyChronicle;
    public ImmutableArray<ChronicleFact> History { get; private set; } = [];
    public WorldSnapshot Snapshot => _simulation.Snapshot;
    public StrategySnapshot InitialView => _initialView;
    public StrategySnapshot View => _view.World.Revision == Snapshot.Revision ? _view : _view = _snapshotSource.Project(Snapshot);

    public SessionRequest Request(SessionOperation operation, string controllerId = "player", Guid? id = null)
    {
        Guid requestId = id ?? NextId();
        return new(requestId, NextOperationSequence, controllerId, operation);
    }

    private Guid NextId() => SimulationIdentity.Request(NextOperationSequence);

    private SessionResult Rejected(SessionRejection reason, CommandRejection command = CommandRejection.None, DevelopmentOutcome? development = null) =>
        new(SessionDisposition.Rejected, reason, [], Snapshot, command, development);

    public DevelopmentOutcome Develop(DevelopmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = NextId();
        var result = Execute(new("player"), new DevelopCommand(id, command.ForceId, Snapshot.Simulation.NextSequence, command));
        return result.Development ?? new(DevelopmentRejection.NotAuthorized);
    }

    public CommandExecutionResult Execute(CommandContext context, GameCommand command)
    {
        var result = Apply(Request(new CommandOperation(command), context.ControllerId, command.CommandId));
        CommandRejection rejection = result.CommandRejection != CommandRejection.None ? result.CommandRejection : result.Rejection switch
        {
            SessionRejection.None => CommandRejection.None,
            SessionRejection.OutOfOrder => CommandRejection.WrongSequence,
            SessionRejection.NotStable or SessionRejection.Terminal => CommandRejection.NotStable,
            _ => CommandRejection.UnsupportedCommand,
        };
        return new(new(result.Disposition != SessionDisposition.Rejected, rejection), result.Events, result.Snapshot, result.Development);
    }

    public AdvanceXunResult EndControl(CommandContext context)
    {
        var result = Apply(Request(new EndControlOperation(), context.ControllerId));
        if (result.Disposition == SessionDisposition.Rejected) throw new InvalidOperationException(result.AutomaticResponseFailure is { } failure
            ? failure.ToString() : result.Rejection.ToString());
        return new(result.Events, result.Snapshot);
    }

    public AdvanceXunResult AdvanceXun()
    {
        if (Snapshot.Simulation.Boundary == SimulationBoundary.Terminal) return new([], Snapshot);
        var result = Apply(Request(new AdvanceOperation()));
        if (result.Disposition == SessionDisposition.Rejected) throw new InvalidOperationException(result.AutomaticResponseFailure is { } failure
            ? failure.ToString() : result.Rejection.ToString());
        return new(result.Events, result.Snapshot);
    }

    public bool CanChangeForceControl => !_executing && Snapshot.Simulation.Boundary == SimulationBoundary.Stable &&
        _originalAi.Continuation is null;

    public SessionResult HandOverForce(int forceId) => Apply(Request(new ForceControlOperation(forceId, ControllerKind.Automatic)));

    public SessionResult TakeOverForce(int forceId) => Apply(Request(new ForceControlOperation(forceId, ControllerKind.Player)));

    public void AddChronicleNote(string text)
    {
        var result = Apply(Request(new ChronicleOperation(text)));
        if (result.Disposition == SessionDisposition.Rejected) throw new ArgumentException(result.Rejection.ToString());
    }
}

public sealed record SessionRecord(GameClock Clock, int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity, int Increment, int Cost);
