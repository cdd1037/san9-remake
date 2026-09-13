using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Returns living soldiers to a geographic recruitment pool without a capacity loss.</summary>
internal static class Demobilization
{
    internal static int? City(WorldMutation world, int placeId, PersonnelRulebook geography)
    {
        int cityId = world.Settlements.GetValueOrDefault(placeId)?.Kind == SettlementKind.City
            ? placeId : geography.RegionCities.GetValueOrDefault(placeId, -1);
        return world.Settlements.TryGetValue(cityId, out var city) && city.Kind == SettlementKind.City ? cityId : null;
    }

    internal static void Return(WorldMutation world, int cityId, long soldiers, string reason, int sourceId,
        List<DomainEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(soldiers);
        if (soldiers == 0) return;
        var city = world.Settlements[cityId];
        if (city.Kind != SettlementKind.City) throw new ArgumentException("Demobilization requires a city.");
        world.Replace(city with { DraftPopulation = checked((uint)(city.DraftPopulation + soldiers)) });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, cityId), ResourceKind.DraftPopulation,
            reason, sourceId, soldiers, soldiers, events);
    }
}
