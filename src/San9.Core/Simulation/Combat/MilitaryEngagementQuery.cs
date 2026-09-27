using San9.Core.Domain;

namespace San9.Core.Simulation;

public static class MilitaryEngagementQuery
{
    /// <summary>Uses the same incoming attack relations as combat and wounded recovery; never advances the world.</summary>
    public static IReadOnlySet<int> EngagedSettlements(WorldSnapshot snapshot, MilitaryRulebook military)
    {
        var world = WorldState.Restore(snapshot).BeginMutation();
        var relations = world.Engagements(military);
        return world.Settlements.Keys.Where(id => relations.Engaged(new(id, true))).ToHashSet();
    }
}
