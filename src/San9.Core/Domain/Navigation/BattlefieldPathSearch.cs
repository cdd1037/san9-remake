using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Four-way coarse A* with bounded fine endpoint connectors. No fine world graph or saved plans.</summary>
internal sealed class BattlefieldPathSearch : IDisposable
{
    [ThreadStatic] private static BattlefieldPathSearch? available;
    private BattlefieldMap map = null!;
    private readonly long[] endpointCosts = new long[225];
    private readonly long[] endpointPassage = new long[225];
    private readonly int[] endpointParents = new int[225];
    private readonly PriorityQueue<int, (long Total, long Line, long Cost, int Node)> endpoints = new();
    private long[] distance = [];
    private long[] travelCosts = [];
    private bool forecast;
    private int[] previous = [];
    private readonly PriorityQueue<int, (long Total, long Line, long Remaining, int Node)> pending = new();
    private Footprint shape;
    private TravelMode mode;
    private Func<WorldPosition, bool> arrived = null!;
    private Func<WorldPosition, long> lowerBound = null!;
    private WorldPosition origin, target;
    private NavigationWorkBudget? work;
    private List<MapOccupancy>?[] objectCells = [];
    private readonly List<int> occupiedCells = [];
    private int minimum;
    private bool materializePaths;
    private long expanded;

    internal static BattlefieldPathSearch Rent(BattlefieldMap map)
    {
        var search = available ?? new();
        available = null;
        search.map = map;
        return search;
    }
    public void Dispose()
    {
        map = null!; arrived = null!; lowerBound = null!; work = null;
        pending.Clear(); endpoints.Clear(); ClearObjects();
        if (available is null || available.distance.Length < distance.Length) available = this;
    }

    internal RouteReachability Reachable(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, PathGoalBounds? bounds, NavigationWorkBudget work)
    {
        var route = Search(start, shape, mode, objects, arrived, lowerBound, null, bounds, work, stepLimit: 0);
        return work.Exhausted ? RouteReachability.Incomplete : route.Reachable ? RouteReachability.Reachable : RouteReachability.Unreachable;
    }

    internal DailyMovement Advance(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination, int budget, PathGoalBounds? bounds = null)
    {
        var step = Navigate(start, shape, mode, objects, arrived, lowerBound, destination, bounds, budget, null);
        return new(step.Position, step.Status is NavigationStatus.Moving or NavigationStatus.Arrived || step.Remainder > 0 || budget == 0 && Search(start, shape, mode, objects, arrived, lowerBound, destination, bounds).Reachable, step.Arrived, step.Spent);
    }

    internal NavigationStep Navigate(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived, Func<WorldPosition, long> lowerBound,
        WorldPosition? destination, PathGoalBounds? bounds, int budget, NavigationWorkBudget? work,
        MapOccupancy? targetArea = null, int lookaheadCells = 25, Func<WorldPosition, bool>? stop = null)
    {
        int minimumCost = map.MinimumStepCost(mode);
        // One unaffordable step is needed to preserve the movement remainder; the distant route is not materialized.
        int stepLimit = minimumCost > 0 ? (int)Math.Min(int.MaxValue, (long)budget / minimumCost + 1) : 0;
        var route = Search(start, shape, mode, objects, arrived, lowerBound, destination, bounds, work, stepLimit, lookaheadCells);
        var lookahead = route.Lookahead is { } point && targetArea is { } area ? new NavigationLookahead(point, area) : null;
        if (work?.Exhausted == true) return new(start, 0, NavigationStatus.Planning);
        if (!route.Reachable)
        {
            bool dynamic = objects.Any(o => o.Hard);
            bool otherwiseReachable = dynamic && Search(start, shape, mode, objects.Where(o => !o.Hard).ToArray(),
                arrived, lowerBound, destination, bounds, work, stepLimit: 0).Reachable;
            return new(start, 0, work?.Exhausted == true ? NavigationStatus.Planning :
                otherwiseReachable ? NavigationStatus.Waiting : NavigationStatus.Unreachable);
        }
        int spent = 0;
        var reached = stop ?? arrived;
        var position = start;
        var accepted = ImmutableArray.CreateBuilder<WorldPosition>();
        foreach (var next in route.Steps)
        {
            int? cost = map.PassageCost(next, shape, mode, objects);
            if (cost is null) break;
            if (cost > budget - spent)
                return new(position, spent, spent > 0 ? NavigationStatus.Moving : NavigationStatus.Waiting)
                    { Remainder = budget - spent, Steps = accepted.ToImmutable(), Lookahead = lookahead };
            spent += cost.Value; position = next; accepted.Add(next);
            if (reached(position)) break;
        }
        return new(position, spent, reached(position) ? NavigationStatus.Arrived : spent > 0 ? NavigationStatus.Moving : NavigationStatus.Waiting)
            { Steps = accepted.ToImmutable(), Lookahead = reached(position) ? null : lookahead };
    }

