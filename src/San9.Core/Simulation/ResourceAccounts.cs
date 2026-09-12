using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ResourceKind { Money, Food, Population, DraftPopulation, Soldiers }
public enum ResourceOwnerKind { Force, Corps, Settlement, Unit }
public sealed record ResourceAccount(ResourceOwnerKind Kind, int Id);
public sealed record ResourceChangedEvent(GameClock Clock, ResourceAccount Account, ResourceKind Resource,
    string Reason, int? SourceId, long Requested, long Actual, long Shortfall, long SaturationLoss) : DomainEvent;
public sealed record OfficerLoyaltyChangedEvent(GameClock Clock, int OfficerId, int ForceId, int Previous, int Current, string Reason) : DomainEvent;
public sealed record ForceTrustChangedEvent(GameClock Clock, int ForceId, int Previous, int Current) : DomainEvent;

internal static class ResourceAccounts
{
    internal static ResourceAccount Resolve(WorldMutation world, int forceId, int? corpsId)
    {
        if (corpsId is int id)
        {
            var corps = world.Corps[id];
            if (corps.OwnerId != forceId) throw new InvalidOperationException("Account ownership mismatch.");
            if (corps.Treasury is not null) return new(ResourceOwnerKind.Corps, id);
        }
        return new(ResourceOwnerKind.Force, forceId);
    }

    internal static uint Balance(WorldMutation world, ResourceAccount account, ResourceKind resource)
    {
        if (resource is not (ResourceKind.Money or ResourceKind.Food)) throw new ArgumentException("Not a treasury resource.");
        if (account.Kind == ResourceOwnerKind.Corps)
        {
            var treasury = world.Corps[account.Id].Treasury!;
            return resource == ResourceKind.Money ? treasury.Money : treasury.Food;
        }
        var force = world.Forces[account.Id];
        return resource == ResourceKind.Money ? force.Money : force.Food;
    }

    internal static uint Change(WorldMutation world, ResourceAccount account, ResourceKind resource,
        long requested, string reason, int? sourceId, List<DomainEvent> events, bool requireFull = false)
    {
        uint before = Balance(world, account, resource);
        uint after = requested < 0 && requireFull && before < -requested ? before :
            (uint)Math.Clamp((long)before + requested, 0, requested > 0 ? Math.Max(before, 1000000L) : uint.MaxValue);
        if (account.Kind == ResourceOwnerKind.Corps)
        {
            var corps = world.Corps[account.Id];
            world.Replace(corps with { Treasury = resource == ResourceKind.Money ? corps.Treasury! with { Money = after } : corps.Treasury! with { Food = after } });
        }
        else
        {
            var force = world.Forces[account.Id];
            world.Replace(resource == ResourceKind.Money ? force with { Money = after } : force with { Food = after });
        }
        Report(world, account, resource, reason, sourceId, requested, (long)after - before, events);
        return after;
    }

    internal static void Report(WorldMutation world, ResourceAccount account, ResourceKind resource,
        string reason, int? sourceId, long requested, long actual, List<DomainEvent> events) =>
        events.Add(new ResourceChangedEvent(world.Clock, account, resource, reason, sourceId, requested, actual,
            requested < 0 ? actual - requested : 0, requested > 0 ? requested - actual : 0));
}
