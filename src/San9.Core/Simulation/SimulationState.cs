using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Simulation;

public enum SimulationBoundary { New, Stable, Terminal, AwaitingDecision }
public enum ControllerKind { Player, Automatic }
public sealed record ControlSeat(string ControllerId, int ForceId, ControllerKind Kind);
public readonly record struct CommandContext(string ControllerId);
public sealed record RandomStreamState(string Name, ulong State);

public sealed record DailyTaskDecision(
    [property: JsonRequired] GameClock XunStarted,
    [property: JsonRequired] int RespondingForceId,
    [property: JsonRequired] ImmutableArray<Guid> RemainingTaskIds);

public sealed record PersistentTask(Guid Id, string Kind, string ControllerId, int ForceId,
    int OfficerId, int OriginSettlementId, GameClock Started, GameClock Due, int ProgressDays, long Sequence)
{
    [JsonRequired] public PersonnelJourney? Journey { get; init; }
    [JsonRequired] public ReinforcementTroops? Reinforcements { get; init; }
    [JsonRequired] public GiftMission? Gift { get; init; }
}

public sealed record ReinforcementTroops(uint Soldiers, int Morale, int DemobilizationCityId);

public sealed record PersonnelJourney(int DestinationPlaceId, int? TargetOfficerId, int? SuccessRoll)
{
    [JsonRequired] public San9.Core.Domain.WorldPosition? DestinationPosition { get; init; }
    [JsonRequired] public UnitStrategyMission? UnitStrategy { get; init; }
    [JsonRequired] public BurnMission? Burn { get; init; }
    [JsonRequired] public SeizeMission? Seize { get; init; }
    [JsonRequired] public GossipMission? Gossip { get; init; }
    public PersonnelReturnLeg? ReturnLeg { get; init; }
    public bool? AcceptRecruitment { get; init; }
}
public sealed record PersonnelReturnLeg(int OriginPlaceId, int CountdownDays, bool AfterDailyCommands)
{
    [JsonRequired] public San9.Core.Domain.WorldPosition? OriginPosition { get; init; }
    [JsonRequired] public int? CorpsId { get; init; }
}

/// <summary>Authoritative scheduling state; immutable and included in every world transaction.</summary>
public sealed record SimulationState(GameClock Started, ulong InitialSeed)
{
    [JsonRequired] public San9.Core.Domain.AllianceState? Alliance { get; init; }
    [JsonRequired] public AllianceProposal? PendingAlliance { get; init; }
    [JsonRequired] public DailyTaskDecision? PendingDailyTask { get; init; }
    [JsonRequired] public ImmutableArray<San9.Core.Domain.AttackRequestState> AttackRequests { get; init; } = [];
    [JsonRequired] public int ComputerDamageBonusPercent { get; init; }
    [JsonRequired] public ulong RunSeed { get; init; } = InitialSeed;
    public SimulationBoundary Boundary { get; init; }
    public string? TerminalReason { get; init; }
    public ImmutableArray<ControlSeat> Controls { get; init; } = [];
    public int ActiveControl { get; init; }
    public long NextSequence { get; init; }
    [JsonRequired] public long NextUnitId { get; init; }
    public ImmutableArray<PersistentTask> Tasks { get; init; } = [];
    public ImmutableArray<RandomStreamState> RandomStreams { get; init; } = [];
}
