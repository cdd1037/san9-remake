using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Terrain-only routes shared for one session operation. Dynamic world decisions remain with each caller.</summary>
internal sealed class OriginalAiTerrainRoutes(SettlementTransitNetwork? initialTransitNetwork = null)
{
    private readonly record struct Key(BattlefieldMap Map, WorldPosition Start, MapOccupancy Target,
        Footprint Shape, TravelMode Mode, bool IntoRange);

    private readonly Dictionary<Key, MovementRoute> routes = [];
    private SettlementTransitNetwork? transitNetwork = initialTransitNetwork;
    private IReadOnlyList<SettlementState>? transitSettlements;
    internal SettlementTransitNetwork? CachedTransitNetwork => transitNetwork;

    internal SettlementTransitNetwork TransitNetwork(MilitaryRulebook rules, WorldSnapshot world)
    {
        if (transitNetwork is null || !transitNetwork.UsesRules(rules))
            transitNetwork = SettlementTransitNetwork.Build(rules, world.Settlements);
        else if (!ReferenceEquals(transitSettlements, world.Settlements))
            transitNetwork = transitNetwork.Update(world.Settlements);
        transitSettlements = world.Settlements;
        return transitNetwork;
    }

    internal MovementRoute FindRoute(BattlefieldMap map, WorldPosition start, WorldPosition destination,
        Footprint shape, TravelMode mode) => Get(new(map, start, new(destination, new(1, 1)), shape, mode, false));

    internal MovementRoute FindRouteIntoRange(BattlefieldMap map, WorldPosition start, MapOccupancy target,
        Footprint shape, TravelMode mode) => Get(new(map, start, target, shape, mode, true));

    private MovementRoute Get(Key key)
    {
        if (routes.TryGetValue(key, out var route)) return route;
        // Map identity includes immutable terrain/travel topology. Endpoints and shape
        // are explicit; no ownership, orders, occupants or query budgets enter this cache.
        route = key.IntoRange
            ? key.Map.FindRouteIntoRange(key.Start, key.Target, key.Shape, key.Mode, [])
            : key.Map.FindRoute(key.Start, key.Target.Position, key.Shape, key.Mode, []);
        routes.Add(key, route);
        return route;
    }
}
