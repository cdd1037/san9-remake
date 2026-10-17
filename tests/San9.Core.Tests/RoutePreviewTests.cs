using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class RoutePreviewTests
{
    [Fact]
    public void PreviewShowsCoarseIntentAndMovementStillRespectsTargetCollision()
    {
        var map = new BattlefieldMap(4, 4, Enumerable.Repeat(0, 16), [new(0, 1, null)]);
        var start = MapCoordinates.Center(new(0, 0));
        var target = new MapOccupancy(MapCoordinates.Center(new(3, 0)), new(3, 3));
        var wall = new MapOccupancy(new(7, 4), new(3, 8));
        var route = map.FindRouteIntoRange(start, target, new(1, 1), TravelMode.Land, [wall]);
        Assert.True(route.Reachable); Assert.NotEmpty(route.Steps);
        Assert.Equal(MapCoordinates.Cell(target.Position), MapCoordinates.Cell(route.Steps[^1]));
        var movement = map.Navigate(start, target, true, new(1, 1), TravelMode.Land, [wall, target], 100, mobility: 100);
        Assert.All(movement.Steps, p => { Assert.False(new MapOccupancy(p, new(1, 1)).Overlaps(target)); Assert.False(new MapOccupancy(p, new(1, 1)).Overlaps(wall)); });
        var blocked = map.FindRouteIntoRange(start, target, new(1, 1), TravelMode.Land, [new(new(7, 10), new(3, 20))]);
        Assert.True(blocked.Reachable); // Dynamic obstacles are coarse costs; they do not prove disconnection.
    }
}
