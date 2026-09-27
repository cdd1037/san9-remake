using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class BattlefieldPathSearchTests
{
    [Fact]
    public void DynamicWallOutsidePreferredCorridorStillAllowsDetour()
    {
        var map = new BattlefieldMap(80, 70, new int[5600], [new(0, 10, null)]);
        var start = new WorldPosition(10, 10);
        var end = new WorldPosition(380, 10);
        MapOccupancy[] wall = [new(new(200, 150), new(3, 300))];
        var route = map.FindRoute(start, end, new(3, 3), TravelMode.Land, wall);
        Assert.True(route.Reachable);
        Assert.Equal(end, route.Steps[^1]);
        Assert.Contains(route.Steps, p => p.Y >= 301);
        Assert.All(route.Steps, p => Assert.NotNull(map.PassageCost(p, new(3, 3), TravelMode.Land, wall)));
    }

    [Fact]
    public void DailyRegionalPlanningEventuallyArrivesAroundLongTerrainWall()
    {
        var map = new BattlefieldMap(80, 40, new int[3200], [new(0, 10, null)],
            Enumerable.Range(0, 35).Select(y => new MapCell(40, y)));
        var position = new WorldPosition(10, 10);
        var end = new WorldPosition(380, 10);
        int days = 0;
        while (position != end && days++ < 160)
        {
            var movement = map.Advance(position, end, new(3, 3), TravelMode.Land, [], 60);
            Assert.True(movement.Reachable);
            Assert.NotEqual(position, movement.Position);
            Assert.InRange(movement.Spent, 1, 60);
            position = movement.Position;
        }
        Assert.Equal(end, position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionalRoutesRemainLegalAndStableAcrossObstaclesAndTopologyChanges(bool rectangular)
    {
        BattlefieldMap Create() => new(40, 8, Enumerable.Range(0, 320).Select(i => i % 7 == 0 ? 1 : 0),
            [new(0, 3, null), new(1, 7, null)]);
        var map = Create();
        var start = new WorldPosition(3, 3);
        var end = new WorldPosition(190, 30);
        for (int trial = 0; trial < 8; trial++)
        {
            MapOccupancy[] objects = trial % 3 == 0 ? [] : [new(new(100, 15), new(3, 25))];
            var expected = Create().FindRoute(start, end, new(3, 3), TravelMode.Land, objects);
            var actual = map.FindRoute(start, end, new(3, 3), TravelMode.Land, objects);
            long optimal = ReferenceCost(map, start, end, new(3, 3), TravelMode.Land, objects)!.Value;
            Assert.InRange(actual.Cost, optimal, optimal * 5 / 4);
            var previous = start;
            foreach (var step in actual.Steps)
            {
                Assert.Equal(1, Math.Abs(step.X - previous.X) + Math.Abs(step.Y - previous.Y));
                Assert.NotNull(map.PassageCost(step, new(3, 3), TravelMode.Land, objects));
                previous = step;
            }
            Assert.Equal(end, previous);
            Assert.Equal(expected.Cost, actual.Cost);
            Assert.Equal(expected.Steps.ToArray(), actual.Steps.ToArray());
            if (trial == 4)
            {
                map.FindRoute(start, end, new(1, 1), TravelMode.Land, []);
                map.FindRoute(start, end, new(2, 2), TravelMode.Land, []);
            }
        }
        Assert.False(map.FindRoute(start, end, new(3, 3), TravelMode.Land,
            [new(new(100, 20), new(3, 40))]).Reachable);
        var target = new MapOccupancy(end, new(3, 3));
        for (int trial = 0; trial < 4; trial++)
        {
            var expected = Create().AdvanceIntoRange(start, target, new(3, 3), TravelMode.Land, [], 400,
                p => p.Y < 20 ? 8 : 3, rectangular, 8);
            Assert.Equal(expected, map.AdvanceIntoRange(start, target, new(3, 3), TravelMode.Land, [], 400,
                p => p.Y < 20 ? 8 : 3, rectangular, 8));
        }
        var uninformed = Create().AdvanceIntoRange(start, target, new(3, 3), TravelMode.Land, [], 100000,
            p => p.Y < 20 ? 8 : 3, rectangular);
        var guided = map.AdvanceIntoRange(start, target, new(3, 3), TravelMode.Land, [], 100000,
            p => p.Y < 20 ? 8 : 3, rectangular, 8);
        Assert.True(guided.Arrived);
        Assert.InRange(guided.Spent, uninformed.Spent, uninformed.Spent * 5 / 4);
    }

    [Fact]
    public void DailyMovementPreservesWeightedRoutePrefixForEveryBudget()
    {
        var map = new BattlefieldMap(3, 2, [0, 1, 0, 0, 0, 0], [new(0, 3, null), new(1, 20, null)]);
        var start = new WorldPosition(2, 2);
        var end = new WorldPosition(12, 2);
        var shape = new Footprint(1, 1);
        var route = map.FindRoute(start, end, shape, TravelMode.Land, []);
        Assert.True(route.Reachable);
        for (int budget = 0; budget <= 200; budget++)
        {
            var expected = start;
            int spent = 0;
            foreach (var step in route.Steps)
            {
                int cost = map.PassageCost(step, shape, TravelMode.Land, [])!.Value;
                if (cost > budget - spent) break;
                spent += cost;
                expected = step;
            }
            Assert.Equal(new DailyMovement(expected, true, expected == end, spent),
                map.Advance(start, end, shape, TravelMode.Land, [], budget));
        }
    }

    [Fact]
    public void CoarseSearchProducesLegalRoutesNoCheaperThanIndependentFineDistances()
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
            if (!expected.HasValue) Assert.False(actual.Reachable);
            if (!actual.Reachable) continue;
            Assert.True(actual.Cost >= expected!.Value);
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
