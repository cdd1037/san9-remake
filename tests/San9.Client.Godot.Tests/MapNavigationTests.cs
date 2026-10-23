using San9.Content.Scenarios;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapNavigationTests
{
    [Theory]
    [InlineData(300, 300)]
    [InlineData(347, 183)]
    public void SelectionUsesMilitaryMapBoundsAndKeepsItsStateAfterRejection(int width, int height)
    {
        var position = San9.Core.Domain.MapCoordinates.Center(new(width - 1, height - 1));
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "expanded", 190, 1, 1, 1,
            [new(1, "player", 0, 100)], [new(1, "city", 1, width - 1, height - 1, 0, 100, 100, 0, [])], [], [])
        {
            Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(100, 100)) { IsRoot = true }], []),
            Military = new(width, height, System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(0, width * height)),
                [new(0, 1, null)], [], [new(1, position, new(1, 1))])
        };
        var selection = new StrategySelection(San9.Application.GameSession.Start(scenario, 1));
        selection.SelectSquare(width - 1, height - 1);
        Assert.Equal(1, selection.BuildingId);
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectSquare(width, height - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectSquare(width - 1, height));
        Assert.Equal((width - 1, height - 1), selection.Square);
    }

    [Fact]
    public void SelectionAndFilteredFacilitiesUseBuildingIdentityNotRegionMembership()
    {
        var presentation = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 190, 1, 1, 5, [new(5,"player",5,100),new(6,"enemy",6,100)],
            [new(100, "port", 5, 80, 65, 1000, 300, 300, 50, []), new(15, "city", 5, 89, 64, 32000, 500, 1000, 50, []),
                new(16, "enemy", 6, 90, 60, 9000, 400, 600, 50, []), new(17, "empty", null, 88, 60, 0, 300, 600, 0, [])], [], []) { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }, new San9.Core.Domain.CorpsState(6, 6, null, new(100, 100)) { IsRoot = true }], []) };
        var selection = new StrategySelection(San9.Application.GameSession.Start(presentation,1)); int changes = 0; selection.Changed += () => changes++;
        selection.SelectBuilding(100); Assert.Equal((80, 65), selection.Square); Assert.Equal(100, selection.BuildingId);
        selection.SelectSquare(89, 64); Assert.Equal(15, selection.BuildingId);
        selection.SelectSquare(88, 64); Assert.Null(selection.BuildingId);
        selection.Clear(); Assert.Null(selection.Square); Assert.Equal(4, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectBuilding(900));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectSquare(-1, 0));
        Assert.Equal(4, changes);
    }
    [Theory]
    [InlineData(200, 200)]
    [InlineData(300, 300)]
    [InlineData(347, 183)]
    public void EveryMinimapSquareRoundTripsAndDecorationDoesNotNavigate(int width, int height)
    {
        var geometry = new MinimapGeometry(width, height);
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            var p = geometry.FromGrid(x, y);
            Assert.True(geometry.TryGrid(p.X, p.Y, out double rx, out double ry));
            Assert.Equal(x, rx, 8); Assert.Equal(y, ry, 8);
            var world = geometry.ToWorld(x, y); var mini = geometry.FromWorld(world.X, world.Y);
            Assert.Equal(p.X, mini.U, 8); Assert.Equal(p.Y, mini.V, 8);
        }
        Assert.False(geometry.TryGrid(0, 0, out _, out _));
        Assert.False(geometry.TryGrid(1, 1, out _, out _));
        Assert.False(geometry.TryGrid(double.NaN, .5, out _, out _));
    }
    [Fact]
    public void ViewportOutlineClipsToDiamondAtWorldEdges()
    {
        var geometry = new MinimapGeometry(200, 200);
        var inside = geometry.ClipViewport(.45, .4, .55, .6);
        Assert.Equal(4, inside.Length);
        Assert.Empty(geometry.ClipViewport(-2, -2, -1, -1));
        foreach (var p in geometry.ClipViewport(-1, -1, 2, 2))
        {
            Assert.InRange(p.U + p.V, .5 - 1e-9, 1.5 + 1e-9);
            Assert.InRange(p.U - p.V, -.5 - 1e-9, .5 + 1e-9);
        }
    }
}
