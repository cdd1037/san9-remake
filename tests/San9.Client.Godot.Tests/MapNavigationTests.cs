using San9.Content.Scenarios;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapNavigationTests
{
    [Fact]
    public void SelectionAndFilteredFacilitiesUseBuildingIdentityNotRegionMembership()
    {
        var presentation = new ScenarioInitialState(8, "fixture", 190, 1, 1, 5, [new(5,"player",5,100,100,100),new(6,"enemy",6,100,100,100)],
            [new(100, "port", 5, 80, 65, 1000, 300, 300, 50, []), new(15, "city", 5, 89, 64, 32000, 500, 1000, 50, []),
                new(16, "enemy", 6, 90, 60, 9000, 400, 600, 50, []), new(17, "empty", null, 88, 60, 0, 300, 600, 0, [])], [], []);
        var selection = new StrategySelection(San9.Application.GameSession.Start(presentation,1)); int changes = 0; selection.Changed += () => changes++;
        selection.SelectBuilding(100); Assert.Equal((80, 65), selection.Square); Assert.Equal(100, selection.BuildingId);
        selection.SelectSquare(89, 64); Assert.Equal(15, selection.BuildingId);
        selection.SelectSquare(88, 64); Assert.Null(selection.BuildingId);
        selection.Clear(); Assert.Null(selection.Square); Assert.Equal(4, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectBuilding(900));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectSquare(-1, 0));
        Assert.Equal(4, changes);
    }
    [Fact]
    public void EveryMinimapSquareRoundTripsAndDecorationDoesNotNavigate()
    {
        for (int x = 0; x < 200; x++) for (int y = 0; y < 200; y++)
        {
            var p = MinimapGeometry.FromGrid(x, y);
            Assert.True(MinimapGeometry.TryGrid(p.X, p.Y, out double rx, out double ry));
            Assert.Equal(x, rx, 8); Assert.Equal(y, ry, 8);
            var world = MapGeometry.Center(x, y); var mini = MinimapGeometry.FromWorld(world.X, world.Y);
            Assert.Equal(p.X, mini.U, 8); Assert.Equal(p.Y, mini.V, 8);
        }
        Assert.False(MinimapGeometry.TryGrid(0, 0, out _, out _));
        Assert.False(MinimapGeometry.TryGrid(1, 1, out _, out _));
        Assert.False(MinimapGeometry.TryGrid(double.NaN, .5, out _, out _));
    }
    [Fact]
    public void ViewportOutlineClipsToDiamondAtWorldEdges()
    {
        var inside = MinimapGeometry.ClipViewport(.45, .4, .55, .6);
        Assert.Equal(4, inside.Length);
        Assert.Empty(MinimapGeometry.ClipViewport(-2, -2, -1, -1));
        foreach (var p in MinimapGeometry.ClipViewport(-1, -1, 2, 2))
        {
            Assert.InRange(p.U + p.V, .5 - 1e-9, 1.5 + 1e-9);
            Assert.InRange(p.U - p.V, -.5 - 1e-9, .5 + 1e-9);
        }
    }
}
