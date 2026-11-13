using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DemolishFacilityCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record FacilityDemolishedEvent(GameClock Clock, int SettlementId, int DestinationCityId, int ForceId, int? FacilityTypeId) : DomainEvent;

internal static class FacilityDemolition
{
    internal static CommandRejection Apply(WorldMutation world, DemolishFacilityCommand command, SimulationRules rules,
        List<DomainEvent> events)
    {
        if (rules.Military is not { } military || !world.Settlements.TryGetValue(command.SettlementId, out var facility) ||
            facility.Kind != SettlementKind.Facility || facility.OwnerId != command.IssuerId || facility.IsDormant || facility.IsDestroyed ||
            !military.TryPlace(facility, out var definition) || !definition.CanDemolish)
            return CommandRejection.InvalidTask;
        if (Demobilization.City(world, facility.Id, rules.Personnel) is not int cityId ||
            !world.Settlements.Values.Any(s => FacilityDemolitionRules.IsOtherDirectFacility(facility, s)))
            return CommandRejection.NoDestination;
        if (facility.ConfusionDaysRemaining > 0) return CommandRejection.SettlementConfused;
        if (world.Engagements(military).Engaged(new(facility.Id, true))) return CommandRejection.InvalidTask;
        if (facility.Facility?.BuilderUnitId is int builder)
        {
            FacilityConstruction.Cancel(world, builder, events);
            MilitaryOrders.ReturnAfterTask(world, world.Units[builder], military, events, "facility-demolished");
            facility = world.Settlements[facility.Id];
        }

        Demobilization.Return(world, cityId, (long)facility.Soldiers + facility.Wounded, "facility-demolished", facility.Id, events);
        // Recruitment population belongs to the geographic city. Personnel require a friendly hosting place.
        var destination = world.Settlements.Values.Where(s => s.Id != facility.Id && s.OwnerId == command.IssuerId &&
                !s.IsDormant && !s.IsDestroyed && s.CanHostPersons && s.Durability > 0 &&
                s.Facility?.BuilderUnitId is null && military.TryPlace(s, out _))
            .OrderBy(s => s.Id == cityId ? 0 : s.CorpsId == facility.CorpsId ? 1 : 2)
            .ThenBy(s => MapCoordinates.Distance(definition.Position, military.Place(s).Position)).ThenBy(s => s.Id).FirstOrDefault();
        MilitaryResearch.CancelAt(world, facility.Id, events);
        world.Replace(facility with { IsDestroyed = true, OwnerId = null, CorpsId = null, Soldiers = 0, Wounded = 0,
            Durability = 0, Morale = 0, ConfusionDaysRemaining = 0 });
        foreach (var entry in new[] { (ResourceKind.Soldiers, facility.Soldiers), (ResourceKind.Wounded, facility.Wounded) })
        {
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, facility.Id), entry.Item1,
                "facility-demolished", cityId, -(long)entry.Item2, -(long)entry.Item2, events);
        }

        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        foreach (var person in world.Officers.Values.Where(p => p.SettlementId == facility.Id && !traveling.Contains(p.Id)).OrderBy(p => p.Id).ToArray())
        {
            if (destination is not null)
                world.Replace(person with { SettlementId = destination.Id, IsCommander = false });
            else if (person.IsServing || person.Status == OfficerStatus.Captive)
            {
                int? home = BattleRetreat.Destination(world, person, definition.Position, facility.Id, military)?.Id;
                if (home is int retreat)
                    BattlePersonnel.Escape(world, person.Id, retreat, rules.Personnel, events, definition.Position, military);
                else BattlePersonnel.DisplaceAtPlace(world, person.Id, facility.Id, rules.Personnel, events);
            }
            else BattleAftermath.Detach(world, person, facility.Id, events);
        }
        SettlementTasks.LocationChanged(world, facility.Id, "facility-demolished", rules, events);
        GovernanceLifecycle.Reconcile(world, new HashSet<int> { command.IssuerId }, rules, events, false);
        MilitaryOrders.SettlementChanged(world, facility.Id, military, events);
        events.Add(new FacilityDemolishedEvent(world.Clock, facility.Id, cityId, command.IssuerId, facility.Facility?.TypeId ?? definition.FacilityTypeId));
        return CommandRejection.None;
    }
}
