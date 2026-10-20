using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class GovernanceMaintenance
{
    internal static void DissolveForced(WorldMutation world, int corpsId, string reason, List<DomainEvent> events)
    {
        var corps = world.Corps[corpsId];
        if (corps.IsRoot) throw new ArgumentException("Cannot dissolve the root corps.");
        int? root = world.Corps.Values.Where(c => c.OwnerId == corps.OwnerId && c.IsRoot).Select(c => (int?)c.Id).SingleOrDefault();
        foreach (var s in world.Settlements.Values.Where(s => s.CorpsId == corps.Id).ToArray()) world.Replace(s with { CorpsId = root });
        foreach (var u in world.Units.Values.Where(u => u.CorpsId == corps.Id).ToArray()) world.Replace(u with { CorpsId = root });
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = ResourceAccounts.Balance(world, new(ResourceOwnerKind.Corps, corps.Id), resource);
            ResourceAccounts.Change(world, new(ResourceOwnerKind.Corps, corps.Id), resource, -amount, $"{reason}-corps-dissolved", null, events);
            ResourceAccounts.Change(world, ResourceAccounts.Resolve(world, corps.OwnerId, null), resource, amount, $"{reason}-corps-dissolved", null, events);
        }
        RedirectReturns(world, corps.Id, root);
        world.RemoveCorps(corps.Id);
        var previousLeader = world.Officers[corps.LeaderId!.Value];
        if (previousLeader.Status == OfficerStatus.Governor) world.Replace(previousLeader with { Status = OfficerStatus.Serving });
        events.Add(new CorpsDissolvedEvent(world.Clock, corps.OwnerId, corps.Id, root ?? -1));
    }

    internal static void RedirectReturns(WorldMutation world, int source, int? destination) =>
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Select(t =>
            t.Journey?.ReturnLeg is { } leg && leg.CorpsId == source
                ? t with { Journey = t.Journey with { ReturnLeg = leg with { CorpsId = destination } } } : t).ToImmutableArray() });


    internal static bool CanRemove(WorldMutation world, int personId)
    {
        var person = world.Officers[personId];
        if (PersonnelLocations.Corps(world, person) is not int id ||
            !world.Corps.TryGetValue(id, out var corps) || corps.IsRoot) return true;
        if (world.Officers.Values.Any(p => p.Id != personId && p.IsServing && p.OwnerId == corps.OwnerId &&
            PersonnelLocations.Corps(world, p) == id) &&
            world.Settlements.Values.Any(s => s.CorpsId == id && s.Kind == SettlementKind.City)) return true;
        var force = world.Forces[corps.OwnerId];
        return (ulong)ResourceAccounts.Root(world, force.Id).Treasury.Money + corps.Treasury.Money <= uint.MaxValue && (ulong)ResourceAccounts.Root(world, force.Id).Treasury.Food + corps.Treasury.Food <= uint.MaxValue;
    }
    internal static CommandRejection RepairCorps(WorldMutation world, int corpsId, GovernanceRulebook rules, List<DomainEvent> events)
    {
        if (!world.Corps.TryGetValue(corpsId, out var corps) || corps.IsRoot) return CommandRejection.None;
        var people = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == corps.OwnerId &&
            PersonnelLocations.Corps(world, p) == corpsId).ToArray();
        if (people.Length == 0 || !world.Settlements.Values.Any(s => s.CorpsId == corps.Id && s.Kind == SettlementKind.City))
            return CorpsGovernance.Dissolve(world, new(Guid.Empty, corps.OwnerId, 0, corps.Id), rules, events);
        if (people.Any(p => p.Id == corps.LeaderId)) return CommandRejection.None;
        var leader = rules.SelectSuccessor(people, corps.LeaderId!.Value, world.AbilitiesFor(people))!;
        var oldLeader = world.Officers[corps.LeaderId!.Value];
        if (oldLeader.IsServing && oldLeader.Status == OfficerStatus.Governor)
            world.Replace(oldLeader with { Status = OfficerStatus.Serving });
        var force = world.Forces[corps.OwnerId];
        if (force.AdviserId == leader.Id) world.Replace(force with { AdviserId = null });
        world.Replace(leader with { Status = leader.Status == OfficerStatus.Ruler ? OfficerStatus.Ruler : OfficerStatus.Governor });
        world.Replace(corps with { LeaderId = leader.Id });
        events.Add(new CorpsLeaderChangedEvent(world.Clock, corps.Id, oldLeader.Id, leader.Id)
        { Occurrence = PersonOccurrence.Capture(world, leader) });
        return CommandRejection.None;
    }
}

public sealed record CommanderChangedEvent(GameClock Clock, int PlaceId, int? OfficerId) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public int? PreviousId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? ForceId { get; init; }
}
public sealed record CorpsLeaderChangedEvent(GameClock Clock, int CorpsId, int PreviousId, int OfficerId) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public PersonOccurrence Occurrence { get; init; } = null!;
}
