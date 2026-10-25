using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Identity and geography when a person fact occurs, before return, transfer or cleanup.</summary>
public sealed record PersonOccurrence(int? ForceId, int? PlaceId, WorldPosition? Position)
{
    internal static PersonOccurrence Capture(WorldMutation world, OfficerState person) =>
        person.UnitId is int id && world.Units.TryGetValue(id, out var unit)
            ? new(person.OwnerId, unit.ServicePlaceId, unit.Position)
            : new(person.OwnerId, person.SettlementId ?? person.RegionId, null);
}

public sealed record SecretContactReportedEvent(GameClock Clock, int RecipientForceId, int OfficerId,
    int ContactForceId, PersonOccurrence Occurrence) : DomainEvent;

public enum UnitPolicyResponse
{
    AttackOrdered, AttackDespiteOrders, AvoidOrdered, AvoidDespiteOrders, RetreatOrdered, RetreatOutnumbered, RetreatLowMorale,
    RetreatDespiteOrders, PursueOrdered, PursueDespiteOrders, ResumeOrders, ReturnHome,
    PostActionNoTarget, PostActionAttackOrdered, PostActionAvoidDespiteOrders, PostActionReturnOrdered, PostActionAttackDespiteOrders,
    ConstructionAborted, TransportAborted, ReturnTargetChanged, EnterCapturedPlace,
    ReturnAfterAction, AttackUnitAfterAction, AttackPlaceAfterAction
}

public enum UnitPolicyReturnOutcome { NotRequested, Started, NoDestination }

public sealed record UnitPolicyRespondedEvent(GameClock Clock, BattleObjectContext Unit,
    UnitPolicyResponse Response, int? OtherOfficerId, int? HomeId) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public int? PreviousPlaceId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool ReportEligible { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public UnitPolicyReturnOutcome ReturnOutcome { get; init; }

    internal static UnitPolicyRespondedEvent Capture(WorldMutation world, UnitState unit, UnitPolicyResponse response,
        int? otherOfficerId, int? homeId, int? previousPlaceId = null,
        UnitPolicyReturnOutcome returnOutcome = UnitPolicyReturnOutcome.NotRequested)
    {
        var corps = unit.CorpsId is int corpsId ? world.Corps.GetValueOrDefault(corpsId)
            : world.Corps.Values.FirstOrDefault(c => c.OwnerId == unit.OwnerId && c.IsRoot);
        return new(world.Clock, BattleObjectContext.Capture(unit), response, otherOfficerId, homeId)
        {
            PreviousPlaceId = previousPlaceId,
            ReturnOutcome = returnOutcome,
            ReportEligible = corps is not null &&
                corps.OwnerId == unit.OwnerId && corps.ControlMode == CorpsControlMode.Direct &&
                world.Simulation.Controls.Any(c => c.ForceId == unit.OwnerId && c.Kind == ControllerKind.Player)
        };
    }
}
