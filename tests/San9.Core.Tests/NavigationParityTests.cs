using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class NavigationParityTests
{
    [Fact]
    public void CoarseSquareEndpointUsesAssociatedBuildingWithoutAddingAvoidance()
    {
        var blocked = Enumerable.Range(9, 3).SelectMany(y => Enumerable.Range(9, 3).Select(x => new MapCell(x, y)));
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)], blocked);
        var city = new MapOccupancy(new(52, 52), new(25, 25)) { Hard = false };
        Assert.False(map.FindRoute(new(52, 52), new(82, 52), new(4, 4), TravelMode.Land, []).Reachable);
        Assert.True(map.FindRoute(new(52, 52), new(82, 52), new(4, 4), TravelMode.Land, [city]).Reachable);
        Assert.Null(map.PassageCost(new(52, 52), new(4, 4), TravelMode.Land, [city]));
    }

    [Fact]
    public void BlockedSourceFootprintCanLeaveButDoesNotOpenOtherTerrain()
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)], [new(10, 10)]);
        var step = map.Navigate(new(52, 52), new(new(82, 52), new(1, 1)), false, new(10, 10), TravelMode.Land, [], 100);
        Assert.NotEqual(new WorldPosition(52, 52), step.Position);
        Assert.Contains(step.Steps, p => p.X >= 55);
        Assert.Null(map.PassageCost(new(52, 52), new(10, 10), TravelMode.Land, []));
        var wall = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)], Enumerable.Range(0, 20).Select(y => new MapCell(10, y)));
        Assert.Equal(NavigationStatus.Unreachable,
            wall.Navigate(new(12, 52), new(new(82, 52), new(1, 1)), false, new(4, 4), TravelMode.Land, [], 100).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndpointRelaxationDoesNotOverrideObjectCollision(bool hard)
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)], [new(10, 10)]);
        var target = new MapOccupancy(new(52, 52), new(15, 15)) { Hard = hard };
        var position = new WorldPosition(32, 52);
        NavigationLookahead? stage = null;
        for (int day = 0; day < 15; day++)
        {
            var step = map.Navigate(position, target, false, new(4, 4), TravelMode.Land, [target], 100, lookahead: stage);
            if (hard) Assert.All(step.Steps, p => Assert.False(new MapOccupancy(p, new(4, 4)).Overlaps(target)));
            position = step.Position; stage = step.Lookahead;
        }
        Assert.Equal(!hard, position == target.Position);
    }

    [Fact]
    public void AttackGoalDoesNotTurnSoftObjectIntoHardCollision()
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)]);
        var target = new MapOccupancy(new(52, 52), new(10, 10)) { Hard = false, Avoidance = NavigationAvoidance.Hostile };
        var step = map.Navigate(new(42, 52), target, true, new(4, 4), TravelMode.Land, [target], 200, continueApproach: true);
        Assert.Contains(step.Steps, p => new MapOccupancy(p, new(4, 4)).Overlaps(target));
        var blocked = map.Navigate(new(42, 52), target, true, new(4, 4), TravelMode.Land, [target with { Hard = true }], 200, continueApproach: true);
        Assert.All(blocked.Steps, p => Assert.False(new MapOccupancy(p, new(4, 4)).Overlaps(target)));
    }

    [Fact]
    public void LargerUnusedShipDoesNotChangeLandNavigation()
    {
        var map = new BattlefieldMap(30, 30, new int[900], [new(0, 10, null)]);
        var land = new FormationRule(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land);
        var ship = new FormationRule(1, 100, 0, 100000, 1, new(10, 10), TravelMode.Water);
        var combined = map.ForTravel(land, ship, 100);
        MapOccupancy[] objects = [new(new(70, 65), new(8, 8)) { Hard = false, Avoidance = NavigationAvoidance.Friendly }];
        for (ulong seed = 0; seed < 20; seed++)
        {
            var a = map.Navigate(new(52, 52), new(new(112, 102), new(1, 1)), false, land.Footprint, TravelMode.Land, objects, 100, randomSeed: seed);
            var b = combined.Navigate(new(52, 52), new(new(112, 102), new(1, 1)), false, ship.Footprint, TravelMode.Land, objects, 100, randomSeed: seed);
            Assert.Equal(a.Steps.ToArray(), b.Steps.ToArray());
            Assert.Equal(a.Lookahead, b.Lookahead);
        }
    }

    [Fact]
    public void RelayCoarseRouteAvoidsFinalBuilding()
    {
        var map = new BattlefieldMap(25, 25, new int[625], [new(0, 10, null)]);
        var building = new MapOccupancy(new(52, 52), new(15, 15));
        var result = map.EstimateTravelCost(new(12, 52), new(new(92, 52), new(1, 1)), new(4, 4), TravelMode.Land,
            [], new(10000), intoRange: false, includeCells: true, excluded: building);
        Assert.Equal(RouteReachability.Reachable, result.Status);
        Assert.DoesNotContain(result.Cells, c => new MapOccupancy(MapCoordinates.Center(c), new(1, 1)).Overlaps(building));
        var arrival = map.EstimateTravelCost(new(12, 52), building, new(4, 4), TravelMode.Land,
            [], new(10000), intoRange: false, includeCells: true, excluded: building);
        Assert.Equal(RouteReachability.Reachable, arrival.Status);
        Assert.Equal(MapCoordinates.Cell(building.Position), arrival.Cells[^1]);
    }

    [Fact]
    public void GradientIncludesOriginalLowerAndRightEdgesWithoutFillingCorner()
    {
        var cells = new Dictionary<(int, int), int>();
        BattlefieldPathSearch.RasterizeGradient(new(new(12, 12), new(4, 4)), 25, 10,
            (x, y, cost) => cells[(x, y)] = Math.Max(cells.GetValueOrDefault((x, y)), cost));
        Assert.Equal(25, cells[(10, 14)]);
        Assert.Equal(25, cells[(14, 11)]);
        Assert.Equal(35, cells[(11, 11)]);
        Assert.False(cells.ContainsKey((14, 14)));
        Assert.False(cells.ContainsKey((14, 13)));
    }

    [Theory]
    [InlineData(true, 95)]
    [InlineData(false, 55)]
    public void ObstacleGradientStartsAtClippedWindowBoundary(bool hard, int expectedEdgeCost)
    {
        var cells = new Dictionary<(int, int), int>();
        BattlefieldPathSearch.RasterizeObstacle(
            new(new(10, 10), new(20, 20)) { Hard = hard, Avoidance = NavigationAvoidance.Friendly },
            new(new(15, 15), new(10, 10)), new(4, 4), 20,
            (x, y, cost) => cells[(x, y)] = cost);
        Assert.Equal(expectedEdgeCost, cells[(10, 15)]);
    }
}
