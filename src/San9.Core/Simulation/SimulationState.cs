using System.Collections.Immutable;

namespace San9.Core.Simulation;

public enum SimulationBoundary { New, Stable, Terminal }
public enum ControllerKind { Player, Automatic }
public sealed record ControlSeat(string ControllerId, int ForceId, ControllerKind Kind);
public readonly record struct CommandContext(string ControllerId);
public sealed record RandomStreamState(string Name, ulong State);

public sealed record PersistentTask(Guid Id, string Kind, string ControllerId, int ForceId,
    int OfficerId, int OriginSettlementId, GameClock Started, GameClock Due, int ProgressDays, long Sequence)
{
    [System.Text.Json.Serialization.JsonRequired] public PersonnelJourney? Journey { get; init; }
}

public sealed record PersonnelJourney(int DestinationPlaceId, int? TargetOfficerId, int? SuccessRoll)
{
    [System.Text.Json.Serialization.JsonRequired] public San9.Core.Domain.WorldPosition? DestinationPosition { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public UnitStrategyMission? UnitStrategy { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public BurnMission? Burn { get; init; }
    public PersonnelReturnLeg? ReturnLeg { get; init; }
    public bool? AcceptRecruitment { get; init; }
}
public sealed record PersonnelReturnLeg(int OriginPlaceId, int CountdownDays, bool AfterDailyCommands)
{
    [System.Text.Json.Serialization.JsonRequired] public San9.Core.Domain.WorldPosition? OriginPosition { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? CorpsId { get; init; }
}

/// <summary>Authoritative scheduling state; immutable and included in every world transaction.</summary>
public sealed record SimulationState(GameClock Started, ulong InitialSeed)
{
    [System.Text.Json.Serialization.JsonRequired] public San9.Core.Domain.AllianceState? Alliance { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int ComputerDamageBonusPercent { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ulong RunSeed { get; init; } = InitialSeed;
    public SimulationBoundary Boundary { get; init; }
    public string? TerminalReason { get; init; }
    public ImmutableArray<ControlSeat> Controls { get; init; } = [];
    public int ActiveControl { get; init; }
    public long NextSequence { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public long NextUnitId { get; init; }
    public ImmutableArray<PersistentTask> Tasks { get; init; } = [];
    public ImmutableArray<RandomStreamState> RandomStreams { get; init; } = [];
}
