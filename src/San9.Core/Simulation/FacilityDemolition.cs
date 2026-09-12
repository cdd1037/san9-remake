using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DemolishFacilityCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record FacilityDemolishedEvent(GameClock Clock, int SettlementId, int DestinationCityId) : DomainEvent;

internal static class FacilityDemolition
{
    internal static CommandRejection Apply(WorldMutation world, DemolishFacilityCommand command, SimulationRules rules,
        List<DomainEvent> events)
    {
        if (rules.Military is not { } military || !world.Settlements.TryGetValue(command.SettlementId, out var facility) ||
            facility.Kind != SettlementKind.Facility || facility.OwnerId != command.IssuerId || facility.IsDestroyed ||
            !military.TryPlace(facility, out var definition) || !definition.CanDemolish)
            return CommandRejection.InvalidTask;
        if (!rules.Personnel.RegionCities.TryGetValue(facility.Id, out int cityId) ||
            !world.Settlements.TryGetValue(cityId, out var city) || city.Kind != SettlementKind.City ||
            city.IsDestroyed || city.OwnerId != command.IssuerId || city.Durability == 0 || !city.CanHostPersons)
            return CommandRejection.NoDestination;
        if (facility.Facility?.BuilderUnitId is int builder)
        {
            FacilityConstruction.Cancel(world, builder, events);
            world.Replace(world.Units[builder] with { Order = null });
            events.Add(new UnitOrderChangedEvent(world.Clock, builder, null, "facility-demolished"));
            facility = world.Settlements[facility.Id];
        }

        uint soldiers = Math.Min(facility.Soldiers, city.SoldierCapacity - city.Soldiers);
        uint wounded = Math.Min(facility.Wounded, MilitaryRulebook.WoundedCapacity - city.Wounded);
        int morale = city.Soldiers + (ulong)soldiers == 0 ? city.Morale :
            (int)(((ulong)city.Soldiers * (uint)city.Morale + (ulong)soldiers * (uint)facility.Morale) / (city.Soldiers + (ulong)soldiers));
        world.Replace(city with { Soldiers = city.Soldiers + soldiers, Wounded = city.Wounded + wounded, Morale = morale });
        world.Replace(facility with { IsDestroyed = true, OwnerId = null, CorpsId = null, Soldiers = 0, Wounded = 0,
            Durability = 0, Morale = 0, ConfusionDaysRemaining = 0 });
        foreach (var entry in new[] { (ResourceKind.Soldiers, facility.Soldiers, soldiers), (ResourceKind.Wounded, facility.Wounded, wounded) })
        {
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, facility.Id), entry.Item1,
                "facility-demolished", cityId, -(long)entry.Item2, -(long)entry.Item2, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, cityId), entry.Item1,
                "facility-demolished", facility.Id, entry.Item2, entry.Item3, events);
        }

        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        foreach (var person in world.Officers.Values.Where(p => p.SettlementId == facility.Id && !traveling.Contains(p.Id)).OrderBy(p => p.Id).ToArray())
            world.Replace(person with { SettlementId = cityId, IsCommander = false });
        foreach (var task in world.Simulation.Tasks.Where(t => t.OriginSettlementId == facility.Id || t.Journey?.DestinationPlaceId == facility.Id)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
            events.Add(new TaskCancelledEvent(task.Id, world.Clock, "facility-demolished"));
            if (task.Journey is not null) PersonnelReturns.Start(world, task, rules.Personnel, events, false);
        }
        foreach (var unit in world.Units.Values.Where(u => u.Order?.SettlementId == facility.Id).OrderBy(u => u.Id).ToArray())
        {
            world.Replace(unit with { Order = null });
            events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, null, "facility-demolished"));
        }
        BattleAftermath.Reconcile(world, new HashSet<int> { command.IssuerId }, rules, events);
        events.Add(new FacilityDemolishedEvent(world.Clock, facility.Id, cityId));
        return CommandRejection.None;
    }
}
