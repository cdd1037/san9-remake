using San9.Content.Maps;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapDecorationCompilerTests
{
    [Fact]
    public void BoundariesFollowRegionCitiesAndUseElevationShapesWithoutDuplicateEdges()
    {
        MapSquare[] squares = [new(0, 1, 8, 1), new(0, 1, 0, 2)];
        var sprite = Assert.Single(MapLayoutCompiler.Boundaries(2, 1, squares, new Dictionary<int, int> { [1] = 1, [2] = 2 }));
        Assert.Equal("boundary-10801", sprite.AssetId);
        var flat = Assert.Single(MapLayoutCompiler.Boundaries(2, 1,
            [squares[0] with { ElevationMask = 0 }, squares[1]], new Dictionary<int, int> { [1] = 1, [2] = 1 }));
        Assert.Equal("boundary-10794", flat.AssetId);
        Assert.Equal(flat.Y - 10, sprite.Y);
        Assert.Empty(MapLayoutCompiler.Boundaries(2, 1, [squares[0], squares[1] with { RegionId = 1 }], new Dictionary<int, int>()));
    }

    [Fact]
    public void ImportRetainsAuthoredBridgeConstructionAndPassageInputs()
    {
        var source = new San9ScenarioSquareRecord(0, 0, 0x360b, 17, 0, 255, 1, 11, 255, 4, 1, 255);
        var cell = MapImporter.Cell(source);
        Assert.Equal(1, cell.BridgeOrientation);
        Assert.Equal(4, cell.TextureVariant);
        Assert.Equal(17, cell.RegionId);
        Assert.True(cell.HideRegionName);
        Assert.True(cell.MovementBlocked);
        Assert.False(cell.InfluenceBlocked);
        Assert.Equal(ConstructionSurface.General | ConstructionSurface.Horizontal | ConstructionSurface.Vertical, cell.Construction);
    }

    [Fact]
    public void ResourceThemePreservesSourceHeightSamplingOutsideTheRampRange()
    {
        var style = MapResourceCompiler.TerrainStyles()[3];
        var cell = new MapCellDefinition(3, 7, 5, null);
        Assert.True(style.Supports(cell));
        Assert.All(style.Levels.Single(l => l.Elevation == 7).Resources, resource => Assert.Equal(10198, resource));
    }
}
