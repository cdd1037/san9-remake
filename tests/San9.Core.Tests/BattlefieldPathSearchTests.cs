using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class BattlefieldPathSearchTests
{
    [Fact]
    public void CachedSearchMatchesIndependentWeightedDistancesAcrossShapesAndDynamicOccupancy()
    {
        var random = new Random(819);
        var map = new BattlefieldMap(8, 8, Enumerable.Range(0, 64).Select(_ => random.Next(2)),
            [new(0, 3, 5), new(1, 11, 2)], [new(3, 3), new(3, 4)]);
        for (int trial = 0; trial < 45; trial++)
        {
            var shape = new Footprint(1 + trial % 3, 1 + trial % 2);
            var mode = trial % 2 == 0 ? TravelMode.Land : TravelMode.Water;
            var start = new WorldPosition(random.Next(2, 38), random.Next(2, 38));
            var end = new WorldPosition(random.Next(2, 38), random.Next(2, 38));
            MapOccupancy[] occupied = [new(new(15 + trial % 10, 20), new(3, 7))];
            long? expected = ReferenceCost(map, start, end, shape, mode, occupied);
            var actual = map.FindRoute(start, end, shape, mode, occupied);
            Assert.Equal(expected.HasValue, actual.Reachable);
            if (!actual.Reachable) continue;
            Assert.Equal(expected!.Value, actual.Cost);
            long total = 0; var previous = start;
            foreach (var step in actual.Steps)
            {
                Assert.Equal(1, Math.Abs(step.X - previous.X) + Math.Abs(step.Y - previous.Y));
                total += map.PassageCost(step, shape, mode, occupied)!.Value;
                previous = step;
            }
            Assert.Equal(end, previous);
            Assert.Equal(total, actual.Cost);
        }
    }

    [Fact]
    public void DynamicBarrierDoesNotPoisonNextSearchAndParallelQueriesRemainDeterministic()
    {
        var map = new BattlefieldMap(10, 10, new int[100], [new(0, 10, null)]);
        var start = new WorldPosition(5, 5); var end = new WorldPosition(45, 45);
        MapOccupancy[] wall = [new(new(25, 25), new(3, 50))];
        Assert.False(map.FindRoute(start, end, new(3, 3), TravelMode.Land, wall).Reachable);
        var expected = map.FindRoute(start, end, new(3, 3), TravelMode.Land, []);
        Assert.True(expected.Reachable);
        Parallel.For(0, 8, _ => Assert.Equal(expected.Steps.ToArray(),
            map.FindRoute(start, end, new(3, 3), TravelMode.Land, []).Steps.ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedVariableRangeMatchesUninformedSearch(bool rectangular)
    {
        var map = new BattlefieldMap(12, 8, Enumerable.Range(0, 96).Select(i => i % 3),
            [new(0, 2, null), new(1, 7, null), new(2, 4, null)]);
        var target = new MapOccupancy(new(50, 30), new(5, 5));
        int Range(WorldPosition p) => p.Y < 20 ? 12 : 3;
        var expected = map.AdvanceIntoRange(new(5, 5), target, new(3, 3), TravelMode.Land, [], 100000, Range, rectangular);
        var actual = map.AdvanceIntoRange(new(5, 5), target, new(3, 3), TravelMode.Land, [], 100000, Range, rectangular, 12);
        Assert.True(actual.Arrived);
        Assert.Equal(expected.Spent, actual.Spent);
        Assert.False(new MapOccupancy(actual.Position, new(3, 3)).Overlaps(target));
    }

    private static long? ReferenceCost(BattlefieldMap map, WorldPosition start, WorldPosition end,
        Footprint shape, TravelMode mode, MapOccupancy[] occupied)
    {
        if (map.PassageCost(start, shape, mode, occupied) is null || map.PassageCost(end, shape, mode, occupied) is null) return null;
        var costs = new Dictionary<WorldPosition, long> { [start] = 0 };
        var queue = new PriorityQueue<WorldPosition, long>(); queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var p, out var cost))
        {
            if (costs[p] != cost) continue;
            if (p == end) return cost;
            WorldPosition[] next = [new(p.X, p.Y - 1), new(p.X + 1, p.Y), new(p.X, p.Y + 1), new(p.X - 1, p.Y)];
            foreach (var n in next)
            {
                int? step = map.PassageCost(n, shape, mode, occupied);
                if (step is null) continue;
                long total = cost + step.Value;
                if (costs.TryGetValue(n, out long old) && old <= total) continue;
                costs[n] = total; queue.Enqueue(n, total);
            }
        }
        return null;
    }
}
