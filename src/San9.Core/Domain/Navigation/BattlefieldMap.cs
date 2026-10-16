using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum TravelMode { Land, Water }

public sealed record TerrainPassage(int Id, int? LandCost, int? WaterCost)
{
    [JsonRequired] public bool IsWater { get; init; }
    [JsonRequired] public bool StopsRockfall { get; init; }
}

public readonly record struct Footprint(int Width, int Height)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Width > 0 && Height > 0;
}

public enum NavigationAvoidance { None, Friendly, Hostile, Facility }

public readonly record struct MapOccupancy(WorldPosition Position, Footprint Footprint)
{
    public bool Hard { get; init; } = true;
    public NavigationAvoidance Avoidance { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public long Left => (long)Position.X - Footprint.Width / 2;
    [System.Text.Json.Serialization.JsonIgnore] public long Top => (long)Position.Y - Footprint.Height / 2;
    [System.Text.Json.Serialization.JsonIgnore] public long Right => Left + Footprint.Width;
    [System.Text.Json.Serialization.JsonIgnore] public long Bottom => Top + Footprint.Height;
    public IEnumerable<WorldPosition> Entrances(Footprint footprint)
    {
        var area = this;
        for (int x = (int)area.Left - (footprint.Width - 1) / 2; x < area.Right + footprint.Width / 2; x++)
            yield return new(x, (int)area.Top - (footprint.Height + 1) / 2);
        for (int y = (int)area.Top - (footprint.Height - 1) / 2; y < area.Bottom + footprint.Height / 2; y++)
            yield return new((int)area.Right + footprint.Width / 2, y);
        for (int x = (int)area.Left - (footprint.Width - 1) / 2; x < area.Right + footprint.Width / 2; x++)
            yield return new(x, (int)area.Bottom + footprint.Height / 2);
        for (int y = (int)area.Top - (footprint.Height - 1) / 2; y < area.Bottom + footprint.Height / 2; y++)
            yield return new((int)area.Left - (footprint.Width + 1) / 2, y);
    }
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

public sealed record MovementRoute(bool Reachable, ImmutableArray<WorldPosition> Steps, long Cost)
{
    internal ImmutableArray<MapCell> Cells { get; init; } = [];
    internal RouteTerrainCost TerrainCost { get; init; }
    internal WorldPosition? Lookahead { get; init; }
}
public sealed record DailyMovement(WorldPosition Position, bool Reachable, bool Arrived, int Spent);
public enum RouteReachability { Reachable, Unreachable, Incomplete }
public readonly record struct RouteTerrainCost(long Land, long Water)
{
    // A shoreline footprint pays the maximum after speed weighting, not the terrain
    // that happened to be most expensive in the neutral planning map.
    public ImmutableArray<(int Land, int Water)> MixedSteps { get; init; }
    public static RouteTerrainCost operator +(RouteTerrainCost a, RouteTerrainCost b) => new(a.Land + b.Land, a.Water + b.Water)
    {
        MixedSteps = a.MixedSteps.IsDefaultOrEmpty ? b.MixedSteps :
            b.MixedSteps.IsDefaultOrEmpty ? a.MixedSteps : a.MixedSteps.AddRange(b.MixedSteps)
    };
    public static RouteTerrainCost operator *(RouteTerrainCost a, int count) => new(a.Land * count, a.Water * count)
    {
        MixedSteps = a.MixedSteps.IsDefaultOrEmpty ? default :
            Enumerable.Range(0, count).SelectMany(_ => a.MixedSteps).ToImmutableArray()
    };
}
public readonly record struct RouteCostEstimate(RouteReachability Status, long Cost = 0)
{
    public ImmutableArray<MapCell> Cells { get; init; } = [];
    public RouteTerrainCost TerrainCost { get; init; }
}

/// <summary>Immutable domain topology. Neither textures nor source-format identities determine passage.</summary>
public sealed class BattlefieldMap
{
    private readonly (TerrainPassage Terrain, bool Blocked)[] cells;
    private readonly ImmutableDictionary<int, TerrainPassage> terrains;
    private readonly TravelTopology? travel;
    private readonly BattlefieldMap? sourceMap;
    private readonly (int Minimum, int Maximum) landCosts, waterCosts;
    private readonly ConcurrentDictionary<TravelTopology, BattlefieldMap> travelMaps = new();
    private BattlefieldMap(BattlefieldMap source, TravelTopology profile)
    {
        Width = source.Width; Height = source.Height; FineWidth = source.FineWidth; FineHeight = source.FineHeight;
        cells = source.cells; terrains = source.terrains;
        travel = profile;
        sourceMap = source;
        landCosts = waterCosts = CostBounds(TravelMode.Land);
    }

    internal BattlefieldMap ForTravel(FormationRule land, FormationRule naval, int budget) =>
        travelMaps.GetOrAdd(new(land.Footprint, naval.Footprint, budget / land.MovementBudget, budget / naval.MovementBudget),
            profile => new(this, profile));

    /// <summary>Shared terrain geometry for strategic estimates; execution retains its actual speed ratio.</summary>
    public BattlefieldMap PlanningMap()
    {
        if (travel is not { } profile) return this;
        var source = sourceMap!;
        return source.travelMaps.GetOrAdd(new(profile.LandShape, profile.WaterShape, 1, 1), p => new(source, p));
    }

    public long Reweight(RouteTerrainCost cost)
    {
        int land = travel?.LandScale ?? 1, water = travel?.WaterScale ?? 1;
        long total = checked(cost.Land * land + cost.Water * water);
        if (!cost.MixedSteps.IsDefaultOrEmpty)
            foreach (var step in cost.MixedSteps)
                total = checked(total + Math.Max((long)step.Land * land, (long)step.Water * water));
        return total;
    }

    internal RouteTerrainCost PassageComponents(WorldPosition position, Footprint footprint, TravelMode mode, bool coarse)
    {
        var area = new MapOccupancy(position, ShapeAt(position, footprint));
        int left = coarse ? position.X / 5 : (int)area.Left / 5;
        int right = coarse ? left : (int)(area.Right - 1) / 5;
        int top = coarse ? position.Y / 5 : (int)area.Top / 5;
        int bottom = coarse ? top : (int)(area.Bottom - 1) / 5;
        int landCost = 0, waterCost = 0;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int index = y * Width + x;
            int value = CellPassageCost(index, mode)!.Value;
            bool water = travel is null ? mode == TravelMode.Water : cells[index].Terrain.IsWater;
            int scale = travel is null ? 1 : water ? travel.Value.WaterScale : travel.Value.LandScale;
            if (water) waterCost = Math.Max(waterCost, value / scale);
            else landCost = Math.Max(landCost, value / scale);
        }
        return landCost > 0 && waterCost > 0
            ? new(0, 0) { MixedSteps = [(landCost, waterCost)] } : new(landCost, waterCost);
    }
    internal IEnumerable<WorldPosition> Entrances(MapOccupancy place, Footprint fallback) =>
        (travel is not { } profile ? new[] { fallback } : new[] { profile.LandShape, profile.WaterShape })
            .Distinct().SelectMany(shape => place.Entrances(shape).Where(p => p.X >= 0 && p.Y >= 0 && p.X < FineWidth && p.Y < FineHeight && ShapeAt(p, fallback) == shape)).Distinct();

    internal Footprint ShapeAt(WorldPosition position, Footprint fallback) =>
        travel is not { } profile ? fallback : IsWater(position) ? profile.WaterShape : profile.LandShape;

    public BattlefieldMap(int width, int height, IEnumerable<int> terrainIds, IEnumerable<TerrainPassage> terrainRules,
        IEnumerable<MapCell>? blockedCells = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        FineWidth = checked(width * MapCoordinates.UnitsPerCell);
        FineHeight = checked(height * MapCoordinates.UnitsPerCell);
        var ids = terrainIds.ToArray();
        terrains = terrainRules.ToImmutableDictionary(t => t.Id);
        var blocked = (blockedCells ?? []).ToHashSet();
        if (ids.Length != checked(width * height) || terrains.Values.Any(t =>
                t.Id < 0 || t.LandCost is <= 0 || t.WaterCost is <= 0) || ids.Any(id => !terrains.ContainsKey(id)) ||
            blocked.Any(c => c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= height))
            throw new ArgumentException("Invalid battlefield terrain catalog or cell references.");
        cells = ids.Select((id, index) => (terrains[id], blocked.Contains(new(index % width, index / width)))).ToArray();
        landCosts = CostBounds(TravelMode.Land);
        waterCosts = CostBounds(TravelMode.Water);
    }

    internal int MaximumStepCost(TravelMode mode) => (mode == TravelMode.Land ? landCosts : waterCosts).Maximum;

    // Terrain and travel scaling are immutable. Bounds belong to topology construction, not every search/day.
    private (int Minimum, int Maximum) CostBounds(TravelMode mode)
    {
        int minimum = int.MaxValue, maximum = 1;
        bool passable = false;
        foreach (var terrain in terrains.Values)
        {
            int? cost = travel is not { } profile ? (mode == TravelMode.Land ? terrain.LandCost : terrain.WaterCost) :
                checked(terrain.IsWater ? terrain.WaterCost * profile.WaterScale : terrain.LandCost * profile.LandScale);
            if (cost is not int value) continue;
            passable = true;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return (passable ? minimum : 0, maximum);
    }

    public int Width { get; }
    public int Height { get; }
    public int FineWidth { get; }
    public int FineHeight { get; }
    public NavigationStep Navigate(WorldPosition start, MapOccupancy target, bool intoRange, Footprint footprint,
        TravelMode mode, IReadOnlyList<MapOccupancy> occupancy, int budget,
        int maximumRange = 1, Func<WorldPosition, int>? range = null, bool rectangularReach = false,
        NavigationWorkBudget? work = null, NavigationLookahead? lookahead = null, int lookaheadCells = 25)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentOutOfRangeException.ThrowIfLessThan(lookaheadCells, 6);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRange);
        if (!footprint.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        if (start.X < 0 || start.Y < 0 || start.X >= FineWidth || start.Y >= FineHeight ||
            target.Position.X < 0 || target.Position.Y < 0 || target.Position.X >= FineWidth || target.Position.Y >= FineHeight)
            return new(start, 0, NavigationStatus.Unreachable);
        var goal = intoRange ? RangeGoal(target, footprint, range ?? (_ => maximumRange), rectangularReach, maximumRange) :
            (Arrived: (Func<WorldPosition, bool>)(p => p == target.Position),
             Distance: (Func<WorldPosition, long>)(p => Math.Abs((long)p.X - target.Position.X) + Math.Abs((long)p.Y - target.Position.Y)),
             Bounds: (PathGoalBounds?)new(target.Position.X, target.Position.Y, target.Position.X, target.Position.Y));
        if (goal.Arrived(start)) return new(start, 0, NavigationStatus.Arrived);
        if (budget == 0) return new(start, 0, NavigationStatus.Waiting);
        var objects = occupancy
            .Select(o => o.Position == target.Position && o.Footprint == target.Footprint ? o with { Avoidance = NavigationAvoidance.None } : o).ToList();
        if (intoRange) objects.Add(target with { Hard = true, Avoidance = NavigationAvoidance.None });
        using var search = BattlefieldPathSearch.Rent(this);
        int minimum = MinimumStepCost(mode);
        if (lookahead is { } stage && stage.Target == target && minimum > 0 &&
            Math.Abs((long)stage.Position.X - start.X) + Math.Abs((long)stage.Position.Y - start.Y) > Math.Max(25L, budget / minimum) &&
            goal.Distance(start) > 25)
        {
            var local = search.Navigate(start, footprint, mode, objects, p => p == stage.Position,
                p => Math.Abs((long)p.X - stage.Position.X) + Math.Abs((long)p.Y - stage.Position.Y),
                stage.Position, null, budget, work, target, lookaheadCells, stop: goal.Arrived);
            if (local.Status == NavigationStatus.Planning) return local;
            if (local.Arrived) return local with { Lookahead = null };
            if (local.Status == NavigationStatus.Moving || local.Remainder > 0)
                return goal.Arrived(local.Position)
                    ? local with { Lookahead = null, Status = NavigationStatus.Arrived }
                    : local with { Lookahead = stage };
            // A blocked stage is disposable; reconsider the real destination in the same daily call.
        }
        return search.Navigate(start, footprint, mode, objects, goal.Arrived, goal.Distance, intoRange ? null : target.Position,
            goal.Bounds, budget, work, target, lookaheadCells);
    }

    internal int MinimumStepCost(TravelMode mode) => (mode == TravelMode.Land ? landCosts : waterCosts).Minimum;

    public bool IsWater(WorldPosition position)
    {
        var cell = MapCoordinates.Cell(position);
        return position.X >= 0 && position.Y >= 0 && cell.X < Width && cell.Y < Height && cells[cell.Y * Width + cell.X].Terrain.IsWater;
    }
    internal bool StopsRockfall(MapCell cell) => cells[cell.Y * Width + cell.X].Terrain.StopsRockfall;

    public DailyMovement Advance(WorldPosition start, WorldPosition destination, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var occupancy = occupied.ToArray();
        using var search = BattlefieldPathSearch.Rent(this);
        return search.Advance(start, footprint, mode, occupancy, p => p == destination,
            p => Math.Abs((long)p.X - destination.X) + Math.Abs((long)p.Y - destination.Y), destination, budget);
    }

    public DailyMovement AdvanceIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget, int range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        return AdvanceIntoRange(start, target, footprint, mode, occupied, budget, _ => range, maximumRange: range);
    }

    /// <param name="maximumRange">Optional upper bound valid at every candidate position; null uses uninformed search.</param>
    public DailyMovement AdvanceIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int budget, Func<WorldPosition, int> range, bool rectangularReach = false, int? maximumRange = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentNullException.ThrowIfNull(range);
        if (maximumRange is <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRange));
        var occupancy = TargetOccupancy(occupied, target);
        var goal = RangeGoal(target, footprint, range, rectangularReach, maximumRange);
        using var search = BattlefieldPathSearch.Rent(this);
        return search.Advance(start, footprint, mode, occupancy, goal.Arrived, goal.Distance, null, budget, goal.Bounds);
    }

    /// <summary>Bounded full reachability; budget exhaustion is never evidence of an unreachable target.</summary>
    public RouteReachability ReachabilityIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, NavigationWorkBudget work, int maximumRange,
        Func<WorldPosition, int>? range = null, bool rectangularReach = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRange);
        if (work.Exhausted) return RouteReachability.Incomplete;
        var goal = RangeGoal(target, footprint, range ?? (_ => maximumRange), rectangularReach, maximumRange);
        using var search = BattlefieldPathSearch.Rent(this);
        return search.Reachable(start, footprint, mode, TargetOccupancy(occupied, target), goal.Arrived, goal.Distance, goal.Bounds, work);
    }

    private static MapOccupancy[] TargetOccupancy(IEnumerable<MapOccupancy> objects, MapOccupancy target) =>
        objects.Where(o => o.Position != target.Position || o.Footprint != target.Footprint)
            .Append(target with { Hard = true, Avoidance = NavigationAvoidance.None }).ToArray();

    private (Func<WorldPosition, bool> Arrived, Func<WorldPosition, long> Distance, PathGoalBounds? Bounds)
        RangeGoal(MapOccupancy target, Footprint footprint, Func<WorldPosition, int> range, bool rectangularReach, int? maximumRange) =>
        (p =>
        {
            // Exact range can depend on elevation, weapons and the opponent. Only nearby nodes need it.
            if (maximumRange is int bound && RangeDistance(p, footprint, target, bound) > 0) return false;
            if (new MapOccupancy(p, ShapeAt(p, footprint)).Overlaps(target)) return false;
            int available = range(p);
            return available > 0 && (rectangularReach ? new MapOccupancy(p, ShapeAt(p, footprint)).IntersectsExpanded(target, available) :
                new MapOccupancy(p, ShapeAt(p, footprint)).DistanceTo(target) <= available);
        }, p => maximumRange is int bound ? RangeDistance(p, footprint, target, bound) : 0,
            maximumRange is int reach ? new PathGoalBounds(
                target.Left - (footprint.Width - footprint.Width / 2) + 1 - reach,
                target.Top - (footprint.Height - footprint.Height / 2) + 1 - reach,
                target.Right + footprint.Width / 2 - 1 + reach,
                target.Bottom + footprint.Height / 2 - 1 + reach) : null);

    private static long RangeDistance(WorldPosition p, Footprint footprint, MapOccupancy target, int bound)
    {
        // Use the maximum travel shape so a larger shape beyond the shore cannot invalidate the lower bound.
        var area = new MapOccupancy(p, footprint);
        long x = Math.Max(0, Math.Max(target.Left - area.Right + 1, area.Left - target.Right + 1));
        long y = Math.Max(0, Math.Max(target.Top - area.Bottom + 1, area.Top - target.Bottom + 1));
        // Both reach predicates are bounded by this rectangle; use its Manhattan distance.
        return Math.Max(0, x - bound) + Math.Max(0, y - bound);
    }

    public int? PassageCost(WorldPosition position, Footprint footprint, TravelMode mode,
        IReadOnlyList<MapOccupancy> occupancy)
    {
        if (!footprint.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        var area = new MapOccupancy(position, ShapeAt(position, footprint));
        foreach (var other in occupancy)
            if (other.Hard && area.Overlaps(other)) return null;
        return StaticPassageCost(position, footprint, mode);
    }

    internal int? StaticPassageCost(WorldPosition position, Footprint footprint, TravelMode mode)
    {
        var area = new MapOccupancy(position, ShapeAt(position, footprint));
        if (area.Left < 0 || area.Top < 0 || area.Right > FineWidth || area.Bottom > FineHeight) return null;
        int cost = 0;
        int lastX = (int)((area.Right - 1) / MapCoordinates.UnitsPerCell);
        int lastY = (int)((area.Bottom - 1) / MapCoordinates.UnitsPerCell);
        for (int y = (int)area.Top / MapCoordinates.UnitsPerCell; y <= lastY; y++)
        for (int x = (int)area.Left / MapCoordinates.UnitsPerCell; x <= lastX; x++)
        {
            int? step = CellPassageCost(y * Width + x, mode);
            if (step is null) return null;
            cost = Math.Max(cost, step.Value);
        }
        return cost;
    }

    internal int? CoarsePassageCost(WorldPosition position, TravelMode mode) =>
        CellPassageCost(position.Y / MapCoordinates.UnitsPerCell * Width + position.X / MapCoordinates.UnitsPerCell, mode);

    private int? CellPassageCost(int index, TravelMode mode)
    {
        var cell = cells[index];
        if (cell.Blocked) return null;
        var terrain = cell.Terrain;
        int? step = travel is null ? (mode == TravelMode.Land ? terrain.LandCost : terrain.WaterCost) :
            terrain.IsWater ? terrain.WaterCost : terrain.LandCost;
        if (step is int raw && travel is { } profile)
            step = checked(raw * (terrain.IsWater ? profile.WaterScale : profile.LandScale));
        return step;
    }

    public MovementRoute FindRoute(WorldPosition start, WorldPosition destination, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied)
    {
        var occupancy = occupied.ToArray();
        return Search(start, footprint, mode, occupancy, p => p == destination, destination);
    }

    /// <summary>Strategic coarse route estimate. Avoidance selects the route but does not add travel time.
    /// Interior cell samples do not guarantee passage for the moving footprint.</summary>
    public RouteCostEstimate EstimateTravelCost(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IReadOnlyList<MapOccupancy> occupied, NavigationWorkBudget work,
        bool intoRange = true, int range = 1, bool rectangularReach = false, bool includeCells = false) =>
        EstimateRouteCostCore(start, target, footprint, mode, occupied, work, intoRange, range, rectangularReach, true, includeCells);

    /// <summary>One bounded path-cost search for planning, without materializing or replaying daily steps.</summary>
    public RouteCostEstimate EstimateRouteCost(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IReadOnlyList<MapOccupancy> occupied, NavigationWorkBudget work,
        bool intoRange = true, int range = 1, bool rectangularReach = false) =>
        EstimateRouteCostCore(start, target, footprint, mode, occupied, work, intoRange, range, rectangularReach, false);

    private RouteCostEstimate EstimateRouteCostCore(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IReadOnlyList<MapOccupancy> occupied, NavigationWorkBudget work,
        bool intoRange, int range, bool rectangularReach, bool forecast, bool includeCells = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        if (work.Exhausted) return new(RouteReachability.Incomplete);
        if (start.X < 0 || start.Y < 0 || start.X >= FineWidth || start.Y >= FineHeight ||
            target.Position.X < 0 || target.Position.Y < 0 || target.Position.X >= FineWidth || target.Position.Y >= FineHeight)
            return new(RouteReachability.Unreachable);
        var goal = intoRange ? RangeGoal(target, footprint, _ => range, rectangularReach, range) :
            (Arrived: (Func<WorldPosition, bool>)(p => p == target.Position),
             Distance: (Func<WorldPosition, long>)(p => Math.Abs((long)p.X - target.Position.X) + Math.Abs((long)p.Y - target.Position.Y)),
             Bounds: (PathGoalBounds?)new(target.Position.X, target.Position.Y, target.Position.X, target.Position.Y));
        if (goal.Arrived(start)) return new(RouteReachability.Reachable) { Cells = includeCells ? [MapCoordinates.Cell(start)] : [] };
        var objects = intoRange ? TargetOccupancy(occupied, target) : occupied;
        using var search = BattlefieldPathSearch.Rent(this);
        var route = search.Search(start, footprint, mode, objects, goal.Arrived, goal.Distance,
            intoRange ? null : target.Position, goal.Bounds, work, stepLimit: 0, forecast: forecast, includeCells: includeCells);
        return work.Exhausted ? new(RouteReachability.Incomplete) :
            route.Reachable ? new(RouteReachability.Reachable, route.Cost) { TerrainCost = route.TerrainCost, Cells = route.Cells } : new(RouteReachability.Unreachable);
    }

    /// <summary>Full presentation route to a legal position adjacent to an occupied target.</summary>
    public MovementRoute FindRouteIntoRange(WorldPosition start, MapOccupancy target, Footprint footprint,
        TravelMode mode, IEnumerable<MapOccupancy> occupied, int range = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        var goal = RangeGoal(target, footprint, _ => range, false, range);
        using var search = BattlefieldPathSearch.Rent(this);
        return search.Search(start, footprint, mode, TargetOccupancy(occupied, target), goal.Arrived, goal.Distance, null, goal.Bounds);
    }

    private MovementRoute Search(WorldPosition start, Footprint footprint, TravelMode mode,
        IReadOnlyList<MapOccupancy> occupancy, Func<WorldPosition, bool> arrived, WorldPosition? destination = null,
        Func<WorldPosition, long>? lowerBound = null)
    {
        using var search = BattlefieldPathSearch.Rent(this);
        return search.Search(start, footprint, mode, occupancy, arrived, lowerBound ?? (p => destination is { } end ?
            Math.Abs((long)p.X - end.X) + Math.Abs((long)p.Y - end.Y) : 0), destination);
    }

    private readonly record struct TravelTopology(Footprint LandShape, Footprint WaterShape, int LandScale, int WaterScale);
}
