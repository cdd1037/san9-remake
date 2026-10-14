using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed record CampaignRoute(ArrivalEstimate Arrival, WorldPosition? Waypoint = null);

/// <summary>Compare direct travel with one useful friendly harbor approach, sharing geometry across formations.</summary>
internal sealed class CampaignRoutePlanner(ImmutableArray<MapOccupancy> harbors, ArrivalProjectionCache routes)
{
    private readonly Dictionary<(BattlefieldMap Map, Footprint Shape, WorldPosition Start, MapOccupancy Target), WorldPosition?> transfers = [];

    internal CampaignRoute Choose(NavigationArrivalInput input, bool fasterOnWater, AllocationWork work)
    {
        var direct = routes.Evaluate(input, work, sharedTerrain: true);
        var result = new CampaignRoute(direct);
        if (!fasterOnWater || direct.Status != ArrivalEstimateStatus.Known || work.Remaining == 0) return result;
        var key = (input.Travel.Map.PlanningMap(), input.Travel.Shape, input.Start, input.Target);
        if (!transfers.TryGetValue(key, out var transfer))
        {
            // This planner belongs to one frozen campaign observation: harbor ownership and occupancy cannot change.
            transfer = FindTransfer(input, work);
            // Budget-limited enumeration is not a completed negative result.
            if (work.Remaining > 0) transfers[key] = transfer;
        }
        if (transfer is not { } waypoint) return result;
        var via = routes.Evaluate(input with { Waypoint = waypoint }, work, sharedTerrain: true);
        // Equal estimates retain the simpler direct order. A waypoint expresses passage through a point,
        // not a promise that either leg stays entirely on water.
        return via.Status == ArrivalEstimateStatus.Known && via.Days < direct.Days ? new(via, waypoint) : result;
    }

    private WorldPosition? FindTransfer(NavigationArrivalInput input, AllocationWork work)
    {
        var travel = input.Travel;
        long direct = Distance(input.Start, input.Target.Position);
        long best = long.MaxValue;
        WorldPosition? result = null;
        foreach (var harbor in harbors)
        {
            if (!work.Spend()) return null;
            if (harbor == input.Target || harbor.Overlaps(new(input.Start, travel.Shape))) continue;
            foreach (var point in travel.Entrances(harbor))
            {
                if (!work.Spend()) return null;
                if (point == input.Start || !travel.Map.IsWater(point)) continue;
                long approach = Distance(input.Start, point), remaining = Distance(point, input.Target.Position);
                // Require forward progress and a transfer no farther away than the destination.
                if (approach >= direct || remaining >= direct || approach + remaining >= best) continue;
                if (travel.Map.PassageCost(point, travel.Shape, travel.Mode, input.Occupancy) is null) continue;
                best = approach + remaining;
                result = point;
            }
        }
        return result;
    }

    private static long Distance(WorldPosition a, WorldPosition b) => Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Y - b.Y);
}
