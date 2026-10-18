using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MinimapRendererTests
{
    [Theory]
    [InlineData(200, 200)]
    [InlineData(300, 300)]
    [InlineData(347, 183)]
    public void MinimapUsesAllEightSourceTerrainFamiliesAndLeavesDecorationTransparent(int width, int height)
    {
        int[] expected = [0, 6, 1, 2, 1, 2, 3, 1, 2, 7, 4, 5, 7, 6, 6, 4, 4];
        Assert.Equal(expected, Enumerable.Range(0, 17).Select(MinimapRenderer.TerrainFamily));
        byte[] palette = new byte[32]; palette[0] = 80; palette[1] = 120; palette[2] = 160;
        var square = new San9.Content.Maps.MapSquare(0, 0, 0, 0);
        var map = MinimapRenderer.Compile(width, height, Enumerable.Repeat(square, width * height).ToArray(), palette);
        Assert.Equal((320, 160), (map.Width, map.Height));
        Assert.Equal(new byte[] { 80, 120, 160, 255 }, map.Pixels.AsSpan((80 * 320 + 160) * 4, 4).ToArray());
        Assert.Equal(0, map.Pixels[3]);
        Assert.Equal(0, map.Pixels[(159 * 320 + 319) * 4 + 3]);
    }

}
