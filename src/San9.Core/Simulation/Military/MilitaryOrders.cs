using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Maintains persistent military missions and their continuation after target changes.</summary>
internal static class MilitaryOrders
{
    internal static void ResolveOrders(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.OrderedUnits.Select(unit => unit.Id).ToArray())
            ResolveOrder(world, world.Units[id], rules, events);
    }

    internal static void ResolveOrder(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events,
        MapObjectReference? changedTarget = null)
    {
        int id = unit.Id;
        if (unit.FormationId is null || unit.IsEmbedded || unit.IsPersonnel) return;
        if (unit.ServicePlaceId is not null) unit = UnitServicePlaces.Repair(world, unit, rules);
        if (changedTarget is { IsSettlement: true } && world.Forces[unit.OwnerId].IsBarbarian)
        {
            Return(world, unit, rules, events, "barbarian-target-changed");
            return;
        }
        // Target changes update the saved mission while retaining the user's chosen condition lifecycle.
        if (unit.HasForcedReturn && changedTarget is null) return;
        var formation = rules.Formation(unit);
        bool lostChangedTarget = changedTarget is { } target && !RetainsChangedTarget(world, unit.OwnerId, target, rules);
        if (unit.ContactTarget is { } contact && (!MilitaryContacts.Valid(world, unit, contact, rules) ||
            contact == changedTarget && lostChangedTarget))
        {
            world.Replace(unit with { ContactTarget = null });
            unit = world.Units[id];
        }
        if (unit.EngagementTargetId is int engaged && (!MilitaryCombat.ValidTarget(world, unit.OwnerId, unit.CommandOrder!, formation, rules) ||
            changedTarget == new MapObjectReference(engaged, false) && lostChangedTarget))
        {
            MilitaryPolicies.Engage(world, unit, null, events, "engagement-target-invalid");
            unit = world.Units[id];
        }
        if (unit.Order is { } order && (!ValidMission(world, unit, order, rules) ||
            order.IsAttack && Matches(order, changedTarget) && lostChangedTarget))
        {
            if (!order.IsAttack)
            {
                ReturnAfterTask(world, unit, rules, events, "target-invalid");
                return;
            }
            if (order.SettlementId is int place && ValidMission(world, unit,
                    new(MilitaryOrderKind.Return, null, place), rules))
            {
                ChangeOrder(world, unit, new(MilitaryOrderKind.Return, null, place), events, "target-captured-entry");
                return;
            }
            unit = unit with { Order = null, AutonomousReturn = false, AwaitingPostAction = true, Waypoint = null, Lookahead = null,
                MovementRemainder = 0, DepartureDaysRemaining = 0 };
            world.Replace(unit);
        }
        MilitaryPolicies.ResolvePostAction(world, unit, rules, events, changedTarget);
    }

    // A persistent mission may require the other member of the land/naval pair later in its route.
    internal static bool ValidMission(IWorldQuery world, UnitState unit, MilitaryOrder order, MilitaryRulebook rules) =>
        MilitaryMovement.ValidOrder(world, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules,
            unit.NavalFormationId, unit.Soldiers, unit.SiegeEquipmentId);

    private static bool Matches(MilitaryOrder? order, MapObjectReference? target) => target is { } reference &&
        (reference.IsSettlement ? order?.SettlementId == reference.Id : order?.UnitId == reference.Id);

    // A changed target is reconsidered as an automatic continuation, independently of explicit strong-attack legality.
    private static bool RetainsChangedTarget(WorldMutation world, int owner, MapObjectReference target, MilitaryRulebook rules) =>
        target.IsSettlement ? world.Settlements.TryGetValue(target.Id, out var place) &&
            (MilitaryDiplomacy.Work(world, target.Id, rules) || MilitaryDiplomacy.AutomaticEnemy(world, owner, place.OwnerId)) :
            world.Units.TryGetValue(target.Id, out var unit) && MilitaryDiplomacy.AutomaticEnemy(world, owner, unit.OwnerId);

    internal static bool HasReturnObject(WorldMutation world, UnitState unit) => unit.ServicePlaceId is int home &&
        world.Settlements.TryGetValue(home, out var place) && !place.IsDestroyed;

    internal static bool Return(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events, string reason)
    {
        if (unit.Order is null && !unit.AwaitingPostAction) return false;
        if (unit.ServicePlaceId is not null) unit = UnitServicePlaces.Repair(world, unit, rules);
        if (!HasReturnObject(world, unit)) return false;
        var order = new MilitaryOrder(MilitaryOrderKind.Return, null, unit.ServicePlaceId);
        FacilityConstruction.Cancel(world, unit.Id, events);
        ChangeOrder(world, world.Units[unit.Id], order, events, reason, autonomousReturn: true);
        return true;
    }

    internal static bool ReturnAfterTask(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events, string reason)
    {
        unit = UnitServicePlaces.Repair(world, unit, rules);
        var order = new MilitaryOrder(MilitaryOrderKind.Return, null, unit.ServicePlaceId);
        if (!HasReturnObject(world, unit)) order = order with { SettlementId = null };
        FacilityConstruction.Cancel(world, unit.Id, events);
        ChangeOrder(world, world.Units[unit.Id], order, events, reason, autonomousReturn: true);
        return order.SettlementId is not null;
    }

    internal static void ChangeOrder(WorldMutation world, UnitState unit, MilitaryOrder? order, List<DomainEvent> events, string reason,
        bool autonomousReturn = false)
    {
        var next = unit with { Order = order, AutonomousReturn = autonomousReturn && order?.Kind == MilitaryOrderKind.Return,
            PursuitResponsePending = autonomousReturn && order is { Kind: MilitaryOrderKind.Return, SettlementId: not null },
            EngagementTargetId = null, ContactTarget = null, AwaitingPostAction = false,
            Waypoint = null, Lookahead = null, MovementRemainder = 0, DepartureDaysRemaining = 0 };
        if (next == unit) return;
        world.Replace(next);
        if (unit.Order != order || unit.AutonomousReturn != next.AutonomousReturn)
            UnitOrderChanges.Report(world, unit.Id, order, reason, events, previousOrder: unit.Order);
        if (unit.EngagementTargetId is not null) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, reason));
    }

    internal static void RemoveTarget(WorldMutation world, int targetId, bool settlement, List<DomainEvent> events)
    {
        foreach (var unit in world.OrderedUnits.ToArray())
        {
            bool main = settlement ? unit.Order?.SettlementId == targetId : unit.Order?.UnitId == targetId;
            bool temporary = !settlement && unit.EngagementTargetId == targetId;
            bool contact = unit.ContactTarget == new MapObjectReference(targetId, settlement);
            if (!main && !temporary && !contact) continue;
            bool endedAttack = main && unit.Order!.IsAttack;
            var order = !main ? unit.Order : endedAttack ? null : new MilitaryOrder(MilitaryOrderKind.Return, null, null);
            world.Replace(unit with { Order = order, AutonomousReturn = main ? order?.Kind == MilitaryOrderKind.Return : unit.AutonomousReturn,
                PursuitResponsePending = main ? false : unit.PursuitResponsePending,
                AwaitingPostAction = unit.AwaitingPostAction || endedAttack,
                EngagementTargetId = temporary ? null : unit.EngagementTargetId,
                ContactTarget = contact ? null : unit.ContactTarget,
                DepartureDaysRemaining = main ? 0 : unit.DepartureDaysRemaining,
                Waypoint = main ? null : unit.Waypoint, Lookahead = null, MovementRemainder = 0 });
            if (main) UnitOrderChanges.Report(world, unit.Id, order, "target-removed", events, previousOrder: unit.Order);
            if (temporary) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, "target-removed"));
        }
    }

    internal static void SettlementChanged(WorldMutation world, int id, MilitaryRulebook rules, List<DomainEvent> events) =>
        TargetChanged(world, new(id, true), rules, events);

    internal static void UnitChanged(WorldMutation world, int id, MilitaryRulebook rules, List<DomainEvent> events) =>
        TargetChanged(world, new(id, false), rules, events);

    private static void TargetChanged(WorldMutation world, MapObjectReference target, MilitaryRulebook rules, List<DomainEvent> events)
    {
        foreach (var unit in world.OrderedUnits.Where(u => Matches(u.Order, target) || u.ContactTarget == target ||
                (target.IsSettlement ? u.Order is { Kind: MilitaryOrderKind.Return, SettlementId: null }
                    : u.EngagementTargetId == target.Id)).ToArray())
            ResolveOrder(world, unit, rules, events, target);
    }
}
