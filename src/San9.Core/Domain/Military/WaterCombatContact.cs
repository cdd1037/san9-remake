using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed partial class MilitaryRulebook
{
    // Only immutable terrain and building geometry enter this cache. Replaced/moved buildings use a new key.
    private readonly ConcurrentDictionary<(WorldPosition Position, Footprint Footprint), ImmutableArray<WorldPosition>> waterEdges = new();

    public bool CanAttackTarget(OrdinaryAttack? attack, MapOccupancy target, bool targetIsSettlement) =>
        attack is not null && (!attack.RequiresWaterContact || (targetIsSettlement
            ? BuildingWaterEdges(target).Length > 0 : NavigableWater(target.Position)));

    private ImmutableArray<WorldPosition> BuildingWaterEdges(MapOccupancy target) =>
        waterEdges.GetOrAdd((target.Position, target.Footprint), key => WaterEdges(new(key.Position, key.Footprint)));

    private ImmutableArray<WorldPosition> WaterEdges(MapOccupancy target) =>
        target.Entrances(new(1, 1)).Where(NavigableWater).ToImmutableArray();

    private bool NavigableWater(WorldPosition position) =>
        position.X >= 0 && position.Y >= 0 && position.X < Map.FineWidth && position.Y < Map.FineHeight && Map.IsWater(position) &&
        Map.StaticPassageCost(position, TravelMode.Water) is not null;

    private bool HasWaterContact(MapOccupancy source, MapOccupancy target, int range, bool targetIsSettlement,
        Func<WorldPosition, bool>? blocksContact)
    {
        if (!NavigableWater(source.Position) || source.Overlaps(target) || !source.IntersectsExpanded(target, range)) return false;
        var edges = targetIsSettlement ? BuildingWaterEdges(target) : WaterEdges(target);
        foreach (var edge in edges)
        {
            var start = new WorldPosition((int)Math.Clamp(edge.X, source.Left, source.Right - 1),
                (int)Math.Clamp(edge.Y, source.Top, source.Bottom - 1));
            // An edge is already one fine unit outside the target; keep the configured rectangular reach.
            if (Math.Abs((long)edge.X - start.X) >= range || Math.Abs((long)edge.Y - start.Y) >= range) continue;
            if (ClearWaterLine(start, edge, target, blocksContact)) return true;
        }
        return false;
    }

    private bool ClearWaterLine(WorldPosition start, WorldPosition end, MapOccupancy target, Func<WorldPosition, bool>? blocksContact)
    {
        bool Clear(WorldPosition point) => NavigableWater(point) && !target.Contains(point) && blocksContact?.Invoke(point) != true;
        int x = start.X, y = start.Y;
        int dx = Math.Abs(end.X - x), dy = Math.Abs(end.Y - y);
        int sx = Math.Sign(end.X - x), sy = Math.Sign(end.Y - y), error = dx - dy;
        while (true)
        {
            if (!Clear(new(x, y))) return false;
            if (x == end.X && y == end.Y) return true;
            int twice = error * 2, nextX = x, nextY = y;
            if (twice > -dy) { error -= dy; nextX += sx; }
            if (twice < dx) { error += dx; nextY += sy; }
            // Diagonal contact cannot cut across a land/blocked corner.
            if (nextX != x && nextY != y && (!Clear(new(nextX, y)) || !Clear(new(x, nextY)))) return false;
            x = nextX; y = nextY;
        }
    }
}
