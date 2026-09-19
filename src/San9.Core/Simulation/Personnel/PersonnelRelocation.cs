using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class PersonnelRelocation
{
    internal static int? City(WorldMutation world, int place, PersonnelRulebook places) =>
        world.Settlements.TryGetValue(place, out var settlement) && settlement.Kind == SettlementKind.City ? place :
        places.RegionCities.TryGetValue(place, out int city) ? city : null;

    internal static int[] Destinations(WorldMutation world, int origin, PersonnelRulebook places)
    {
        if (City(world, origin, places) is not int city) return [];
        var cities = places.CityConnections.Where(c => c.FromCityId == city).Select(c => c.ToCityId)
            .Where(id => world.Settlements.TryGetValue(id, out var destination) &&
                (destination.OwnerId is not int owner || !world.Forces[owner].IsBarbarian)).ToHashSet();
        return places.Places.Keys.Where(id => City(world, id, places) is int ownerCity && cities.Contains(ownerCity) &&
            (!world.Settlements.TryGetValue(id, out var target) ||
                target.CanHostPersons && !target.IsDestroyed && target.Durability > 0)).Order().ToArray();
    }

}
