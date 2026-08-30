namespace San9.Core.Simulation;

public sealed class SimulationEngine
{
    private readonly List<GameCommand> _commands = [];
    private readonly HashSet<Guid> _commandIds = [];
    private readonly StableRandom _random;
    private readonly WorldState _world;

    public SimulationEngine(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _random = new StableRandom(world.RandomState);
    }

    public WorldSnapshot Snapshot => _world.CreateSnapshot();

    public CommandSubmissionResult Validate(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

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
                     .OrderBy(static command => command.IssuerId.Value)
                     .ThenBy(static command => command.Sequence)
                     .ThenBy(static command => command.CommandId))
        {
            events.Add(new CommandResolvedEvent(
                command.CommandId,
                command.IssuerId,
                command.GetType().Name));
        }

        ulong resolutionToken = _random.NextUInt64();
        GameClock current = previous.Advance();
        _world.CommitXun(current, _random.State);
        events.Add(new XunAdvancedEvent(previous, current, resolutionToken));

        _commands.Clear();
        _commandIds.Clear();
        return new AdvanceXunResult(events.AsReadOnly(), _world.CreateSnapshot());
    }
}

public sealed record AdvanceXunResult(
    IReadOnlyList<DomainEvent> Events,
    WorldSnapshot Snapshot);
