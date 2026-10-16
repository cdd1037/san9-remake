using San9.Application.AI;
using System.Collections.Immutable;
using San9.Core.Simulation;
using San9.Core.Domain;
using San9.Content.Scenarios;
using San9.Application.Presentation;

namespace San9.Application;

public sealed partial class GameSession
{
    private SimulationEngine _simulation;
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
    private StrategySnapshot _view;
    private SessionSnapshot? _capture;
    private bool _executing;
    internal bool IsExecuting => _executing;
    private EndingSelection? _ending;
    public ControllerKind PlayerForceController { get; private set; } = ControllerKind.Player;
    private StrategicContextState _strategicContext;
    public StrategicContextState StrategicContext => _strategicContext;
    public event Action? Changed;

    private GameSession(ScenarioSessionDefinition definition, SimulationEngine simulation)
    {
        _definition = definition;
        _simulation = simulation;
        _initialEvents = simulation.InitialEvents;
        _snapshotSource = new(definition.Content, definition.MilitaryRules);
        _view = _snapshotSource.Project(simulation.Snapshot);
        _initialView = _view;
        _strategicContext = StrategicContextState.Empty with { Clock = simulation.Snapshot.Clock.IsXunBoundary ? simulation.Snapshot.Clock : null };
        _readOnlyRecords = _records.AsReadOnly();
        _readOnlyChronicle = _chronicle.AsReadOnly();
        InitialDate = $"{definition.Content.Year}年{definition.Content.Month}月{definition.Content.Day}日";
    }

    public string InitialDate { get; }
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
        return new(_recordingInitial ?? throw new InvalidOperationException("Recording is not enabled."),
            _recording.ToImmutableArray());
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
    public WorldMessageContext Messages(San9.Content.Text.TextCatalog texts) => new(_definition.Content, Snapshot, texts);
    public WorldMessageContext Messages(San9.Content.Text.TextCatalog texts, WorldSnapshot snapshot) => new(_definition.Content, snapshot, texts);
    public CountryTitlePrompt? GetCountryTitlePrompt() => _simulation.GetCountryTitlePrompt();
    public EmperorTransferPrompt? GetEmperorTransferPrompt() => _simulation.GetEmperorTransferPrompt();
    public OfficeRequestPrompt? GetOfficeRequestPrompt() => _simulation.GetOfficeRequestPrompt();
    public SearchRecruitmentPrompt? GetSearchRecruitmentPrompt() => _simulation.GetSearchRecruitmentPrompt();
    public ShanyangGongPrompt? GetShanyangGongPrompt() => _simulation.GetShanyangGongPrompt();
    public MysticBookOfferPrompt? GetMysticBookOfferPrompt() => _simulation.GetMysticBookOfferPrompt();
    public MysticBookReturnPrompt? GetMysticBookReturnPrompt() => _simulation.GetMysticBookReturnPrompt();
    public ScholarInsultPrompt? GetScholarInsultPrompt() => _simulation.GetScholarInsultPrompt();
    public LongevityPrompt? GetLongevityPrompt() => _simulation.GetLongevityPrompt();
    public TreasureMonumentPrompt? GetTreasureMonumentPrompt() => _simulation.GetTreasureMonumentPrompt();
    public ScholarReturnPrompt? GetScholarReturnPrompt() => _simulation.GetScholarReturnPrompt();
    public FoodTradeQuote GetFoodTradeQuote(int forceId, int settlementId, ImmutableArray<int> officerIds, FoodTradeDirection direction) =>
        _simulation.GetFoodTradeQuote(forceId, settlementId, officerIds, direction);
    public MerchantInvestmentPrompt? GetMerchantInvestmentPrompt() => _simulation.GetMerchantInvestmentPrompt();
    public CommunitySearchPrompt? GetCommunitySearchPrompt() => _simulation.GetCommunitySearchPrompt();
    public SearchConflictPrompt? GetSearchConflictPrompt() => _simulation.GetSearchConflictPrompt();
    public TigerSearchPrompt? GetTigerSearchPrompt() => _simulation.GetTigerSearchPrompt();
    public BeastSearchPrompt? GetBeastSearchPrompt() => _simulation.GetBeastSearchPrompt();
    public TerminalResult GetTerminalResult() => Snapshot.Simulation.Boundary == SimulationBoundary.Terminal
        ? TerminalEvaluation.Evaluate(Snapshot, _definition.Content.Terminal) : new(TerminalRoute.Continue, null, null);
    public EndingSnapshot? GetEndingPrompt() => _ending is null ? EndingSession.Create(_definition.Content, Snapshot) : null;
    public EndingResult? Ending => _ending is null ? null : EndingSession.Resolve(_definition.Content, Snapshot, _ending);
    public TacticStudyPrompt? GetTacticStudyPrompt() => _simulation.GetTacticStudyPrompt();

