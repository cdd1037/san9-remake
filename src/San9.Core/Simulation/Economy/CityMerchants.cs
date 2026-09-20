using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record CityMerchantChangedEvent(GameClock Clock, int SettlementId, bool Previous, bool Current) : DomainEvent;

internal static class CityMerchants
{
    internal static void Refresh(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var city in world.Settlements.Values.Where(s => s.Kind == SettlementKind.City && !s.IsDestroyed).OrderBy(s => s.Id))
        {
            bool present = city.MerchantPresenceChance == 100 || city.MerchantPresenceChance > 0 &&
                world.NextRandom("city-merchants") % 100 < (ulong)city.MerchantPresenceChance;
            world.Replace(city with { MerchantPresent = present });
            if (present != city.MerchantPresent) events.Add(new CityMerchantChangedEvent(world.Clock, city.Id, city.MerchantPresent, present));
        }
    }
}
