using San9.Content.Maps;
using San9.Content.Packages;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapSurfaceTests
{
    [Fact]
    public void CellFiveFiveRemainsSelectableBesideHigherNeighbors()
    {
        const int width = 12, height = 12, target = 5 * width + 5;
        var cells = Enumerable.Repeat(new MapCellDefinition(2, 2, 2, null), width * height).ToArray();
        foreach (var (x, y) in new[] { (4, 4), (5, 4), (6, 4), (4, 5), (4, 6), (4, 7) })
            cells[y * width + x] = cells[y * width + x] with { Elevation = 3 };
        var squares = MapLayoutCompiler.Squares(new(MapDefinition.CurrentSchemaVersion, "fixture", width, height, cells, []));
        Assert.Equal(13, squares[target].ElevationMask);
        var map = new MapSurface(width, height, squares);
        var surface = map[target];
        foreach (var (dx, dy) in new[] { (0, 0), (-8.25, -10.25), (8.25, -10.25) })
            Assert.Equal(target, map.PickCell(surface.CenterX + dx, surface.CenterY + dy));
    }
}
