using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class BattlefieldMapTests
{
    [Fact]
    public void TerrainCostIsSharedByRoutingAndEveryMovementEntry()
    {
        var map = new BattlefieldMap(8, 1, Enumerable.Repeat(0, 8), [new(0, 12, null)]);
        Assert.Equal(240, map.FindRoute(new(2, 2), new(22, 2), new(1, 1), TravelMode.Land, []).Cost);
        Assert.Equal(new WorldPosition(7, 2), map.Advance(new(2, 2), new(22, 2), new(1, 1), TravelMode.Land, [], 60).Position);
        var target = new MapOccupancy(new(22, 2), new(1, 1));
        Assert.Equal(new WorldPosition(7, 2), map.AdvanceIntoRange(new(2, 2), target, new(1, 1), TravelMode.Land, [], 60, 1).Position);
        Assert.Equal(new WorldPosition(7, 2), map.AdvanceIntoRange(new(2, 2), target, new(1, 1), TravelMode.Land, [], 60,
            _ => 1, true, 1).Position);
    }

    [Fact]
    public void DailyBudgetDoesNotCrossUnaffordableTerrainAndBlockedDayCanResume()
    {
        var map = new BattlefieldMap(2, 1, [0, 1], [new(0, 1, null), new(1, 4, null)]);
        var first = map.Advance(new(2, 2), new(7, 2), new(1, 1), TravelMode.Land, [], 5);
        Assert.Equal(new WorldPosition(4, 2), first.Position);
        Assert.Equal(2, first.Spent);
        Assert.False(first.Arrived);
        var blocked = map.Advance(first.Position, new(7, 2), new(1, 1), TravelMode.Land,
            [new(new(5, 2), new(1, 5))], 12);
        Assert.True(blocked.Reachable); // A dynamic collision is not proof that the coarse terrain is disconnected.
        Assert.False(blocked.Arrived);
        Assert.True(blocked.Position.X < 5);
        var resumed = map.Navigate(blocked.Position, new(new(7, 2), new(1, 1)), false, new(1, 1), TravelMode.Land, [], 100);
        Assert.True(resumed.Arrived);
        Assert.Equal(resumed.Steps.Sum(p => p.X < 5 ? 1 : 4), resumed.Spent);
        Assert.Equal(new WorldPosition(7, 2), resumed.Position);
    }

    [Fact]
    public void RouteGoesAroundWallAndReplansWhenGapIsBlocked()
    {
        var map = new BattlefieldMap(3, 3, [0, 1, 0, 0, 1, 0, 0, 0, 0],
            [new(0, 1, null), new(1, null, null)]);
        var route = map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land, []);
        Assert.True(route.Reachable);
        Assert.Equal(30, route.Cost);
        Assert.Contains(route.Steps, p => MapCoordinates.Cell(p).Y == 2);
        var blocked = map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land,
            [new(new(7, 12), new(5, 5))]);
        Assert.True(blocked.Reachable);
        var actual = map.Navigate(new(2, 2), new(new(12, 2), new(1, 1)), false, new(1, 1), TravelMode.Land,
            [new(new(7, 12), new(5, 5))], 100, mobility: 20);
        Assert.False(actual.Arrived);
        Assert.NotNull(actual.Collision);
        Assert.Equal(route.Steps.ToArray(), map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land, []).Steps.ToArray());
    }

    [Fact]
    public void MinimumCostCanTakeLongerPathAndTiesHaveStableOrder()
    {
        var map = new BattlefieldMap(3, 2, [0, 1, 0, 0, 0, 0], [new(0, 1, null), new(1, 20, null)]);
        var route = map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land, []);
        Assert.Equal(20, route.Cost);
        Assert.DoesNotContain(route.Steps, p => p.X is >= 5 and < 10 && p.Y < 5);
        var plain = new BattlefieldMap(1, 1, [0], [new(0, 1, null)]);
        var local = plain.Navigate(new(2, 2), new(new(3, 1), new(1, 1)), false, new(1, 1), TravelMode.Land, [], 10);
        Assert.True(local.Arrived);
        Assert.Equal(2, local.Spent);
        Assert.Equal(local.Steps.ToArray(), plain.Navigate(new(2, 2), new(new(3, 1), new(1, 1)), false,
            new(1, 1), TravelMode.Land, [], 10).Steps.ToArray());
    }

    [Fact]
    public void WholeFootprintMustFitAndAdjacentOccupantsDoNotCollide()
    {
        var map = new BattlefieldMap(2, 1, [0, 1], [new(0, 1, null), new(1, null, 2)]);
        Assert.Equal(1, map.PassageCost(new(4, 1), new(2, 2), TravelMode.Land, []));
        Assert.Null(map.PassageCost(new(5, 1), new(2, 2), TravelMode.Land, []));
        Assert.Null(map.PassageCost(new(10, 1), new(2, 2), TravelMode.Water, []));
        Assert.Equal(2, map.PassageCost(new(6, 1), new(2, 2), TravelMode.Water, []));
        Assert.Equal(1, map.PassageCost(new(1, 1), new(2, 2), TravelMode.Land, [new(new(3, 1), new(2, 2))]));
        Assert.Null(map.PassageCost(new(2, 1), new(2, 2), TravelMode.Land, [new(new(3, 1), new(2, 2))]));
        Assert.False(map.Navigate(new(2, 2), new(new(7, 2), new(1, 1)), false, new(1, 1), TravelMode.Land, [], 100).Arrived);
    }

    [Fact]
    public void TopologyCopiesInputsAndRejectsUnknownOrInvalidCosts()
    {
        int[] cells = [40];
        var map = new BattlefieldMap(1, 1, cells, [new(40, 3, null)]);
        cells[0] = 99;
        Assert.Equal(3, map.PassageCost(new(0, 0), new(1, 1), TravelMode.Land, []));
        Assert.Throws<ArgumentException>(() => new BattlefieldMap(1, 1, [99], [new(40, 3, null)]));
        Assert.Throws<ArgumentException>(() => new BattlefieldMap(1, 1, [40], [new(40, 0, null)]));
        Assert.True(map.FindRoute(new(2, 2), new(2, 2), new(1, 1), TravelMode.Land, []).Reachable);
        Assert.Throws<ArgumentException>(() => map.FindRoute(new(2, 2), new(2, 2), new(0, 1), TravelMode.Land, []));
    }
}
