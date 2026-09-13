namespace San9.Core.Domain;

/// <summary>Map-owned derived static data shared by independent simulation workers.</summary>
internal sealed class BattlefieldTopologies(BattlefieldMap map)
{
    internal sealed record Topology(Footprint Shape, TravelMode Mode, int[] Costs, int Minimum, BattlefieldRegions Regions);
    private readonly List<Topology> topologies = [];
    private readonly object gate = new();
    internal Topology Get(Footprint shape, TravelMode mode)
    {
        lock (gate)
        {
            int found = topologies.FindIndex(t => t.Shape == shape && t.Mode == mode);
            if (found >= 0)
            {
                var cached = topologies[found]; topologies.RemoveAt(found); topologies.Add(cached); return cached;
            }
            int count = checked(map.FineWidth * map.FineHeight);
            if (PathSearchDiagnostics.Current is { } diagnostics) diagnostics.TopologyBuilds++;
            var costs = new int[count];
            int minimum = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                costs[i] = map.StaticPassageCost(Position(i), shape, mode) ?? 0;
                if (costs[i] > 0) minimum = Math.Min(minimum, costs[i]);
            }
            var result = new Topology(shape, mode, costs, minimum == int.MaxValue ? 0 : minimum, new(map.FineWidth, map.FineHeight, costs));
            // Bound retained terrain arrays by bytes.
            while (topologies.Count > 0 && topologies.Sum(t => (long)t.Costs.Length * 2 * sizeof(int)) + (long)count * 2 * sizeof(int) > 128L * 1024 * 1024) topologies.RemoveAt(0);
            topologies.Add(result);
            return result;
        }
    }

    private WorldPosition Position(int index) => new(index % map.FineWidth, index / map.FineWidth);
}
