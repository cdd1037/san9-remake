using System.Collections.Immutable;
using San9.Core.Simulation;
using San9.Core.Domain;
using San9.Content.Scenarios;
using San9.Application.Presentation;

namespace San9.Application;

public sealed class GameSession
{
    private SimulationEngine _simulation;
    private readonly Func<WorldState, SimulationEngine> _engineFactory;
    private readonly ScenarioSessionDefinition _definition;
    private readonly StrategySnapshotSource _snapshotSource;
    private readonly StrategySnapshot _initialView;
    private readonly IReadOnlyList<DomainEvent> _initialEvents;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<SessionRecord> _readOnlyRecords;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<ChronicleEntry> _readOnlyChronicle;
    private readonly List<SessionRecord> _records = [];
    private readonly List<ChronicleEntry> _chronicle = [];
    public const int RecentRecordLimit = 128;
    private SessionSnapshot? _recordingInitial;
    private readonly List<RecordedOperation> _recording = [];
    public bool IsRecording => _recordingInitial is not null;
    public long NextOperationSequence { get; private set; }
    private StrategySnapshot _view;
    private SessionSnapshot? _capture;
    private bool _executing;
    public event Action? Changed;

    private GameSession(ScenarioSessionDefinition definition, SimulationEngine simulation,
        Func<WorldState, SimulationEngine> engineFactory)
    {
        _definition = definition;
        _simulation = simulation;
        _engineFactory = engineFactory;
        _initialEvents = simulation.InitialEvents;
        _snapshotSource = new(definition.Content, definition.MilitaryRules);
        _view = _snapshotSource.Project(simulation.Snapshot);
        _initialView = _view;
        _readOnlyRecords = _records.AsReadOnly();
        _readOnlyChronicle = _chronicle.AsReadOnly();
        InitialDate = $"{definition.Content.Year}年{definition.Content.Month}月{definition.Content.Day}日";
    }

    public string InitialDate { get; }
    public string ContentFingerprint => _definition.Fingerprint;
    internal MilitaryRulebook? MilitaryRules => _definition.MilitaryRules;
    public IReadOnlyList<DomainEvent> InitialEvents => _initialEvents;
    public IReadOnlyList<SessionRecord> Records => _readOnlyRecords;
    public IReadOnlyList<ChronicleEntry> Chronicle => _readOnlyChronicle;
    public void BeginRecording()
    {
        if (IsRecording) throw new InvalidOperationException("Recording is already active.");
        _recordingInitial = Capture();
        _recording.Clear();
    }

    public SessionRecording CaptureRecording()
    {
        CaptureStableState();
        return new(ContentFingerprint, _recordingInitial ?? throw new InvalidOperationException("Recording is not enabled."),
            _recording.ToImmutableArray(), Capture().StateHash);
    }

    public SessionRecording EndRecording()
    {
        var recording = CaptureRecording();
        _recordingInitial = null;
        _recording.Clear();
        _recording.TrimExcess();
        return recording;
    }
    public WorldSnapshot Snapshot => _simulation.Snapshot;
    public AttackRequestPrompt? GetAttackRequestPrompt() => _simulation.GetAttackRequestPrompt();
    public CommandAdvice GetAttackRequestAdvice(int forceId, int officerId, int targetForceId, int targetSettlementId) =>
        _simulation.GetAttackRequestAdvice(forceId, officerId, targetForceId, targetSettlementId);
    public PrisonerReturnPrompt? GetPrisonerReturnPrompt() => _simulation.GetPrisonerReturnPrompt();
    public CommandAdvice GetPrisonerReturnAdvice(int forceId, int officerId, int targetForceId, int prisonerId) =>
        _simulation.GetPrisonerReturnAdvice(forceId, officerId, targetForceId, prisonerId);
    public CommandAdvice GetGiftAdvice(int forceId, int officerId, int targetForceId, GiftOffer offer) =>
        _simulation.GetGiftAdvice(forceId, officerId, targetForceId, offer);
    public AllianceFormationCandidates GetAllianceCandidates() => _simulation.GetAllianceCandidates();
    public AllianceParticipationPrompt? GetAllianceParticipationPrompt() => _simulation.GetAllianceParticipationPrompt();
    public AttackWarning GetAttackWarning(int forceId, MilitaryOrder order) => _simulation.GetAttackWarning(forceId, order);

    public CommandAdvice GetBurnAdvice(int forceId, int officerId, int targetSettlementId) =>
        _simulation.GetBurnAdvice(forceId, officerId, targetSettlementId);

    public CommandAdvice GetUnitStrategyAdvice(int forceId, int officerId, UnitStrategyKind strategy, int targetUnitId) =>
        _simulation.GetUnitStrategyAdvice(forceId, officerId, strategy, targetUnitId);
    public CommandAdvice GetAlienateAdvice(int forceId, int officerId, int targetOfficerId) =>
        _simulation.GetAlienateAdvice(forceId, officerId, targetOfficerId);
    public CommandAdvice GetSeizeAdvice(int forceId, int officerId, int targetSettlementId) =>
        _simulation.GetSeizeAdvice(forceId, officerId, targetSettlementId);
    public CommandAdvice GetGossipAdvice(int forceId, int officerId, int firstTargetForceId,
        int secondTargetForceId) => _simulation.GetGossipAdvice(forceId, officerId, firstTargetForceId,
        secondTargetForceId);
    public StrategySnapshot InitialView => _initialView;
    public StrategySnapshot View => _view.World.Revision == Snapshot.Revision ? _view : _view = _snapshotSource.Project(Snapshot);

