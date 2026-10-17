using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Reverse coarse cost field, bounded fine cost field, then authoritative footprint collision.</summary>
internal sealed class BattlefieldPathSearch : IDisposable
{
    [ThreadStatic] private static BattlefieldPathSearch? available;
    private BattlefieldMap map = null!;
    private int[] costs = [], fineCosts = [];
    private long[] distance = [], fineDistance = [];
    private readonly PriorityQueue<int, (long Cost, int Node)> queue = new();
    private readonly List<int> route = [];
    private long expanded;

    internal static BattlefieldPathSearch Rent(BattlefieldMap map)
    {
        var search = available ?? new(); available = null; search.map = map; return search;
    }
    public void Dispose()
    {
        map = null!; queue.Clear(); route.Clear();
        if (available is null || available.costs.Length < costs.Length) available = this;
    }

    private void Allocate(int count, bool fine)
    {
        if (fine ? fineCosts.Length >= count : costs.Length >= count) return;
        if (fine) { fineCosts = new int[count]; fineDistance = new long[count]; }
        else { costs = new int[count]; distance = new long[count]; }
        if (PathSearchDiagnostics.Current is { } d) { d.WorkspaceGrowths++; d.WorkspaceNodes = costs.Length + fineCosts.Length; }
    }

    private bool Solve(int[] weights, long[] values, int width, int height, int start, int end, NavigationWorkBudget? work, bool fine,
        Func<int, bool>? isGoal = null)
    {
        int count = checked(width * height);
        Array.Fill(values, long.MaxValue, 0, count); queue.Clear();
        if (weights[start] == 0) return false;
        if (isGoal is null)
        {
            if (weights[end] == 0) return false;
            values[end] = 0; queue.Enqueue(end, (0, end));
        }
        else
            for (int node = 0; node < count; node++)
                if (weights[node] != 0 && isGoal(node))
                { values[node] = 0; queue.Enqueue(node, (0, node)); }
        Span<int> neighbors = stackalloc int[4];
        while (queue.TryDequeue(out int node, out var priority))
        {
            if (priority.Cost != values[node]) continue;
            if (work is not null && !work.TryExpand()) return false;
            expanded++;
            if (PathSearchDiagnostics.Current is { } d) { d.Expanded++; if (fine) d.EndpointExpanded++; }
            if (node == start) return true;
            int n = Neighbors(node, width, height, neighbors);
            for (int i = 0; i < n; i++)
            {
                int next = neighbors[i];
                if (weights[next] == 0) continue;
                long value = priority.Cost + weights[next];
                if (value >= values[next]) continue;
                values[next] = value; queue.Enqueue(next, (value, next));
            }
        }
        return false;
    }

    // Stable cardinal order is part of the sequential equal-cost replacement rule.
    private static int Neighbors(int node, int width, int height, Span<int> result)
    {
        int x = node % width, y = node / width, n = 0;
        if (y > 0) result[n++] = node - width;
        if (x > 0) result[n++] = node - 1;
        if (y + 1 < height) result[n++] = node + width;
        if (x + 1 < width) result[n++] = node + 1;
        return n;
    }

