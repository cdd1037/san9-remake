using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed class SimulationEngine
{
    private readonly WorldState _world;
    private readonly SimulationRules _rules;
    private readonly ResolutionPipeline _pipeline;

    public SimulationEngine(WorldState world) : this(world, new SimulationRules()) { }
    public SimulationEngine(WorldState world, EconomyRulebook economy) : this(world, new SimulationRules { Economy = economy }) { }
    public SimulationEngine(WorldState world, EconomyRulebook economy, PersonnelRulebook personnel)
        : this(world, new SimulationRules { Economy = economy, Personnel = personnel }) { }

    public SimulationEngine(WorldState world, EconomyRulebook economy, PersonnelRulebook personnel, TreasureRulebook treasures,
        GovernanceRulebook? governance = null, MilitaryRulebook? military = null)
        : this(ValidateContent(world, treasures, governance), new SimulationRules
            { Economy = economy, Personnel = personnel, Treasures = treasures, Governance = governance ?? new([]), Military = military }) { }

    private static WorldState ValidateContent(WorldState world, TreasureRulebook treasures, GovernanceRulebook? governance)
    {
        var snapshot = world.CreateSnapshot();
        if (snapshot.Treasures.Any(t => t.Discovery != TreasureDiscovery.Disabled && !treasures.Items.ContainsKey(t.Id)) ||
            snapshot.Officers.Any(p => p.OfficeId is int id && (governance is null || !governance.Offices.ContainsKey(id))))
            throw new ArgumentException("Unknown ability source definition.");
        return world;
    }

    internal SimulationEngine(WorldState world, SimulationRules rules)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _rules = rules;
        MilitaryMovement.Validate(world.CreateSnapshot(), rules.Military);
        rules.Economy.Validate();
        _pipeline = new(rules);
        foreach (var task in world.Simulation.Tasks)
            if (rules.FindTask(task.Kind) is not TaskRule rule || !rule.HasValidPayload(task) || rule.DurationDays(task) != task.Due.DayNumber - task.Started.DayNumber)
                throw new ArgumentException($"Unsupported or invalid task '{task.Kind}'.");
        if (world.Simulation.Boundary == SimulationBoundary.New)
        {
            _world.EnterExecution();
            try
            {
                var mutation = world.BeginMutation(rules.Abilities);
                List<DomainEvent> events = [];
                _pipeline.Prepare(mutation, events);
                world.Commit(mutation);
                InitialEvents = events.AsReadOnly();
            }
            finally { _world.ExitExecution(); }
        }
    }

    public IReadOnlyList<DomainEvent> InitialEvents { get; } = Array.Empty<DomainEvent>();
    public WorldSnapshot Snapshot => _world.CreateSnapshot();
    public WorldSnapshot CaptureStableState()
    {
        if (_world.IsExecuting || _world.Simulation.Boundary == SimulationBoundary.New)
            throw new InvalidOperationException("Simulation is executing.");
        return Snapshot;
    }

    public CommandSubmissionResult Validate(CommandContext context, GameCommand command) =>
        Evaluate(context, command, commit: false).Submission;

    public CommandAdvice GetUnitStrategyAdvice(int forceId, int officerId, UnitStrategyKind strategy, int targetUnitId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return UnitStrategies.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, strategy, targetUnitId, _rules);
    }

    public AttackWarning GetAttackWarning(int forceId, MilitaryOrder order)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return MilitaryDiplomacy.Warning(_world.BeginMutation(_rules.Abilities), forceId, order, _rules.Military);
    }

    public CommandExecutionResult Execute(CommandContext context, GameCommand command) =>
        Evaluate(context, command, commit: true);

    private CommandExecutionResult Evaluate(CommandContext context, GameCommand command, bool commit)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rejection = ValidateEnvelope(context, command);
        if (rejection != CommandRejection.None) return Reject(rejection);
        _world.EnterExecution();
        try
        {
            var mutation = _world.BeginMutation(_rules.Abilities);
            List<DomainEvent> events = [];
            DevelopmentOutcome? development = null;
            switch (command)
            {
                case WaitCommand:
                    break;
                case UnitStrategyCommand strategy:
                    var strategyRejection = UnitStrategies.Start(mutation, context, strategy, _rules, events);
                    if (strategyRejection != CommandRejection.None) return Reject(strategyRejection);
                    GovernanceMaintenance.RepairCommander(mutation, mutation.Officers[strategy.OfficerId].SettlementId!.Value, _rules.Governance, events);
                    break;
                case SelectTacticCommand tactic:
                    var tacticRejection = MilitaryTactics.Select(mutation, tactic, _rules, events);
                    if (tacticRejection != CommandRejection.None) return Reject(tacticRejection);
                    break;
                case DeployUnitCommand deploy:
                    var deployRejection = MilitaryMovement.Deploy(mutation, deploy, _rules.Military, _rules.Governance, _rules.Treasures, events);
                    if (deployRejection != CommandRejection.None) return Reject(deployRejection);
                    break;
                case SetUnitPoliciesCommand policies:
                    var policyRejection = MilitaryPolicies.Set(mutation, policies, events);
                    if (policyRejection != CommandRejection.None) return Reject(policyRejection);
                    break;
                case DemolishFacilityCommand demolish:
                    var demolitionRejection = FacilityDemolition.Apply(mutation, demolish, _rules, events);
                    if (demolitionRejection != CommandRejection.None) return Reject(demolitionRejection);
                    break;
                case ConstructFacilityCommand construct:
                    var constructionRejection = FacilityConstruction.Start(mutation, construct, _rules, events);
                    if (constructionRejection != CommandRejection.None) return Reject(constructionRejection);
                    break;
                case OrderUnitCommand order:
                    var orderRejection = MilitaryMovement.Order(mutation, order, _rules.Military, events);
                    if (orderRejection != CommandRejection.None) return Reject(orderRejection);
                    break;
                case ReformUnitCommand reform:
                    var reformRejection = UnitReformation.Apply(mutation, reform, _rules.Military, _rules.Treasures, _rules.Governance, events);
                    if (reformRejection != CommandRejection.None) return Reject(reformRejection);
                    break;
                case SetCorpsControlCommand control:
                    var controlRejection = CorpsControl.Set(mutation, control, events);
                    if (controlRejection != CommandRejection.None) return Reject(controlRejection);
                    break;
                case DissolveCorpsCommand dissolve:
                    var dissolveRejection = CorpsGovernance.Dissolve(mutation, dissolve, _rules.Governance, events);
                    if (dissolveRejection != CommandRejection.None) return Reject(dissolveRejection);
                    break;
                case CreateCorpsCommand create:
                    var createRejection = CorpsGovernance.Create(mutation, create, _rules.Personnel, _rules.Governance, events);
                    if (createRejection != CommandRejection.None) return Reject(createRejection);
                    break;
                case ChangeCorpsCommand change:
                    var changeRejection = CorpsGovernance.Change(mutation, change, _rules.Personnel, _rules.Governance, events);
                    if (changeRejection != CommandRejection.None) return Reject(changeRejection);
                    break;
                case AppointAdviserCommand appoint:
                    var appointRejection = PersonnelAppointments.Apply(mutation, appoint, events);
                    if (appointRejection != CommandRejection.None) return Reject(appointRejection);
                    break;
                case AssignOfficesCommand offices:
                    var officesRejection = PersonnelOffices.Apply(mutation, offices, _rules.Governance, events);
                    if (officesRejection != CommandRejection.None) return Reject(officesRejection);
                    break;
                case RewardOfficerCommand reward:
                    var rewardRejection = PersonnelRewards.Apply(mutation, reward, events);
                    if (rewardRejection != CommandRejection.None) return Reject(rewardRejection);
                    break;
                case GrantTreasureCommand grant:
                    var grantRejection = PersonnelTreasures.Grant(mutation, grant, _rules.Treasures, _rules.Economy, events);
                    if (grantRejection != CommandRejection.None) return Reject(grantRejection);
                    break;
                case ConfiscateTreasureCommand confiscate:
                    var confiscateRejection = PersonnelTreasures.Confiscate(mutation, confiscate, _rules.Treasures, _rules.Economy, events);
                    if (confiscateRejection != CommandRejection.None) return Reject(confiscateRejection);
                    break;
                case ExileOfficerCommand exile:
                    var exileRejection = PersonnelExile.Apply(mutation, exile, _rules.Personnel, _rules.Economy, _rules.Governance, events);
                    if (exileRejection != CommandRejection.None) return Reject(exileRejection);
                    break;
                case ReleasePrisonerCommand release:
                    var releaseRejection = PrisonerRelease.Apply(mutation, release, release.OfficerId, _rules.Personnel, _rules.Economy, events);
                    if (releaseRejection != CommandRejection.None) return Reject(releaseRejection);
                    break;
                case SearchCommand search:
                    var searchRejection = PersonnelMissions.StartSearch(mutation, context, search, _rules.Personnel, events);
                    if (searchRejection != CommandRejection.None) return Reject(searchRejection);
                    foreach (int origin in new[] { search.OfficerId }.Select(id => mutation.Officers[id].SettlementId!.Value).Distinct())
                        GovernanceMaintenance.RepairCommander(mutation, origin, _rules.Governance, events);
                    break;
                case RecruitOfficerCommand recruit:
                    var recruitRejection = PersonnelMissions.StartRecruit(mutation, context, recruit, _rules.Personnel, events);
                    if (recruitRejection != CommandRejection.None) return Reject(recruitRejection);
                    foreach (int origin in new[] { recruit.OfficerId }.Select(id => mutation.Officers[id].SettlementId!.Value).Distinct())
                        GovernanceMaintenance.RepairCommander(mutation, origin, _rules.Governance, events);
                    break;
                case MoveOfficersCommand move:
                    var moveRejection = PersonnelMissions.StartMove(mutation, context, move, _rules.Personnel, events);
                    if (moveRejection != CommandRejection.None) return Reject(moveRejection);
                    foreach (int origin in move.OfficerIds.Select(id => mutation.Officers[id].SettlementId!.Value).Distinct())
                        GovernanceMaintenance.RepairCommander(mutation, origin, _rules.Governance, events);
                    break;
                case DevelopCommand develop:
                    if (develop.Development is null || develop.Development.ForceId != command.IssuerId)
                        return Reject(CommandRejection.NotActiveController);
                    development = DomesticDevelopmentHandler.Apply(mutation, develop.Development, _rules.Economy, events);
                    if (!development.Value.Accepted) return Reject(CommandRejection.DevelopmentFailed, development);
                    events.Add(new DevelopmentCompletedEvent(mutation.Clock, develop.Development,
                        development.Value.Increment, development.Value.Cost));
                    break;
                case StartTaskCommand start:
                    var rule = _rules.FindTask(start.Kind);
                    if (rule is null) return Reject(CommandRejection.UnsupportedCommand);
                    var task = new PersistentTask(start.CommandId, start.Kind, context.ControllerId, start.IssuerId,
                        start.OfficerId, start.TargetId, mutation.Clock, mutation.Clock, 0, mutation.Simulation.NextSequence);
                    int duration = rule.DurationDays(task);
                    if (duration <= 0) return Reject(CommandRejection.InvalidTask);
                    GameClock due = mutation.Clock;
                    for (int day = 0; day < duration; day++) due = due.NextDay();
                    task = task with { Due = due };
                    if (!ResolutionPipeline.TaskReferencesValid(mutation, task) ||
                        mutation.Officers[task.OfficerId].Acted || mutation.Simulation.Tasks.Any(t => t.OfficerId == task.OfficerId) ||
                        !rule.Validate(mutation, task)) return Reject(CommandRejection.InvalidTask);
                    mutation.Replace(mutation.Officers[task.OfficerId] with { Acted = true });
                    mutation.SetSimulation(mutation.Simulation with { Tasks = mutation.Simulation.Tasks.Add(task) });
                    events.Add(new TaskStartedEvent(task));
                    break;
                default:
                    return Reject(CommandRejection.UnsupportedCommand);
            }
            mutation.SetSimulation(mutation.Simulation with
            {
                NextSequence = checked(mutation.Simulation.NextSequence + 1),
            });
            WorldInvariants.Validate(mutation);
            if (!commit) return new(CommandSubmissionResult.Success, [], Snapshot, development);
            _world.Commit(mutation);
            events.Add(new CommandResolvedEvent(command.CommandId, command.IssuerId, command.GetType().Name)
                { Sequence = command.Sequence });
            return new(CommandSubmissionResult.Success, events.AsReadOnly(), Snapshot, development);
        }
        finally { _world.ExitExecution(); }
    }

    public System.Collections.Immutable.ImmutableDictionary<int, OfficerAbilities> GetAbilities(WorldSnapshot snapshot) =>
        _rules.Abilities.EvaluateMany(snapshot.Officers, snapshot.Treasures);

    private CommandRejection ValidateEnvelope(CommandContext context, GameCommand command)
    {
        if (command.IssuerId < 0) return CommandRejection.NegativeIssuerId;
        if (command.Sequence < 0) return CommandRejection.NegativeSequence;
        if (command.CommandId == Guid.Empty) return CommandRejection.InvalidCommandId;
        if (_world.IsExecuting || _world.Simulation.Boundary != SimulationBoundary.Stable) return CommandRejection.NotStable;
        if (_world.Simulation.Tasks.Any(t => t.Id == command.CommandId)) return CommandRejection.DuplicateCommandId;
        if (command.Sequence != _world.Simulation.NextSequence) return CommandRejection.WrongSequence;
        var sim = _world.Simulation;
        if (sim.ActiveControl >= sim.Controls.Length) return CommandRejection.NotActiveController;
        var seat = sim.Controls[sim.ActiveControl];
        return CorpsControl.Authorize(Snapshot, seat, context, command);
    }

    private CommandExecutionResult Reject(CommandRejection rejection, DevelopmentOutcome? development = null) =>
        new(CommandSubmissionResult.Reject(rejection), [], Snapshot, development);

    public AdvanceXunResult EndControl(CommandContext context)
    {
        if (_world.IsExecuting || _world.Simulation.Boundary != SimulationBoundary.Stable ||
            _world.Simulation.ActiveControl >= _world.Simulation.Controls.Length)
            throw new InvalidOperationException("No active stable control.");
        var seat = _world.Simulation.Controls[_world.Simulation.ActiveControl];
        if (seat.ControllerId != context.ControllerId) throw new InvalidOperationException("Not the active controller.");
        var mutation = _world.BeginMutation(_rules.Abilities);
        mutation.SetSimulation(mutation.Simulation with { ActiveControl = mutation.Simulation.ActiveControl + 1 });
        _world.Commit(mutation);
        return new(Array.AsReadOnly<DomainEvent>([new ControlEndedEvent(seat.ControllerId, seat.ForceId)]), Snapshot);
    }

    public AdvanceXunResult AdvanceXun()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (_world.Simulation.Boundary == SimulationBoundary.Terminal) return new([], Snapshot);
        if (_world.Simulation.Boundary != SimulationBoundary.Stable ||
            _world.Simulation.ActiveControl != _world.Simulation.Controls.Length)
            throw new InvalidOperationException("All controllers must finish before advancing.");
        _world.EnterExecution();
        try
        {
            GameClock previous = _world.Clock;
            GameClock next = previous.Advance();
            var mutation = _world.BeginMutation(_rules.Abilities);
            List<DomainEvent> events = [];
            MilitaryDiplomacy.BeginAdvance(mutation, _rules, events);
            while (mutation.Clock != next && mutation.Simulation.Boundary != SimulationBoundary.Terminal)
                _pipeline.AdvanceDay(mutation, events);
            if (mutation.Simulation.Boundary != SimulationBoundary.Terminal)
                _pipeline.Prepare(mutation, events);
            _world.Commit(mutation);
            if (mutation.Clock == next) events.Add(new XunAdvancedEvent(previous, mutation.Clock));
            return new(events.AsReadOnly(), Snapshot);
        }
        finally { _world.ExitExecution(); }
    }
}

public sealed record AdvanceXunResult(IReadOnlyList<DomainEvent> Events, WorldSnapshot Snapshot);
