using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class MovementNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FacilityPenaltyAndCollisionUseTheSameExtent(bool hard)
    {
        var map = new BattlefieldMap(15, 15, new int[225], [new(0, 10, null)]);
        var city = new MapOccupancy(new(37, 37), new(25, 25))
        { Avoidance = NavigationAvoidance.Facility, Hard = hard };
        for (int y = 4; y <= 10; y++)
        for (int x = 4; x <= 10; x++)
        {
            var end = MapCoordinates.Center(new(x, y));
            var start = end with { X = end.X - 1 };
            var route = map.FindRoute(start, end, new(1, 1), TravelMode.Land, [city]);
            bool inside = x is >= 5 and <= 9 && y is >= 5 and <= 9;
            if (inside && hard)
            {
                Assert.True(route.Reachable); // Coarse avoidance never grants physical passage.
                Assert.Null(map.PassageCost(end, new(1, 1), TravelMode.Land, [city]));
                continue;
            }
            Assert.True(route.Reachable);
            Assert.Single(route.Steps);
            Assert.Equal(10L, route.Cost);
            Assert.Equal(10, map.PassageCost(end, new(1, 1), TravelMode.Land, [city]));
        }
        var targetRoute = map.FindRouteIntoRange(new(12, 37), city, new(1, 1), TravelMode.Land, [city]);
        Assert.True(targetRoute.Reachable);
        Assert.Equal((targetRoute.Steps.Length - 1) * 50L, targetRoute.Cost);
    }

    [Fact]
    public void WorkerWorkspaceDoesNotKeepDiscardedBatchMapsAlive()
    {
        var reference = CreateDiscardedMap();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDiscardedMap()
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)]);
        map.Navigate(new(12, 12), new(new(80, 10), new(1, 1)), false, new(1, 1), TravelMode.Land, [], 60);
        return new(map);
    }

    [Fact]
    public void LocalPrefixUsesMobilityIndependentlyOfTimeBudget()
    {
        var map = new BattlefieldMap(80, 10, new int[800], [new(0, 10, null)]);
        var step = map.Navigate(new(12, 12), new(new(382, 12), new(1, 1)), false, new(1, 1), TravelMode.Land, [], 1000);
        Assert.InRange(step.Spent, 1, 999);
        var fast = map.Navigate(new(12, 12), new(new(382, 12), new(1, 1)), false, new(1, 1), TravelMode.Land, [], 1000, mobility: 200);
        Assert.Equal(1000, fast.Spent);
        Assert.Equal(new WorldPosition(112, 12), fast.Position);
    }

    [Fact]
    public void LongTerrainDetourArrivesAndRepeatedQueriesChooseIdenticalSteps()
    {
        var map = new BattlefieldMap(80, 40, new int[3200], [new(0, 10, null)],
            Enumerable.Range(0, 35).Select(y => new MapCell(40, y)));
        var position = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(382, 12), new(1, 1));
        using var diagnostics = new PathSearchDiagnostics();
        for (int day = 0; day < 200 && position != target.Position; day++)
        {
            var result = map.Navigate(position, target, false, new(3, 3), TravelMode.Land, [], 60);
            var replay = map.Navigate(position, target, false, new(3, 3), TravelMode.Land, [], 60);
            Assert.Equal(result.Position, replay.Position);
            Assert.Equal(result.Spent, replay.Spent);
            Assert.Equal(result.Status, replay.Status);
            Assert.Equal(result.Remainder, replay.Remainder);
            Assert.Equal<WorldPosition>(result.Steps, replay.Steps);
            Assert.Equal(result.Lookahead, replay.Lookahead);
            Assert.NotEqual(NavigationStatus.Unreachable, result.Status);
            Assert.NotNull(map.PassageCost(result.Position, new(3, 3), TravelMode.Land, []));
            Assert.InRange(result.Spent, 0, 60);
            position = result.Position;
        }
        Assert.Equal(target.Position, position);
        Assert.True(diagnostics.Queries > 0);
    }

    [Fact]
    public void LocalCollisionDoesNotUpgradeToGlobalFineDetour()
    {
        var map = new BattlefieldMap(80, 70, new int[5600], [new(0, 10, null)]);
        MapOccupancy[] wall = [new(new(200, 150), new(3, 300))];
        var position = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(382, 12), new(1, 1));
        bool wentAround = false;
        for (int day = 0; day < 250 && position != target.Position; day++)
        {
            var result = map.Navigate(position, target, false, new(3, 3), TravelMode.Land,
                wall, 60);
            Assert.NotEqual(NavigationStatus.Unreachable, result.Status);
            Assert.NotNull(map.PassageCost(result.Position, new(3, 3), TravelMode.Land, wall));
            position = result.Position;
            wentAround |= position.Y >= 301;
        }
        Assert.False(wentAround);
        Assert.NotEqual(target.Position, position);
        Assert.True(position.X < 200);
        var released = map.Navigate(position, target, false, new(3, 3), TravelMode.Land, [], 60);
        Assert.True(released.Position.X > position.X);
    }

    [Fact]
    public void OccupiedPointWaitsAndResumesWhenReleased()
    {
        var map = new BattlefieldMap(10, 10, new int[100], [new(0, 10, null)]);
        var start = new WorldPosition(12, 12);
        var target = new MapOccupancy(new(22, 12), new(1, 1));
        var waiting = map.Navigate(start, target, false, new(1, 1), TravelMode.Land, [target], 100);
        Assert.False(waiting.Arrived);
        Assert.NotNull(waiting.Collision);
        Assert.DoesNotContain(target.Position, waiting.Steps);
        var released = map.Navigate(waiting.Position, target, false, new(1, 1), TravelMode.Land, [], 100);
        Assert.True(released.Arrived);
        Assert.Equal(target.Position, released.Position);
    }

    [Fact]
    public void FineExecutionChecksObstaclesBetweenCoarseCenters()
    {
        var map = new BattlefieldMap(10, 1, new int[10], [new(0, 10, null)]);
        MapOccupancy[] wall = [new(new(25, 2), new(1, 5))];
        Assert.NotNull(map.PassageCost(new(22, 2), new(1, 1), TravelMode.Land, wall));
        Assert.NotNull(map.PassageCost(new(27, 2), new(1, 1), TravelMode.Land, wall));
        Assert.True(map.FindRoute(new(2, 2), new(47, 2), new(1, 1), TravelMode.Land, wall).Reachable);
        var step = map.Navigate(new(22, 2), new(new(47, 2), new(1, 1)), false, new(1, 1), TravelMode.Land, wall, 100);
        Assert.False(step.Arrived);
        Assert.NotNull(step.Collision);
        Assert.All(step.Steps, p => Assert.True(p.X < 25));
    }

    [Fact]
    public void DiagonalGoalsAlternateCoarseAxesAndRemainFourWay()
    {
        var map = new BattlefieldMap(30, 30, new int[900], [new(0, 10, null)]);
        var route = map.FindRoute(new(2, 2), new(142, 142), new(1, 1), TravelMode.Land, []);
        Assert.True(route.Reachable);
        var last = new WorldPosition(2, 2);
        foreach (var p in route.Steps.Skip(1))
        {
            Assert.Equal(5, Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y));
            Assert.InRange(Math.Abs(p.X - p.Y), 0, 5);
            last = p;
        }
    }

    [Fact]
    public void SoftAvoidanceChangesRouteWithoutAddingMovementTime()
    {
        var map = new BattlefieldMap(20, 5, new int[100], [new(0, 10, null)]);
        var obstacle = new MapOccupancy(new(47, 12), new(10, 3)) { Hard = false, Avoidance = NavigationAvoidance.Hostile };
        var route = map.FindRoute(new(2, 12), new(97, 12), new(1, 1), TravelMode.Land, [obstacle]);
        Assert.True(route.Reachable);
        Assert.DoesNotContain(obstacle.Position, route.Steps); // Coarse unit avoidance is applied to its center cell.
        var narrow = new BattlefieldMap(20, 1, new int[20], [new(0, 10, null)]);
        var crowd = obstacle with { Position = new(47, 2), Footprint = new(10, 5) };
        var move = narrow.Navigate(new(2, 2), new(new(97, 2), new(1, 1)), false, new(1, 1), TravelMode.Land, [crowd], 1000, mobility: 200);
        Assert.True(move.Arrived);
        Assert.Equal(move.Steps.Length * 10, move.Spent);
    }
}
