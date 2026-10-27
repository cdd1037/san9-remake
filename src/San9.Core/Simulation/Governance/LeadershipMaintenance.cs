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
        // This table is local to one maintenance pass and is never published as world state.
        var abilities = people.ToDictionary(p => p.Id, world.Abilities);
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

    /// <summary>The partial reselection of a consistent world: the changed places and officers, then every unit as
    /// <see cref="Apply(WorldMutation, GovernanceRulebook)"/> does. Abilities are read only where an order needs them.</summary>
    internal static void Apply(WorldMutation world, GovernanceRulebook governance, IReadOnlySet<int> places, IReadOnlySet<int> officers)
    {
        var abilities = new LazyAbilities(world);
        var commanders = new HashSet<int>();
        var touched = new HashSet<int>(officers);
        foreach (int home in places)
        {
            var members = world.SettlementMembers(home).ToArray();
            foreach (var p in members) touched.Add(p.Id);
            if (!world.Settlements.TryGetValue(home, out var place) || !place.CanHostPersons || place.IsDestroyed) continue;
            var group = members.Where(p => p.IsServing && !world.IsTraveling(p.Id) && place.OwnerId == p.OwnerId).ToArray();
            if (group.Length > 0) commanders.Add(governance.SelectCommander(group, abilities)!.Id);
        }
        foreach (var person in world.Officers.Values.Where(p => touched.Contains(p.Id)).ToArray())
            if (person.IsCommander != commanders.Contains(person.Id))
                world.Replace(person with { IsCommander = commanders.Contains(person.Id) });
        foreach (var unit in world.Units.Values.ToArray())
            UpdateUnit(world, unit, world.UnitMembers(unit.Id).Where(p => p.IsServing), abilities, governance);
    }

    /// <summary>Abilities on first use, as <see cref="WorldMutation.Abilities"/> gives them; a selection pass changes no
    /// input of theirs.</summary>
    private sealed class LazyAbilities(WorldMutation world) : IReadOnlyDictionary<int, OfficerAbilities>
    {
        private readonly Dictionary<int, OfficerAbilities> _known = [];
        public OfficerAbilities this[int key] => _known.TryGetValue(key, out var value) ? value : _known[key] = world.Abilities(world.Officers[key]);
        public bool ContainsKey(int key) => world.Officers.ContainsKey(key);
        public bool TryGetValue(int key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out OfficerAbilities value)
        {
            if (!ContainsKey(key)) { value = default!; return false; }
            value = this[key]; return true;
        }
        public IEnumerable<int> Keys => world.Officers.Keys;
        public IEnumerable<OfficerAbilities> Values => Keys.Select(k => this[k]);
        public int Count => world.Officers.Count;
        public IEnumerator<KeyValuePair<int, OfficerAbilities>> GetEnumerator() => Keys.Select(k => new KeyValuePair<int, OfficerAbilities>(k, this[k])).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
