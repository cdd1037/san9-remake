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

}
