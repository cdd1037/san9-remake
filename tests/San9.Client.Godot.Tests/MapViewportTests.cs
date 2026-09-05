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
        var minimum = new MapViewport(-100, 10000, .01).Clamp(13056, 6656, 1024, 768);
        Assert.Equal(new MapViewport(2048, 5120, .25), minimum);
        var chunks = minimum.Chunks(13056, 6656, 1024, 768).ToArray();
        Assert.Equal(48, chunks.Length);
        Assert.All(chunks, p => { Assert.InRange(p.X, 0, 7); Assert.InRange(p.Y, 7, 12); });
    }
}
