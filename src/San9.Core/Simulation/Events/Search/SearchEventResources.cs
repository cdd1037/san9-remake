using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class SearchEventResources
{
    internal static ResourceAccount Account(WorldMutation world, PersistentTask task) =>
        ResourceAccounts.Resolve(world, task.ForceId, world.Settlements[task.OriginSettlementId].CorpsId);

    internal static bool HasFunds(WorldMutation world, PersistentTask task, uint minimumExclusive, uint maximumCost)
    {
        uint money = ResourceAccounts.Balance(world, Account(world, task), ResourceKind.Money);
        return money > minimumExclusive && money >= maximumCost;
    }
}
