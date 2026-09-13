using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Derived topology and serialized reusable search workspace, owned by one immutable map.</summary>
internal sealed class BattlefieldPathSearch(BattlefieldMap map)
{
    private sealed record Topology(Footprint Shape, TravelMode Mode, int[] Costs, int[] Components, int Minimum);
    private readonly List<Topology> topologies = [];
    private readonly object gate = new();
    private int[] seen = [], previous = [], occupied = [];
    private long[] distances = [];
    private int generation;
    private readonly PriorityQueue<(int Node, long Cost), (long Estimate, long Remaining, long Sequence)> pending = new();

    internal MovementRoute Search(WorldPosition start, Footprint shape, TravelMode mode,
        IReadOnlyList<MapOccupancy> objects, Func<WorldPosition, bool> arrived,
        Func<WorldPosition, long> lowerBound, WorldPosition? destination)
    {
        lock (gate)
        {
            if (!shape.IsValid || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid movement shape or mode.");
            foreach (var area in objects)
                if (!area.Footprint.IsValid || area.Left < 0 || area.Top < 0 || area.Right > map.FineWidth || area.Bottom > map.FineHeight)
                    throw new ArgumentException("Occupancy lies outside battlefield.");
            if (!Inside(start) || destination is { } end && !Inside(end)) return new(false, [], 0);
            var topology = GetTopology(shape, mode);
            int count = topology.Costs.Length;
            if (seen.Length != count)
            {
                seen = new int[count]; previous = new int[count]; occupied = new int[count]; distances = new long[count];
            }
            if (generation == int.MaxValue) { Array.Clear(seen); Array.Clear(occupied); generation = 0; }
            int stamp = ++generation;
            pending.Clear();
            foreach (var area in objects)
            {
                int left = (int)Math.Max(0, area.Left - (shape.Width - shape.Width / 2) + 1);
                int right = (int)Math.Min(map.FineWidth - 1, area.Right + shape.Width / 2 - 1);
                int top = (int)Math.Max(0, area.Top - (shape.Height - shape.Height / 2) + 1);
                int bottom = (int)Math.Min(map.FineHeight - 1, area.Bottom + shape.Height / 2 - 1);
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++) occupied[y * map.FineWidth + x] = stamp;
            }
            int source = Index(start);
            if (!Passable(source)) return new(false, [], 0);
            if (destination is { } target)
            {
                int goal = Index(target);
                if (!Passable(goal) || topology.Components[source] != topology.Components[goal]) return new(false, [], 0);
            }
            long sequence = 0;
            seen[source] = stamp; distances[source] = 0;
            long initial = Estimate(source);
            pending.Enqueue((source, 0), (initial, initial, sequence++));
            while (pending.TryDequeue(out var item, out _))
            {
                int current = item.Node;
                if (distances[current] != item.Cost) continue;
                if (arrived(Position(current)))
                {
                    var steps = new List<WorldPosition>();
                    for (int p = current; p != source; p = previous[p]) steps.Add(Position(p));
                    steps.Reverse();
                    return new(true, steps.ToImmutableArray(), item.Cost);
                }
                Visit(current - map.FineWidth);
                if (current % map.FineWidth < map.FineWidth - 1) Visit(current + 1);
                Visit(current + map.FineWidth);
                if (current % map.FineWidth > 0) Visit(current - 1);
                void Visit(int next)
                {
                    if (next < 0 || next >= count || !Passable(next)) return;
                    long total = checked(item.Cost + topology.Costs[next]);
                    if (seen[next] == stamp && distances[next] <= total) return;
                    seen[next] = stamp; distances[next] = total; previous[next] = current;
                    long h = Estimate(next);
                    pending.Enqueue((next, total), (checked(total + h), h, sequence++));
                }
            }
            return new(false, [], 0);
            bool Passable(int p) => topology.Costs[p] > 0 && occupied[p] != stamp;
            long Estimate(int p) => checked(lowerBound(Position(p)) * topology.Minimum);
        }
    }

    private Topology GetTopology(Footprint shape, TravelMode mode)
    {
        int found = topologies.FindIndex(t => t.Shape == shape && t.Mode == mode);
        if (found >= 0)
        {
            var cached = topologies[found]; topologies.RemoveAt(found); topologies.Add(cached); return cached;
        }
        int count = checked(map.FineWidth * map.FineHeight);
        var costs = new int[count]; var components = new int[count];
        int minimum = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            costs[i] = map.StaticPassageCost(Position(i), shape, mode) ?? 0;
            if (costs[i] > 0) minimum = Math.Min(minimum, costs[i]);
        }
        var queue = new int[count]; int component = 0;
        for (int i = 0; i < count; i++)
        {
            if (costs[i] == 0 || components[i] != 0) continue;
            int head = 0, tail = 0; queue[tail++] = i; components[i] = ++component;
            while (head < tail)
            {
                int current = queue[head++];
                Visit(current - map.FineWidth);
                if (current % map.FineWidth < map.FineWidth - 1) Visit(current + 1);
                Visit(current + map.FineWidth);
                if (current % map.FineWidth > 0) Visit(current - 1);
                void Visit(int next)
                {
                    if (next < 0 || next >= count || costs[next] == 0 || components[next] != 0) return;
                    components[next] = component; queue[tail++] = next;
                }
            }
        }
        var result = new Topology(shape, mode, costs, components, minimum == int.MaxValue ? 0 : minimum);
        // Bound retained topology memory independently of the number of authored formations.
        if (topologies.Count == 2) topologies.RemoveAt(0);
        topologies.Add(result);
        return result;
    }

    private bool Inside(WorldPosition p) => p.X >= 0 && p.Y >= 0 && p.X < map.FineWidth && p.Y < map.FineHeight;
    private int Index(WorldPosition p) => p.Y * map.FineWidth + p.X;
    private WorldPosition Position(int index) => new(index % map.FineWidth, index / map.FineWidth);
}
