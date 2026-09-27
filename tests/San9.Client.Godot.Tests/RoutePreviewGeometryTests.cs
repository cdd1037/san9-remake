using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class RoutePreviewGeometryTests
{
    [Fact]
    public void PulseMovesTowardTargetAndCameraCullingDoesNotChangePhase()
    {
        Assert.Equal(RoutePreviewGeometry.Frame(0, 3), RoutePreviewGeometry.Frame(200, 4));
        Assert.Equal(RoutePreviewGeometry.Frame(0, 3), RoutePreviewGeometry.Frame(1600, 3));
        int[] route = Enumerable.Range(0, 20).Select(i => RoutePreviewGeometry.Frame(731, i)).ToArray();
        Assert.Equal(route.Skip(12), Enumerable.Range(12, 8).Select(i => RoutePreviewGeometry.Frame(731, i)));
    }
}
