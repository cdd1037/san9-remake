using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed class SimulationEngine
{
    private readonly List<GameCommand> _commands = [];
    private readonly HashSet<Guid> _commandIds = [];
    private readonly WorldState _world;

    public SimulationEngine(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    public WorldSnapshot Snapshot => _world.CreateSnapshot();

    public DevelopmentExecutionResult Develop(DevelopmentCommand command)
    {
        var mutation = _world.BeginMutation();
        DevelopmentOutcome outcome = DomesticDevelopmentHandler.Apply(mutation, command);
        if (!outcome.Accepted) return new(outcome, [], Snapshot);

        _world.Commit(mutation);
        return new(outcome, Array.AsReadOnly<DomainEvent>(
            [new DevelopmentCompletedEvent(mutation.Clock, command, outcome.Increment, outcome.Cost)]), Snapshot);
    }

    public CommandSubmissionResult Validate(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.IssuerId < 0)
        {
            return CommandSubmissionResult.Reject(CommandRejection.NegativeIssuerId);
        }

        if (command.Sequence < 0)
        {
            return CommandSubmissionResult.Reject(CommandRejection.NegativeSequence);
        }

        if (_commandIds.Contains(command.CommandId))
        {
            return CommandSubmissionResult.Reject(CommandRejection.DuplicateCommandId);
        }

        return CommandSubmissionResult.Success;
    }

    public CommandSubmissionResult Submit(GameCommand command)
    {
        CommandSubmissionResult validation = Validate(command);
        if (!validation.Accepted)
        {
            return validation;
        }

        _commandIds.Add(command.CommandId);
        _commands.Add(command);
        return CommandSubmissionResult.Success;
    }

    public AdvanceXunResult AdvanceXun()
    {
        GameClock previous = _world.Clock;
        List<DomainEvent> events = [];

        foreach (GameCommand command in _commands
                     .OrderBy(static command => command.IssuerId)
                     .ThenBy(static command => command.Sequence)
                     .ThenBy(static command => command.CommandId))
        {
            events.Add(new CommandResolvedEvent(
                command.CommandId,
                command.IssuerId,
                command.GetType().Name));
        }

        GameClock current = previous.Advance();
        var mutation = _world.BeginMutation();
        mutation.SetSimulationState(current, mutation.RandomState);
        XunRecovery.Apply(mutation);
        _world.Commit(mutation);
        events.Add(new XunAdvancedEvent(previous, current));

        _commands.Clear();
        _commandIds.Clear();
        return new AdvanceXunResult(events.AsReadOnly(), _world.CreateSnapshot());
    }
}

public sealed record AdvanceXunResult(
    IReadOnlyList<DomainEvent> Events,
    WorldSnapshot Snapshot);

public sealed record DevelopmentExecutionResult(
    DevelopmentOutcome Outcome,
    IReadOnlyList<DomainEvent> Events,
    WorldSnapshot Snapshot);
