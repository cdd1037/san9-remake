using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class RegionHoverTests
{
    [Theory]
    [InlineData(300, 300)]
    [InlineData(347, 183)]
    public void HoverAtTheFarCornerReadsTheCurrentMapRowWidth(int width, int height)
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "hover", 190, 1, 1, 5,
            [new(5, "player", 5, 100)], [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])],
            new() { [99] = "region" }, new() { [99] = 15 })
        { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var view = San9.Application.GameSession.Start(scenario, 1).View;
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["city"] = "城市", ["region"] = "边角地域" });
        var squares = Enumerable.Repeat(new MapSquare(0, 1, 0, -1), width * height).ToArray();
        squares[^1] = new(0, 1, 0, 99);
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "hover", "spring", width, height,
            0, 0, squares, [], [], [], [], [], [], [], [], [], [], [], new([], []), []);
        Assert.Equal(new("边角地域", "城市"), RegionHoverInfo.Resolve(map, new(width - 1, height - 1), view, texts));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionHoverInfo.Resolve(map, new(width, height - 1), view, texts));
    }

    [Fact]
    public void RegionAndCityAreIndependentEvenWhenTheyAreTheSame()
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "fixture", 190, 1, 1, 5, [new(5,"player",5,100)], [new(15,"city",5,0,0,0,100,100,0,[])],
            new() { [15] = "city", [99] = "gate" }, new() { [15] = 15, [99] = 15 }) { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var view = San9.Application.GameSession.Start(scenario,1).View;
        var text = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new() { ["city"] = "洛阳", ["gate"] = "虎牢关" });
        Assert.Equal(new("洛阳", "洛阳"), RegionHoverInfo.Resolve(new(0, 0, 0, 15), view, text));
        Assert.Equal(new("虎牢关", "洛阳"), RegionHoverInfo.Resolve(new(0, 0, 0, 99), view, text));
        Assert.Equal(new("———", "——"), RegionHoverInfo.Resolve(new(0, 0, 0, 99) { HideRegionName = true }, view, text));
        Assert.Equal(new("———", "——"), RegionHoverInfo.Resolve(new(0, 0, 0, -1), view, text));
    }

    [Fact]
    public void RegionCanReferenceHighCityIdWithoutSharingItsNameEntry()
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "expanded", 190, 1, 1, 5,
            [new(5, "player", 5, 100)], [new(1000, "city", 5, 0, 0, 0, 100, 100, 0, [])],
            new() { [99] = "region" }, new() { [99] = 1000 }) { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var view = San9.Application.GameSession.Start(scenario, 1).View;
        string name = new('城', 100);
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new() { ["city"] = name, ["region"] = "地域" });
        Assert.Equal(new("地域", name), RegionHoverInfo.Resolve(new(0, 0, 0, 99), view, texts));
    }

    [Theory]
    [InlineData("请选择命令起点。", 388, 248)]
    [InlineData("ABC", 440, 144)]
    [InlineData("", 452, 120)]
    public void MessagePanelUsesLogicalCharacterColumns(string text, int x, int width)
    {
        Assert.Equal((x, width), MessagePanelGeometry.Bounds(text));
        Assert.Equal(512, x + width / 2);
    }
}