    internal MovementRoute Search(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination, PathGoalBounds? bounds = null, NavigationWorkBudget? work = null,
        int stepLimit = int.MaxValue, int lookaheadCells = 25, bool forecast = false)
    {
        if (!shape.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        foreach (var area in objects)
            if (!area.Footprint.IsValid || area.Left < 0 || area.Top < 0 || area.Right > map.FineWidth || area.Bottom > map.FineHeight)
                throw new ArgumentException("Occupancy lies outside battlefield.");
        this.forecast = forecast;
        this.shape = shape; this.mode = mode; this.arrived = arrived;
        this.lowerBound = lowerBound; this.work = work; origin = start;
        target = destination ?? (bounds is { } b ? new((int)((b.Left + b.Right) / 2), (int)((b.Top + b.Bottom) / 2)) : start);
        minimum = map.MinimumStepCost(mode); expanded = 0;
        // Route costs retain the execution graph; strategic forecasts use coarse edges and direct endpoint links.
        materializePaths = stepLimit != 0;
        if (PathSearchDiagnostics.Current is { } diagnostics) diagnostics.Queries++;
        var failure = new MovementRoute(false, [], 0);
        ClearObjects();
        int count = checked(map.Width * map.Height);
        if (objectCells.Length < count) objectCells = new List<MapOccupancy>?[count];
        foreach (var o in objects)
        {
            int left = (int)Math.Max(0, o.Left - shape.Width) / 5;
            int right = (int)Math.Min(map.FineWidth - 1L, o.Right + shape.Width) / 5;
            int top = (int)Math.Max(0, o.Top - shape.Height) / 5;
            int bottom = (int)Math.Min(map.FineHeight - 1L, o.Bottom + shape.Height) / 5;
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int index = y * map.Width + x;
                var bucket = objectCells[index] ??= [];
                if (bucket.Count == 0) occupiedCells.Add(index);
                bucket.Add(o);
            }
        }
        if (minimum == 0 || Cost(start) is null) return failure;
        if (arrived(start)) return new(true, [], 0);
        if (destination is { } end && Cost(end) is null) return failure;
        if (distance.Length < count)
        {
            distance = new long[count]; previous = new int[count];
            if (PathSearchDiagnostics.Current is { } d) { d.WorkspaceGrowths++; d.WorkspaceNodes = Math.Max(d.WorkspaceNodes, count); }
        }
        if (forecast && travelCosts.Length < count) travelCosts = new long[count];
        Array.Fill(distance, long.MaxValue, 0, count); pending.Clear();
        bool centered = start == MapCoordinates.Center(MapCoordinates.Cell(start));
        var source = centered
            ? new Connections(new() { [start.Y / 5 * map.Width + start.X / 5] = new(0, []) },
                lowerBound(start) <= 14 ? Connect(start, collectCenters: false).Goal : null)
            : Connect(start, collectCenters: true);
        if (work?.Exhausted == true) return failure;
        long best = source.Goal?.Cost ?? long.MaxValue;
        long bestTravel = source.Goal?.TravelCost ?? 0;
        ImmutableArray<WorldPosition> tail = source.Goal?.Steps ?? [];
        int finish = -1;
        foreach (var (node, link) in source.Centers)
        {
            distance[node] = link.Cost;
            if (forecast) travelCosts[node] = link.TravelCost;
            if (materializePaths) previous[node] = -1;
            Enqueue(node);
        }
        while (pending.TryDequeue(out int current, out var priority))
        {
            var position = Position(current);
            long h = checked(lowerBound(position) * minimum);
            if (priority.Total != distance[current] + h) continue;
            if (priority.Total >= best) break;
            if (!Expand(false)) return failure;
            // Only endpoint windows can perform fine search. Their maximum Manhattan reach is 14.
            if (lowerBound(position) <= 14)
            {
                var connector = arrived(position) ? new Link(0, []) : Connect(position, collectCenters: false, best - distance[current]).Goal;
                if (work?.Exhausted == true) return failure;
                if (connector is { } link && distance[current] + link.Cost < best)
                { best = distance[current] + link.Cost; finish = current; tail = link.Steps;
                    if (forecast) bestTravel = travelCosts[current] + link.TravelCost; }
            }
            int x = current % map.Width, y = current / map.Width;
            if (y > 0) Visit(current - map.Width);
            if (x + 1 < map.Width) Visit(current + 1);
            if (y + 1 < map.Height) Visit(current + map.Width);
            if (x > 0) Visit(current - 1);
            void Visit(int next)
            {
                long cost = 0, movement = 0;
                var end = Position(next);
                int dx = Math.Sign(end.X - position.X), dy = Math.Sign(end.Y - position.Y);
                var step = position;
                if (forecast)
                {
                    // Strategic estimate: one sample per coarse cell; endpoints retain legal footprint checks.
                    var value = Cost(end, true, out int terrainCost);
                    if (value is null) return;
                    cost = value.Value * MapCoordinates.UnitsPerCell;
                    movement = terrainCost * (long)MapCoordinates.UnitsPerCell;
                }
                else for (int i = 0; i < MapCoordinates.UnitsPerCell; i++)
                {
                    step = new(step.X + dx, step.Y + dy);
                    var value = Cost(step);
                    if (value is null) return;
                    cost = checked(cost + value.Value);
                }
                long total = checked(distance[current] + cost);
                if (total >= distance[next]) return;
                distance[next] = total;
                if (forecast) travelCosts[next] = travelCosts[current] + movement;
                if (materializePaths) previous[next] = current;
                Enqueue(next);
            }
        }
        if (best == long.MaxValue) return failure;
        if (stepLimit == 0) return new(true, [], forecast ? bestTravel : best);
        var result = ImmutableArray.CreateBuilder<WorldPosition>();
        WorldPosition? lookahead = null;
        if (finish >= 0)
        {
            var nodes = new List<int>();
            for (int node = finish; node >= 0; node = previous[node]) nodes.Add(node);
            nodes.Reverse();
            if (nodes.Count > 6) lookahead = Position(nodes[Math.Min(lookaheadCells, nodes.Count - 1)]);
            foreach (var step in source.Centers[nodes[0]].Steps)
            {
                if (result.Count == stepLimit) break;
                result.Add(step);
            }
            for (int i = 1; i < nodes.Count && result.Count < stepLimit; i++)
            {
                var point = Position(nodes[i - 1]);
                var nextCenter = Position(nodes[i]);
                int dx = Math.Sign(nextCenter.X - point.X), dy = Math.Sign(nextCenter.Y - point.Y);
                while (point != nextCenter && result.Count < stepLimit)
                {
                    point = new(point.X + dx, point.Y + dy);
                    result.Add(point);
                }
            }
        }
        foreach (var step in tail)
        {
            if (result.Count == stepLimit) break;
            result.Add(step);
        }
        return new(true, result.ToImmutable(), best) { Lookahead = lookahead };
    }

