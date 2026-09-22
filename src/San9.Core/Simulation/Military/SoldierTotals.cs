namespace San9.Core.Simulation;

/// <summary>Counts soldiers at their current owner, including troops carried by personnel tasks.</summary>
internal static class SoldierTotals
{
    internal static long Force(WorldMutation world, int forceId) => Count(world, forceId, false, null);

    internal static long Corps(WorldMutation world, int forceId, int? corpsId) => Count(world, forceId, true, corpsId);

    internal static long Living(WorldMutation world, int? forceId = null) => Count(world, forceId, false, null, includeWounded: true);

    private static long Count(WorldMutation world, int? forceId, bool restrictCorps, int? corpsId, bool includeWounded = false)
    {
        long total = 0;
        foreach (var place in world.Settlements.Values)
            if (!place.IsDestroyed && (forceId is null || place.OwnerId == forceId) && (!restrictCorps || place.CorpsId == corpsId))
                total += (long)place.Soldiers + (includeWounded ? place.Wounded : 0);
        foreach (var unit in world.Units.Values)
            if ((forceId is null || unit.OwnerId == forceId) && (!restrictCorps || unit.CorpsId == corpsId))
                total += (long)unit.Soldiers + (includeWounded ? unit.Wounded : 0);
        foreach (var task in world.Simulation.Tasks)
        {
            if (forceId is not null && task.ForceId != forceId || task.Reinforcements is not { } troops) continue;
            // Return tasks use their current destination as OriginSettlementId. Reassignment and corps
            // changes therefore take effect without persisting a second ownership field on the payload.
            if (!restrictCorps || world.Settlements.TryGetValue(task.OriginSettlementId, out var home) &&
                !home.IsDestroyed && home.OwnerId == forceId && home.CorpsId == corpsId)
                total += troops.Soldiers;
        }
        return total;
    }
}
