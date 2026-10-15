using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public enum AllocationOutcome { Pending, Completed, Cancelled, Rejected, InputChanged }
public sealed record AllocationTrace(Guid CommandId, string ControllerId, string Purpose, GameClock Started,
    int? UnitId, int? TargetId, int? PredictedDays, uint? PredictedSoldiers, long PredictedAdditionalFood,
    AllocationOutcome Outcome, long WorkRemaining, ImmutableArray<EconomicAccountObservation> Accounts)
{
    public int? ActualDays { get; init; }
    public int? ArrivalError => ActualDays is int actual && PredictedDays is int predicted ? actual - predicted : null;
    public int? AccountId { get; init; }
    public long? PredictedAccountFoodExpenditure { get; init; }
    public long ActualAccountFoodExpenditure { get; init; }
    public long? FoodExpenditureError => PredictedAccountFoodExpenditure is long expected ? ActualAccountFoodExpenditure - expected : null;
    public uint? ActualDeliveredSoldiers { get; init; }
    public long? SoldierLoss => PredictedSoldiers is uint expected && ActualDeliveredSoldiers is uint delivered ? (long)expected - delivered : null;
    public string CommandType { get; init; } = "";
    public int ForceId { get; init; }
    public CommandRejection Rejection { get; init; }
}
public sealed record AllocationBatchTrace(GameClock Clock, int ForceId, long Work, bool SearchComplete,
    ImmutableArray<EconomicAccountObservation> Accounts, ImmutableArray<ArrivalProjection> Arrivals)
{
    public ImmutableArray<AllocationCandidate> Candidates { get; init; } = [];
    public bool IsTurnSummary { get; init; }
    public bool BudgetExhausted { get; init; }
    public ImmutableDictionary<string, long> WorkByKind { get; init; } = ImmutableDictionary<string, long>.Empty;
    public ImmutableDictionary<string, long> AllocatedBytesByKind { get; init; } = ImmutableDictionary<string, long>.Empty;
    public ImmutableDictionary<string, long> ElapsedTicksByKind { get; init; } = ImmutableDictionary<string, long>.Empty;
    public ImmutableDictionary<string, long> PathQueriesByKind { get; init; } = ImmutableDictionary<string, long>.Empty;
}

public sealed record AllocationWorkSummary(long Turns, long Work, long IncompleteTurns, long ExhaustedTurns,
    long AcceptedCommands, long RejectedCommands);

/// <summary>Bounded development evidence, excluded from session state, saves, hashes and decision inputs.</summary>
public sealed class AllocationDiagnostics
{
    public const int Capacity = 128;
    public ImmutableArray<AllocationTrace> Actions { get; private set; } = [];
    public ImmutableArray<AllocationBatchTrace> Batches { get; private set; } = [];
    public ImmutableDictionary<int, AllocationWorkSummary> WorkByForce { get; private set; } =
        ImmutableDictionary<int, AllocationWorkSummary>.Empty;
    public ImmutableDictionary<string, long> AcceptedCommands { get; private set; } =
        ImmutableDictionary<string, long>.Empty;
    public ImmutableDictionary<string, long> RejectedCommands { get; private set; } =
        ImmutableDictionary<string, long>.Empty;
    public ImmutableDictionary<CommandRejection, long> RejectionReasons { get; private set; } =
        ImmutableDictionary<CommandRejection, long>.Empty;

    internal void Publish(IEnumerable<AllocationTrace> actions, IEnumerable<AllocationBatchTrace> batches, IReadOnlyList<DomainEvent> events)
    {
        var actionArray = actions.ToArray();
        var batchArray = batches.ToArray();
        foreach (var turn in batchArray.Where(b => b.IsTurnSummary))
        {
            var previous = WorkByForce.GetValueOrDefault(turn.ForceId) ?? new(0, 0, 0, 0, 0, 0);
            // Summaries are published only after the outer transaction commits. Detailed rows may be evicted.
            WorkByForce = WorkByForce.SetItem(turn.ForceId, previous with
            {
                Turns = previous.Turns + 1, Work = previous.Work + turn.Work,
                IncompleteTurns = previous.IncompleteTurns + (turn.SearchComplete ? 0 : 1),
                ExhaustedTurns = previous.ExhaustedTurns + (turn.BudgetExhausted ? 1 : 0),
                AcceptedCommands = previous.AcceptedCommands + actionArray.Count(a => a.Outcome != AllocationOutcome.Rejected && a.ForceId == turn.ForceId),
                RejectedCommands = previous.RejectedCommands + actionArray.Count(a => a.Outcome == AllocationOutcome.Rejected && a.ForceId == turn.ForceId)
            });
        }
        foreach (var action in actionArray)
        {
            if (action.Outcome == AllocationOutcome.Rejected)
            {
                RejectedCommands = RejectedCommands.SetItem(action.CommandType, RejectedCommands.GetValueOrDefault(action.CommandType) + 1);
                RejectionReasons = RejectionReasons.SetItem(action.Rejection, RejectionReasons.GetValueOrDefault(action.Rejection) + 1);
            }
            else
                AcceptedCommands = AcceptedCommands.SetItem(action.CommandType, AcceptedCommands.GetValueOrDefault(action.CommandType) + 1);
        }
        Actions = Actions.Concat(actionArray).TakeLast(Capacity).ToImmutableArray();
        Batches = Batches.Concat(batchArray).TakeLast(Capacity).ToImmutableArray();
        foreach (var e in events)
        {
            if (e is ResourceChangedEvent { Resource: ResourceKind.Food, Reason: "daily-rations", Account.Kind: ResourceOwnerKind.Corps } ration)
                Actions = Actions.Select(a => a.Outcome == AllocationOutcome.Pending && a.AccountId == ration.Account.Id ? a with
                    { ActualAccountFoodExpenditure = a.ActualAccountFoodExpenditure - ration.Actual } : a).ToImmutableArray();
            if (e is ResourceChangedEvent { Resource: ResourceKind.Soldiers, Reason: "unit-absorbed", Account.Kind: ResourceOwnerKind.Settlement } delivered)
                Actions = Actions.Select(a => a.UnitId == delivered.SourceId && a.Outcome == AllocationOutcome.Pending ? a with
                    { ActualDeliveredSoldiers = checked((uint)delivered.Actual) } : a).ToImmutableArray();
            if (e is UnitAbsorbedEvent arrived)
                Actions = Actions.Select(a => a.UnitId == arrived.UnitId && a.Outcome == AllocationOutcome.Pending ? a with
                    { Outcome = a.TargetId == arrived.SettlementId ? AllocationOutcome.Completed : AllocationOutcome.InputChanged,
                        ActualDays = checked((int)(arrived.Clock.DayNumber - a.Started.DayNumber)) } : a).ToImmutableArray();
            if (e is UnitOrderChangedEvent changed)
                Actions = Actions.Select(a => a.UnitId == changed.UnitId && a.Outcome == AllocationOutcome.Pending &&
                    changed.Order?.SettlementId != a.TargetId ? a with { Outcome = AllocationOutcome.Cancelled } : a).ToImmutableArray();
            if (e is UnitTerminatedEvent terminated)
                Actions = Actions.Select(a => a.UnitId == terminated.UnitId && a.Outcome == AllocationOutcome.Pending
                    ? a with { Outcome = AllocationOutcome.Cancelled } : a).ToImmutableArray();
        }
    }
}
