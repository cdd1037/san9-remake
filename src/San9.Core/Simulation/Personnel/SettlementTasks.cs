using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Repairs service attachments independently of a journey's historical origin and current target.</summary>
internal static class SettlementTasks
{
    internal static void LocationChanged(WorldMutation world, int placeId, string reason, SimulationRules rules,
        List<DomainEvent> events)
    {
        var place = world.Settlements[placeId];
        foreach (var task in world.Simulation.Tasks.Where(t => t.OriginSettlementId == placeId ||
                t.Journey?.DestinationPlaceId == placeId || world.Officers[t.OfficerId].SettlementId == placeId)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            if (!world.Simulation.Tasks.Any(t => t.Id == task.Id)) continue;
            if (place.OwnerId == task.ForceId && !place.IsDestroyed && place.Durability > 0) continue;
            if (task.Journey is not { } journey || journey.DestinationPlaceId == placeId)
            {
                TaskCancellation.Apply(world, task, reason, events);
                if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
                continue;
            }

            var person = world.Officers[task.OfficerId];
            if (!person.IsServing || person.SettlementId != placeId) continue;
            bool Usable(SettlementState home) => home.OwnerId == person.OwnerId && home.CanHostPersons &&
                !home.IsDormant && !home.IsDestroyed && home.Durability > 0 && home.Facility?.BuilderUnitId is null;
            SettlementState? home = world.Settlements.TryGetValue(journey.DestinationPlaceId, out var destination) && Usable(destination)
                ? destination : null;
            home ??= world.Settlements.Values.Where(s => Usable(s) && rules.Personnel.Places.ContainsKey(s.Id))
                .OrderBy(s => MapCoordinates.Distance(journey.Route.Origin, PersonnelLocations.Position(world, rules.Personnel, s.Id)))
                .ThenBy(s => s.Id).FirstOrDefault();
            if (home is not null)
                world.Replace(person with { SettlementId = home.Id, IsCommander = false });
            else
                BattlePersonnel.DisplaceAtPlace(world, person.Id, placeId, rules.Personnel, events);
        }
    }
}
