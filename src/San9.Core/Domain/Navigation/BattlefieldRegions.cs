namespace San9.Core.Domain;

/// <summary>Static connected components within small tiles; no terrain connection is invented by coarsening.</summary>
internal sealed class BattlefieldRegions
{
    internal const int TileSize = 16;
    internal sealed record Guide(long[] Costs, int[] Next);
    private readonly int[][] neighbors;
    private readonly long[] averageCosts;
    private readonly int[] components;
    private readonly WorldPosition[] origins;
    private readonly List<(int[] Goals, Guide Guide)> guides = [];
    private const long GuideBudget = 8L * 1024 * 1024;
    internal int[] Regions { get; }

    internal BattlefieldRegions(int width, int height, int[] costs)
    {
        Regions = new int[costs.Length];
        Array.Fill(Regions, -1);
        var means = new List<long>();
        var anchors = new List<WorldPosition>();
        var queue = new int[TileSize * TileSize];
        for (int origin = 0; origin < costs.Length; origin++)
        {
            if (costs[origin] == 0 || Regions[origin] >= 0) continue;
            int id = means.Count;
            anchors.Add(new(origin % width / TileSize * TileSize, origin / width / TileSize * TileSize));
            int tileX = origin % width / TileSize, tileY = origin / width / TileSize;
            int head = 0, tail = 0;
            long sum = 0;
            Regions[origin] = id; queue[tail++] = origin;
            while (head < tail)
            {
                int node = queue[head++]; sum += costs[node];
                Visit(node - width);
                if (node % width < width - 1) Visit(node + 1);
                Visit(node + width);
                if (node % width > 0) Visit(node - 1);
                void Visit(int next)
                {
                    if (next < 0 || next >= costs.Length || costs[next] == 0 || Regions[next] >= 0 ||
                        next % width / TileSize != tileX || next / width / TileSize != tileY) return;
                    Regions[next] = id; queue[tail++] = next;
                }
            }
            means.Add(sum / tail);
        }
        averageCosts = means.ToArray();
        origins = anchors.ToArray();
        var links = Enumerable.Range(0, means.Count).Select(_ => new List<int>()).ToArray();
        for (int node = 0; node < costs.Length; node++)
        {
            if (Regions[node] < 0) continue;
            if (node % width < width - 1) Link(node + 1);
            if (node / width < height - 1) Link(node + width);
            void Link(int next)
            {
                int a = Regions[node], b = Regions[next];
                if (b < 0 || a == b || links[a].Contains(b)) return;
                links[a].Add(b); links[b].Add(a);
            }
        }
        neighbors = links.Select(list => list.Order().ToArray()).ToArray();
        components = new int[neighbors.Length];
        var pending = new Stack<int>();
        for (int region = 0; region < neighbors.Length; region++)
        {
            if (components[region] != 0) continue;
            int component = region + 1;
            components[region] = component;
            pending.Push(region);
            while (pending.TryPop(out int current))
                foreach (int next in neighbors[current])
                    if (components[next] == 0) { components[next] = component; pending.Push(next); }
        }

    }

    internal bool Connected(int firstNode, int secondNode) =>
        components[Regions[firstNode]] == components[Regions[secondNode]];

    internal Guide ToGoals(int[] goals)
    {
        lock (guides)
        {
            int found = guides.FindIndex(g => g.Goals.AsSpan().SequenceEqual(goals));
            if (found >= 0)
            {
                var cached = guides[found]; guides.RemoveAt(found); guides.Add(cached); return cached.Guide;
            }
            var distances = new long[neighbors.Length]; Array.Fill(distances, long.MaxValue);
            var next = new int[neighbors.Length]; Array.Fill(next, -1);
            var queue = new PriorityQueue<int, (long Cost, int Node)>();
            foreach (int goal in goals) { distances[goal] = 0; queue.Enqueue(goal, (0, goal)); }
            while (queue.TryDequeue(out int current, out var priority))
            {
                if (distances[current] != priority.Cost) continue;
                foreach (int neighbor in neighbors[current])
                {
                    long cost = checked(priority.Cost + (averageCosts[current] + averageCosts[neighbor]) * (TileSize / 2));
                    if (cost >= distances[neighbor]) continue;
                    distances[neighbor] = cost; next[neighbor] = current;
                    queue.Enqueue(neighbor, (cost, neighbor));
                }
            }
            var guide = new Guide(distances, next);
            long bytes = (long)neighbors.Length * (sizeof(long) + sizeof(int));
            if (bytes <= GuideBudget)
            {
                while (guides.Count > 0 && (guides.Count >= 32 || (guides.Count + 1L) * bytes > GuideBudget)) guides.RemoveAt(0);
                guides.Add((goals, guide));
            }
            return guide;
        }
    }

    internal long DistanceToTile(WorldPosition source, int region)
    {
        var p = origins[region];
        return Math.Max(0, Math.Max((long)p.X - source.X, source.X - ((long)p.X + TileSize - 1))) +
            Math.Max(0, Math.Max((long)p.Y - source.Y, source.Y - ((long)p.Y + TileSize - 1)));
    }

    // Include the selected corridor and one neighboring ring. The caller can retry without this restriction.
    internal bool[] Corridor(int source, Guide guide)
    {
        var included = new bool[neighbors.Length];
        for (int node = source; node >= 0; node = guide.Next[node])
        {
            included[node] = true;
            foreach (int neighbor in neighbors[node]) included[neighbor] = true;
        }
        return included;
    }
}
