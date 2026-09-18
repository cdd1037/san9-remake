using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record FreeOfficerMigratedEvent(GameClock Clock, int OfficerId, int OriginId, int DestinationId) : DomainEvent;

internal static class FreeOfficerMigration
{
    internal static void Apply(WorldMutation world, PersonnelRulebook places, List<DomainEvent> events)
    {
        if (!world.Simulation.FreeOfficerMigrationEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        foreach (int id in world.Officers.Keys.Order().ToArray())
        {
            var person = world.Officers[id];
            if (person.Status != OfficerStatus.Free) continue;
            if (world.NextRandom("personnel-free-migration") % 100 >= 50) continue;
            if ((person.SettlementId ?? person.RegionId) is not int origin) continue;
            var destinations = PersonnelRelocation.Destinations(world, origin, places);
            if (destinations.Length == 0) continue;
            int destination = destinations[world.NextRandom("personnel-free-migration-placement") % (ulong)destinations.Length];
            PersonnelTransitions.CancelMissions(world, id, "person-migrated", events, places, false);
            world.Replace(world.Officers[id] with
            {
                SettlementId = world.Settlements.ContainsKey(destination) ? destination : null,
                RegionId = world.Settlements.ContainsKey(destination) ? null : destination
            });
            events.Add(new FreeOfficerMigratedEvent(world.Clock, id, origin, destination));
        }
    }
}
