using San9.Core.Domain;

namespace San9.Core.Simulation;

public abstract record DomainEvent;

public sealed record CommandResolvedEvent(
    Guid CommandId,
    ForceId IssuerId,
    string CommandType)
    : DomainEvent;

public sealed record XunAdvancedEvent(
    GameClock Previous,
    GameClock Current,
    ulong ResolutionToken)
    : DomainEvent;
