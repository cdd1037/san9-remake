using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application;

/// <summary>Read-only location candidates, not confirmation that a construction command can be paid or staffed.</summary>
public static class ConstructionLocations
{
    public static ImmutableArray<ConstructionSite> Read(GameSession session, int forceId, int facilityId)
    {
        var world = session.Snapshot;
        if (session.MilitaryRules is not { } rules || !rules.Facilities.TryGetValue(facilityId, out var facility) ||
            !world.Forces.Any(f => f.Id == forceId && !f.IsEliminated)) return [];
        return rules.ConstructionSites.Values.Where(s => ConstructionPlacement.IsAvailable(forceId, s, facility, rules,
            session.View.RegionCityIds, world.Settlements, world.Units)).OrderBy(s => s.RegionId)
            .ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X).ToImmutableArray();
    }
}