    private void Enqueue(int node)
    {
        var p = Position(node);
        long h = checked(lowerBound(p) * minimum);
        pending.Enqueue(node, (checked(distance[node] + h), LineDistance(p), h, node));
    }
    private long LineDistance(WorldPosition p) => Math.Abs(checked(((long)target.X - origin.X) * ((long)p.Y - origin.Y) -
        ((long)target.Y - origin.Y) * ((long)p.X - origin.X)));
    private WorldPosition Position(int node) => MapCoordinates.Center(new(node % map.Width, node / map.Width));
    private long? Cost(WorldPosition p) => Cost(p, false, out _);
    private long? Cost(WorldPosition p, bool coarse, out int terrainCost)
    {
        var baseCost = coarse ? map.CoarsePassageCost(p, mode) : map.StaticPassageCost(p, shape, mode);
        terrainCost = baseCost.GetValueOrDefault();
        if (baseCost is null) return null;
        var area = new MapOccupancy(p, coarse ? new(1, 1) : map.ShapeAt(p, shape));
        bool friendly = false, hostile = false, facility = false;
        var nearby = objectCells[p.Y / 5 * map.Width + p.X / 5];
        if (nearby is null) return baseCost.Value;
        foreach (var o in nearby)
        {
            if (o.Hard && (coarse ? new MapOccupancy(p, new(5, 5)) : area).Overlaps(o)) return null;
            if (o.Avoidance == NavigationAvoidance.Facility)
            {
                var cell = MapCoordinates.Center(MapCoordinates.Cell(p));
                facility |= cell.X >= o.Left && cell.X < o.Right && cell.Y >= o.Top && cell.Y < o.Bottom;
            }
            else if (area.Overlaps(o))
            { friendly |= o.Avoidance == NavigationAvoidance.Friendly; hostile |= o.Avoidance == NavigationAvoidance.Hostile; }
        }
        return checked((long)baseCost.Value + (friendly ? Math.Max(1, minimum / 2) : 0) + (hostile ? minimum * 4L : 0) + (facility ? minimum : 0));
    }
    private bool Expand(bool local)
    {
        if (work is not null && !work.TryExpand()) return false;
        expanded++;
        if (PathSearchDiagnostics.Current is { } d)
        {
            d.Expanded++; if (local) d.EndpointExpanded++;
            d.MaximumExpanded = Math.Max(d.MaximumExpanded, expanded);
        }
        return true;
    }

