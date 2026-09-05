using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapViewportTests
{
    [Fact]
    public void IncompleteAndSupersededTargetsNeverReplacePresentedView()
    {
        var first = new MapViewport(1024, 1024, 1);
        var state = new MapViewportTransition(first);
        Assert.False(state.TryPresent(_ => false));
        Assert.False(state.HasPresented);
        Assert.True(state.TryPresent(_ => true));
        var abandoned = new MapViewport(8000, 4000, .25);
        var latest = new MapViewport(9000, 3000, 2);
        state.Request(abandoned);
        Assert.False(state.TryPresent(_ => false));
        Assert.Equal(first, state.Presented);
        state.Request(latest);
        Assert.False(state.TryPresent(v => v == abandoned));
        Assert.Equal(first, state.Presented);
        Assert.True(state.TryPresent(v => v == latest));
        Assert.Equal(latest, state.Presented);
        Assert.False(state.IsPending);
    }

    [Fact]
    public void PreparedSeasonViewDoesNotLoseNewerNavigationRequest()
    {
        var first = new MapViewport(1024, 1024, 1);
        var latest = new MapViewport(9000, 3000, .5);
        var state = new MapViewportTransition(first);
        state.Request(latest);
        state.PresentPrepared(first);
        Assert.True(state.IsPending);
        Assert.Equal(latest, state.Requested);
        Assert.True(state.TryPresent(v => v == latest));
    }

    [Fact]
    public void ChunkCoverageUsesExclusiveRightEdgeAndCoversMinimumZoom()
    {
        var exact = new MapViewport(512, 384, 1);
        Assert.Equal(new[] { (0, 0), (1, 0), (0, 1), (1, 1) }, exact.Chunks(13056, 6656, 1024, 768));
        var minimum = new MapViewport(2048, 5120, .25);
        Assert.Equal(48, minimum.Chunks(13056, 6656, 1024, 768).Count());
    }

    [Theory]
    [InlineData(6496, -10000, 6496, 82)]
    [InlineData(6496, 20000, 6496, 6482)]
    [InlineData(-20000, 3282, 96, 3282)]
    [InlineData(30000, 3282, 12896, 3282)]
    public void OutsideWindowStopsAtPerimeterAtEveryZoom(double x, double y, double expectedX, double expectedY)
    {
        var squares = Enumerable.Repeat(new San9.Content.Maps.MapSquare(0, 4, 0, 0), 40000).ToArray();
        foreach (double zoom in new[] { .25, 1, 2 })
        {
            var result = new MapViewport(x, y, zoom).Clamp(squares);
            Assert.Equal(new MapViewport(expectedX, expectedY, zoom), result);
            Assert.Equal(result, result.Clamp(squares));
        }
    }

    [Fact]
    public void WindowSlidesContinuouslyAlongSlopedBoundaryWithoutGridSnap()
    {
        var squares = Enumerable.Repeat(new San9.Content.Maps.MapSquare(0, 1, 0, 0), 40000).ToArray();
        var center = MapGeometry.Center(0, 100);
        var a = new MapViewport(center.X - 20.25, center.Y - 15.5, 1).Clamp(squares);
        var b = new MapViewport(center.X - 20, center.Y - 15.625, 1).Clamp(squares);
        Assert.Equal(.25, b.X - a.X, 8);
        Assert.Equal(-.125, b.Y - a.Y, 8);
        Assert.NotEqual((double)center.X, a.X);
        Assert.Equal(a.X, a.Clamp(squares).X, 8);
        Assert.Equal(a.Y, a.Clamp(squares).Y, 8);
    }

    [Fact]
    public void InteriorPixelPositionIsPreserved()
    {
        var squares = Enumerable.Repeat(new San9.Content.Maps.MapSquare(0, 1, 0, 0), 40000).ToArray();
        var center = MapGeometry.Center(80, 70);
        var view = new MapViewport(center.X + 3.5, center.Y + 1.25, 1);
        Assert.Equal(view, view.Clamp(squares));
    }
}
