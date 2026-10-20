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
    ConstructionAborted, TransportAborted, ReturnTargetChanged, EnterCapturedPlace,
    ReturnAfterAction, AttackUnitAfterAction, AttackPlaceAfterAction
}

public sealed record UnitPolicyRespondedEvent(GameClock Clock, BattleObjectContext Unit,
    UnitPolicyResponse Response, int? OtherOfficerId, int? HomeId) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public int? PreviousPlaceId { get; init; }
}
