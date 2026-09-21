using System.Numerics;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class BattleTreasury
{
    internal static long Weight(SettlementState place) => (long)place.Soldiers + place.Wounded +
        (place.Kind == SettlementKind.City ? (long)place.Population + place.DraftPopulation : 0);

    internal static void Transfer(WorldMutation world, int owner, int? corps, long weight,
        BattleSource winner, int defeatedId, string reason, List<DomainEvent> events)
        => TransferTo(world, owner, corps, weight, ResourceAccounts.Resolve(world, winner.Owner(world), winner.Corps(world)),
            defeatedId, reason, events);

    internal static void TransferTo(WorldMutation world, int owner, int? corps, long weight,
        ResourceAccount target, int defeatedId, string reason, List<DomainEvent> events)
    {
        var source = ResourceAccounts.Resolve(world, owner, corps);
        long total = world.Settlements.Values.Where(s => s.OwnerId == owner && !s.IsDestroyed &&
                ResourceAccounts.Resolve(world, owner, s.CorpsId) == source).Sum(Weight) +
            world.Units.Values.Where(u => u.OwnerId == owner && !u.IsEmbedded && !u.IsPersonnel &&
                ResourceAccounts.Resolve(world, owner, u.CorpsId) == source).Sum(u => (long)u.Soldiers + u.Wounded);
        BigInteger ratio = total == 0 ? 250 : (BigInteger)weight * 250 / total;
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = (long)((BigInteger)ResourceAccounts.Balance(world, source, resource) * ratio / 500);
            ResourceAccounts.Change(world, source, resource, -amount, reason, defeatedId, events);
            ResourceAccounts.Change(world, target, resource, amount, reason, defeatedId, events);
        }
    }
}