    public SnipingSuccessionPrompt? GetSnipingSuccessionPrompt() => _simulation.GetSnipingSuccessionPrompt();

    public DuelSuccessionPrompt? GetDuelSuccessionPrompt() => _simulation.GetDuelSuccessionPrompt();

    public ResponseRequest? GetPendingResponse() => _simulation.GetPendingResponse();
    public CapturedRulerPrompt? GetCapturedRulerPrompt() => _simulation.GetCapturedRulerPrompt();
    public ExecutionSuccessionPrompt? GetExecutionSuccessionPrompt() => _simulation.GetExecutionSuccessionPrompt();
    public GameCommand ChooseDomesticCommand(ControlSeat seat, Guid commandId, int turn, bool scripted = false) =>
        DomesticCommandSelection.Choose(Snapshot, Validate, seat, new(seat.ControllerId), commandId, turn, scripted);
    public CommandAdvice GetSurrenderAdvice(int forceId, int officerId, int targetForceId) =>
        _simulation.GetSurrenderAdvice(forceId, officerId, targetForceId);
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

    public System.Collections.Immutable.ImmutableArray<int> GetSearchPlaces(int forceId) => _simulation.GetSearchPlaces(forceId);
    public System.Collections.Immutable.ImmutableArray<int> GetRecruitTargets(int forceId, int officerId) => _simulation.GetRecruitTargets(forceId, officerId);
    public PersonnelJourneyPreview GetPersonnelJourney(int forceId, int officerId, int targetId, PersonnelJourneyKind kind) =>
        _simulation.GetPersonnelJourney(forceId, officerId, targetId, kind);

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
            definition.DomesticPairs, definition.ImperialSealId)
        { FoodMarket = definition.FoodMarket, ImperialCourt = definition.ImperialCourt, RegionCityIds = definition.RegionCityIds.ToImmutableDictionary() };
        var personnel = new PersonnelRulebook(definition.Settlements.Select(s => new PersonPlace(s.Id, s.Position)).Concat(definition.UnbuiltPlaces), definition.RegionCityIds, definition.CityConnections, definition.CityProvinceIds, definition.TacticCourses, definition.SearchEvents);
        var treasures = new TreasureRulebook(definition.Information.Treasures.Select(t => t.ToRule()));
        var governance = new GovernanceRulebook(definition.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { AbilityBonuses = o.AbilityBonuses, Salary = o.Salary, RequiredRankId = o.RequiredRankId }),
            definition.Information.Ranks.ToDictionary(r => r.Id, r => r.Precedence));
        var military = definition.Military is { } militaryDefinition ? new MilitaryRulebook(militaryDefinition) : null;
        var politics = new PoliticalRulebook(definition.PoliticalEvents,
            definition.Information.Ranks.Select(r => new PeerageRule(r.Id, r.Precedence, r.RequiredTrust, r.LoyaltyBonus)),
            definition.Information.CountryTitles.Select(t => t.Rule), definition.ProvinceCountryTitleIds);
        return world => new(world, rules, personnel, treasures, governance, military, definition.Terminal, politics, definition.QiaoMarriage, definition.SunMarriage,
            definition.HanzhongKing, definition.FiveTigers, definition.ExpeditionMemorial, definition.ThreeVisits, definition.ChainedPlot, definition.RedCliffs, definition.ShanyangGong, definition.Guandu);
    }

    public static GameSession Start(ScenarioInitialState definition, ulong seed, int computerDamageBonusPercent = 0,
        bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player)
    {
        if (!Enum.IsDefined(playerForceController)) throw new ArgumentOutOfRangeException(nameof(playerForceController));
        return Start(definition, seed, EngineFactory(definition), computerDamageBonusPercent, battleDeathsEnabled, playerForceController);
    }

    internal static GameSession Start(ScenarioInitialState definition, ulong seed, Func<WorldState, SimulationEngine> factory, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player)
    {
        var owned = new ScenarioSessionDefinition(definition);
        return new(owned, factory(owned.CreateWorld(seed, computerDamageBonusPercent, battleDeathsEnabled, playerForceController)))
            { PlayerForceController = playerForceController };
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
        var session = new GameSession(owned, simulation);
        session._records.AddRange(snapshot.Records);
        session._chronicle.AddRange(snapshot.Chronicle);
        session.NextOperationSequence = snapshot.NextOperationSequence;
        session._ending = snapshot.Ending;
        session._strategicContext = snapshot.StrategicContext;
        session.PlayerForceController = snapshot.PlayerForceController;
        session.HasUnsavedChanges = false;
        return session;
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
            { Ending = _ending, StrategicContext = _strategicContext, PlayerForceController = PlayerForceController };
    }

    public SessionRequest Request(SessionOperation operation, string controllerId = "player", Guid? id = null)
    {
        Guid requestId = id ?? NextId();
        return new(requestId, NextOperationSequence, controllerId, operation);
    }

    private Guid NextId() => SimulationIdentity.Request(NextOperationSequence);

    public SessionResult Apply(SessionRequest request) => Apply(request, false, out _);

    public PresentedAdvance AdvanceForPresentation()
    {
        var result = Apply(Request(new AdvanceOperation()), true, out var frames);
        return new(result, frames.Select(_snapshotSource.Project).ToImmutableArray());
    }

    private SessionResult Apply(SessionRequest request, bool capturePresentation, out IReadOnlyList<WorldSnapshot> presentationFrames)
    {
        presentationFrames = [];
        if (_executing) return Rejected(SessionRejection.NotStable);
        if (request is null || request.Id == Guid.Empty || request.Sequence < 0 ||
            string.IsNullOrWhiteSpace(request.ControllerId) || request.Operation is null)
            return Rejected(SessionRejection.InvalidRequest);
        if (request.Sequence < NextOperationSequence)
            return new(SessionDisposition.AlreadyProcessed, SessionRejection.None, [], Snapshot);
        if (request.Sequence != NextOperationSequence || NextOperationSequence == long.MaxValue)
            return Rejected(SessionRejection.OutOfOrder);
        if (Snapshot.Simulation.Boundary == SimulationBoundary.Terminal && request.Operation is not EndingOperation)
            return Rejected(SessionRejection.Terminal);
        _executing = true;
        SessionResult result;
        try
        {
            // One operation can close several controls and advance. Publish only after all validation succeeds.
            var candidate = _simulation.Fork();
            List<DomainEvent> events = [];
            List<WorldSnapshot> frames = [];
            DevelopmentOutcome? development = null;
            ChronicleEntry? note = null;
            var ending = _ending;
            switch (request.Operation)
            {
                case EndingOperation selection:
                    if (request.ControllerId != "player" || ending is not null) return Rejected(SessionRejection.InvalidRequest);
                    try { _ = EndingSession.Resolve(_definition.Content, candidate.Snapshot, selection.Selection); }
                    catch (ArgumentException) { return Rejected(SessionRejection.InvalidRequest); }
                    ending = selection.Selection;
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
                    AutomaticTurnRunner.EndControl(candidate, new(request.ControllerId), events);
                    break;
                case PrepareAdvanceOperation:
                case AdvanceOperation:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    if (candidate.Snapshot.Simulation.Boundary != SimulationBoundary.Stable)
                        return Rejected(SessionRejection.NotStable);
                    while (candidate.Snapshot.Simulation.Boundary == SimulationBoundary.Stable &&
                        candidate.Snapshot.Simulation.ActiveControl < candidate.Snapshot.Simulation.Controls.Length)
                    {
                        var seat = candidate.Snapshot.Simulation.Controls[candidate.Snapshot.Simulation.ActiveControl];
                        AutomaticTurnRunner.EndControl(candidate, new(seat.ControllerId), events);
                    }
                    if (request.Operation is AdvanceOperation && candidate.Snapshot.Simulation.Boundary == SimulationBoundary.Stable)
                    {
                        var advance = candidate.AdvanceXun(capturePresentation);
                        events.AddRange(advance.Events);
                        frames.AddRange(advance.PresentationFrames);
                    }
                    break;
                case AdvanceDayOperation:
                    if (request.ControllerId != "player") return Rejected(SessionRejection.InvalidRequest);
                    events.AddRange(candidate.AdvanceDay().Events);
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
            AutomaticResponseRunner.Run(candidate, events, capturePresentation ? frames : null);
            var completed = events.OfType<DevelopmentCompletedEvent>().Select(e =>
                new SessionRecord(e.Clock, e.Command.ForceId, e.Command.SettlementId, e.Command.OfficerIds[0],
                    e.Command.Activity, e.Increment, e.Cost)).ToArray();
            var snapshot = candidate.Snapshot;
            var strategicContext = Presentation.StrategicContext.Prepare(_snapshotSource.Project(snapshot), _strategicContext);
            var publishedEvents = events.ToImmutableArray();
            var recorded = IsRecording ? new RecordedOperation(request, publishedEvents) : null;
            _simulation = candidate;
            _ending = ending;
            _strategicContext = strategicContext;
            _records.AddRange(completed);
            if (_records.Count > RecentRecordLimit) _records.RemoveRange(0, _records.Count - RecentRecordLimit);
            if (note is not null) _chronicle.Add(note);
            NextOperationSequence++;
            HasUnsavedChanges = true;
            if (recorded is not null) _recording.Add(recorded);
            _capture = null;
            result = new(SessionDisposition.Accepted, SessionRejection.None, publishedEvents, snapshot, Development: development);
            presentationFrames = frames;
        }
        catch (AutomaticResponseException failure)
        {
            return Rejected(SessionRejection.AutomaticResponseFailed) with { AutomaticResponseFailure = failure.Failure };
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
    public CommandPreparationPreview Preview(CommandContext context, GameCommand command) => _executing
        ? new(CommandRejection.NotStable, null, null) : _simulation.Preview(context, command);
    public ImmutableArray<int> GetGiftTreasures(int forceId) => _executing ? [] : _simulation.GetGiftTreasures(forceId);
    public CommandPreparationPreview GetMissionPreparation(CommandContext context, GameCommand command) => _executing
        ? new(CommandRejection.NotStable, null, null) : _simulation.GetMissionPreparation(context, command);
    public bool CanSelectCorpsDelegation(CommandContext context, int owner, CorpsDelegation delegation) =>
        !_executing && _simulation.CanSelectCorpsDelegation(context, owner, delegation);
    public CommandRejection GetPreparationEligibility(CommandContext context, GameCommand command) => _executing
        ? CommandRejection.NotStable : _simulation.GetPreparationEligibility(context, command);

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

    public void AddChronicleNote(string text)
    {
        var result = Apply(Request(new ChronicleOperation(text)));
        if (result.Disposition == SessionDisposition.Rejected) throw new ArgumentException(result.Rejection.ToString());
    }
}

public sealed record SessionRecord(GameClock Clock, int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity, int Increment, int Cost);

public sealed record PresentedAdvance(SessionResult Result, IReadOnlyList<StrategySnapshot> Frames);
