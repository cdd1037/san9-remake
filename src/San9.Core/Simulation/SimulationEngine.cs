using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed class SimulationEngine
{
    private readonly WorldState _world;
    private readonly SimulationRules _rules;
    private readonly ResolutionPipeline _pipeline;

    public SimulationEngine(WorldState world) : this(world, new SimulationRules()) { }

    internal SimulationEngine(WorldState world, SimulationRules rules)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _rules = rules;
        _pipeline = new(rules);
        foreach (var task in world.Simulation.Tasks)
            if (rules.FindTask(task.Kind) is not TaskRule rule || rule.DurationDays != task.Due.DayNumber - task.Started.DayNumber)
                throw new ArgumentException($"Unsupported or invalid task '{task.Kind}'.");
        if (world.Simulation.Boundary == SimulationBoundary.New)
        {
            _world.EnterExecution();
            try
            {
                var mutation = world.BeginMutation();
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
            var mutation = _world.BeginMutation();
            List<DomainEvent> events = [];
            DevelopmentOutcome? development = null;
            switch (command)
            {
                case WaitCommand:
                    break;
                case DevelopCommand develop:
                    if (develop.Development is null || develop.Development.ForceId != command.IssuerId)
                        return Reject(CommandRejection.NotActiveController);
                    development = DomesticDevelopmentHandler.Apply(mutation, develop.Development);
                    if (!development.Value.Accepted) return Reject(CommandRejection.DevelopmentFailed, development);
                    events.Add(new DevelopmentCompletedEvent(mutation.Clock, develop.Development,
                        development.Value.Increment, development.Value.Cost));
                    break;
                case StartTaskCommand start:
                    var rule = _rules.FindTask(start.Kind);
                    if (rule is null) return Reject(CommandRejection.UnsupportedCommand);
                    if (rule.DurationDays <= 0) return Reject(CommandRejection.InvalidTask);
                    GameClock due = mutation.Clock;
                    for (int day = 0; day < rule.DurationDays; day++) due = due.NextDay();
                    var task = new PersistentTask(start.CommandId, start.Kind, context.ControllerId, start.IssuerId,
                        start.OfficerId, start.TargetId, mutation.Clock, due, 0, mutation.Simulation.NextSequence);
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
        if (seat.ControllerId != context.ControllerId || seat.ForceId != command.IssuerId)
            return CommandRejection.NotActiveController;
        return CommandRejection.None;
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
        var mutation = _world.BeginMutation();
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
            var mutation = _world.BeginMutation();
            List<DomainEvent> events = [];
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
