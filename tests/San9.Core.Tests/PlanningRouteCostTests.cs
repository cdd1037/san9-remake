using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class PlanningRouteCostTests
{
    [Fact]
    public void CoarseRouteCellsIncludeEndpointRegionsWithoutChangingCost()
    {
        var map = new BattlefieldMap(9, 1, Enumerable.Repeat(0, 9), [new(0, 2, null)]);
        var start = new WorldPosition(1, 2);
        var target = new MapOccupancy(new(43, 2), new(1, 1));
        var cost = map.EstimateTravelCost(start, target, new(1, 1), TravelMode.Land, [], new(10000), intoRange: false);
        using var diagnostics = new PathSearchDiagnostics();
        var route = map.EstimateTravelCost(start, target, new(1, 1), TravelMode.Land, [], new(10000), intoRange: false, includeCells: true);
        Assert.Equal(RouteReachability.Reachable, route.Status);
        Assert.Equal(cost.Cost, route.Cost);
        Assert.Equal(Enumerable.Range(0, 9).Select(x => new MapCell(x, 0)), route.Cells);
        Assert.Equal(1, diagnostics.Queries);
    }

    [Theory]
    [InlineData(20, 5)]
    [InlineData(5, 20)]
    public void SharedShorelineEndpointWeightsBothTerrainsBeforeTakingMaximum(int landSpeed, int waterSpeed)
    {
        var map = new BattlefieldMap(3, 3, Enumerable.Range(0, 9).Select(i => i % 3 == 0 ? 0 : 1),
            [new(0, 1, null), new(1, null, 1) { IsWater = true }]);
        var land = new FormationRule(0, landSpeed, 0, 1000, 1, new(3, 1), TravelMode.Land);
        var ship = land with { Id = 1, MovementBudget = waterSpeed, TravelMode = TravelMode.Water };
        var travel = map.ForTravel(land, ship, 20);
        var start = new WorldPosition(3, 7);
        var target = new MapOccupancy(new(4, 7), new(1, 1));
        var shared = travel.PlanningMap().EstimateTravelCost(start, target, land.Footprint,
            TravelMode.Land, [], new(65536), intoRange: false);
        var actual = travel.EstimateTravelCost(start, target, land.Footprint,
            TravelMode.Land, [], new(65536), intoRange: false);
        Assert.Equal(RouteReachability.Reachable, shared.Status);
        Assert.Equal(4, actual.Cost);
        Assert.Equal(actual.Cost, travel.Reweight(shared.TerrainCost));
    }

    [Fact]
    public void SharedGeometryRetainsLandAndWaterCostsForDifferentSpeedRatios()
    {
        var terrain = Enumerable.Range(0, 33).Select(i => i / 11 == 1 && i % 11 is >= 3 and <= 7 ? 1 : 0);
        var map = new BattlefieldMap(11, 3, terrain, [new(0, 1, null), new(1, null, 1) { IsWater = true }]);
        var land = new FormationRule(0, 20, 0, 1000, 1, new(1, 1), TravelMode.Land);
        var ship = land with { Id = 1, MovementBudget = 5, TravelMode = TravelMode.Water };
        var travel = map.ForTravel(land, ship, 20);
        var shared = travel.PlanningMap();
        var cost = shared.EstimateTravelCost(new(2, 7), new(new(52, 7), new(1, 1)),
            new(1, 1), TravelMode.Land, [], new(65536), intoRange: false);
        Assert.Equal(RouteReachability.Reachable, cost.Status);
        Assert.Equal(cost.Cost, cost.TerrainCost.Land + cost.TerrainCost.Water);
        Assert.True(cost.TerrainCost.Water > 0);
        Assert.Equal(cost.TerrainCost.Land + 4 * cost.TerrainCost.Water, travel.Reweight(cost.TerrainCost));
        var waterFast = map.ForTravel(land with { MovementBudget = 5 }, ship with { MovementBudget = 20 }, 20);
        Assert.Equal(4 * cost.TerrainCost.Land + cost.TerrainCost.Water, waterFast.Reweight(cost.TerrainCost));
        Assert.Same(shared, waterFast.PlanningMap());
    }

    [Fact]
    public void ForecastEndpointCostsDoNotLeakBetweenModesMapsOrObstacles()
    {
        var map = new BattlefieldMap(3, 3, Enumerable.Repeat(0, 9), [new(0, 2, 3)]);
        var other = new BattlefieldMap(2, 2, Enumerable.Repeat(0, 4), [new(0, 7, null)]);
        var start = new WorldPosition(1, 1);
        var target = new MapOccupancy(new(3, 3), new(1, 1));
        RouteCostEstimate Estimate(BattlefieldMap field, TravelMode mode, params MapOccupancy[] objects) =>
            field.EstimateTravelCost(start, target, new(1, 1), mode, objects, new(10000), intoRange: false);

        Assert.Equal(8, Estimate(map, TravelMode.Land).Cost);
        Assert.Equal(12, Estimate(map, TravelMode.Water).Cost);
        Assert.Equal(28, Estimate(other, TravelMode.Land).Cost);
        Assert.Equal(RouteReachability.Unreachable, Estimate(map, TravelMode.Land, target).Status);
        var restored = Estimate(map, TravelMode.Land);
        Assert.Equal(RouteReachability.Reachable, restored.Status);
        Assert.Equal(8, restored.Cost);
    }

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