    internal static Func<WorldState, SimulationEngine> EngineFactory(ScenarioInitialState definition)
    {
        var rules = new EconomyRulebook(definition.Information.Offices.Select(o => new OfficeSalary(o.Id, o.Salary)).ToImmutableArray(),
            definition.DomesticPairs, definition.ImperialSealId, definition.EmperorId);
        var personnel = new PersonnelRulebook(definition.Settlements.Select(s => new PersonPlace(s.Id, s.Position)).Concat(definition.UnbuiltPlaces), definition.RegionCityIds, definition.CityConnections, definition.CityProvinceIds);
        var treasures = new TreasureRulebook(definition.Information.Treasures.Select(t => t.ToRule()));
        var governance = new GovernanceRulebook(definition.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { AbilityBonuses = o.AbilityBonuses, Salary = o.Salary, RequiredRankId = o.RequiredRankId }),
            definition.Information.Ranks.ToDictionary(r => r.Id, r => r.RequiredTrust));
        var military = definition.Military is { } militaryDefinition ? new MilitaryRulebook(militaryDefinition) : null;
        return world => new(world, rules, personnel, treasures, governance, military);
    }

    public static string Fingerprint(ScenarioInitialState definition) => new ScenarioSessionDefinition(definition).Fingerprint;
    public static GameSession Start(ScenarioInitialState definition, ulong seed, int computerDamageBonusPercent = 0) =>
        Start(definition, seed, EngineFactory(definition), computerDamageBonusPercent);

    internal static GameSession Start(ScenarioInitialState definition, ulong seed, Func<WorldState, SimulationEngine> factory, int computerDamageBonusPercent = 0)
    {
        var owned = new ScenarioSessionDefinition(definition);
        return new(owned, factory(owned.CreateWorld(seed, computerDamageBonusPercent)), factory);
    }

    public static GameSession Restore(ScenarioInitialState definition, SessionSnapshot snapshot) =>
        Restore(definition, snapshot, EngineFactory(definition));

    // A player load starts a new random run; exact restoration above is used by replay and diagnostics.
    internal static GameSession Load(ScenarioSessionDefinition owned, SessionSnapshot snapshot,
        Func<WorldState, SimulationEngine> factory)
    {
        _ = WorldState.Restore(snapshot.World);
        SessionValidation.Validate(owned, snapshot);
        ulong seed;
        do
        {
            seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
        } while (seed == snapshot.World.Simulation.RunSeed);
        var next = snapshot with { World = snapshot.World with
        {
            Simulation = snapshot.World.Simulation with { RunSeed = seed, RandomStreams = [] }
        } };
        return RestoreValidated(owned, next, WorldState.Restore(next.World), factory);
    }

    internal static GameSession Restore(ScenarioInitialState definition, SessionSnapshot snapshot, Func<WorldState, SimulationEngine> factory)
    {
        return Restore(new ScenarioSessionDefinition(definition), snapshot, factory);
    }

    internal static GameSession Restore(ScenarioSessionDefinition owned, SessionSnapshot snapshot, Func<WorldState, SimulationEngine> factory)
    {
        var world = WorldState.Restore(snapshot.World);
        SessionValidation.Validate(owned, snapshot);
        return RestoreValidated(owned, snapshot, world, factory);
    }

    private static GameSession RestoreValidated(ScenarioSessionDefinition owned, SessionSnapshot snapshot, WorldState world,
        Func<WorldState, SimulationEngine> factory)
    {
        var simulation = factory(world);
        var session = new GameSession(owned, simulation, factory);
        session._records.AddRange(snapshot.Records);
        session._chronicle.AddRange(snapshot.Chronicle);
        session.NextOperationSequence = snapshot.NextOperationSequence;
        return session;
    }

    public WorldSnapshot CaptureStableState()
    {
        if (_executing) throw new InvalidOperationException("Session is executing.");
        return _simulation.CaptureStableState();
    }

    public SessionSnapshot Capture()
    {
        var world = CaptureStableState();
        if (_capture is not null && ReferenceEquals(_capture.World, world)) return _capture;
        var records = _records.ToImmutableArray();
        var chronicle = _chronicle.ToImmutableArray();
        return _capture = new(world, records, chronicle, NextOperationSequence);
    }

    public SessionRequest Request(SessionOperation operation, string controllerId = "player", Guid? id = null)
    {
        Guid requestId = id ?? NextId();
        return new(requestId, NextOperationSequence, controllerId, operation);
    }

    private Guid NextId()
    {
        // Stable sequence watermarks identify requests without retaining their history.
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, Snapshot.Simulation.InitialSeed);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)NextOperationSequence + 1);
        return new Guid(bytes, bigEndian: true);
    }

    public SessionResult Apply(SessionRequest request)
    {
        if (_executing) return Rejected(SessionRejection.NotStable);
        if (request is null || request.Id == Guid.Empty || request.Sequence < 0 ||
            string.IsNullOrWhiteSpace(request.ControllerId) || request.Operation is null)
            return Rejected(SessionRejection.InvalidRequest);
        if (request.Sequence < NextOperationSequence)
            return new(SessionDisposition.AlreadyProcessed, SessionRejection.None, [], Snapshot);
        if (request.Sequence != NextOperationSequence || NextOperationSequence == long.MaxValue)
            return Rejected(SessionRejection.OutOfOrder);
        if (Snapshot.Simulation.Boundary == SimulationBoundary.Terminal) return Rejected(SessionRejection.Terminal);
        _executing = true;
        SessionResult result;
        try
        {
            // One operation can close several controls and advance. Publish only after all validation succeeds.
            var candidate = _engineFactory(WorldState.Restore(_simulation.CaptureStableState()));
            List<DomainEvent> events = [];
            DevelopmentOutcome? development = null;
            ChronicleEntry? note = null;
            switch (request.Operation)
            {
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
                    var active = candidate.Snapshot.Simulation;
                    if (active.ActiveControl < active.Controls.Length && active.Controls[active.ActiveControl].ControllerId == request.ControllerId)
                        AutomaticCorpsPlanner.Run(candidate, active.Controls[active.ActiveControl], MilitaryRules, events);
                    events.AddRange(candidate.EndControl(new(request.ControllerId)).Events);
                    break;
                case AdvanceOperation:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    while (candidate.Snapshot.Simulation.ActiveControl < candidate.Snapshot.Simulation.Controls.Length)
                    {
                        var seat = candidate.Snapshot.Simulation.Controls[candidate.Snapshot.Simulation.ActiveControl];
                        AutomaticCorpsPlanner.Run(candidate, seat, MilitaryRules, events);
                        events.AddRange(candidate.EndControl(new(seat.ControllerId)).Events);
                    }
                    events.AddRange(candidate.AdvanceXun().Events);
                    break;
                case ChronicleOperation chronicle:
                    if (request.ControllerId != "player" || chronicle.Text is null || chronicle.Text.Trim().Length is < 1 or > 240)
                        return Rejected(SessionRejection.InvalidRequest);
                    var clock = candidate.Snapshot.Clock;
                    note = new(clock.Year, clock.Month, clock.Day, View.PlayerForceId, chronicle.Text.Trim());
                    break;
                default:
                    return Rejected(SessionRejection.InvalidRequest);
            }
            var completed = events.OfType<DevelopmentCompletedEvent>().Select(e =>
                new SessionRecord(e.Clock, e.Command.ForceId, e.Command.SettlementId, e.Command.OfficerIds[0],
                    e.Command.Activity, e.Increment, e.Cost)).ToArray();
            var snapshot = candidate.CaptureStableState();
            var publishedEvents = events.ToImmutableArray();
            var recorded = IsRecording ? new RecordedOperation(request, publishedEvents) : null;
            _simulation = candidate;
            _records.AddRange(completed);
            if (_records.Count > RecentRecordLimit) _records.RemoveRange(0, _records.Count - RecentRecordLimit);
            if (note is not null) _chronicle.Add(note);
            NextOperationSequence++;
            if (recorded is not null) _recording.Add(recorded);
            _capture = null;
            result = new(SessionDisposition.Accepted, SessionRejection.None, publishedEvents, snapshot, Development: development);
        }
        catch (InvalidOperationException) { return Rejected(SessionRejection.NotStable); }
        finally { _executing = false; }
        Changed?.Invoke();
        return result;
    }

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

    public CommandSubmissionResult Validate(CommandContext context, GameCommand command) => _executing
        ? CommandSubmissionResult.Reject(CommandRejection.NotStable) : _simulation.Validate(context, command);

    public AdvanceXunResult EndControl(CommandContext context)
    {
        var result = Apply(Request(new EndControlOperation(), context.ControllerId));
        if (result.Disposition == SessionDisposition.Rejected) throw new InvalidOperationException(result.Rejection.ToString());
        return new(result.Events, result.Snapshot);
    }

    public AdvanceXunResult AdvanceXun()
    {
        if (Snapshot.Simulation.Boundary == SimulationBoundary.Terminal) return new([], Snapshot);
        var result = Apply(Request(new AdvanceOperation()));
        if (result.Disposition == SessionDisposition.Rejected) throw new InvalidOperationException(result.Rejection.ToString());
        return new(result.Events, result.Snapshot);
    }

    public void AddChronicleNote(string text)
    {
        var result = Apply(Request(new ChronicleOperation(text)));
        if (result.Disposition == SessionDisposition.Rejected) throw new ArgumentException(result.Rejection.ToString());
    }
}

public sealed record SessionRecord(GameClock Clock, int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity, int Increment, int Cost);
