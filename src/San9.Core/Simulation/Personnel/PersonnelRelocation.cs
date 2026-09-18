using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class PersonnelRelocation
{
    internal static int[] Destinations(WorldMutation world, int origin, PersonnelRulebook places)
    {
        if (!places.RegionCities.TryGetValue(origin, out int city)) return [];
        var cities = places.CityConnections.Where(c => c.FromCityId == city).Select(c => c.ToCityId)
            .Where(id => world.Settlements.TryGetValue(id, out var destination) &&
                (destination.OwnerId is not int owner || !world.Forces[owner].IsBarbarian)).ToHashSet();
        return places.RegionCities.Where(p => cities.Contains(p.Value) && places.Places.ContainsKey(p.Key) &&
            (!world.Settlements.TryGetValue(p.Key, out var target) ||
                target.CanHostPersons && !target.IsDestroyed && target.Durability > 0)).Select(p => p.Key).Order().ToArray();
    }

}
