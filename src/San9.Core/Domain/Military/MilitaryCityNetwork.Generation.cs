using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed partial class MilitaryCityNetwork
{
    // Calibrated dimensionless rule parameters; training and evidence are recorded in EXP-SAN9PK-0002.
    private const double GeographicBoundary = 1.0013798752193321;
    private const double GeographicBypassBoundary = 0.6064695440821803;
    private const double WeakGeometryBoundary = 1.0579306938141682;
    private const double StrongGeometryBoundary = 1.0396093530020951;
    private const double MixedBoundary = 0.9315336133428574;
    private const double ClearanceBoundary = 0.20820433436532507;
    private const double BypassLimit = 1.5195091575091575;
    private const long Infinity = long.MaxValue / 4;
    private const int CellSize = MapCoordinates.UnitsPerCell;

    private sealed class Grid
    {
        internal readonly long[] Costs;
        internal readonly int[] Adjacent;
        internal int Count => Costs.Length;
        internal Grid(long[] costs, int width, int height)
        {
            Costs = costs;
            Adjacent = new int[checked(costs.Length * 4)];
            Array.Fill(Adjacent, -1);
            for (int node = 0; node < costs.Length; node++)
            {
                if (costs[node] == 0) continue;
                int x = node % width, y = node / width;
                Add(0, y > 0 ? node - width : -1);
                Add(1, x + 1 < width ? node + 1 : -1);
                Add(2, y + 1 < height ? node + width : -1);
                Add(3, x > 0 ? node - 1 : -1);
                void Add(int side, int other)
                {
                    if (other >= 0 && costs[other] > 0) Adjacent[node * 4 + side] = other;
                }
            }
        }
    }

    private sealed class Generator
    {
        private readonly int[] ids, seeds;
        private readonly MapCell[] positions;
        private readonly Grid movement, geography;
        private readonly int width, height, words;
        private readonly ulong[] defenses;
        private readonly int[] visited;
        private readonly long[] searchCosts;
        private readonly Queue<int> corridorQueue = new();
        private readonly PriorityQueue<int, (long Estimate, long NegativeCost, int Node)> searchQueue = new();
        private int stamp, clearanceSearches, bypassSearches;
        private long expanded;

        internal Generator(MilitaryRulebook rules, int[][] sites, MilitaryCityReferenceProfile reference)
        {
            var ids = sites.Select(s => s[0]).ToArray();
            this.ids = ids;
            width = rules.Map.Width;
            height = rules.Map.Height;
            positions = ids.Select(id => MapCoordinates.Cell(rules.Places[id].Position)).ToArray();
            seeds = positions.Select(p => p.Y * width + p.X).ToArray();
            int size = checked(width * height);
            var raw = new long[size];
            var legal = new bool[size];
            var geographic = new long[size];
            var water = new bool[size];
            var blocked = rules.Geography.BlockedCells.ToHashSet();
            int endpointCost = Math.Max(1, rules.Map.MinimumStepCost(TravelMode.Land));
            for (int node = 0; node < size; node++)
            {
                var cell = new MapCell(node % width, node / width);
                var center = MapCoordinates.Center(cell);
                water[node] = rules.Map.IsWater(center);
                var mode = reference.WaterShape is not null && water[node] ? TravelMode.Water : TravelMode.Land;
                int? cost = rules.Map.TerrainPassageCost(center, mode);
                raw[node] = checked((long)(cost ?? endpointCost) *
                    (mode == TravelMode.Water ? reference.WaterCostScale : reference.LandCostScale) * MapCoordinates.UnitsPerCell);
                legal[node] = rules.Map.CoarsePassageCost(center, mode) is > 0;
                geographic[node] = blocked.Contains(cell) ? 0 :
                    rules.Map.TerrainPassageCost(center, water[node] ? TravelMode.Water : TravelMode.Land) ?? 0;
            }
            foreach (int seed in seeds)
            {
                legal[seed] = true;
                if (geographic[seed] == 0) geographic[seed] = endpointCost;
            }
            var landShape = reference.LandShape;
            var navalShape = reference.WaterShape ?? landShape;
            var passable = (bool[])legal.Clone();
            for (int node = 0; node < size; node++)
            {
                if (!legal[node]) continue;
                var shape = water[node] && reference.WaterShape is not null ? navalShape : landShape;
                if (Math.Max(shape.Width, shape.Height) <= MapCoordinates.UnitsPerCell) continue;
                var area = new MapOccupancy(MapCoordinates.Center(new(node % width, node / width)), shape);
                if (area.Left < 0 || area.Top < 0 || area.Right > rules.Map.FineWidth || area.Bottom > rules.Map.FineHeight)
                { passable[node] = false; continue; }
                for (int y = (int)area.Top / CellSize; y <= (area.Bottom - 1) / CellSize && passable[node]; y++)
                    for (int x = (int)area.Left / CellSize; x <= (area.Right - 1) / CellSize; x++)
                        if (!legal[y * width + x]) { passable[node] = false; break; }
            }
            for (int node = 0; node < size; node++)
                if (!passable[node]) raw[node] = 0;
            movement = new(raw, width, height);
            geography = new(geographic, width, height);
            words = (ids.Length + 63) / 64;
            defenses = new ulong[checked(size * words)];
            for (int city = 0; city < ids.Length; city++)
            foreach (int cityId in sites[city])
            {
                var place = rules.Places[cityId];
                var area = rules.BuildingArea(place);
                int reach = rules.MaximumFacilityAttackRange(rules.FacilityAttack(place));
                long padding = (long)reach + Math.Max(Math.Max(landShape.Width, landShape.Height), Math.Max(navalShape.Width, navalShape.Height));
                int top = (int)Math.Max(0, (area.Top - padding) / CellSize);
                int bottom = (int)Math.Min(height, (area.Bottom + padding + CellSize - 1) / CellSize);
                int left = (int)Math.Max(0, (area.Left - padding) / CellSize);
                int right = (int)Math.Min(width, (area.Right + padding + CellSize - 1) / CellSize);
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                    {
                        int node = y * width + x;
                        var shape = water[node] && reference.WaterShape is not null ? navalShape : landShape;
                        var unit = new MapOccupancy(MapCoordinates.Center(new(x, y)), shape);
                        if (area.DistanceTo(unit) <= reach)
                            defenses[node * words + city / 64] |= 1UL << (city % 64);
                    }
            }
            visited = new int[size];
            searchCosts = new long[size];
        }

        internal MilitaryCityNetwork Build()
        {
            var distances = Fields(movement);
            var geographicDistances = Fields(geography);
            var ranks = new int[ids.Length][];
            for (int city = 0; city < ids.Length; city++)
            {
                var values = seeds.Where((_, i) => i != city).Select(seed => distances[city][seed]).Order().ToArray();
                ranks[city] = seeds.Select(seed => UpperBound(values, distances[city][seed])).ToArray();
            }
            var links = ImmutableArray.CreateBuilder<CityConnection>();
            for (int a = 0; a < ids.Length; a++)
                for (int b = a + 1; b < ids.Length; b++)
                {
                    long distance = distances[a][seeds[b]];
                    if (distance >= Infinity || !Local(a, b, distance)) continue;
                    if (!CanBypass(a, b, distance, distances[b])) continue;
                    links.Add(new(ids[a], ids[b]));
                    links.Add(new(ids[b], ids[a]));
                }
            return new(links.OrderBy(c => c.FromCityId).ThenBy(c => c.ToCityId).ToImmutableArray(),
                new(ids.Length * 2, clearanceSearches, bypassSearches, expanded));

            bool Local(int a, int b, long distance)
            {
                double geo = double.PositiveInfinity, mixed = double.PositiveInfinity, euclidean = double.PositiveInfinity;
                long geographicDistance = geographicDistances[a][seeds[b]];
                double square = Squared(positions[a], positions[b]);
                int blocker = -1;
                for (int c = 0; c < ids.Length; c++)
                {
                    if (c == a || c == b || distances[a][seeds[c]] >= Infinity || distances[b][seeds[c]] >= Infinity) continue;
                    geo = Math.Min(geo, Lens(geographicDistances[a][seeds[c]], geographicDistances[b][seeds[c]], geographicDistance));
                    double competition = Lens(distances[a][seeds[c]], distances[b][seeds[c]], distance);
                    if (competition < mixed || competition == mixed && Before(c, blocker))
                    { mixed = competition; blocker = c; }
                    euclidean = Math.Min(euclidean, (Squared(positions[a], positions[c]) + Squared(positions[b], positions[c])) / square);
                }
                // Boolean-equivalent pruning of the calibrated confidence tree. Features requiring
                // route searches are evaluated only on the branch that actually consumes them.
                if (geo > GeographicBoundary)
                    return Math.Min(ranks[a][b], ranks[b][a]) <= 4 || euclidean > StrongGeometryBoundary;
                if (Math.Max(ranks[a][b], ranks[b][a]) > 7) return false;
                if (blocker >= 0 && !HasClearance(a, b, distance, distances[a], distances[b], distances[blocker])) return false;
                return geo <= GeographicBypassBoundary || euclidean > WeakGeometryBoundary || mixed > MixedBoundary;
            }
        }

        private bool Before(int city, int other) => other < 0 || positions[city].X < positions[other].X ||
            positions[city].X == positions[other].X && positions[city].Y < positions[other].Y;
        private static double Squared(MapCell a, MapCell b) => Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2);
        private static double Lens(long a, long b, long distance) => ((double)a * a + (double)b * b) / ((double)distance * distance);
        private static int UpperBound(long[] values, long value)
        {
            int left = 0, right = values.Length;
            while (left < right)
            {
                int middle = left + (right - left) / 2;
                if (values[middle] <= value) left = middle + 1;
                else right = middle;
            }
            return left;
        }

        private long[][] Fields(Grid grid)
        {
            var result = new long[ids.Length][];
            var queue = new PriorityQueue<int, long>();
            for (int city = 0; city < ids.Length; city++)
            {
                var distance = result[city] = new long[grid.Count];
                Array.Fill(distance, Infinity);
                distance[seeds[city]] = 0;
                queue.Enqueue(seeds[city], 0);
                while (queue.TryDequeue(out int node, out long cost))
                {
                    if (cost != distance[node]) continue;
                    expanded++;
                    for (int side = 0; side < 4; side++)
                    {
                        int other = grid.Adjacent[node * 4 + side];
                        if (other < 0) continue;
                        long next = cost + grid.Costs[node] + grid.Costs[other];
                        if (next >= distance[other]) continue;
                        distance[other] = next;
                        queue.Enqueue(other, next);
                    }
                }
            }
            return result;
        }

        private void NextSearch()
        {
            if (stamp == int.MaxValue) { Array.Clear(visited); stamp = 0; }
            stamp++;
        }

        private bool HasClearance(int a, int b, long distance, long[] from, long[] to, long[] competitor)
        {
            clearanceSearches++;
            int start = seeds[a], end = seeds[b];
            if (competitor[start] / (double)distance <= ClearanceBoundary || competitor[end] / (double)distance <= ClearanceBoundary)
                return false;
            NextSearch();
            corridorQueue.Clear();
            corridorQueue.Enqueue(start);
            visited[start] = stamp;
            while (corridorQueue.TryDequeue(out int node))
            {
                expanded++;
                if (node == end) return true;
                for (int side = 0; side < 4; side++)
                {
                    int other = movement.Adjacent[node * 4 + side];
                    if (other < 0 || visited[other] == stamp || from[other] != from[node] + movement.Costs[node] + movement.Costs[other] ||
                        from[other] + to[other] != distance || competitor[other] / (double)distance <= ClearanceBoundary) continue;
                    visited[other] = stamp;
                    corridorQueue.Enqueue(other);
                }
            }
            return false;
        }

        private bool Blocked(int node, int a, int b)
        {
            for (int word = 0; word < words; word++)
            {
                ulong allowed = (a / 64 == word ? 1UL << (a % 64) : 0) | (b / 64 == word ? 1UL << (b % 64) : 0);
                if ((defenses[node * words + word] & ~allowed) != 0) return true;
            }
            return false;
        }

        private bool CanBypass(int a, int b, long direct, long[] lower)
        {
            bypassSearches++;
            int start = seeds[a], end = seeds[b];
            if (Blocked(start, a, b) || Blocked(end, a, b)) return false;
            NextSearch();
            searchQueue.Clear();
            visited[start] = stamp;
            searchCosts[start] = 0;
            searchQueue.Enqueue(start, (lower[start], 0, start));
            while (searchQueue.TryDequeue(out int node, out var priority))
            {
                long cost = -priority.NegativeCost;
                if (visited[node] != stamp || searchCosts[node] != cost) continue;
                expanded++;
                if (node == end) return true;
                for (int side = 0; side < 4; side++)
                {
                    int other = movement.Adjacent[node * 4 + side];
                    if (other < 0 || lower[other] >= Infinity || Blocked(other, a, b)) continue;
                    long next = cost + movement.Costs[node] + movement.Costs[other];
                    long estimate = next + lower[other];
                    if (estimate / (double)direct > BypassLimit || visited[other] == stamp && next >= searchCosts[other]) continue;
                    visited[other] = stamp;
                    searchCosts[other] = next;
                    searchQueue.Enqueue(other, (estimate, -next, other));
                }
            }
            return false;
        }
    }
}
