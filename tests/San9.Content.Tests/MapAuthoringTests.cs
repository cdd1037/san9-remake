using San9.Content.Maps;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class MapAuthoringTests
{
    [Fact]
    public void BlockingDefaultsUseTerrainRulesHarborLayoutsAndNeighborClearance()
    {
        var cells = Enumerable.Repeat(new MapCellDefinition(42, 1, 0, null), 81).ToArray();
        cells[4 * 9 + 4] = new(70, 1, 0, null);
        cells[4 * 9 + 6] = new(71, 1, 0, null);
        var map = new MapDefinition(MapDefinition.CurrentSchemaVersion, "custom", 9, 9, cells, [])
        { BlockingDefaults = new([70, 71], [71], 2) };
        var generated = MapBlockingGenerator.Generate(map, [new MapCell(8, 6)]);
        Assert.True(generated[4 * 9 + 4].InfluenceBlocked);
        Assert.False(generated[4 * 9 + 6].InfluenceBlocked);
        Assert.True(generated[3 * 9 + 3].MovementBlocked);
        Assert.False(generated[3 * 9 + 2].MovementBlocked);
        Assert.True(generated[0].MovementBlocked);
        Assert.False(cells[4 * 9 + 4].InfluenceBlocked);
        var moved = MapBlockingGenerator.Generate(map, [new MapCell(8, 7)]);
        Assert.True(moved[4 * 9 + 6].InfluenceBlocked);
        Assert.True(moved[5 * 9 + 7].MovementBlocked);
    }

    [Fact]
    public void LocalBlockingGenerationRetainsRemoteOverridesAndUsesNeighborInfluenceOverrides()
    {
        var cells = Enumerable.Repeat(new MapCellDefinition(42, 1, 0, null), 49).ToArray();
        cells[3 * 7 + 3] = cells[3 * 7 + 3] with { InfluenceBlocked = true };
        cells[5 * 7 + 5] = cells[5 * 7 + 5] with { InfluenceBlocked = true, MovementBlocked = true };
        var map = new MapDefinition(MapDefinition.CurrentSchemaVersion, "custom", 7, 7, cells, []);
        var generated = MapBlockingGenerator.Generate(map, [], [3 * 7 + 2]);
        Assert.False(generated[3 * 7 + 2].InfluenceBlocked);
        Assert.True(generated[3 * 7 + 2].MovementBlocked);
        Assert.True(generated[3 * 7 + 3].InfluenceBlocked);
        Assert.Equal(cells[5 * 7 + 5], generated[5 * 7 + 5]);
        Assert.Equal(cells[0], generated[0]);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(2, 8)]
    public void SouthernDiagonalShoreUsesTheMaskFacingTheWater(int landX, int diagonalPart)
    {
        var cells = Enumerable.Repeat(new MapCellDefinition(0, 1, 0, null), 9).ToArray();
        cells[2 * 3 + landX] = new(1, 1, 0, null);
        var map = new MapDefinition(MapDefinition.CurrentSchemaVersion, "fixture", 3, 3, cells, []);
        var resources = new MapResources([], [], [], [], [], new int[288], new int[16], [], new byte[32])
        {
            TerrainStyles =
            [
                new(0, 0, false, [0], [new(1, Enumerable.Repeat(100, 16).ToArray())]) { WaterEdge = true },
                new(1, 1, false, [0], [new(1, Enumerable.Repeat(200, 16).ToArray())])
            ]
        };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 190, 1, 1, null, [], [], [], []);
        var display = MapLayoutCompiler.Compile(map, [], resources, scenario);
        int horizontalPart = landX == 0 ? 2 : 3;
        Assert.Equal(new[] { 10950 + horizontalPart, 10954, 10950 + diagonalPart },
            display.Terrain.Where(stamp => !stamp.IsCenter).Select(stamp => stamp.MaskId).Order());
    }

    [Fact]
    public void RectangularMapsRebuildElevationCornersWithoutChangingAuthorInputs()
    {
        var map = new MapDefinition(MapDefinition.CurrentSchemaVersion, "custom", 3, 2,
            [new(42, 1, 0, null), new(42, 2, 0, 7), new(42, 1, 0, null),
             new(42, 1, 0, null), new(42, 1, 0, null), new(42, 1, 0, null)], []);
        var before = map.Cells.ToArray();
        var squares = MapLayoutCompiler.Squares(map);
        Assert.Equal(8 | 2, squares[0].ElevationMask);
        Assert.Equal(0, squares[1].ElevationMask);
        Assert.Equal(1 | 8, squares[4].ElevationMask);
        Assert.Equal(before, map.Cells);
        var changed = map with { Cells = map.Cells.Select(c => c with { Elevation = 1 }).ToArray() };
        Assert.All(MapLayoutCompiler.Squares(changed), cell => Assert.Equal(0, cell.ElevationMask));
    }
}