    private bool Coarse(WorldPosition start, WorldPosition end, MapOccupancy sourceArea, MapOccupancy endpoint, TravelMode mode, IReadOnlyList<MapOccupancy> objects, NavigationWorkBudget? work, MapOccupancy? excluded = null)
    {
        route.Clear(); expanded = 0;
        if (PathSearchDiagnostics.Current is { } d) d.Queries++;
        if (!Inside(start) || !Inside(end)) return false;
        int width = map.Width, height = map.Height, count = checked(width * height);
        Allocate(count, false);
        var a = MapCoordinates.Cell(start); var b = MapCoordinates.Cell(end);
        // Immutable terrain templates are shared; only this request's object costs and line corridor are mutable.
        map.CopyPlanningCosts(mode, costs);
        foreach (var obj in objects)
        {
            if (obj.Avoidance is NavigationAvoidance.Friendly or NavigationAvoidance.Hostile)
            {
                if (!Inside(obj.Position)) continue;
                int index = obj.Position.Y / 5 * width + obj.Position.X / 5;
                if (costs[index] > 0) costs[index] = Math.Min(250, costs[index] + (obj.Avoidance == NavigationAvoidance.Friendly ? 5 : 40));
            }
            else if (obj.Hard || obj.Avoidance == NavigationAvoidance.Facility)
            {
                int left = (int)Math.Max(0, obj.Left) / 5, right = (int)Math.Min(map.FineWidth - 1, obj.Right - 1) / 5;
                int top = (int)Math.Max(0, obj.Top) / 5, bottom = (int)Math.Min(map.FineHeight - 1, obj.Bottom - 1) / 5;
                for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
                {
                    int index = y * width + x;
                    if (costs[index] > 0) costs[index] = Math.Min(250, costs[index] + 50);
                }
            }
        }
        long dx = b.X - a.X, dy = b.Y - a.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length > 0)
            for (int y = Math.Max(0, Math.Min(a.Y, b.Y) - 5); y <= Math.Min(height - 1, Math.Max(a.Y, b.Y) + 5); y++)
            for (int x = Math.Max(0, Math.Min(a.X, b.X) - 5); x <= Math.Min(width - 1, Math.Max(a.X, b.X) + 5); x++)
            {
                int discount = 5 - (int)(Math.Abs(dx * (y - a.Y) - dy * (x - a.X)) / length);
                int index = y * width + x;
                if (discount > 0 && costs[index] > 0) costs[index] = Math.Max(1, costs[index] - discount);
            }
        int source = a.Y * width + a.X, target = b.Y * width + b.X;
        if (excluded is { } barrier) CoarseArea(barrier, index => costs[index] = 0);
        Relax(sourceArea, source);
        Relax(endpoint, target);
        void Relax(MapOccupancy area, int selected)
        {
            if (costs[selected] == 0) costs[selected] = 1;
            CoarseArea(area, index => { if (costs[index] == 0) costs[index] = 251; });
        }
        void CoarseArea(MapOccupancy area, Action<int> apply)
        {
            int x0 = (int)Math.Max(0, area.Left) / 5, y0 = (int)Math.Max(0, area.Top) / 5;
            int x1 = (int)Math.Min(map.FineWidth - 1, area.Right - 1) / 5;
            int y1 = (int)Math.Min(map.FineHeight - 1, area.Bottom - 1) / 5;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) apply(y * width + x);
        }
        bool found = Solve(costs, distance, width, height, source, target, work, false);
        if (found)
        {
            int node = source; route.Add(node);
            Span<int> neighbors = stackalloc int[4];
            while (node != target)
            {
                int n = Neighbors(node, width, height, neighbors), best = -1;
                long value = long.MaxValue, line = long.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    int next = neighbors[i];
                    if (costs[next] == 0 || distance[next] >= distance[node]) continue;
                    long deviation = Math.Abs(dx * (next / width - a.Y) - dy * (next % width - a.X));
                    if (distance[next] < value || distance[next] == value && deviation < line)
                    { best = next; value = distance[next]; line = deviation; }
                }
                if (best < 0) { route.Clear(); found = false; break; }
                route.Add(best); node = best;
            }
        }
        if (PathSearchDiagnostics.Current is { } diagnostics) diagnostics.MaximumExpanded = Math.Max(diagnostics.MaximumExpanded, expanded);
        return found;
    }

    private static MapOccupancy Endpoint(WorldPosition position, IReadOnlyList<MapOccupancy> objects)
    {
        foreach (var area in objects)
            if (area.Avoidance is NavigationAvoidance.None or NavigationAvoidance.Facility &&
                position.X >= area.Left && position.X < area.Right && position.Y >= area.Top && position.Y < area.Bottom)
                return area;
        return new(MapCoordinates.Center(MapCoordinates.Cell(position)), new(5, 5));
    }

    // Ring boundary coverage is deliberately asymmetric; do not replace with rectangle-depth distance.
    internal static void RasterizeGradient(MapOccupancy area, int initial, int increment, Action<int, int, int> paint)
    {
        for (int y = (int)area.Top; y <= area.Bottom; y++)
        for (int x = (int)area.Left; x <= area.Right; x++)
        {
            int layer = (int)Math.Min(Math.Min(x - area.Left, area.Right - x), Math.Min(y - area.Top, area.Bottom - y));
            long l = area.Left + layer, r = area.Right - layer, t = area.Top + layer, b = area.Bottom - layer;
            if (l >= r || t >= b) continue;
            bool horizontalEdge = x >= l && x < r && (y == t || y == b);
            bool verticalEdge = y > t && y < b - 1 && (x == l || x == r);
            if (horizontalEdge || verticalEdge) paint(x, y, Math.Min(250, initial + layer * increment));
        }
    }

    private bool Inside(WorldPosition p) => p.X >= 0 && p.Y >= 0 && p.X < map.FineWidth && p.Y < map.FineHeight;
    private WorldPosition Center(int node) => MapCoordinates.Center(new(node % map.Width, node / map.Width));

    internal static void RasterizeObstacle(MapOccupancy obstacle, MapOccupancy window, Footprint shape, int mobility,
        Action<int, int, int> paint)
    {
        if ((!obstacle.Hard && obstacle.Avoidance == NavigationAvoidance.None) || !obstacle.Overlaps(window)) return;
        int left = (int)Math.Max(obstacle.Left, window.Left), top = (int)Math.Max(obstacle.Top, window.Top);
        int width = (int)(Math.Min(obstacle.Right, window.Right) - left);
        int height = (int)(Math.Min(obstacle.Bottom, window.Bottom) - top);
        int padding = obstacle.Hard ? 0 : shape.Width / 2 + 1;
        var field = new MapOccupancy(new(left + width / 2, top + height / 2), new(width + 2 * padding, height + 2 * padding));
        RasterizeGradient(field, obstacle.Hard ? mobility + 75 : 25, obstacle.Hard ? 5 : 10, paint);
    }

    internal NavigationStep Daily(WorldPosition start, MapOccupancy target, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, int budget, Func<WorldPosition, bool> arrived, NavigationWorkBudget? work,
        NavigationLookahead? lookahead, int lookaheadCells, ulong randomSeed, bool continueApproach, int mobility, Func<WorldPosition, bool>? hold = null, MapOccupancy? excluded = null,
        bool requireTargetClearance = false)
    {
        if ((!continueApproach && arrived(start)) || hold?.Invoke(start) == true) return new(start, 0, NavigationStatus.Arrived);
        if (budget == 0) return new(start, 0, arrived(start) ? NavigationStatus.Arrived : NavigationStatus.Waiting);
        var stage = lookahead;
        WorldPosition destination = stage?.Position ?? target.Position;
        var currentShape = map.ShapeAt(start, shape);
        var initialArea = new MapOccupancy(start, currentShape);
        var endpoint = stage is not null ? Endpoint(stage.Position, objects) :
            target.Footprint == new Footprint(1, 1) ? Endpoint(target.Position, objects) : target;
        if (!Coarse(start, destination, initialArea, endpoint, mode, objects, work, excluded))
            return new(start, 0, work?.Exhausted == true ? NavigationStatus.Planning : NavigationStatus.Unreachable) { Lookahead = stage };
        NavigationLookahead? nextStage = route.Count < 6 ? null : stage ?? new(Center(route[Math.Min(lookaheadCells, route.Count) - 1]));
        int prefix = Math.Min(route.Count, Math.Max(3, (mobility + currentShape.Width + 4) / 5));
        WorldPosition fineTarget = prefix == route.Count ? destination : Center(route[prefix - 1]);
        var fineEndpoint = prefix == route.Count ? endpoint : Endpoint(fineTarget, objects);
        int left = (int)initialArea.Left, top = (int)initialArea.Top, right = (int)initialArea.Right, bottom = (int)initialArea.Bottom;
        for (int i = 0; i < prefix; i++)
        {
            int x = route[i] % map.Width * 5, y = route[i] / map.Width * 5;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 5); bottom = Math.Max(bottom, y + 5);
        }
        left = (int)Math.Min(left, fineEndpoint.Left); top = (int)Math.Min(top, fineEndpoint.Top);
        right = (int)Math.Max(right, fineEndpoint.Right); bottom = (int)Math.Max(bottom, fineEndpoint.Bottom);
        int margin = (currentShape.Width + 1) / 2 + 2;
        left = Math.Max(0, left - margin); top = Math.Max(0, top - margin);
        right = Math.Min(map.FineWidth, right + margin); bottom = Math.Min(map.FineHeight, bottom + margin);
        int width = right - left, height = bottom - top;
        Allocate(checked(width * height), true);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
            fineCosts[(y - top) * width + x - left] = map.StaticPassageCost(new(x, y), mode) is null ? 0 : 5;
        var window = new MapOccupancy(new(left + width / 2, top + height / 2), new(width, height));
        foreach (var obj in objects)
            RasterizeObstacle(obj, window, currentShape, mobility, Paint);
        void Paint(int x, int y, int value)
        {
            if (x < left || x >= right || y < top || y >= bottom) return;
            int index = (y - top) * width + x - left;
            if (fineCosts[index] != 0) fineCosts[index] = Math.Max(fineCosts[index], value);
        }
        int source = (start.Y - top) * width + start.X - left;
        int end = (fineTarget.Y - top) * width + fineTarget.X - left;
        RelaxFine(initialArea, source);
        RelaxFine(fineEndpoint, end);
        void RelaxFine(MapOccupancy area, int selected)
        {
            if (fineCosts[selected] == 0) fineCosts[selected] = 1;
            for (int y = (int)Math.Max(top, area.Top); y < Math.Min(bottom, area.Bottom); y++)
            for (int x = (int)Math.Max(left, area.Left); x < Math.Min(right, area.Right); x++)
            {
                int index = (y - top) * width + x - left;
                if (fineCosts[index] == 0) fineCosts[index] = 251;
            }
        }
        // A work site can be traversable without being a valid stopping position. At the final
        // approach, route to legal arrival positions instead of the site's occupied center.
        Func<int, bool>? arrivalGoal = requireTargetClearance && stage is null && prefix == route.Count ? index =>
        {
            var candidate = new WorldPosition(left + index % width, top + index / width);
            return arrived(candidate) && map.PassageCost(candidate, shape, mode, objects) is not null;
        } : null;
        if (!Solve(fineCosts, fineDistance, width, height, source, end, work, true, arrivalGoal))
            return new(start, 0, work?.Exhausted == true ? NavigationStatus.Planning : NavigationStatus.Waiting) { Lookahead = nextStage };
        var random = new NavigationRandom(randomSeed);
        var steps = ImmutableArray.CreateBuilder<WorldPosition>();
        int node = source, spent = 0;
        var position = start;
        MapOccupancy? collision = null;
        Span<int> adjacent = stackalloc int[4];
        while (fineDistance[node] != 0)
        {
            int n = Neighbors(node, width, height, adjacent), best = -1;
            long value = long.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int candidate = adjacent[i];
                if (fineCosts[candidate] == 0 || fineDistance[candidate] >= fineDistance[node]) continue;
                if (fineDistance[candidate] < value || fineDistance[candidate] == value && random.ReplaceEqual())
                { best = candidate; value = fineDistance[candidate]; }
            }
            if (best < 0) break;
            var next = new WorldPosition(left + best % width, top + best / width);
            int? cost = map.StaticPassageCost(next, mode) ?? map.EndpointPassageCost(next, mode);
            var area = new MapOccupancy(next, map.ShapeAt(next, shape));
            // Expansion can introduce overlap. Permit only motion that reduces an existing overlap,
            // never entry into a new hard object; there is no teleport or permanent invalid-start state.
            var previousArea = new MapOccupancy(position, map.ShapeAt(position, shape));
            foreach (var obj in objects)
                if (obj.Hard && area.Overlaps(obj) &&
                    (!initialArea.Overlaps(obj) || Overlap(area, obj) >= Overlap(previousArea, obj)) &&
                    (collision is null || CollisionPrecedes(obj, collision.Value, window))) collision = obj;
            if (cost is null || collision is not null) break;
            if (cost > budget - spent)
                return new(position, spent, spent == 0 ? NavigationStatus.Waiting : NavigationStatus.Moving)
                { Steps = steps.ToImmutable(), Remainder = budget - spent, Lookahead = nextStage };
            spent += cost.Value; position = next; steps.Add(next); node = best;
            if ((!continueApproach && arrived(position)) || hold?.Invoke(position) == true) break;
        }
        if (PathSearchDiagnostics.Current is { } diagnostics) diagnostics.MaximumExpanded = Math.Max(diagnostics.MaximumExpanded, expanded);
        bool reached = arrived(position);
        return new(position, spent, reached ? NavigationStatus.Arrived : spent > 0 ? NavigationStatus.Moving : NavigationStatus.Waiting)
        { Steps = steps.ToImmutable(), Lookahead = reached && !continueApproach ? null : nextStage, Collision = collision };
    }

    private static long Overlap(MapOccupancy a, MapOccupancy b) =>
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
        Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));

    // Buildings enter the local candidate list in row-major spatial order, before field units.
    // Equal-area collision ties must not inherit the world's building identity order.
    private static bool CollisionPrecedes(MapOccupancy candidate, MapOccupancy current, MapOccupancy window)
    {
        long candidateArea = (long)candidate.Footprint.Width * candidate.Footprint.Height;
        long currentArea = (long)current.Footprint.Width * current.Footprint.Height;
        if (candidateArea != currentArea) return candidateArea > currentArea;
        bool candidateBuilding = candidate.Object?.IsSettlement == true;
        bool currentBuilding = current.Object?.IsSettlement == true;
        if (candidateBuilding != currentBuilding) return candidateBuilding;
        if (!candidateBuilding) return false;
        long row = Math.Max(window.Top, candidate.Top), currentRow = Math.Max(window.Top, current.Top);
        return row < currentRow || row == currentRow && Math.Max(window.Left, candidate.Left) < Math.Max(window.Left, current.Left);
    }

    internal MovementRoute Search(WorldPosition start, WorldPosition end, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, NavigationWorkBudget? work = null,
        bool includeSteps = true, bool includeCells = false, MapOccupancy? endpoint = null, MapOccupancy? excluded = null)
    {
        if (!Coarse(start, end, Endpoint(start, objects), endpoint is { Footprint: not { Width: 1, Height: 1 } } area ? area : Endpoint(end, objects), mode, objects, work, excluded)) return new(false, [], 0);
        var cells = includeCells ? ImmutableArray.CreateBuilder<MapCell>(route.Count) : null;
        var steps = includeSteps ? ImmutableArray.CreateBuilder<WorldPosition>(route.Count) : null;
        RouteTerrainCost components = new(0, 0);
        for (int i = 0; i < route.Count; i++)
        {
            var p = Center(route[i]);
            cells?.Add(MapCoordinates.Cell(p));
            steps?.Add(p);
            if (i > 0 && map.TerrainPassageCost(p, mode) is not null)
                components += map.PassageComponents(p, mode) * 5;
        }
        void AddEndpoint(WorldPosition cursor, WorldPosition to)
        {
            while (cursor != to)
            {
                cursor = cursor.X != to.X ? cursor with { X = cursor.X + Math.Sign(to.X - cursor.X) } : cursor with { Y = cursor.Y + Math.Sign(to.Y - cursor.Y) };
                if (map.TerrainPassageCost(cursor, mode) is not null)
                    components += map.PassageComponents(cursor, mode);
            }
        }
        if (route.Count == 1) AddEndpoint(start, end);
        else { AddEndpoint(start, Center(route[0])); AddEndpoint(Center(route[^1]), end); }
        return new(true, steps?.MoveToImmutable() ?? [], map.Reweight(components))
            { Cells = cells?.MoveToImmutable() ?? [], TerrainCost = components };
    }

    internal RouteReachability Reachable(WorldPosition start, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        NavigationWorkBudget work, MapOccupancy endpoint)
    {
        var end = endpoint.Position;
        if (arrived(start)) return RouteReachability.Reachable;
        var result = Search(start, end, mode, objects, work, includeSteps: false, endpoint: endpoint);
        return work.Exhausted ? RouteReachability.Incomplete : result.Reachable ? RouteReachability.Reachable : RouteReachability.Unreachable;
    }

    internal DailyMovement Advance(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination, int budget, PathGoalBounds? bounds = null, MapOccupancy? endpoint = null)
    {
        var target = destination ?? (bounds is { } b ? new WorldPosition((int)((b.Left + b.Right) / 2), (int)((b.Top + b.Bottom) / 2)) : start);
        var result = Daily(start, endpoint ?? new(target, new(1, 1)), shape, mode, objects, budget, arrived, null, null, 25, 0, false, Math.Max(1, budget / 5));
        return new(result.Position, result.Status != NavigationStatus.Unreachable, result.Arrived, result.Spent);
    }
}

internal readonly record struct PathGoalBounds(long Left, long Top, long Right, long Bottom);
