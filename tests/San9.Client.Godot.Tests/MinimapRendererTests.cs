using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MinimapRendererTests
{
    [Theory]
    [InlineData(158, 2, 0, 3)]
    [InlineData(160, 2, 2, 2)]
    [InlineData(158, 3, 1, 4)]
    [InlineData(160, 80, 99, 99)]
    public void MinimapSamplesHonorOriginalSharedEdgeOwnership(int px, int py, int x, int y)
        => Assert.Equal((x, y), MinimapRenderer.SampleSquare(px, py));

    [Fact]
    public void MinimapUsesAllEightSourceTerrainFamiliesAndLeavesDecorationTransparent()
    {
        int[] expected = [0, 6, 1, 2, 1, 2, 3, 1, 2, 7, 4, 5, 7, 6, 6, 4, 4];
        Assert.Equal(expected, Enumerable.Range(0, 17).Select(MinimapRenderer.TerrainFamily));
        byte[] palette = new byte[32]; palette[0] = 80; palette[1] = 120; palette[2] = 160;
        var square = new San9.Content.Maps.MapSquare(0, 0, 0, 0);
        var map = MinimapRenderer.Compile(Enumerable.Repeat(square, 40000).ToArray(), palette);
        Assert.Equal((320, 160), (map.Width, map.Height));
        Assert.Equal(new byte[] { 80, 120, 160, 255 }, map.Pixels.AsSpan((80 * 320 + 160) * 4, 4).ToArray());
        Assert.Equal(0, map.Pixels[3]);
        Assert.Equal(0, map.Pixels[(159 * 320 + 319) * 4 + 3]);
    }

}
