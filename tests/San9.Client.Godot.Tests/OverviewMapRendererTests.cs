using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class OverviewMapRendererTests
{
    [Theory]
    [InlineData(200, 200)]
    [InlineData(300, 300)]
    [InlineData(347, 183)]
    public void TerritoryDrawingPickingAndBordersFollowMembershipForArbitraryCityIds(int width, int height)
    {
        var squares = Enumerable.Range(0, width * height)
            .Select(i => new MapSquare(0, 0, 0, i % width < width / 2 ? 17 : 901)).ToArray();
        var renderer = new OverviewMapRenderer(width, height, squares, new Dictionary<int, int> { [17] = 730, [901] = 1200 });
        foreach (var (x, city) in new[] { (0, 730), (width - 1, 1200) })
        {
            var point = renderer.Geometry.FromGrid(x, height / 2);
            Assert.Equal(city, renderer.CityAt(point.X, point.Y));
        }
        Assert.Null(renderer.CityAt(0, 0));
        Assert.Single(renderer.Borders);
        var red = renderer.Territories(new Dictionary<int, OverviewColor> { [730] = new(240, 0, 0) });
        var blue = renderer.Territories(new Dictionary<int, OverviewColor> { [730] = new(0, 0, 240) });
        int coloredPixel = Enumerable.Range(0, red.Width * red.Height).First(i => red.Pixels[i * 4 + 3] != 0) * 4;
        Assert.Equal(240, red.Pixels[coloredPixel]);
        Assert.Equal(240, blue.Pixels[coloredPixel + 2]);
        Assert.Equal(0, red.Pixels[3]);
    }

    [Fact]
    public void ExcludedRegionSquaresAreNotColoredPickedOrJoinedToTheCityOutline()
    {
        MapSquare[] squares = [new(3, 0, 0, 17), new(16, 0, 0, 17) { HideRegionName = true },
            new(16, 0, 0, 17) { HideRegionName = true }, new(16, 0, 0, 901)];
        var renderer = new OverviewMapRenderer(4, 1, squares, new Dictionary<int, int> { [17] = 730, [901] = 1200 });
        var territories = renderer.Territories(new Dictionary<int, OverviewColor> { [730] = new(240, 0, 0), [1200] = new(200, 200, 200) });
        foreach (var (x, city) in new (int X, int? City)[] { (0, 730), (1, null), (2, null), (3, 1200) })
        {
            var point = renderer.Geometry.FromGrid(x, 0);
            Assert.Equal(city, renderer.CityAt(point.X, point.Y));
            int pixel = ((int)(point.Y * territories.Height) * territories.Width + (int)(point.X * territories.Width)) * 4;
            Assert.Equal(city is not null, territories.Pixels[pixel + 3] != 0);
        }
        // There is a coast at each visible area, but no border between excluded cells.
        Assert.Equal(2, renderer.Borders.Count);
        Assert.All(renderer.Borders, border => {
            var point = border.Points[0];
            double x = 2.5 * (point.X + point.Y) - 1;
            Assert.True(Math.Abs(x - .5) < 1e-8 || Math.Abs(x - 2.5) < 1e-8);
        });
    }

    [Fact]
    public void ClosedCityBoundaryRemainsContinuousAndDoesNotChangeTerritoryPicking()
    {
        var squares = Enumerable.Range(0, 9).Select(i => new MapSquare(0, 0, 0, i == 4 ? 17 : 901)).ToArray();
        var renderer = new OverviewMapRenderer(3, 3, squares, new Dictionary<int, int> { [17] = 730, [901] = 1200 });
        var border = Assert.Single(renderer.Borders);
        Assert.Equal(border.Points[0], border.Points[^1]);
        foreach (var (x, y, city) in new[] { (1, 1, 730), (0, 1, 1200), (1, 2, 1200) })
        {
            var point = renderer.Geometry.FromGrid(x, y);
            Assert.Equal(city, renderer.CityAt(point.X, point.Y));
        }
    }

    [Fact]
    public void BoundaryLoopReturningToAJunctionPreservesItsAnchor()
    {
        var squares = Enumerable.Range(0, 9)
            .Select(i => new MapSquare(0, 0, 0, i is 0 or 4 ? 17 : 901)).ToArray();
        var renderer = new OverviewMapRenderer(3, 3, squares,
            new Dictionary<int, int> { [17] = 730, [901] = 1200 });
        var point = renderer.Geometry.FromGrid(.5, .5);
        var junction = new OverviewPoint(point.X, point.Y);
        var loop = Assert.Single(renderer.Borders, b => b.Points[0] == b.Points[^1]);
        Assert.Equal(junction, loop.Points[0]);
        Assert.All(renderer.Borders, border =>
            Assert.True(border.Points[0] == junction || border.Points[^1] == junction));
    }

    [Fact]
    public void ThreeCityJunctionAnchorsAllThreePaths()
    {
        MapSquare[] squares = [new(0, 0, 0, 17), new(0, 0, 0, 901), new(0, 0, 0, 300), new(0, 0, 0, 300)];
        var renderer = new OverviewMapRenderer(2, 2, squares,
            new Dictionary<int, int> { [17] = 730, [901] = 1200, [300] = 1300 });
        var point = renderer.Geometry.FromGrid(.5, .5);
        var junction = new OverviewPoint(point.X, point.Y);
        Assert.Equal(3, renderer.Borders.Count);
        Assert.All(renderer.Borders, border => Assert.True(border.Points[0] == junction || border.Points[^1] == junction));
    }

    [Fact]
    public void EditingRegionMembershipChangesTerritoryAndSharedBoundary()
    {
        MapSquare[] squares = [new(0, 0, 0, 17), new(0, 0, 0, 901)];
        var divided = new OverviewMapRenderer(2, 1, squares, new Dictionary<int, int> { [17] = 730, [901] = 1200 });
        var joined = new OverviewMapRenderer(2, 1, squares, new Dictionary<int, int> { [17] = 730, [901] = 730 });
        var point = divided.Geometry.FromGrid(1, 0);
        Assert.Equal(1200, divided.CityAt(point.X, point.Y));
        Assert.Equal(730, joined.CityAt(point.X, point.Y));
        Assert.Single(divided.Borders);
        Assert.Empty(joined.Borders);
    }
}