    private sealed record Link(long Cost, ImmutableArray<WorldPosition> Steps, long TravelCost = 0);
    private sealed record Connections(Dictionary<int, Link> Centers, Link? Goal);
    private void ClearObjects()
    {
        foreach (int cell in occupiedCells) objectCells[cell]!.Clear();
        occupiedCells.Clear();
    }

    // Only the two axis orders are sampled; this is a bounded estimate, not local pathfinding.
    private Connections ConnectForecast(WorldPosition start, bool collectCenters)
    {
        var cell = MapCoordinates.Cell(start);
        var centers = new Dictionary<int, Link>();
        Link? goal = null;
        if (!Expand(true)) return new(centers, goal);
        int left = Math.Max(0, (cell.X - 1) * 5), top = Math.Max(0, (cell.Y - 1) * 5);
        int right = Math.Min(map.FineWidth, (cell.X + 2) * 5), bottom = Math.Min(map.FineHeight, (cell.Y + 2) * 5);
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            var p = new WorldPosition(x, y);
            bool isGoal = arrived(p);
            bool isCenter = collectCenters && x % 5 == 2 && y % 5 == 2;
            if (!isGoal && !isCenter) continue;
            var link = Direct(p, true);
            var other = Direct(p, false);
            if (other is not null && (link is null || other.Cost < link.Cost)) link = other;
            if (link is null) continue;
            if (isCenter) centers.Add(y / 5 * map.Width + x / 5, link);
            if (isGoal && (goal is null || link.Cost < goal.Cost)) goal = link;
        }
        return new(centers, goal);

