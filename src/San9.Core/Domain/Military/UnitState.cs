using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum UnitOrderClass { None, NonMilitary, Military }
public enum UnitCondition { Normal, Engaged, Trap, Disguised, NoFormation, Confused, Routed, HighSpirits }

// Active field forces are separate from settlement garrisons. Movement rules are separate use cases.
public sealed record UnitState(int Id, int OwnerId, [property: JsonRequired] WorldPosition Position, uint Soldiers)
{
    [JsonRequired] public System.Collections.Immutable.ImmutableDictionary<int, BattleRank> MemberRanks { get; init; } = System.Collections.Immutable.ImmutableDictionary<int, BattleRank>.Empty;
    [JsonRequired] public int? FormationId { get; init; }
    [JsonRequired] public int? NavalFormationId { get; init; }
    [JsonRequired] public int Morale { get; init; }
    [JsonRequired] public uint Wounded { get; init; }
    [JsonRequired] public uint CargoMoney { get; init; }
    [JsonRequired] public uint CargoFood { get; init; }
    [JsonRequired] public MilitaryOrder? Order { get; init; }
    [JsonRequired] public bool CommandedThisXun { get; init; }
    // Authored field units may already be in progress; public deployment explicitly starts this as false.
    [JsonRequired] public bool HasEnteredDailyCycle { get; init; } = true;
    [JsonRequired] public int MovementRemainder { get; init; }
    [JsonRequired] public int DepartureDaysRemaining { get; init; }
    [JsonRequired] public WorldPosition? Waypoint { get; init; }
    [JsonRequired] public NavigationLookahead? Lookahead { get; init; }
    [JsonRequired] public UnitPolicies Policies { get; init; } = new();
    [JsonRequired] public int? CommanderId { get; init; }
    [JsonRequired] public int? EngagementTargetId { get; init; }
    [JsonRequired] public MapObjectReference? ContactTarget { get; init; }
    [JsonRequired] public bool ApproachNotificationPending { get; init; } = true;
    [JsonRequired] public bool ActiveEngagement { get; init; }
    [JsonRequired] public bool PassiveEngagement { get; init; }
    [JsonRequired] public bool AwaitingPostAction { get; init; }
    [JsonIgnore] public MilitaryOrder? EffectiveOrder
    {
        get
        {
            if (HasForcedReturn) return ServicePlaceId is int home ? new(MilitaryOrderKind.Return, null, home) : null;
            var command = CommandOrder;
            if (ContactTarget is not { } contact || command is { IsAttack: true } &&
                (contact.IsSettlement ? command.SettlementId == contact.Id : command.UnitId == contact.Id)) return command;
            return contact.IsSettlement ? new(MilitaryOrderKind.AttackSettlement, null, contact.Id)
                : new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = contact.Id };
        }
    }
    [JsonIgnore] public MilitaryOrder? CommandOrder => EngagementTargetId is int target
        ? new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target } : Order;

    [JsonIgnore] public WorldPosition? ActiveWaypoint =>
        EngagementTargetId is null && ContactTarget is null && !HasForcedReturn ? Waypoint : null;

    [JsonIgnore] public bool HasForcedReturn => Condition is UnitCondition.Disguised or UnitCondition.Routed;
    [JsonIgnore] public bool CanReceiveOrders => Condition is not (UnitCondition.Disguised or UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed);

    /// <summary>Projects a newly assigned command without carrying the previous command's navigation or contacts.</summary>
    public UnitState WithMilitaryOrder(MilitaryOrder order, UnitPolicies policies, WorldPosition? waypoint) => this with
    {
        Order = order, Policies = policies, Waypoint = waypoint, DepartureDaysRemaining = 0,
        EngagementTargetId = null, ContactTarget = null, AwaitingPostAction = false, Lookahead = null
    };
    [System.Text.Json.Serialization.JsonIgnore] public MapCell Cell => MapCoordinates.Cell(Position);
    [JsonRequired] public int? CorpsId { get; init; }
    [JsonRequired] public int? ServicePlaceId { get; init; }
    [JsonRequired] public bool IsEmbedded { get; init; }
    [JsonRequired] public bool IsPersonnel { get; init; }
    [JsonRequired] public UnitOrderClass OrderClass { get; init; }
    [JsonRequired] public UnitCondition Condition { get; init; }
    [JsonRequired] public int ConditionDaysRemaining { get; init; }
    [JsonRequired] public bool HighSpiritsMoralePenaltyPending { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public UnitOrderClass EffectiveOrderClass => Condition is UnitCondition.Disguised or UnitCondition.Routed
        ? UnitOrderClass.Military : OrderClass;

    public bool FollowsServicePlace(bool creatingCorps) =>
        ServicePlaceId is not null && (IsPersonnel || EffectiveOrderClass == UnitOrderClass.Military) &&
        (!creatingCorps || !IsEmbedded);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Id >= 0 && OwnerId >= 0 && ServicePlaceId is not < 0 && Position.X >= 0 && Position.Y >= 0 && Soldiers > 0 &&
        Enum.IsDefined(OrderClass) && Enum.IsDefined(Condition) && ConditionDaysRemaining >= 0 && FormationId is not < 0 && NavalFormationId is not < 0 && (NavalFormationId is null || FormationId is not null) && MovementRemainder >= 0 && Morale is >= 0 and <= 100 && Wounded <= MilitaryRulebook.WoundedCapacity &&
        (!HighSpiritsMoralePenaltyPending || Condition == UnitCondition.HighSpirits) &&
        DepartureDaysRemaining is >= 0 and <= 9 &&
        (Waypoint is null || Waypoint.Value.X >= 0 && Waypoint.Value.Y >= 0 && Order is not null) &&
        (Lookahead is null || EffectiveOrder is not null && Lookahead.Position.X >= 0 && Lookahead.Position.Y >= 0) &&
        (ContactTarget is null || ContactTarget.Id >= 0 && (ContactTarget.IsSettlement || ContactTarget.Id != Id) && FormationId is not null) &&
        (DepartureDaysRemaining == 0 || FormationId is not null && Order is not null) &&
        Policies is not null && CommanderId is not < 0 && EngagementTargetId is not < 0 && EngagementTargetId != Id &&
        (!AwaitingPostAction || Order is null) &&
        (FormationId is not null || EngagementTargetId is null && !AwaitingPostAction && CommanderId is null) &&
        MemberRanks is not null && MemberRanks.All(p => p.Key >= 0 && Enum.IsDefined(p.Value)) &&
        (Order is null || FormationId is not null && Order.IsValid) &&
        (MovementRemainder == 0 || FormationId is not null && EffectiveOrder is not null && Condition != UnitCondition.Confused);
}
