using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Application.Presentation;

namespace San9.Application;

/// <summary>Read-only location candidates, not confirmation that a construction command can be paid or staffed.</summary>
public static class ConstructionLocations
{
    public static ImmutableArray<ConstructionSite> Read(GameSession session, int forceId, int facilityId)
        => Read(session.View, forceId, facilityId);

    public static ImmutableArray<ConstructionSite> Read(StrategySnapshot view, int forceId, int facilityId)
    {
        var world = view.World;
        if (view.MilitaryRules is not { } rules || !rules.Facilities.TryGetValue(facilityId, out var facility) ||
            !world.Forces.Any(f => f.Id == forceId && !f.IsEliminated)) return [];
        var reserved = world.Units.Where(u => u.Order?.Construction is not null).Select(u => u.Order!.Construction!.RegionId).ToHashSet();
        reserved.UnionWith(world.Settlements.Where(s => s.IsDormant).Select(s => s.Id));
        return rules.ConstructionSites.Values.Where(s => !reserved.Contains(s.RegionId) && ConstructionPlacement.IsAvailable(s, facility, rules,
            view.RegionCityIds, world.Settlements, world.Units)).OrderBy(s => s.RegionId)
            .ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X).ToImmutableArray();
    }
}
