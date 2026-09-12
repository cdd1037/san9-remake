using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class GovernanceMaintenance
{
    internal static bool CanRemove(WorldMutation world, int personId)
    {
        var person = world.Officers[personId];
        if (PersonnelLocations.Corps(world, person) is not int id ||
            !world.Corps.TryGetValue(id, out var corps) || corps.Treasury is null) return true;
        if (world.Officers.Values.Any(p => p.Id != personId && p.IsServing && p.OwnerId == corps.OwnerId &&
            PersonnelLocations.Corps(world, p) == id) &&
            world.Settlements.Values.Any(s => s.CorpsId == id && s.Kind == SettlementKind.City)) return true;
        var force = world.Forces[corps.OwnerId];
        return (ulong)force.Money + corps.Treasury.Money <= uint.MaxValue && (ulong)force.Food + corps.Treasury.Food <= uint.MaxValue;
    }
    internal static void RepairCommander(WorldMutation world, int placeId, GovernanceRulebook rules, List<DomainEvent> events)
    {
        var people = world.Officers.Values.Where(p => p.SettlementId == placeId && p.IsServing).ToArray();
        var busy = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var selected = rules.SelectCommander(people.Where(p => !busy.Contains(p.Id)), world.AbilitiesFor(people));
        var previous = people.Where(p => p.IsCommander).Select(p => p.Id).Order().ToArray();
        foreach (var person in people) world.Replace(person with { IsCommander = person.Id == selected?.Id });
        if (!previous.SequenceEqual(selected is null ? [] : new[] { selected.Id }))
            events.Add(new CommanderChangedEvent(world.Clock, placeId, selected?.Id));
    }

    internal static CommandRejection RepairCorps(WorldMutation world, int corpsId, GovernanceRulebook rules, List<DomainEvent> events)
    {
        if (!world.Corps.TryGetValue(corpsId, out var corps) || corps.Treasury is null) return CommandRejection.None;
        var people = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == corps.OwnerId &&
            PersonnelLocations.Corps(world, p) == corpsId).ToArray();
        if (people.Length == 0 || !world.Settlements.Values.Any(s => s.CorpsId == corps.Id && s.Kind == SettlementKind.City))
            return CorpsGovernance.Dissolve(world, new(Guid.Empty, corps.OwnerId, 0, corps.Id), rules, events);
        if (people.Any(p => p.Id == corps.LeaderId)) return CommandRejection.None;
        var leader = rules.SelectSuccessor(people, corps.LeaderId, world.AbilitiesFor(people))!;
        var oldLeader = world.Officers[corps.LeaderId];
        if (oldLeader.IsServing && oldLeader.Status == OfficerStatus.Governor)
            world.Replace(oldLeader with { Status = OfficerStatus.Serving });
        var force = world.Forces[corps.OwnerId];
        if (force.AdviserId == leader.Id) world.Replace(force with { AdviserId = null });
        world.Replace(leader with { Status = leader.Status == OfficerStatus.Ruler ? OfficerStatus.Ruler : OfficerStatus.Governor });
        world.Replace(corps with { LeaderId = leader.Id });
        if (leader.SettlementId is int home) RepairCommander(world, home, rules, events);
        events.Add(new CorpsLeaderChangedEvent(world.Clock, corps.Id, oldLeader.Id, leader.Id));
        return CommandRejection.None;
    }
}

public sealed record CommanderChangedEvent(GameClock Clock, int PlaceId, int? OfficerId) : DomainEvent;
public sealed record CorpsLeaderChangedEvent(GameClock Clock, int CorpsId, int PreviousId, int OfficerId) : DomainEvent;
