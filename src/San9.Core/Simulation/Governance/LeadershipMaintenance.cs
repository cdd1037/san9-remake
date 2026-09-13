using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Maintains appointed leaders after selection inputs change. Consumers read the resulting identities.</summary>
internal static class LeadershipMaintenance
{
    internal static OfficerState? Commander(WorldMutation world, int placeId) => world.Officers.Values
        .FirstOrDefault(p => p.IsCommander && p.IsServing && p.SettlementId == placeId &&
            world.Settlements.TryGetValue(placeId, out var place) && place.OwnerId == p.OwnerId &&
            place.CanHostPersons && !place.IsDestroyed &&
            !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null));

    internal static void Apply(WorldMutation world, GovernanceRulebook governance)
    {
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var people = world.Officers.Values.Where(p => p.IsServing).ToArray();
        var abilities = world.AbilitiesFor(people);
        var commanders = people.Where(p => p.SettlementId is int home && !traveling.Contains(p.Id) &&
                world.Settlements.TryGetValue(home, out var place) && place.OwnerId == p.OwnerId && place.CanHostPersons && !place.IsDestroyed)
            .GroupBy(p => p.SettlementId!.Value)
            .Select(group => governance.SelectCommander(group, abilities)!.Id).ToHashSet();
        foreach (var person in world.Officers.Values.ToArray())
            if (person.IsCommander != commanders.Contains(person.Id))
                world.Replace(person with { IsCommander = commanders.Contains(person.Id) });
        var members = people.Where(p => p.UnitId is not null).ToLookup(p => p.UnitId!.Value);
        foreach (var unit in world.Units.Values.ToArray())
        {
            var eligible = members[unit.Id].Where(p => p.OwnerId == unit.OwnerId).ToDictionary(p => p.Id);
            var ordered = unit.MemberOrder.Where(eligible.ContainsKey)
                .Concat(eligible.Keys.Where(id => !unit.MemberOrder.Contains(id))).Select(id => eligible[id]);
            int? leader = governance.SelectCommander(ordered, abilities)?.Id;
            var order = leader is int first ? ordered.Select(p => p.Id).Where(id => id != first).Prepend(first).ToImmutableArray() : [];
            var ranks = unit.MemberRanks.Where(p => eligible.ContainsKey(p.Key)).ToImmutableDictionary();
            if (unit.CommanderId != leader || !unit.MemberOrder.SequenceEqual(order) || unit.MemberRanks.Count != ranks.Count)
                world.Replace(unit with { CommanderId = leader, MemberOrder = order, MemberRanks = ranks });
        }
    }
}
