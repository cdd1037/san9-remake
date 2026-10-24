using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ResourceKind { Money, Food, Population, DraftPopulation, Soldiers, Wounded }
public enum ResourceOwnerKind { Corps, Settlement, Unit }
public sealed record ResourceAccount(ResourceOwnerKind Kind, int Id);
public sealed record ResourceChangedEvent(GameClock Clock, ResourceAccount Account, ResourceKind Resource,
    string Reason, int? SourceId, long Requested, long Actual, long Shortfall, long SaturationLoss) : DomainEvent;
public sealed record OfficerLoyaltyChangedEvent(GameClock Clock, int OfficerId, int ForceId, int Previous, int Current, string Reason) : DomainEvent;
public sealed record ForceTrustChangedEvent(GameClock Clock, int ForceId, int Previous, int Current) : DomainEvent;

internal static class ResourceAccounts
{
    internal static CorpsState Root(WorldMutation world, int forceId) =>
        world.Corps.Values.Single(c => c.OwnerId == forceId && c.IsRoot);

    internal static ResourceAccount Resolve(WorldMutation world, int forceId, int? corpsId)
    {
        var corps = corpsId is int id ? world.Corps[id] : Root(world, forceId);
        if (corps.OwnerId != forceId) throw new InvalidOperationException("Account ownership mismatch.");
        return new(ResourceOwnerKind.Corps, corps.Id);
    }

    internal static uint Balance(WorldMutation world, ResourceAccount account, ResourceKind resource)
    {
        if (account.Kind != ResourceOwnerKind.Corps || resource is not (ResourceKind.Money or ResourceKind.Food))
            throw new ArgumentException("Not a corps treasury resource.");
        var treasury = world.Corps[account.Id].Treasury;
        return resource == ResourceKind.Money ? treasury.Money : treasury.Food;
    }

    internal static uint Receivable(WorldMutation world, ResourceAccount account, ResourceKind resource)
    {
        return TreasuryRules.Receivable(Balance(world, account, resource));
    }

    internal static uint Change(WorldMutation world, ResourceAccount account, ResourceKind resource,
        long requested, string reason, int? sourceId, List<DomainEvent> events, bool requireFull = false)
    {
        uint before = Balance(world, account, resource);
        uint after = Project(before, requested, requireFull);
        var corps = world.Corps[account.Id];
        world.Replace(corps with { Treasury = resource == ResourceKind.Money
            ? corps.Treasury with { Money = after } : corps.Treasury with { Food = after } });
        Report(world, account, resource, reason, sourceId, requested, (long)after - before, events);
        return after;
    }

    internal static uint Project(uint before, long requested, bool requireFull = false) =>
        requested < 0 && requireFull && before < -requested ? before :
            (uint)TreasuryRules.Project(before, requested);

    internal static void Report(WorldMutation world, ResourceAccount account, ResourceKind resource,
        string reason, int? sourceId, long requested, long actual, List<DomainEvent> events) =>
        events.Add(new ResourceChangedEvent(world.Clock, account, resource, reason, sourceId, requested, actual,
            requested < 0 ? actual - requested : 0, requested > 0 ? requested - actual : 0));
}
