using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class RegionHoverTests
{
    [Fact]
    public void RegionAndCityAreIndependentEvenWhenTheyAreTheSame()
    {
        var scenario = new ScenarioInitialState(11, "fixture", 190, 1, 1, 5, [new(5,"player",5,100,100,100)], [],
            new() { [15] = "city", [99] = "gate" }, new() { [15] = 15, [99] = 15 });
        var view = San9.Application.GameSession.Start(scenario,1).View;
        var text = new TextCatalog(3, new() { ["city"] = new("洛阳", "洛陽"), ["gate"] = new("虎牢关", "虎牢關") });
        Assert.Equal(new("洛阳", "洛阳"), RegionHoverInfo.Resolve(new(0, 0, 0, 15), view, text));
        Assert.Equal(new("虎牢关", "洛阳"), RegionHoverInfo.Resolve(new(0, 0, 0, 99), view, text));
        Assert.Equal(new("———", "——"), RegionHoverInfo.Resolve(new(0, 0, 0, 99) { HideRegionName = true }, view, text));
        Assert.Equal(new("———", "——"), RegionHoverInfo.Resolve(new(0, 0, 0, -1), view, text));
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
