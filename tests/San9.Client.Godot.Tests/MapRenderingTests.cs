using San9.Content.Images;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapRenderingTests
{
    [Fact]
    public void DisplayElevationPreservesCentersAndInterpolatesBothAxesAtMapEdges()
    {
        MapSquare[] squares = [new(0, 1, 0, 0), new(0, 3, 0, 0), new(0, 5, 0, 0), new(0, 9, 0, 0)];
        var origin = MapGeometry.Center(0, 0);
        double At(double x, double y) => MapGeometry.SampleElevation(origin.X + 32 * (x - y), origin.Y + 16 * (x + y), 2, 2, squares);
        Assert.Equal(1, At(0, 0)); Assert.Equal(9, At(1, 1));
        Assert.Equal(4.5, At(.5, .5));
        Assert.Equal(1, At(-1, -1)); Assert.Equal(9, At(2, 2));
        Assert.Equal(2, At(.5, -1)); Assert.Equal(7, At(.5, 2));
    }

    [Fact]
    public void EdgeBlendKeepsWeightsCoverageAndChannelOrder()
    {
        byte[] weights = [0, 1, 128, 255, 0];
        Dictionary<string, PngImage> common = new()
        {
            ["weight"] = new(5, 1, 1, weights), ["coverage"] = new(5, 1, 1, [255, 255, 255, 255, 0]),
            ["cloud-fill"] = new(1, 1, 3, [0, 0, 0])
        };
        var map = new MapPackage(16, "fixture", "fixture", "spring", 200, 200, 13056, 6656, [], [], [], [new(1, "weight", "coverage")],
            [new(1, 1, 510, 10, 0, 0, true), new(2, 1, 510, 10, 0, 0, false)], [], [], [], [],
            Enumerable.Repeat<int?>(0, 6656).ToArray(), Enumerable.Repeat<int?>(13056, 6656).ToArray(), [], new([], []), []);
        var renderer = new TerrainRenderer(map, common, new()
        {
            [1] = new(128, 1, 3, Enumerable.Range(0, 128).SelectMany(_ => new byte[] { 100, 20, 250 }).ToArray()),
            [2] = new(128, 1, 3, Enumerable.Range(0, 128).SelectMany(_ => new byte[] { 200, 80, 10 }).ToArray())
        });
        var chunks = new[] { renderer.RenderChunk(0, 0), renderer.RenderChunk(1, 0) };
        byte[][] expected = [[200, 80, 10], [199, 79, 10], [150, 50, 130], [100, 20, 249], [0, 0, 0]];
        for (int i = 0; i < 5; i++)
        {
            var chunk = chunks[(510 + i) / 512]; int offset = (10 * chunk.Width + (510 + i) % 512) * 3;
            Assert.Equal(expected[i], chunk.Pixels[offset..(offset + 3)]);
        }
    }
    [Fact]
    public void EverySquareCenterRoundTripsAndOutsideDiamondIsRejected()
    {
        for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++)
        {
            var p = MapGeometry.Center(x, y); Assert.True(MapGeometry.TrySquare(p.X, p.Y, out int rx, out int ry));
            Assert.Equal(x, rx); Assert.Equal(y, ry);
        }
        Assert.False(MapGeometry.TrySquare(0, 0, out _, out _));
    }
    [Fact]
    public void StampCrossingChunkBoundaryKeepsGlobalSamplePhaseAndCenterSpan()
    {
        byte[] texture = Enumerable.Range(0, 128).SelectMany(i => new byte[] { (byte)i, (byte)i, (byte)i }).ToArray();
        byte[] coverage = Enumerable.Repeat((byte)255, 64).ToArray(); coverage[0] = 0; coverage[3] = 0;
        Dictionary<string, PngImage> common = new() { ["weight"] = new(64, 1, 1, new byte[64]), ["coverage"] = new(64, 1, 1, coverage), ["cloud-fill"] = new(1, 1, 3, [0, 0, 0]) };
        var stamp = new TerrainStamp(1, 1, 500, 10, -99, 0, true);
        var map = new MapPackage(16, "fixture", "fixture", "spring", 200, 200, 13056, 6656, [], [], [], [new(1, "weight", "coverage")], [stamp], [], [], [], [], Enumerable.Repeat<int?>(0, 6656).ToArray(), Enumerable.Repeat<int?>(13056, 6656).ToArray(), [], new([], []), []);
        var renderer = new TerrainRenderer(map, common, new() { [1] = new(128, 1, 3, texture) });
        var a = renderer.RenderChunk(0, 0); var b = renderer.RenderChunk(1, 0);
        for (int mx = 1; mx < 64; mx++)
        {
            int worldX = 500 + mx; var image = worldX < 512 ? a : b; int x = worldX % 512;
            Assert.Equal((byte)(MapGeometry.Mod(-98, 64) + mx - 1), image.Pixels[(10 * image.Width + x) * 3]);
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => renderer.RenderChunk(0, 0, cancel.Token));
    }
}
