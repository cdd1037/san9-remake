using System.Collections.Immutable;

namespace San9.Core.Simulation;

public enum SimulationBoundary { New, Stable, Terminal }
public enum ControllerKind { Player, Automatic }
public sealed record ControlSeat(string ControllerId, int ForceId, ControllerKind Kind);
public readonly record struct CommandContext(string ControllerId);
public sealed record RandomStreamState(string Name, ulong State);

public sealed record PersistentTask(Guid Id, string Kind, string ControllerId, int ForceId,
    int OfficerId, int TargetId, GameClock Started, GameClock Due, int ProgressDays, long Sequence);

/// <summary>Authoritative scheduling state; immutable and included in every world transaction.</summary>
public sealed record SimulationState(GameClock Started, ulong InitialSeed)
{
    public SimulationBoundary Boundary { get; init; }
    public string? TerminalReason { get; init; }
    public ImmutableArray<ControlSeat> Controls { get; init; } = [];
    public int ActiveControl { get; init; }
    public long NextSequence { get; init; }
    public ImmutableArray<Guid> AcceptedCommandIds { get; init; } = [];
    public ImmutableArray<PersistentTask> Tasks { get; init; } = [];
    public ImmutableArray<RandomStreamState> RandomStreams { get; init; } = [];
}
