using San9.Core.Domain;

namespace San9.Core.Simulation;

public static class MilitaryEngagementQuery
{
    /// <summary>Uses the same close engagement view as wounded recovery; never advances the world.</summary>
    public static MilitaryEngagementSnapshot Capture(WorldSnapshot snapshot, MilitaryRulebook military)
    {
        var world = WorldState.Restore(snapshot).BeginMutation();
        var relations = world.Engagements(military);
        return new(world.Units.Keys.Where(id => relations.Engaged(new(id, false))).ToHashSet(),
            world.Settlements.Keys.Where(id => relations.Engaged(new(id, true))).ToHashSet());
    }
}

public sealed record MilitaryEngagementSnapshot(IReadOnlySet<int> Units, IReadOnlySet<int> Settlements);
