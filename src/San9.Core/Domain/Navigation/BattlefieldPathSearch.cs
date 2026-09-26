using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Exclusively leased thread-local workspace; idle workspaces never retain maps.</summary>
internal sealed class BattlefieldPathSearch : IDisposable
{
    [ThreadStatic] private static BattlefieldPathSearch? available;
    private BattlefieldMap? currentMap;
    private BattlefieldMap map => currentMap!;
    private int[] seen = [], previous = [], occupied = [], closed = [];
    private long[] distances = [];
    private int generation;
    private readonly PriorityQueue<(int Node, long Cost), (long Estimate, long Remaining, long Sequence)> pending = new();

    internal static BattlefieldPathSearch Rent(BattlefieldMap map)
    {
        var workspace = available ?? new();
        available = null;
        workspace.currentMap = map;
        return workspace;
    }

    public void Dispose()
    {
        currentMap = null;
        pending.Clear();
        // Nested callbacks may rent another workspace. Keep only the largest idle one.
        if (available is null || available.seen.Length < seen.Length) available = this;
    }

    internal RouteReachability Reachable(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, PathGoalBounds? bounds, NavigationWorkBudget work) =>
        Search(start, shape, mode, objects, arrived, lowerBound, null, bounds, RouteReachability.Unreachable,
            (_, _, _, _) => RouteReachability.Reachable, work: work, incomplete: () => RouteReachability.Incomplete);

    internal MovementRoute Search(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination) =>
        Search(start, shape, mode, objects, arrived, lowerBound, destination, null, new MovementRoute(false, [], 0),
            (source, goal, cost, _) => new(true, Trace(source, goal), cost));

    internal DailyMovement Advance(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination, int budget, PathGoalBounds? goalBounds = null)
    {
        return Search(start, shape, mode, objects, arrived, lowerBound, destination, goalBounds, new DailyMovement(start, false, false, 0),
            (source, goal, _, costs) =>
            {
                // Project the selected path onto today's budget without materializing a full route.
                long spent = 0;
                for (int p = goal; p != source; p = previous[p]) spent += costs[p];
                int position = goal;
                while (spent > budget)
                {
                    spent -= costs[position];
                    position = previous[position];
                }
                return new(Position(position), true, position == goal, (int)spent);
            });
    }

