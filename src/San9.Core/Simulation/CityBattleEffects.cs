using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record CityBattleDamageEvent(GameClock Clock, int CityId, int AttackerId,
    int PublicOrderLoss, int CommerceLoss, int CultivationLoss) : DomainEvent;
public sealed record CityCapturePublicOrderEvent(GameClock Clock, int CityId, int Previous, int Current) : DomainEvent;

internal static class CityBattleEffects
{
    internal static int CapturedPublicOrder(WorldMutation world, SettlementState city, int newOwner, List<DomainEvent> events)
    {
        if (city.Kind != SettlementKind.City || city.OwnerId == newOwner) return city.PublicOrder;
        int oldTrust = city.OwnerId is int old ? world.Forces[old].Trust : 300;
        int newTrust = world.Forces[newOwner].Trust;
        int random = (int)(world.NextRandom("city-capture-public-order") % 50);
        int value = Math.Clamp(Math.Min(city.PublicOrder + 300,
            Math.Min(city.PublicOrder + newTrust - oldTrust, newTrust + 150 + random)), 0, 1000);
        events.Add(new CityCapturePublicOrderEvent(world.Clock, city.Id, city.PublicOrder, value));
        return value;
    }

    internal static void CompleteAttack(WorldMutation world, int attackerId, int cityId, int? previousOwner, List<DomainEvent> events)
    {
        if (!world.Units.TryGetValue(attackerId, out var attacker) || attacker.Soldiers == 0 || attacker.Morale == 0 ||
            !world.Settlements.TryGetValue(cityId, out var city) || city.Kind != SettlementKind.City ||
            city.OwnerId != previousOwner || MilitaryCombat.Defeated(city) || city.IsDestroyed) return;
        var next = city with { PublicOrder = city.PublicOrder * 99 / 100,
            Commerce = city.Commerce * 99 / 100, Cultivation = city.Cultivation * 99 / 100 };
        world.Replace(next);
        events.Add(new CityBattleDamageEvent(world.Clock, cityId, attackerId, city.PublicOrder - next.PublicOrder,
            city.Commerce - next.Commerce, city.Cultivation - next.Cultivation));
    }
}
