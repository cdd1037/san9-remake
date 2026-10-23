using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Maintains persistent military missions and their continuation after target changes.</summary>
internal static class MilitaryOrders
{
    internal static void ResolveOrders(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
            ResolveOrder(world, world.Units[id], rules, events);
    }

    internal static void ResolveOrder(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events)
    {
        int id = unit.Id;
        if (unit.FormationId is null || unit.IsEmbedded || unit.IsPersonnel) return;
        if (unit.ServicePlaceId is not null) unit = UnitServicePlaces.Repair(world, unit, rules);
        if (unit.HasForcedReturn) return;
        var formation = rules.Formation(unit);
        if (unit.ContactTarget is { } contact && !MilitaryContacts.Valid(world, unit, contact, rules))
        {
            world.Replace(unit with { ContactTarget = null });
            unit = world.Units[id];
        }
        if (unit.EngagementTargetId is not null && !MilitaryCombat.ValidTarget(world, unit.OwnerId, unit.CommandOrder!, formation, rules))
        {
            MilitaryPolicies.Engage(world, unit, null, events, "engagement-target-invalid");
            unit = world.Units[id];
        }
        if (unit.Order is { } order && !ValidMission(world, unit, order, rules))
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
            unit = unit with { Order = null, AwaitingPostAction = true, Waypoint = null, Lookahead = null,
                MovementRemainder = 0, DepartureDaysRemaining = 0 };
            world.Replace(unit);
        }
        MilitaryPolicies.ResolvePostAction(world, unit, rules, events);
    }

    // A persistent mission may require the other member of the land/naval pair later in its route.
    internal static bool ValidMission(IWorldQuery world, UnitState unit, MilitaryOrder order, MilitaryRulebook rules) =>
        MilitaryMovement.ValidOrder(world, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules,
            unit.NavalFormationId, unit.Soldiers);

    internal static bool Return(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events, string reason)
    {
        if (unit.Order is null && !unit.AwaitingPostAction) return false;
        if (unit.ServicePlaceId is not null) unit = UnitServicePlaces.Repair(world, unit, rules);
        var order = unit.ServicePlaceId is int home ? new MilitaryOrder(MilitaryOrderKind.Return, null, home) : null;
        if (!MilitaryMovement.ValidOrder(world, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId)) return false;
        FacilityConstruction.Cancel(world, unit.Id, events);
        ChangeOrder(world, world.Units[unit.Id], order, events, reason);
        return true;
    }

    internal static void ReturnAfterTask(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events, string reason)
    {
        unit = UnitServicePlaces.Repair(world, unit, rules);
        var order = new MilitaryOrder(MilitaryOrderKind.Return, null, unit.ServicePlaceId);
        if (!ValidMission(world, unit, order, rules)) order = order with { SettlementId = null };
        FacilityConstruction.Cancel(world, unit.Id, events);
        ChangeOrder(world, world.Units[unit.Id], order, events, reason);
    }

    internal static void ChangeOrder(WorldMutation world, UnitState unit, MilitaryOrder? order, List<DomainEvent> events, string reason)
    {
        var next = unit with { Order = order, EngagementTargetId = null, ContactTarget = null, AwaitingPostAction = false,
            Waypoint = null, Lookahead = null, MovementRemainder = 0, DepartureDaysRemaining = 0 };
        if (next == unit) return;
        world.Replace(next);
        if (unit.Order != order) UnitOrderChanges.Report(world, unit.Id, order, reason, events, previousOrder: unit.Order);
        if (unit.EngagementTargetId is not null) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, reason));
    }

    internal static void RemoveTarget(WorldMutation world, int targetId, bool settlement, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
        {
            bool main = settlement ? unit.Order?.SettlementId == targetId : unit.Order?.UnitId == targetId;
            bool temporary = !settlement && unit.EngagementTargetId == targetId;
            bool contact = unit.ContactTarget == new MapObjectReference(targetId, settlement);
            if (!main && !temporary && !contact) continue;
            bool endedAttack = main && unit.Order!.IsAttack;
            var order = !main ? unit.Order : endedAttack ? null : new MilitaryOrder(MilitaryOrderKind.Return, null, null);
            world.Replace(unit with { Order = order,
                AwaitingPostAction = unit.AwaitingPostAction || endedAttack,
                EngagementTargetId = temporary ? null : unit.EngagementTargetId,
                ContactTarget = contact ? null : unit.ContactTarget,
                DepartureDaysRemaining = main ? 0 : unit.DepartureDaysRemaining,
                Waypoint = main ? null : unit.Waypoint, Lookahead = null, MovementRemainder = 0 });
            if (main) UnitOrderChanges.Report(world, unit.Id, order, "target-removed", events, previousOrder: unit.Order);
            if (temporary) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, "target-removed"));
        }
    }

    internal static void SettlementChanged(WorldMutation world, int id, MilitaryRulebook rules, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.Where(u => u.Order?.SettlementId == id ||
                u.Order is { Kind: MilitaryOrderKind.Return, SettlementId: null }).OrderBy(u => u.Id).ToArray())
            ResolveOrder(world, unit, rules, events);
    }
}
