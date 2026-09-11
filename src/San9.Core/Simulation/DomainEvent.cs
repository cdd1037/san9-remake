namespace San9.Core.Simulation;

public abstract record DomainEvent;

public sealed record DevelopmentCompletedEvent(
    GameClock Clock,
    San9.Core.Domain.DevelopmentCommand Command,
    int Increment,
    int Cost) : DomainEvent;

public sealed record CommandResolvedEvent(
    Guid CommandId,
    int IssuerId,
    string CommandType)
    : DomainEvent;

public sealed record XunAdvancedEvent(
    GameClock Previous,
    GameClock Current)
    : DomainEvent;
