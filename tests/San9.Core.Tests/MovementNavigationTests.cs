using System.Text.Json;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class MovementNavigationTests
{
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
        map.Navigate(new(10, 10), new(new(80, 10), new(1, 1)), false, new(1, 1), TravelMode.Land, _ => [], 60);
        return new(map);
    }

    [Fact]
    public void DistantTargetMotionWithinSameRegionKeepsChosenShortRoute()
    {
        var map = new BattlefieldMap(100, 20, new int[2000], [new(0, 10, null)]);
        var first = map.Navigate(new(10, 10), new(new(400, 10), new(1, 1)), false,
            new(1, 1), TravelMode.Land, _ => [], 10);
        Assert.NotNull(first.Plan);
        using var diagnostics = new PathSearchDiagnostics();
        var next = map.Navigate(first.Position, new(new(401, 10), new(1, 1)), false,
            new(1, 1), TravelMode.Land, _ => [], 10, first.Plan);
        Assert.Equal(first.Plan.Steps[0], next.Position);
        Assert.Equal(0, diagnostics.Queries);
        Assert.Equal(new WorldPosition(401, 10), next.Plan!.Target);
    }

    [Fact]
    public void RemainderOfBudgetContinuesPastAConsumedShortRoute()
    {
        var map = new BattlefieldMap(100, 10, new int[1000], [new(0, 10, null)]);
        var start = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(400, 10), new(1, 1));
        var plan = new MovementPlan(target.Position, new(1, 1), TravelMode.Land, [new(11, 10)]);
        var result = map.Navigate(start, target, false, new(1, 1), TravelMode.Land, _ => [], 60, plan);
        Assert.Equal(60, result.Spent);
        Assert.Equal(new WorldPosition(16, 10), result.Position);
    }

    [Fact]
    public void LargeDailyBudgetIsNotTruncatedAtAnArtificialShortWaypoint()
    {
        var map = new BattlefieldMap(80, 10, new int[800], [new(0, 10, null)]);
        var step = map.Navigate(new(10, 10), new(new(380, 10), new(1, 1)), false, new(1, 1), TravelMode.Land, _ => [], 1000);
        Assert.Equal(1000, step.Spent);
        Assert.Equal(new WorldPosition(110, 10), step.Position);
    }

    [Fact]
    public void UnchangedBlockageBacksOffButChangedOccupancyWakesImmediately()
    {
        var map = new BattlefieldMap(10, 10, new int[100], [new(0, 10, null)]);
        var start = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(20, 10), new(1, 1));
        MovementPlan? plan = null;
        for (int day = 0; day < 5; day++)
            plan = map.Navigate(start, target, false, new(1, 1), TravelMode.Land, _ => [target], 100, plan).Plan;
        Assert.NotNull(plan);
        using var diagnostics = new PathSearchDiagnostics();
        var result = map.Navigate(start, target, false, new(1, 1), TravelMode.Land, _ => [], 100, plan);
        Assert.True(result.Arrived);
    }

    [Fact]
    public void LongTerrainDetourArrivesAndRestoredPlanChoosesIdenticalSteps()
    {
        var map = new BattlefieldMap(80, 40, new int[3200], [new(0, 10, null)],
            Enumerable.Range(0, 35).Select(y => new MapCell(40, y)));
        var position = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(380, 10), new(1, 1));
        MovementPlan? plan = null;
        using var diagnostics = new PathSearchDiagnostics();
        for (int day = 0; day < 200 && position != target.Position; day++)
        {
            var restored = plan is null ? null : JsonSerializer.Deserialize<MovementPlan>(JsonSerializer.Serialize(plan));
            var result = map.Navigate(position, target, false, new(3, 3), TravelMode.Land, _ => [], 60, plan);
            var replay = map.Navigate(position, target, false, new(3, 3), TravelMode.Land, _ => [], 60, restored);
            Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
            Assert.NotEqual(NavigationStatus.Unreachable, result.Status);
            Assert.NotNull(map.PassageCost(result.Position, new(3, 3), TravelMode.Land, []));
            Assert.InRange(result.Spent, 0, 60);
            position = result.Position; plan = result.Plan;
        }
        Assert.Equal(target.Position, position);
        Assert.True(diagnostics.ReusedPlans > diagnostics.Queries);
    }

    [Fact]
    public void PersistentDynamicWallEscalatesBeyondInitialWindowAndEventuallyArrives()
    {
        var map = new BattlefieldMap(80, 70, new int[5600], [new(0, 10, null)]);
        MapOccupancy[] wall = [new(new(200, 150), new(3, 300))];
        var position = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(380, 10), new(1, 1));
        MovementPlan? plan = null;
        bool wentAround = false;
        for (int day = 0; day < 250 && position != target.Position; day++)
        {
            var result = map.Navigate(position, target, false, new(3, 3), TravelMode.Land,
                area => wall.Where(w => w.Overlaps(area)).ToArray(), 60, plan);
            Assert.NotEqual(NavigationStatus.Unreachable, result.Status);
            Assert.NotNull(map.PassageCost(result.Position, new(3, 3), TravelMode.Land, wall));
            position = result.Position; plan = result.Plan;
            wentAround |= position.Y >= 301;
        }
        Assert.True(wentAround);
        Assert.Equal(target.Position, position);
    }

    [Fact]
    public void OccupiedPointWaitsAndResumesWhenReleased()
    {
        var map = new BattlefieldMap(10, 10, new int[100], [new(0, 10, null)]);
        var start = new WorldPosition(10, 10);
        var target = new MapOccupancy(new(20, 10), new(1, 1));
        var waiting = map.Navigate(start, target, false, new(1, 1), TravelMode.Land, _ => [target], 100);
        Assert.Equal(NavigationStatus.Waiting, waiting.Status);
        var released = map.Navigate(start, target, false, new(1, 1), TravelMode.Land, _ => [], 100, waiting.Plan);
        Assert.True(released.Arrived);
        Assert.Equal(target.Position, released.Position);
    }

    [Fact]
    public void CachedPathChecksEveryStepAgainstNewBlocker()
    {
        var map = new BattlefieldMap(80, 10, new int[800], [new(0, 10, null)]);
        var target = new MapOccupancy(new(380, 10), new(1, 1));
        var first = map.Navigate(new(10, 10), target, false, new(1, 1), TravelMode.Land, _ => [], 20);
        Assert.NotNull(first.Plan);
        Assert.True(first.Plan.Steps.Length > 4);
        var obstacle = new MapOccupancy(first.Plan.Steps[3], new(1, 1));
        var next = map.Navigate(first.Position, target, false, new(1, 1), TravelMode.Land, _ => [obstacle], 100, first.Plan);
        Assert.Equal(30, next.Spent);
        Assert.Equal(first.Plan.Steps[2], next.Position);
    }
}
