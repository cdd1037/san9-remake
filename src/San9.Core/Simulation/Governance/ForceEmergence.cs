using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ForceFoundedEvent(GameClock Clock, int ForceId, int SettlementId, int FounderId,
    ImmutableArray<int> MemberIds) : DomainEvent;

internal static class ForceEmergence
{
    private static IOrderedEnumerable<OfficerState> Order(IEnumerable<OfficerState> people) => people
        .OrderByDescending(p => p.ForceFounderPriority > 0)
        .ThenByDescending(p => p.ForceFounderPriority)
        .ThenByDescending(p => p.ForceFounderPriority == 0 ? p.Ambition : 0).ThenBy(p => p.Id);

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Simulation.ForceEmergenceEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        int? City(OfficerState person)
        {
            if ((person.SettlementId ?? person.RegionId) is not int place) return null;
            if (world.Settlements.TryGetValue(place, out var settlement) && settlement.Kind == SettlementKind.City) return place;
            return rules.Personnel.RegionCities.TryGetValue(place, out int city) ? city : null;
        }
        bool EmptyCity(int? id) => id is int city && world.Settlements.TryGetValue(city, out var place) &&
            place.Kind == SettlementKind.City && place.OwnerId is null && !place.IsDestroyed && place.CanHostPersons && place.Durability > 0;
        var free = world.Officers.Values.Where(p => p.Status == OfficerStatus.Free).ToArray();
        foreach (var candidate in Order(free.Where(p => (p.Ambition > 9 || p.ForceFounderPriority > 0) && EmptyCity(City(p)))))
        {
            int city = City(candidate)!.Value;
            var cities = rules.Personnel.CityConnections.Where(c => c.FromCityId == city).Select(c => c.ToCityId).Append(city).ToHashSet();
            var supporters = free.Where(p => p.Id != candidate.Id && City(p) is int current && cities.Contains(current) &&
                PersonnelRulebook.AffinityDistance(candidate, p) <= 10).ToArray();
            if (supporters.Length < 2) continue;
            var members = Order(supporters.Append(candidate)).ToArray();
            int force = ForceCreation.Found(world, city, members[0], members, ForceCreationKind.EmptyCityUprising, rules, events);
            events.Add(new ForceFoundedEvent(world.Clock, force, city, members[0].Id, members.Select(p => p.Id).Order().ToImmutableArray()));
            return;
        }
    }

}
