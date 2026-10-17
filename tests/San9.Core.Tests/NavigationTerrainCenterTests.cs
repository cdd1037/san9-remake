using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class NavigationTerrainCenterTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(10)]
    public void WideUnitCanFollowTraversableCenterCorridor(int size)
    {
        var terrain = Enumerable.Repeat(1, 20 * 20).ToArray();
        for (int y = 0; y < 20; y++) terrain[y * 20 + 10] = 0;
        var map = new BattlefieldMap(20, 20, terrain, [new(0, 10, null), new(1, null, null)]);
        var position = new WorldPosition(52, 12);
        var target = new MapOccupancy(new(52, 82), new(1, 1));
        NavigationLookahead? stage = null;
        for (int day = 0; day < 30 && position != target.Position; day++)
        {
            var next = map.Navigate(position, target, false, new(size, size), TravelMode.Land, [], 60, lookahead: stage);
            Assert.NotEqual(position, next.Position);
            position = next.Position;
            stage = next.Lookahead;
        }
        Assert.Equal(target.Position, position);
    }

    [Fact]
    public void CenterTerrainDoesNotRemoveBodyCollisionOrCenterBounds()
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)]);
        var wall = new MapOccupancy(new(52, 52), new(10, 10));
        Assert.Null(map.PassageCost(new(44, 52), new(10, 10), TravelMode.Land, [wall]));
        Assert.Equal(10, map.PassageCost(new(2, 52), new(10, 10), TravelMode.Land, []));
        foreach (var outside in new WorldPosition[] { new(-1, 52), new(100, 52), new(52, -1), new(52, 100) })
            Assert.Null(map.PassageCost(outside, new(10, 10), TravelMode.Land, []));
        Assert.Equal(10, map.PassageCost(new(42, 52), new(10, 10), TravelMode.Land, [wall]));
    }

    [Theory]
    [InlineData(0, 0, 0, 10)]
    [InlineData(99, 0, 89, 0)]
    [InlineData(0, 99, 10, 99)]
    [InlineData(99, 99, 99, 89)]
    public void OverhangingBodiesCanMoveAlongEveryMapEdge(int x, int y, int endX, int endY)
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)]);
        var step = map.Navigate(new(x, y), new(new(endX, endY), new(1, 1)), false,
            new(10, 10), TravelMode.Land, [], 100);
        Assert.True(step.Arrived);
        Assert.Equal(new WorldPosition(endX, endY), step.Position);
        Assert.All(step.Steps, p => { Assert.InRange(p.X, 0, 99); Assert.InRange(p.Y, 0, 99); });
    }

    [Fact]
    public void SlowTerrainChargesOnlyWhenCenterEntersIt()
    {
        var terrain = new int[100];
        for (int y = 0; y < 10; y++) for (int x = 5; x < 10; x++) terrain[y * 10 + x] = 1;
        var map = new BattlefieldMap(10, 10, terrain, [new(0, 10, null), new(1, 12, null)]);
        Assert.Equal(10, map.PassageCost(new(24, 22), new(10, 10), TravelMode.Land, []));
        Assert.Equal(12, map.PassageCost(new(25, 22), new(10, 10), TravelMode.Land, []));
        var step = map.Navigate(new(24, 22), new(new(25, 22), new(1, 1)), false, new(10, 10), TravelMode.Land, [], 12);
        Assert.True(step.Arrived);
        Assert.Equal(12, step.Spent);
    }
}
