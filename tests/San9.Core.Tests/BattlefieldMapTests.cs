using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class BattlefieldMapTests
{
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
        Assert.False(blocked.Reachable);
        Assert.Equal(first.Position, blocked.Position);
        Assert.Equal(0, blocked.Spent);
        var resumed = map.Advance(blocked.Position, new(7, 2), new(1, 1), TravelMode.Land, [], 12);
        Assert.True(resumed.Arrived);
        Assert.Equal(12, resumed.Spent);
        Assert.Equal(new WorldPosition(7, 2), resumed.Position);
    }

    [Fact]
    public void RouteGoesAroundWallAndReplansWhenGapIsBlocked()
    {
        var map = new BattlefieldMap(3, 3, [0, 1, 0, 0, 1, 0, 0, 0, 0],
            [new(0, 1, null), new(1, null, null)]);
        var route = map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land, []);
        Assert.True(route.Reachable);
        Assert.Equal(26, route.Cost);
        Assert.Contains(route.Steps, p => p.Y == 10);
        var blocked = map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land,
            [new(new(7, 12), new(5, 5))]);
        Assert.False(blocked.Reachable);
        Assert.Empty(blocked.Steps);
        Assert.Equal(route.Steps.ToArray(), map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land, []).Steps.ToArray());
    }

    [Fact]
    public void MinimumCostCanTakeLongerPathAndTiesHaveStableOrder()
    {
        var map = new BattlefieldMap(3, 2, [0, 1, 0, 0, 0, 0], [new(0, 1, null), new(1, 20, null)]);
        var route = map.FindRoute(new(2, 2), new(12, 2), new(1, 1), TravelMode.Land, []);
        Assert.Equal(16, route.Cost);
        Assert.DoesNotContain(route.Steps, p => p.X is >= 5 and < 10 && p.Y < 5);
        var plain = new BattlefieldMap(1, 1, [0], [new(0, 1, null)]);
        Assert.Equal(new WorldPosition[] { new(2, 1), new(3, 1) },
            plain.FindRoute(new(2, 2), new(3, 1), new(1, 1), TravelMode.Land, []).Steps.ToArray());
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
        Assert.False(map.FindRoute(new(2, 2), new(7, 2), new(1, 1), TravelMode.Land, []).Reachable);
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
