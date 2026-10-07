using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Maintains appointed leaders after selection inputs change. Consumers read the resulting identities.</summary>
internal static class LeadershipMaintenance
{
    internal static int? SelectUnitCommander(WorldMutation world, IEnumerable<int> members, GovernanceRulebook governance)
    {
        var candidates = members.Select(id => world.Officers[id]).Where(p => p.IsServing).ToArray();
        return governance.SelectCommander(candidates, world.AbilitiesFor(candidates))?.Id;
    }

    internal static OfficerState? Commander(WorldMutation world, int placeId) => world.ServingSettlementMembers(placeId)
        .FirstOrDefault(p => p.IsCommander &&
            world.Settlements.TryGetValue(placeId, out var place) && place.OwnerId == p.OwnerId &&
            place.CanHostPersons && !place.IsDestroyed);

    internal static void Apply(WorldMutation world, GovernanceRulebook governance)
    {
        var people = world.Officers.Values.Where(p => p.IsServing).ToArray();
        var abilities = world.AbilitiesFor(people);
        var commanders = people.Where(p => p.SettlementId is int home && !world.IsTraveling(p.Id) &&
                world.Settlements.TryGetValue(home, out var place) && place.OwnerId == p.OwnerId && place.CanHostPersons && !place.IsDestroyed)
            .GroupBy(p => p.SettlementId!.Value)
            .Select(group => governance.SelectCommander(group, abilities)!.Id).ToHashSet();
        foreach (var person in world.Officers.Values.ToArray())
            if (person.IsCommander != commanders.Contains(person.Id))
                world.Replace(person with { IsCommander = commanders.Contains(person.Id) });
        var members = people.Where(p => p.UnitId is not null).ToLookup(p => p.UnitId!.Value);
        foreach (var unit in world.Units.Values.ToArray())
            UpdateUnit(world, unit, members[unit.Id], abilities, governance);
    }

    private static void UpdateUnit(WorldMutation world, UnitState unit, IEnumerable<OfficerState> members,
        IReadOnlyDictionary<int, OfficerAbilities> abilities, GovernanceRulebook governance)
    {
        var eligible = members.Where(p => p.OwnerId == unit.OwnerId).ToDictionary(p => p.Id);
        int? leader = governance.SelectCommander(eligible.Values, abilities)?.Id;
        var ranks = unit.MemberRanks.Where(p => eligible.ContainsKey(p.Key)).ToImmutableDictionary();
        if (unit.CommanderId != leader || unit.MemberRanks.Count != ranks.Count)
            world.Replace(unit with { CommanderId = leader, MemberRanks = ranks });
    }
}
