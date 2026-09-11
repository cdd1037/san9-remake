namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapDecorationCompilerTests
{
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
