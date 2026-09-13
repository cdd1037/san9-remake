using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum TravelMode { Land, Water }

public sealed record TerrainPassage(int Id, int? LandCost, int? WaterCost)
{
    [System.Text.Json.Serialization.JsonRequired] public bool IsWater { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool StopsRockfall { get; init; }
}

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
    public bool IntersectsExpanded(MapOccupancy other, int range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range);
        return Left - range < other.Right && other.Left < Right + range &&
            Top - range < other.Bottom && other.Top < Bottom + range;
    }
    public long DistanceTo(MapOccupancy other) =>
        Math.Max(0, Math.Max(other.Left - Right + 1, Left - other.Right + 1)) +
        Math.Max(0, Math.Max(other.Top - Bottom + 1, Top - other.Bottom + 1));
}

public sealed record MovementRoute(bool Reachable, ImmutableArray<WorldPosition> Steps, long Cost);
public sealed record DailyMovement(WorldPosition Position, bool Reachable, bool Arrived, int Spent);

/// <summary>Immutable domain topology. Neither textures nor source-format identities determine passage.</summary>
public sealed class BattlefieldMap
{
    private readonly BattlefieldPathSearch search;
    private readonly ImmutableArray<int> cells;
    private readonly ImmutableDictionary<int, TerrainPassage> terrains;
    private readonly ImmutableHashSet<MapCell> blocked;

    public BattlefieldMap(int width, int height, IEnumerable<int> terrainIds, IEnumerable<TerrainPassage> terrainRules,
        IEnumerable<MapCell>? blockedCells = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        search = new(this);
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
    public bool IsWater(WorldPosition position)
    {
        var cell = MapCoordinates.Cell(position);
        return cell.X < Width && cell.Y < Height && terrains[cells[cell.Y * Width + cell.X]].IsWater;
    }
    internal bool StopsRockfall(MapCell cell) => terrains[cells[cell.Y * Width + cell.X]].StopsRockfall;

    public DailyMovement Advance(WorldPosition start, WorldPosition destination, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget, int? movementStepCost = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var occupancy = occupied.ToArray();
        var route = FindRoute(start, destination, footprint, mode, occupancy);
        return Consume(start, route, footprint, mode, occupancy, budget, movementStepCost);
    }

    public DailyMovement AdvanceIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget, int range, int? movementStepCost = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        return AdvanceIntoRange(start, target, footprint, mode, occupied, budget, _ => range, maximumRange: range, movementStepCost: movementStepCost);
    }

    /// <param name="maximumRange">Optional upper bound valid at every candidate position; null uses uninformed search.</param>
    public DailyMovement AdvanceIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget, Func<WorldPosition, int> range, bool rectangularReach = false, int? maximumRange = null, int? movementStepCost = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentNullException.ThrowIfNull(range);
        if (maximumRange is <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRange));
        var occupancy = occupied.Append(target).ToArray();
        var route = Search(start, footprint, mode, occupancy, p =>
        {
            int available = range(p);
            return available > 0 && (rectangularReach ? new MapOccupancy(p, footprint).IntersectsExpanded(target, available) :
                new MapOccupancy(p, footprint).DistanceTo(target) <= available);
        }, lowerBound: p => maximumRange is int bound ? RangeDistance(p, bound) : 0);
        long RangeDistance(WorldPosition p, int bound)
        {
            var area = new MapOccupancy(p, footprint);
            long x = Math.Max(0, Math.Max(target.Left - area.Right + 1, area.Left - target.Right + 1));
            long y = Math.Max(0, Math.Max(target.Top - area.Bottom + 1, area.Top - target.Bottom + 1));
            // Both reach predicates are bounded by this rectangle; use its Manhattan distance.
            return Math.Max(0, x - bound) + Math.Max(0, y - bound);
        }
        return Consume(start, route, footprint, mode, occupancy, budget, movementStepCost);
    }

    private DailyMovement Consume(WorldPosition start, MovementRoute route, Footprint footprint,
        TravelMode mode, IReadOnlyList<MapOccupancy> occupancy, int budget, int? movementStepCost)
    {
        if (movementStepCost is <= 0) throw new ArgumentOutOfRangeException(nameof(movementStepCost));
        if (!route.Reachable) return new(start, false, false, 0);
        WorldPosition position = start;
        int spent = 0;
        foreach (var step in route.Steps)
        {
            int cost = movementStepCost ?? PassageCost(step, footprint, mode, occupancy)!.Value;
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

    internal int? StaticPassageCost(WorldPosition position, Footprint footprint, TravelMode mode) =>
        PassageCost(position, footprint, mode, static _ => false);

    private MovementRoute Search(WorldPosition start, Footprint footprint, TravelMode mode,
        IReadOnlyList<MapOccupancy> occupancy, Func<WorldPosition, bool> arrived, WorldPosition? destination = null,
        Func<WorldPosition, long>? lowerBound = null) =>
        search.Search(start, footprint, mode, occupancy, arrived, lowerBound ?? (p => destination is { } end ?
            Math.Abs((long)p.X - end.X) + Math.Abs((long)p.Y - end.Y) : 0), destination);
}
