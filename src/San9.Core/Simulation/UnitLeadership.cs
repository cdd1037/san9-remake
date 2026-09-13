using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class UnitLeadership
{
    internal static ImmutableArray<int> Order(WorldMutation world, IEnumerable<int> members, GovernanceRulebook governance)
    {
        var candidates = members.Select(id => world.Officers[id]).Where(p => p.IsServing).ToArray();
        return governance.OrderCommanders(candidates, world.AbilitiesFor(candidates)).Select(p => p.Id).ToImmutableArray();
    }

    internal static void Repair(WorldMutation world, GovernanceRulebook governance)
    {
        foreach (var unit in world.Units.Values.Where(u => !u.MemberOrder.IsEmpty).OrderBy(u => u.Id).ToArray())
        {
            var remaining = unit.MemberOrder.Where(id => world.Officers.TryGetValue(id, out var p) && p.IsServing && p.UnitId == unit.Id).ToImmutableArray();
            if (remaining.Length == unit.MemberOrder.Length) continue;
            int? commander = remaining.Contains(unit.CommanderId!.Value) ? unit.CommanderId :
                Order(world, remaining, governance).Select(id => (int?)id).FirstOrDefault();
            // Reselection moves only the winner to the front; other members retain their relative order.
            var order = commander is int leader ? remaining.Where(id => id != leader).Prepend(leader).ToImmutableArray() : [];
            world.Replace(unit with { CommanderId = commander, MemberOrder = order,
                MemberRanks = unit.MemberRanks.Where(p => remaining.Contains(p.Key)).ToImmutableDictionary() });
        }
    }
}