        Link? Direct(WorldPosition end, bool horizontalFirst)
        {
            var p = start;
            long routing = 0, movement = 0;
            while (p != end)
            {
                bool horizontal = horizontalFirst ? p.X != end.X : p.Y == end.Y;
                p = horizontal ? new(p.X + Math.Sign(end.X - p.X), p.Y) : new(p.X, p.Y + Math.Sign(end.Y - p.Y));
                if (Cost(p, false, out int terrainCost) is not { } cost) return null;
                routing += cost;
                movement += terrainCost;
            }
            return new(routing, [], movement);
        }
    }

    private Connections Connect(WorldPosition start, bool collectCenters, long maximumCost = long.MaxValue)
    {
        if (forecast) return ConnectForecast(start, collectCenters);
        var cell = MapCoordinates.Cell(start);
        int left = Math.Max(0, (cell.X - 1) * 5), top = Math.Max(0, (cell.Y - 1) * 5);
        int right = Math.Min(map.FineWidth, (cell.X + 2) * 5), bottom = Math.Min(map.FineHeight, (cell.Y + 2) * 5);
        int width = right - left, count = width * (bottom - top);
        var costs = endpointCosts; Array.Fill(costs, long.MaxValue, 0, count);
        // This window belongs to one query: -2 means unevaluated, -1 means impassable.
        Array.Fill(endpointPassage, -2L, 0, count);
        var parents = endpointParents;
        var queue = endpoints; queue.Clear();
        int first = Index(start); costs[first] = 0;
        if (materializePaths) parents[first] = -1;
        queue.Enqueue(first, (collectCenters ? 0 : lowerBound(start) * minimum, LineDistance(start), 0, first));
        var centers = new Dictionary<int, Link>();
        Link? goal = null;
        while (queue.TryDequeue(out int current, out var priority))
        {
            if (costs[current] != priority.Cost) continue;
            if (!collectCenters && priority.Total >= maximumCost) break;
            if (!Expand(true)) break;
            var p = Point(current);
            if (arrived(p))
            {
                goal ??= new(costs[current], materializePaths ? Trace(current) : []);
                if (!collectCenters) break;
            }
            if (collectCenters && p.X % 5 == 2 && p.Y % 5 == 2)
                centers.Add(p.Y / 5 * map.Width + p.X / 5, new(costs[current], materializePaths ? Trace(current) : []));
            if (p.Y > top) Visit(current - width);
            if (p.X + 1 < right) Visit(current + 1);
            if (p.Y + 1 < bottom) Visit(current + width);
            if (p.X > left) Visit(current - 1);
            void Visit(int next)
            {
                long value = endpointPassage[next];
                if (value == -2) endpointPassage[next] = value = Cost(Point(next)) ?? -1;
                if (value < 0) return;
                long total = checked(costs[current] + value);
                if (total >= costs[next]) return;
                costs[next] = total;
                if (materializePaths) parents[next] = current;
                queue.Enqueue(next, (checked(total + (collectCenters ? 0 : lowerBound(Point(next)) * minimum)), LineDistance(Point(next)), total, next));
            }
        }
        return new(centers, goal);
        int Index(WorldPosition p) => (p.Y - top) * width + p.X - left;
        WorldPosition Point(int node) => new(node % width + left, node / width + top);
        ImmutableArray<WorldPosition> Trace(int node)
        {
            var steps = new List<WorldPosition>();
            for (; node != first; node = parents[node]) steps.Add(Point(node));
            steps.Reverse(); return steps.ToImmutableArray();
        }
    }
}

internal readonly record struct PathGoalBounds(long Left, long Top, long Right, long Bottom);
