using San9.Core.Domain;

namespace San9.Application.AI;

/// <summary>Terrain-only routes shared for one session operation. Dynamic world decisions remain with each caller.</summary>
internal sealed class OriginalAiTerrainRoutes
{
    private readonly record struct Key(BattlefieldMap Map, WorldPosition Start, MapOccupancy Target,
        Footprint Shape, TravelMode Mode, bool IntoRange);

    private readonly Dictionary<Key, MovementRoute> routes = [];

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
