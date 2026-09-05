namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapDecorationCompilerTests
{
    [Theory]
    [InlineData(158, 2, 0, 3)]
    [InlineData(160, 2, 2, 2)]
    [InlineData(158, 3, 1, 4)]
    [InlineData(160, 80, 99, 99)]
    public void MinimapSamplesHonorOriginalSharedEdgeOwnership(int px, int py, int x, int y)
        => Assert.Equal((x, y), MinimapCompiler.SampleSquare(px, py));

    [Fact]
    public void MinimapUsesAllEightSourceTerrainFamiliesAndLeavesDecorationTransparent()
    {
        int[] expected = [0, 6, 1, 2, 1, 2, 3, 1, 2, 7, 4, 5, 7, 6, 6, 4, 4];
        Assert.Equal(expected, Enumerable.Range(0, 17).Select(MinimapCompiler.TerrainFamily));
        byte[] palette = new byte[1024]; palette[4] = 80; palette[5] = 120; palette[6] = 160;
        var square = new San9ScenarioSquareRecord(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var map = MinimapCompiler.Compile(Enumerable.Repeat(square, 40000).ToArray(), palette);
        Assert.Equal((320, 160), (map.Width, map.Height));
        Assert.Equal(new byte[] { 80, 120, 160, 255 }, map.Pixels.AsSpan((80 * 320 + 160) * 4, 4).ToArray());
        Assert.Equal(0, map.Pixels[3]);
        Assert.Equal(0, map.Pixels[(159 * 320 + 319) * 4 + 3]);
    }

    [Fact]
    public void BoundaryOverlapPrefersCityStyleAndElevationSelectsShape()
    {
        var square = new San9ScenarioSquareRecord(0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0x11);
        var sprite = Assert.Single(MapBoundaryCompiler.Compile([square]));
        Assert.Equal("boundary-10804", sprite.AssetId);
        var flat = Assert.Single(MapBoundaryCompiler.Compile([square with { ElevationTransitionMask = 0, OpaqueField0D = 1 }]));
        Assert.Equal("boundary-10797", flat.AssetId);
        Assert.Equal(flat.Y - 10, sprite.Y);
        Assert.Empty(MapBoundaryCompiler.Compile([square with { OpaqueField0D = 0 }]));
    }
}