    private T Search<T>(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination, PathGoalBounds? goalBounds, T unreachable,
        Func<int, int, long, int[], T> complete, Func<T>? limited = null, int horizon = 128, int maximumWork = 8192, int minimumStageDistance = 32,
        NavigationWorkBudget? work = null, Func<T>? blocked = null, Func<T>? incomplete = null)
    {
        var diagnostics = PathSearchDiagnostics.Current;
        if (diagnostics is not null) diagnostics.Queries++;
        long expanded = 0;
        if (!shape.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
        foreach (var area in objects)
            if (!area.Footprint.IsValid || area.Left < 0 || area.Top < 0 || area.Right > map.FineWidth || area.Bottom > map.FineHeight)
                throw new ArgumentException("Occupancy lies outside battlefield.");
        if (!Inside(start) || destination is { } end && !Inside(end)) return unreachable;
        var topology = map.Topologies.Get(map, shape, mode);
        int count = topology.Costs.Length;
        if (seen.Length < count)
        {
            seen = new int[count]; previous = new int[count]; occupied = new int[count]; closed = new int[count]; distances = new long[count];
            if (diagnostics is not null) diagnostics.WorkspaceGrowths++;
        }
        if (generation >= int.MaxValue - 64) { Array.Clear(seen); Array.Clear(occupied); Array.Clear(closed); generation = 0; }
        int stamp = ++generation;
        int occupancyStamp = stamp;
        pending.Clear();
        foreach (var area in objects)
        {
            int left = (int)Math.Max(0, area.Left - (shape.Width - shape.Width / 2) + 1);
            int right = (int)Math.Min(map.FineWidth - 1, area.Right + shape.Width / 2 - 1);
            int top = (int)Math.Max(0, area.Top - (shape.Height - shape.Height / 2) + 1);
            int bottom = (int)Math.Min(map.FineHeight - 1, area.Bottom + shape.Height / 2 - 1);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                    if (area.Overlaps(new(new(x, y), map.ShapeAt(new(x, y), shape)))) occupied[y * map.FineWidth + x] = stamp;
        }
        int source = Index(start);
        if (!Passable(source)) return limited is not null && topology.Costs[source] > 0 ? (blocked ?? limited)() : unreachable;
        if (destination is { } target)
        {
            int goal = Index(target);
            if (topology.Costs[goal] <= 0 || !topology.Regions.Connected(source, goal)) return unreachable;
            if (!Passable(goal)) return limited is null ? unreachable : (blocked ?? limited)();
        }
        if (arrived(start)) return complete(source, source, 0, topology.Costs);
        var regionGraph = topology.Regions;
        var bounds = goalBounds ?? (destination is { } point ? new PathGoalBounds(point.X, point.Y, point.X, point.Y) : null);
        BattlefieldRegions.Guide? guide = null;
        bool[]? corridor = null;
        if (bounds is { } goalRegion)
        {
            var goals = new List<int>();
            CollectGoals(ignoreOccupancy: false);
            // Remote temporary occupancy must not stop travel toward an otherwise legal approach.
            if (goals.Count == 0 && limited is not null && lowerBound(start) >= 256)
                CollectGoals(ignoreOccupancy: true);
            void CollectGoals(bool ignoreOccupancy)
            {
                for (int y = (int)Math.Clamp(goalRegion.Top, 0, map.FineHeight); y <= Math.Min(goalRegion.Bottom, map.FineHeight - 1); y++)
                for (int x = (int)Math.Clamp(goalRegion.Left, 0, map.FineWidth); x <= Math.Min(goalRegion.Right, map.FineWidth - 1); x++)
                {
                    int node = y * map.FineWidth + x;
                    if ((ignoreOccupancy ? topology.Costs[node] <= 0 : !Passable(node)) || !arrived(new(x, y))) continue;
                    int region = regionGraph.Regions[node];
                    if (!goals.Contains(region)) goals.Add(region);
                }
            }
            if (goals.Count == 0) return limited is null ? unreachable : (blocked ?? limited)();
            goals.Sort();
            guide = regionGraph.ToGoals(goals.ToArray());
            int startRegion = regionGraph.Regions[source];
            if (guide.Costs[startRegion] == long.MaxValue) return unreachable;
            if (lowerBound(start) >= (limited is null ? 128 : 256)) corridor = regionGraph.Corridor(startRegion, guide);
            else guide = null;
        }
        bool restrictCorridor = corridor is not null;
        int stage = -1;
        if (limited is not null && guide is not null)
        {
            stage = regionGraph.Regions[source];
            for (int i = 0; guide.Next[stage] >= 0 && (i < 8 || regionGraph.DistanceToTile(start, stage) < minimumStageDistance); i++)
                stage = guide.Next[stage];
            if (stage == regionGraph.Regions[source] || guide.Next[stage] < 0) stage = -1;
        }
        while (true)
        {
            bool clipped = false;
            stamp = ++generation;
            pending.Clear();
            long sequence = 0;
            seen[source] = stamp; distances[source] = 0;
            long initial = Estimate(source);
            pending.Enqueue((source, 0), (initial, initial, sequence++));
            while (pending.TryDequeue(out var item, out _))
            {
                int current = item.Node;
                if (distances[current] != item.Cost || closed[current] == stamp) continue;
                closed[current] = stamp;
                if (work is not null && !work.TryExpand()) return incomplete is not null ? incomplete() : limited is not null ? limited() : unreachable;
                expanded++;
                if (diagnostics is not null)
                {
                    diagnostics.Expanded++;
                    diagnostics.MaximumExpanded = Math.Max(diagnostics.MaximumExpanded, expanded);
                }
                if (arrived(Position(current)) || stage >= 0 && guide!.Costs[regionGraph.Regions[current]] <= guide.Costs[stage])
                    return complete(source, current, item.Cost, topology.Costs);
                if (limited is not null && expanded >= maximumWork) return limited();
                Visit(current - map.FineWidth);
                if (current % map.FineWidth < map.FineWidth - 1) Visit(current + 1);
                Visit(current + map.FineWidth);
                if (current % map.FineWidth > 0) Visit(current - 1);
                void Visit(int next)
                {
                    if (next < 0 || next >= count || !Passable(next) || closed[next] == stamp) return;
                    if (limited is not null && (Math.Abs(next % map.FineWidth - start.X) > horizon ||
                        Math.Abs(next / map.FineWidth - start.Y) > horizon)) { clipped = true; return; }
                    int region = regionGraph.Regions[next];
                    if (corridor is not null && (guide!.Costs[region] == long.MaxValue || restrictCorridor && !corridor[region])) return;
                    long total = checked(item.Cost + topology.Costs[next]);
                    if (seen[next] == stamp && distances[next] <= total) return;
                    seen[next] = stamp; distances[next] = total; previous[next] = current;
                    long h = Estimate(next);
                    pending.Enqueue((next, total), (checked(total + (guide is null || limited is not null ? h : h * 2)), h, sequence++));
                }
            }
            // A corridor is a preference, not a reachability boundary. Eventually search the whole component.
            if (!restrictCorridor) return limited is null ? unreachable : !clipped && blocked is not null ? blocked() : limited();
            restrictCorridor = false;
            if (diagnostics is not null) diagnostics.CorridorRetries++;
        }
        bool Passable(int p) => topology.Costs[p] > 0 && occupied[p] != occupancyStamp;
        long Estimate(int p) => Math.Max(checked(lowerBound(Position(p)) * topology.Minimum),
            guide?.Costs[regionGraph.Regions[p]] ?? 0);
    }

    internal NavigationStep Navigate(WorldPosition start, WorldPosition target, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived, Func<WorldPosition, long> lowerBound,
        WorldPosition? destination, PathGoalBounds bounds, int budget, MovementPlan? plan, NavigationWorkBudget? work,
        BlockedNavigation? blockedGoal)
    {
        if (arrived(start)) return new(start, 0, NavigationStatus.Arrived, null);
        if (budget == 0) return new(start, 0, NavigationStatus.Waiting, plan);
        var topology = map.Topologies.Get(map, shape, mode);
        if (topology.Minimum == 0) return new(start, 0, NavigationStatus.Unreachable, null);
        var steps = plan?.Steps ?? [];
        if (steps.IsEmpty && blockedGoal is not null && plan?.BlockedSearch is { } blockedSearch && blockedSearch.Matches(blockedGoal) && plan.Blockers.SequenceEqual(objects))
            return new(start, 0, NavigationStatus.Waiting, plan);
        if (steps.IsEmpty && plan is { BlockedSearch: null, RetryDelay: > 0 } && plan.Blockers.SequenceEqual(objects))
            return new(start, 0, plan.FailureStatus, plan with { RetryDelay = plan.RetryDelay - 1 });
        bool Legal(WorldPosition p) => Inside(p) && topology.Costs[Index(p)] > 0 &&
            !objects.Any(o => o.Overlaps(new(p, map.ShapeAt(p, shape))));
        if (steps.IsEmpty || !Legal(steps[0]))
        {
            int failures = plan?.BlockedAttempts ?? 0;
            int horizon = NavigationHorizon(failures, budget / topology.Minimum);
            var segment = Search(start, shape, mode, objects, arrived, lowerBound, destination, bounds,
                new PathSegment([], NavigationStatus.Unreachable),
                (source, goal, _, _) => new PathSegment(Trace(source, goal), NavigationStatus.Moving), () => new([], NavigationStatus.Planning), horizon,
                (int)Math.Min(int.MaxValue, Math.Min((long)map.FineWidth * map.FineHeight * 2, 8192L << Math.Min(failures, 16))),
                (int)Math.Min(int.MaxValue, (long)budget / topology.Minimum + 16), work, () => new([], NavigationStatus.Waiting, true));
            if (segment.Status != NavigationStatus.Moving)
                return new(start, 0, segment.Status, new MovementPlan(target, shape, mode, [], Math.Min(failures + 1, 16))
                { Blockers = objects.ToImmutableArray(), RetryDelay = segment.FullyBlocked && blockedGoal is not null ? 0 : Math.Min(3, failures / 2), FailureStatus = segment.Status,
                    BlockedSearch = segment.FullyBlocked ? blockedGoal : null });
            steps = segment.Steps;
        }
        else if (PathSearchDiagnostics.Current is { } diagnostics) diagnostics.ReusedPlans++;
        int spent = 0, taken = 0, remainder = 0;
        var position = start;
        foreach (var next in steps)
        {
            if (!Legal(next)) break;
            int cost = topology.Costs[Index(next)];
            if (cost > budget - spent) { remainder = budget - spent; break; }
            position = next; spent += cost; taken++;
            if (arrived(position)) return new(position, spent, NavigationStatus.Arrived, null);
        }
        return new(position, spent, spent > 0 ? NavigationStatus.Moving : NavigationStatus.Waiting,
            new(target, shape, mode, steps.RemoveRange(0, taken))) { Remainder = remainder };
    }

    internal static int NavigationHorizon(int failures, int maximumSteps) =>
        (int)Math.Min(int.MaxValue, Math.Max(256L, (long)maximumSteps + 64) << Math.Min(failures, 16));

    private ImmutableArray<WorldPosition> Trace(int source, int goal)
    {
        var steps = new List<WorldPosition>();
        for (int node = goal; node != source; node = previous[node]) steps.Add(Position(node));
        steps.Reverse();
        return steps.ToImmutableArray();
    }

    private bool Inside(WorldPosition p) => p.X >= 0 && p.Y >= 0 && p.X < map.FineWidth && p.Y < map.FineHeight;
    private int Index(WorldPosition p) => p.Y * map.FineWidth + p.X;
    private WorldPosition Position(int index) => new(index % map.FineWidth, index / map.FineWidth);
}

internal readonly record struct PathGoalBounds(long Left, long Top, long Right, long Bottom);
