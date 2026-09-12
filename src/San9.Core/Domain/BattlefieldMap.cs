using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum TravelMode { Land, Water }

public sealed record TerrainPassage(int Id, int? LandCost, int? WaterCost);

public readonly record struct Footprint(int Width, int Height)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Width > 0 && Height > 0;
}

public readonly record struct MapOccupancy(WorldPosition Position, Footprint Footprint)
{
    public long Left => (long)Position.X - Footprint.Width / 2;
    public long Top => (long)Position.Y - Footprint.Height / 2;
    public long Right => Left + Footprint.Width;
    public long Bottom => Top + Footprint.Height;
    public bool Overlaps(MapOccupancy other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    public long DistanceTo(MapOccupancy other) =>
        Math.Max(0, Math.Max(other.Left - Right + 1, Left - other.Right + 1)) +
        Math.Max(0, Math.Max(other.Top - Bottom + 1, Top - other.Bottom + 1));
}

public sealed record MovementRoute(bool Reachable, ImmutableArray<WorldPosition> Steps, long Cost);
public sealed record DailyMovement(WorldPosition Position, bool Reachable, bool Arrived, int Spent);

/// <summary>Immutable domain topology. Neither textures nor source-format identities determine passage.</summary>
public sealed class BattlefieldMap
{
    private readonly ImmutableArray<int> cells;
    private readonly ImmutableDictionary<int, TerrainPassage> terrains;
    private readonly ImmutableHashSet<MapCell> blocked;

    public BattlefieldMap(int width, int height, IEnumerable<int> terrainIds, IEnumerable<TerrainPassage> terrainRules,
        IEnumerable<MapCell>? blockedCells = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        FineWidth = checked(width * MapCoordinates.UnitsPerCell);
        FineHeight = checked(height * MapCoordinates.UnitsPerCell);
        cells = terrainIds.ToImmutableArray();
        terrains = terrainRules.ToImmutableDictionary(t => t.Id);
        blocked = (blockedCells ?? []).ToImmutableHashSet();
        if (cells.Length != checked(width * height) || terrains.Values.Any(t =>
                t.Id < 0 || t.LandCost is <= 0 || t.WaterCost is <= 0) || cells.Any(id => !terrains.ContainsKey(id)) ||
            blocked.Any(c => c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= height))
            throw new ArgumentException("Invalid battlefield terrain catalog or cell references.");
    }

    public int Width { get; }
    public int Height { get; }
    public int FineWidth { get; }
    public int FineHeight { get; }

    public DailyMovement Advance(WorldPosition start, WorldPosition destination, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var occupancy = occupied.ToArray();
        var route = FindRoute(start, destination, footprint, mode, occupancy);
        return Consume(start, route, footprint, mode, occupancy, budget);
    }

    public DailyMovement AdvanceIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget, int range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        var occupancy = occupied.Append(target).ToArray();
        var route = Search(start, footprint, mode, occupancy, p =>
            new MapOccupancy(p, footprint).DistanceTo(target) <= range);
        return Consume(start, route, footprint, mode, occupancy, budget);
    }

    private DailyMovement Consume(WorldPosition start, MovementRoute route, Footprint footprint,
        TravelMode mode, IReadOnlyList<MapOccupancy> occupancy, int budget)
    {
        if (!route.Reachable) return new(start, false, false, 0);
        WorldPosition position = start;
        int spent = 0;
        foreach (var step in route.Steps)
        {
            int cost = PassageCost(step, footprint, mode, occupancy)!.Value;
            if (cost > budget - spent) break;
            position = step;
            spent += cost;
        }
        return new(position, true, route.Steps.Length == 0 || position == route.Steps[^1], spent);
    }

    public int? PassageCost(WorldPosition position, Footprint footprint, TravelMode mode,
        IReadOnlyList<MapOccupancy> occupancy) => PassageCost(position, footprint, mode, area => occupancy.Any(area.Overlaps));

    private int? PassageCost(WorldPosition position, Footprint footprint, TravelMode mode,
        Func<MapOccupancy, bool> occupied)
    {
        if (!footprint.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        var area = new MapOccupancy(position, footprint);
        if (area.Left < 0 || area.Top < 0 || area.Right > FineWidth || area.Bottom > FineHeight) return null;
        if (occupied(area)) return null;
        int cost = 0;
        int lastX = (int)((area.Right - 1) / MapCoordinates.UnitsPerCell);
        int lastY = (int)((area.Bottom - 1) / MapCoordinates.UnitsPerCell);
        for (int y = (int)area.Top / MapCoordinates.UnitsPerCell; y <= lastY; y++)
        for (int x = (int)area.Left / MapCoordinates.UnitsPerCell; x <= lastX; x++)
        {
            if (blocked.Contains(new(x, y))) return null;
            var terrain = terrains[cells[y * Width + x]];
            int? step = mode == TravelMode.Land ? terrain.LandCost : terrain.WaterCost;
            if (step is null) return null;
            cost = Math.Max(cost, step.Value);
        }
        return cost;
    }

    public MovementRoute FindRoute(WorldPosition start, WorldPosition destination, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied)
    {
        var occupancy = occupied.ToArray();
        return Search(start, footprint, mode, occupancy, p => p == destination, destination);
    }

    private MovementRoute Search(WorldPosition start, Footprint footprint, TravelMode mode,
        IReadOnlyList<MapOccupancy> occupancy, Func<WorldPosition, bool> arrived, WorldPosition? destination = null)
    {
        if (occupancy.Any(o => !o.Footprint.IsValid || o.Position.X < 0 || o.Position.Y < 0))
            throw new ArgumentException("Invalid battlefield occupancy.");
        var blocks = IndexOccupancy(occupancy);
        if (PassageCost(start, footprint, mode, blocks) is null ||
            destination is WorldPosition end && PassageCost(end, footprint, mode, blocks) is null) return new(false, [], 0);
        if (arrived(start)) return new(true, [], 0);

        var distances = new Dictionary<WorldPosition, long> { [start] = 0 };
        var previous = new Dictionary<WorldPosition, WorldPosition>();
        var pending = new PriorityQueue<WorldPosition, (long Cost, long Sequence)>();
        long sequence = 0;
        pending.Enqueue(start, (0, sequence++));
        while (pending.TryDequeue(out var current, out var priority))
        {
            if (distances[current] != priority.Cost) continue;
            if (arrived(current))
            {
                var steps = new List<WorldPosition>();
                for (var p = current; p != start; p = previous[p]) steps.Add(p);
                steps.Reverse();
                return new(true, steps.ToImmutableArray(), priority.Cost);
            }
            foreach (var next in Neighbors(current))
            {
                int? cost = PassageCost(next, footprint, mode, blocks);
                if (cost is null) continue;
                long total = checked(priority.Cost + cost.Value);
                if (distances.TryGetValue(next, out long known) && known <= total) continue;
                distances[next] = total;
                previous[next] = current;
                pending.Enqueue(next, (total, sequence++));
            }
        }
        return new(false, [], 0);
    }

    // A per-search index of the current snapshot; never retained across simulation days.
    private Func<MapOccupancy, bool> IndexOccupancy(IEnumerable<MapOccupancy> occupancy)
    {
        var buckets = new Dictionary<MapCell, List<MapOccupancy>>();
        foreach (var area in occupancy)
        {
            if (area.Left < 0 || area.Top < 0 || area.Right > FineWidth || area.Bottom > FineHeight)
                throw new ArgumentException("Occupancy lies outside battlefield.");
            foreach (var cell in CoveredCells(area))
            {
                if (!buckets.TryGetValue(cell, out var list)) buckets[cell] = list = [];
                list.Add(area);
            }
        }
        return area => CoveredCells(area).Any(cell => buckets.TryGetValue(cell, out var list) && list.Any(area.Overlaps));
    }

    private static IEnumerable<MapCell> CoveredCells(MapOccupancy area)
    {
        for (int y = (int)area.Top / MapCoordinates.UnitsPerCell; y <= (area.Bottom - 1) / MapCoordinates.UnitsPerCell; y++)
        for (int x = (int)area.Left / MapCoordinates.UnitsPerCell; x <= (area.Right - 1) / MapCoordinates.UnitsPerCell; x++)
            yield return new(x, y);
    }

    private static IEnumerable<WorldPosition> Neighbors(WorldPosition position)
    {
        yield return new(position.X, position.Y - 1);
        yield return new(position.X + 1, position.Y);
        yield return new(position.X, position.Y + 1);
        yield return new(position.X - 1, position.Y);
    }
}
