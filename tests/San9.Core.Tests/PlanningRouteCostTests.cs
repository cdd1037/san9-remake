using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class PlanningRouteCostTests
{
    [Fact]
    public void ForecastUsesTerrainTimeWithoutChargingAvoidancePenalties()
    {
        var map = new BattlefieldMap(9, 1, Enumerable.Repeat(0, 9), [new(0, 2, null)]);
        var start = new WorldPosition(2, 2);
        var target = new MapOccupancy(new(42, 2), new(1, 1));
        MapOccupancy[] objects = [new(new(22, 2), new(5, 5)) { Hard = false, Avoidance = NavigationAvoidance.Facility }];
        var estimate = map.EstimateTravelCost(start, target, new(1, 1), TravelMode.Land, objects, new(10000), intoRange: false);
        Assert.Equal(RouteReachability.Reachable, estimate.Status);
        Assert.Equal(80, estimate.Cost);
        Assert.True(map.EstimateRouteCost(start, target, new(1, 1), TravelMode.Land, objects, new(10000), intoRange: false).Cost > estimate.Cost);
        Assert.Equal(80, map.FindRoute(start, target.Position, new(1, 1), TravelMode.Land, []).Cost);
    }

    [Fact]
    public void ForecastPreservesBarriersAndBudgetExhaustion()
    {
        var map = new BattlefieldMap(9, 3, Enumerable.Repeat(0, 27), [new(0, 1, null)]);
        var start = new WorldPosition(2, 7);
        var target = new MapOccupancy(new(42, 7), new(1, 1));
        MapOccupancy[] wall = [new(new(20, 7), new(1, 15))];
        Assert.Equal(RouteReachability.Unreachable, map.EstimateTravelCost(start, target, new(1, 1), TravelMode.Land,
            wall, new(10000), intoRange: false).Status);
        Assert.Equal(RouteReachability.Incomplete, map.EstimateTravelCost(start, target, new(1, 1), TravelMode.Land,
            [], new(1), intoRange: false).Status);
        Assert.Equal(RouteReachability.Unreachable, map.EstimateTravelCost(start, target, new(1, 1), TravelMode.Water,
            [], new(10000), intoRange: false).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForecastMayOverestimateClearanceButDoesNotChangeActualNavigation(bool formationTopology)
    {
        var blocked = Enumerable.Range(0, 5).Where(y => y != 2).Select(y => new MapCell(5, y));
        var map = new BattlefieldMap(13, 5, Enumerable.Repeat(0, 65), [new(0, 1, null)], blocked);
        if (formationTopology)
        {
            var formation = new FormationRule(0, 10, 0, 10000, 5, new(7, 7), TravelMode.Land);
            map = map.ForTravel(formation, formation, 10);
        }
        var start = new WorldPosition(7, 12);
        var target = new MapOccupancy(new(57, 12), new(1, 1));
        Assert.Equal(RouteReachability.Reachable, map.EstimateTravelCost(start, target, new(7, 7), TravelMode.Land,
            [], new(10000), intoRange: false).Status);
        Assert.False(map.FindRoute(start, target.Position, new(7, 7), TravelMode.Land, []).Reachable);
    }

    [Fact]
    public void CostSearchRespectsWallsAndDoesNotChangeSubsequentMovement()
    {
        var map = new BattlefieldMap(3, 3, [0, 1, 0, 0, 1, 0, 0, 0, 0],
            [new(0, 1, null), new(1, null, null)]);
        var start = new WorldPosition(2, 2);
        var target = new MapOccupancy(new(12, 2), new(1, 1));
        var route = map.FindRoute(start, target.Position, new(1, 1), TravelMode.Land, []);
        var estimate = map.EstimateRouteCost(start, target, new(1, 1), TravelMode.Land, [], new(100000),
            intoRange: false);
        Assert.Equal(RouteReachability.Reachable, estimate.Status);
        Assert.Equal(30, estimate.Cost);
        Assert.Equal(RouteReachability.Unreachable, map.EstimateRouteCost(start, target, new(1, 1), TravelMode.Land,
            [new(new(7, 12), new(5, 5))], new(100000), intoRange: false).Status);
        Assert.Equal(RouteReachability.Incomplete, map.EstimateRouteCost(start, target, new(1, 1), TravelMode.Land,
            [], new(1), intoRange: false).Status);
        var after = map.FindRoute(start, target.Position, new(1, 1), TravelMode.Land, []);
        Assert.Equal(route.Cost, after.Cost);
        Assert.Equal(route.Steps.ToArray(), after.Steps.ToArray());
    }

    [Fact]
    public void CostSearchCannotIgnoreUnitWidthOrWater()
    {
        var map = new BattlefieldMap(5, 3, Enumerable.Repeat(0, 15), [new(0, 1, null)]);
        MapOccupancy[] walls = [new(new(12, 3), new(1, 7)), new(new(12, 11), new(1, 7))];
        var start = new WorldPosition(2, 7);
        var target = new MapOccupancy(new(22, 7), new(1, 1));
        Assert.Equal(RouteReachability.Reachable, map.EstimateRouteCost(start, target, new(1, 1), TravelMode.Land,
            walls, new(100000), intoRange: false).Status);
        Assert.Equal(RouteReachability.Unreachable, map.EstimateRouteCost(start, target, new(3, 3), TravelMode.Land,
            walls, new(100000), intoRange: false).Status);
        Assert.Equal(RouteReachability.Unreachable, map.EstimateRouteCost(start, target, new(1, 1), TravelMode.Water,
            [], new(100000), intoRange: false).Status);
    }
}
