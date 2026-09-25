using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapFrameTransitionTests
{
    private sealed class Frame : IDisposable
    {
        public bool Terrain, Objects;
        public Exception? Error;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public void BothResourceSetsMustBeReadyAndRetirementFollowsCommit()
    {
        var retired = new List<Frame>();
        using var view = new MapFrameTransition<Frame>(f => f.Terrain && f.Objects, f => f.Error, retired.Add);
        var first = new Frame { Terrain = true, Objects = true };
        view.Request(first); Assert.True(view.TryCommit());
        var next = new Frame { Terrain = true };
        view.Request(next); Assert.False(view.TryCommit()); Assert.Same(first, view.Presented); Assert.Empty(retired);
        next.Objects = true; Assert.True(view.TryCommit()); Assert.Same(next, view.Presented);
        Assert.Same(first, Assert.Single(retired)); Assert.False(first.Disposed);
    }
    [Fact]
    public void SupersededAndFailedRequestsCannotReplaceEitherViewsPresentedFrame()
    {
        using var main = new MapFrameTransition<Frame>(f => f.Terrain && f.Objects, f => f.Error, f => f.Dispose());
        using var preview = new MapFrameTransition<Frame>(f => f.Terrain && f.Objects, f => f.Error, f => f.Dispose());
        var mainFrame = new Frame { Terrain = true, Objects = true }; main.Request(mainFrame); main.TryCommit();
        var first = new Frame { Terrain = true, Objects = true }; preview.Request(first); preview.TryCommit();
        var abandoned = new Frame(); preview.Request(abandoned);
        var latest = new Frame { Error = new InvalidDataException("failed") }; preview.Request(latest);
        Assert.True(abandoned.Disposed); abandoned.Terrain = abandoned.Objects = true;
        Assert.False(preview.TryCommit()); Assert.True(latest.Disposed);
        Assert.Same(first, preview.Presented); Assert.Same(mainFrame, main.Presented);
        Assert.False(first.Disposed); Assert.False(mainFrame.Disposed);
        preview.Dispose(); Assert.True(first.Disposed); Assert.False(mainFrame.Disposed);
    }
}
