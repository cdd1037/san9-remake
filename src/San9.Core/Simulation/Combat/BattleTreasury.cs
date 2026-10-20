using System.Numerics;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class BattleTreasury
{
    internal static long Weight(SettlementState place) => (long)place.Soldiers + place.Wounded +
        (place.Kind == SettlementKind.City ? (long)place.Population + place.DraftPopulation : 0);

    internal static void Transfer(WorldMutation world, int owner, int? corps, long weight,
        BattleSource winner, BattleSource defeated, string reason, List<DomainEvent> events)
        => TransferTo(world, owner, corps, weight, ResourceAccounts.Resolve(world, winner.Owner(world), winner.Corps(world)),
            defeated, reason, events);

    internal static void TransferTo(WorldMutation world, int owner, int? corps, long weight,
        ResourceAccount target, BattleSource defeated, string reason, List<DomainEvent> events)
    {
        var source = ResourceAccounts.Resolve(world, owner, corps);
        long total = world.Settlements.Values.Where(s => s.OwnerId == owner && !s.IsDestroyed &&
                ResourceAccounts.Resolve(world, owner, s.CorpsId) == source).Sum(Weight) +
            world.Units.Values.Where(u => u.OwnerId == owner && !u.IsEmbedded && !u.IsPersonnel &&
                ResourceAccounts.Resolve(world, owner, u.CorpsId) == source).Sum(u => (long)u.Soldiers + u.Wounded);
        BigInteger ratio = total == 0 ? 250 : (BigInteger)weight * 250 / total;
        long money = 0, food = 0;
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = (long)((BigInteger)ResourceAccounts.Balance(world, source, resource) * ratio / 500);
            ResourceAccounts.Change(world, source, resource, -amount, reason, defeated.Id, events);
            long previous = ResourceAccounts.Balance(world, target, resource);
            ResourceAccounts.Change(world, target, resource, amount, reason, defeated.Id, events);
            long credited = ResourceAccounts.Balance(world, target, resource) - previous;
            if (resource == ResourceKind.Money) money = credited; else food = credited;
        }
        if (money + food <= 0) return;
        var recipient = world.Corps[target.Id];
        var place = defeated.IsSettlement ? world.Settlements[defeated.Id] : null;
        var unit = defeated.IsSettlement ? null : world.Units[defeated.Id];
        events.Add(new TreasuryCapturedEvent(world.Clock, owner, corps, recipient.OwnerId, recipient.Id,
            defeated.Id, defeated.IsSettlement, unit?.Position, place?.Id ?? unit?.ServicePlaceId, money, food));
    }
}
