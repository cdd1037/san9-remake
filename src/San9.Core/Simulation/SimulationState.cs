using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Simulation;

public enum SimulationBoundary { New, Stable, Terminal, AwaitingDecision }
public enum ControllerKind { Player, Automatic }
public sealed record ControlSeat(string ControllerId, int ForceId, ControllerKind Kind);
public readonly record struct CommandContext(string ControllerId);
public sealed record RandomStreamState(string Name, ulong State);
public enum TaskResolutionPhase { DailyCommands, XunBoundary }

public sealed record DailyTaskDecision(
    [property: JsonRequired] GameClock XunStarted,
    [property: JsonRequired] int RespondingForceId,
    [property: JsonRequired] ImmutableArray<Guid> RemainingTaskIds)
{
    [JsonRequired] public TaskResolutionPhase Phase { get; init; }
}

public sealed record PersistentTask(Guid Id, string Kind, string ControllerId, int ForceId,
    int OfficerId, int OriginSettlementId, GameClock Started, GameClock Due, int ProgressDays, long Sequence)
{
    [JsonRequired] public int? StudyTacticId { get; init; }
    [JsonIgnore] public int ResolutionStep { get; init; }
    [JsonIgnore] public SearchEncounter? SearchEncounter { get; init; }
    [JsonRequired] public PersonnelJourney? Journey { get; init; }
    [JsonRequired] public ReinforcementTroops? Reinforcements { get; init; }
    [JsonRequired] public GiftMission? Gift { get; init; }
    [JsonRequired] public PrisonerReturnMission? PrisonerReturn { get; init; }
    [JsonRequired] public AttackRequestMission? AttackRequest { get; init; }
    [JsonRequired] public SurrenderMission? Surrender { get; init; }
}

public sealed record ReinforcementTroops(uint Soldiers, int Morale, int DemobilizationCityId);

public sealed record PersonnelJourney(int DestinationPlaceId, int? TargetOfficerId, int? SuccessRoll)
{
    [JsonRequired] public PersonnelRoute Route { get; init; } = null!;
    [JsonRequired] public UnitStrategyMission? UnitStrategy { get; init; }
    [JsonRequired] public BurnMission? Burn { get; init; }
    [JsonRequired] public SeizeMission? Seize { get; init; }
    [JsonRequired] public GossipMission? Gossip { get; init; }
    public PersonnelReturnLeg? ReturnLeg { get; init; }
}
public sealed record PersonnelRoute(
    [property: JsonRequired] San9.Core.Domain.WorldPosition Origin,
    [property: JsonRequired] San9.Core.Domain.WorldPosition Destination,
    [property: JsonRequired] bool FieldDestination)
{
    [JsonIgnore] public int Days => San9.Core.Domain.PersonnelRulebook.JourneyDays(Origin, Destination);
    [JsonIgnore] public int XunRoundedDays => Math.Max(10, checked((Days + 9) / 10 * 10));
}
public sealed record PersonnelReturnLeg(int OriginPlaceId, int CountdownDays, bool AfterDailyCommands)
{
    [JsonRequired] public bool FieldTravel { get; init; }
    [JsonRequired] public bool DiplomaticEnvoy { get; init; }
    [JsonRequired] public int? CorpsId { get; init; }
}

/// <summary>Authoritative scheduling state; immutable and included in every world transaction.</summary>
public sealed record SimulationState(GameClock Started, ulong InitialSeed)
{
    [JsonRequired] public bool CityIndependenceEnabled { get; init; } = true;
    [JsonRequired] public bool ForceEmergenceEnabled { get; init; } = true;
    [JsonRequired] public bool ForceConsolidationEnabled { get; init; } = true;
    [JsonRequired] public bool ForceConsolidationPending { get; init; }
    [JsonRequired] public bool OfficeRequestsEnabled { get; init; } = true;
    [JsonRequired] public bool FreeOfficerMigrationEnabled { get; init; } = true;
    [JsonRequired] public bool AnnualAppearancesEnabled { get; init; } = true;
    [JsonRequired] public bool NaturalDeathsEnabled { get; init; } = true;
    [JsonRequired] public bool BattleDeathsEnabled { get; init; }
    [JsonRequired] public San9.Core.Domain.AllianceState? Alliance { get; init; }
    [JsonIgnore] public DuelCombatDecision? PendingDuel { get; init; }
    [JsonIgnore] public EventSession? PendingEvent { get; init; }
    [JsonIgnore] public DailyTaskDecision? PendingDailyTask { get; init; }
    [JsonIgnore] public TacticStudyDecision? PendingTacticStudy { get; init; }
    [JsonIgnore] public ExecutionSuccessionDecision? PendingExecutionSuccession { get; init; }
    [JsonRequired] public ImmutableArray<San9.Core.Domain.AttackRequestState> AttackRequests { get; init; } = [];
    /// <summary>Consumed authored-event keys. This is authoritative world state, not a UI/session cache.</summary>
    [JsonRequired] public ImmutableArray<PersistentEventKey> ConsumedEventKeys { get; init; } = [];
    [JsonRequired] public int ComputerDamageBonusPercent { get; init; }
    [JsonRequired] public ulong RunSeed { get; init; } = InitialSeed;
    public SimulationBoundary Boundary { get; init; }
    [JsonIgnore]
    public bool CanPersist => (Boundary is SimulationBoundary.Stable or SimulationBoundary.Terminal) &&
        PendingDuel is null && PendingEvent is null && PendingDailyTask is null && PendingTacticStudy is null &&
        PendingExecutionSuccession is null && Tasks.All(t => t.ResolutionStep == 0 && t.SearchEncounter is null &&
            t.AttackRequest?.Offer is null && t.PrisonerReturn?.Offer is null);
    public string? TerminalReason { get; init; }
    public ImmutableArray<ControlSeat> Controls { get; init; } = [];
    public int ActiveControl { get; init; }
    public long NextSequence { get; init; }
    [JsonRequired] public long NextUnitId { get; init; }
    public ImmutableArray<PersistentTask> Tasks { get; init; } = [];
    public ImmutableArray<RandomStreamState> RandomStreams { get; init; } = [];
}
