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

public readonly record struct MapOccupancy(WorldPosition Position, Footprint Footprint)
{
    [System.Text.Json.Serialization.JsonIgnore] public long Left => (long)Position.X - Footprint.Width / 2;
    [System.Text.Json.Serialization.JsonIgnore] public long Top => (long)Position.Y - Footprint.Height / 2;
    [System.Text.Json.Serialization.JsonIgnore] public long Right => Left + Footprint.Width;
    [System.Text.Json.Serialization.JsonIgnore] public long Bottom => Top + Footprint.Height;
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
public enum RouteReachability { Reachable, Unreachable, Incomplete }

/// <summary>Immutable domain topology. Neither textures nor source-format identities determine passage.</summary>
public sealed class BattlefieldMap
{
    internal BattlefieldTopologies Topologies { get; }
    private readonly ImmutableArray<int> cells;
    private readonly ImmutableDictionary<int, TerrainPassage> terrains;
    private readonly ImmutableHashSet<MapCell> blocked;
    private readonly TravelTopology? travel;
    private readonly ConcurrentDictionary<TravelTopology, BattlefieldMap> travelMaps = new();

    private BattlefieldMap(BattlefieldMap source, TravelTopology profile)
    {
        Width = source.Width; Height = source.Height; FineWidth = source.FineWidth; FineHeight = source.FineHeight;
        cells = source.cells; terrains = source.terrains; blocked = source.blocked;
        travel = profile;
        Topologies = source.Topologies;
    }

    internal BattlefieldMap ForTravel(FormationRule land, FormationRule naval, int budget) =>
        travelMaps.GetOrAdd(new(land.Footprint, naval.Footprint, budget / land.MovementBudget, budget / naval.MovementBudget),
            profile => new(this, profile));
    internal IEnumerable<WorldPosition> Entrances(BattlefieldPlace place, Footprint fallback) =>
        (travel is not { } profile ? new[] { fallback } : new[] { profile.LandShape, profile.WaterShape })
            .Distinct().SelectMany(shape => place.Entrances(shape).Where(p => p.X >= 0 && p.Y >= 0 && p.X < FineWidth && p.Y < FineHeight && ShapeAt(p, fallback) == shape)).Distinct();

    internal Footprint ShapeAt(WorldPosition position, Footprint fallback) =>
        travel is not { } profile ? fallback : IsWater(position) ? profile.WaterShape : profile.LandShape;

    public BattlefieldMap(int width, int height, IEnumerable<int> terrainIds, IEnumerable<TerrainPassage> terrainRules,
        IEnumerable<MapCell>? blockedCells = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Topologies = new();
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

    internal int MaximumStepCost(TravelMode mode) => Math.Max(1, terrains.Values.Max(t =>
        travel is not { } profile ? ((mode == TravelMode.Land ? t.LandCost : t.WaterCost) ?? 0) :
            checked(((t.IsWater ? t.WaterCost : t.LandCost) ?? 0) * (t.IsWater ? profile.WaterScale : profile.LandScale))));

    public int Width { get; }
    public int Height { get; }
    public int FineWidth { get; }
    public int FineHeight { get; }
    public NavigationStep Navigate(WorldPosition start, MapOccupancy target, bool intoRange, Footprint footprint,
        TravelMode mode, Func<MapOccupancy, IReadOnlyList<MapOccupancy>> occupancy, int budget,
        MovementPlan? plan = null, int maximumRange = 1,
        Func<WorldPosition, int>? range = null, bool rectangularReach = false, NavigationWorkBudget? work = null,
        AttackApproachState? rangeState = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRange);
        if (start.X < 0 || start.Y < 0 || start.X >= FineWidth || start.Y >= FineHeight ||
            target.Position.X < 0 || target.Position.Y < 0 || target.Position.X >= FineWidth || target.Position.Y >= FineHeight)
            return new(start, 0, NavigationStatus.Unreachable, null);
        if (plan is not null && (plan.Shape != footprint || plan.Mode != mode)) plan = null;
        if (plan is not null && plan.Target != target.Position)
        {
            bool reuse = false;
            if (!plan.Steps.IsEmpty && plan.Target.X >= 0 && plan.Target.Y >= 0 && plan.Target.X < FineWidth && plan.Target.Y < FineHeight &&
                Math.Abs((long)start.X - target.Position.X) + Math.Abs((long)start.Y - target.Position.Y) >= 256)
            {
                var regions = Topologies.Get(this, footprint, mode).Regions.Regions;
                int oldRegion = regions[plan.Target.Y * FineWidth + plan.Target.X];
                reuse = oldRegion >= 0 && oldRegion == regions[target.Position.Y * FineWidth + target.Position.X];
            }
            plan = reuse ? plan with { Target = target.Position } : null;
        }
        if (!footprint.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        long Distance(WorldPosition p) => intoRange ? RangeDistance(p, footprint, target, maximumRange) :
            Math.Abs((long)p.X - target.Position.X) + Math.Abs((long)p.Y - target.Position.Y);
        bool Arrived(WorldPosition p)
        {
            if (!intoRange) return p == target.Position;
            if (Distance(p) > 0 || new MapOccupancy(p, ShapeAt(p, footprint)).Overlaps(target)) return false;
            int available = range?.Invoke(p) ?? maximumRange;
            return available > 0 && (rectangularReach ? new MapOccupancy(p, ShapeAt(p, footprint)).IntersectsExpanded(target, available) :
                new MapOccupancy(p, ShapeAt(p, footprint)).DistanceTo(target) <= available);
        }
        if (Arrived(start)) return new(start, 0, NavigationStatus.Arrived, null);
        if (budget == 0) return new(start, 0, NavigationStatus.Waiting, plan);
        var topology = Topologies.Get(this, footprint, mode);
        if (topology.Minimum == 0) return new(start, 0, NavigationStatus.Unreachable, null);
        int radius = BattlefieldPathSearch.NavigationHorizon(plan?.BlockedAttempts ?? 0, budget / topology.Minimum);
        long left = (long)start.X - radius, right = (long)start.X + radius;
        long top = (long)start.Y - radius, bottom = (long)start.Y + radius;
        if (plan is not null)
            foreach (var p in plan.Steps)
            { left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y); }
        left = Math.Max(0, left - footprint.Width); right = Math.Min(FineWidth, right + footprint.Width + 1);
        top = Math.Max(0, top - footprint.Height); bottom = Math.Min(FineHeight, bottom + footprint.Height + 1);
        var area = new MapOccupancy(new((int)(left + (right - left) / 2), (int)(top + (bottom - top) / 2)),
            new((int)(right - left), (int)(bottom - top)));
        var bounds = intoRange ? new PathGoalBounds(target.Left - footprint.Width - maximumRange,
            target.Top - footprint.Height - maximumRange, target.Right + footprint.Width + maximumRange,
            target.Bottom + footprint.Height + maximumRange) :
            new PathGoalBounds(target.Position.X, target.Position.Y, target.Position.X, target.Position.Y);
        var objects = occupancy(area).ToList();
        if (intoRange)
        {
            int goalLeft = (int)Math.Max(0, bounds.Left - footprint.Width);
            int goalTop = (int)Math.Max(0, bounds.Top - footprint.Height);
            int goalRight = (int)Math.Min(FineWidth, bounds.Right + footprint.Width + 1);
            int goalBottom = (int)Math.Min(FineHeight, bounds.Bottom + footprint.Height + 1);
            var goalArea = new MapOccupancy(new(goalLeft + (goalRight - goalLeft) / 2, goalTop + (goalBottom - goalTop) / 2),
                new(goalRight - goalLeft, goalBottom - goalTop));
            foreach (var obstacle in occupancy(goalArea)) if (!objects.Contains(obstacle)) objects.Add(obstacle);
            if (!objects.Contains(target)) objects.Add(target);
        }
        NavigationStep result;
        using (var search = BattlefieldPathSearch.Rent(this))
            result = search.Navigate(start, target.Position, footprint, mode, objects, Arrived, Distance,
                intoRange ? null : target.Position, bounds, budget, plan, work,
                range is null || rangeState is not null ? new(start, target, intoRange, maximumRange, rectangularReach) { Approach = rangeState } : null);
        if (result.Status == NavigationStatus.Moving && result.Spent < budget && result.Plan is { Steps.IsEmpty: true })
        {
            var remainder = Navigate(result.Position, target, intoRange, footprint, mode, occupancy,
                budget - result.Spent, result.Plan, maximumRange, range, rectangularReach, work, rangeState);
            return remainder with { Spent = result.Spent + remainder.Spent,
                Status = remainder.Status == NavigationStatus.Waiting ? NavigationStatus.Moving : remainder.Status };
        }
        return result;
    }
    public bool IsWater(WorldPosition position)
    {
        var cell = MapCoordinates.Cell(position);
        return position.X >= 0 && position.Y >= 0 && cell.X < Width && cell.Y < Height && terrains[cells[cell.Y * Width + cell.X]].IsWater;
    }
    internal bool StopsRockfall(MapCell cell) => terrains[cells[cell.Y * Width + cell.X]].StopsRockfall;

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
        var occupancy = occupied.Append(target).ToArray();
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
        return search.Reachable(start, footprint, mode, occupied.Append(target).ToArray(), goal.Arrived, goal.Distance, goal.Bounds, work);
    }

    private (Func<WorldPosition, bool> Arrived, Func<WorldPosition, long> Distance, PathGoalBounds? Bounds)
        RangeGoal(MapOccupancy target, Footprint footprint, Func<WorldPosition, int> range, bool rectangularReach, int? maximumRange) =>
        (p =>
        {
            // Exact range can depend on elevation, weapons and the opponent. Only nearby nodes need it.
            if (maximumRange is int bound && RangeDistance(p, footprint, target, bound) > 0) return false;
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
        IReadOnlyList<MapOccupancy> occupancy) => PassageCost(position, footprint, mode, area => occupancy.Any(area.Overlaps));

    private int? PassageCost(WorldPosition position, Footprint footprint, TravelMode mode,
        Func<MapOccupancy, bool> occupied)
    {
        if (!footprint.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        var area = new MapOccupancy(position, ShapeAt(position, footprint));
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
            int? step = travel is null ? (mode == TravelMode.Land ? terrain.LandCost : terrain.WaterCost) :
                terrain.IsWater ? terrain.WaterCost : terrain.LandCost;
            if (step is int raw && travel is { } profile)
                step = checked(raw * (terrain.IsWater ? profile.WaterScale : profile.LandScale));
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
        Func<WorldPosition, long>? lowerBound = null)
    {
        using var search = BattlefieldPathSearch.Rent(this);
        return search.Search(start, footprint, mode, occupancy, arrived, lowerBound ?? (p => destination is { } end ?
            Math.Abs((long)p.X - end.X) + Math.Abs((long)p.Y - end.Y) : 0), destination);
    }

    private readonly record struct TravelTopology(Footprint LandShape, Footprint WaterShape, int LandScale, int WaterScale);
}
