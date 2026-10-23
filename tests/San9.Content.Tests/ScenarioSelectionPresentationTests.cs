using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Content.Tests;

public sealed class ScenarioSelectionPresentationTests
{
    [Fact]
    public void PreviewReferencesMustResolveWithinItsCatalogAndMap()
    {
        var sprite = new StartMenuSprite("image", 0, 0);
        var button = sprite with { Columns = 4 };
        ImageAsset[] images = [new("image", "image.png", 160, 40)];
        var entry = new ScenarioPreviewEntry("first", "history", 184, 2, "黄巾", "简介", new() { [7] = "green" });
        var scene = new ScenarioSelectionPresentation("选择", "提示", "返回", [sprite], sprite, new(sprite, 7), sprite,
            sprite, "image", sprite, button, [button], new() { [7] = new(4, 8) }, new() { ["green"] = "image" },
            [new("history", "一般", "提示", new(0, 0))], [entry]);
        scene.Validate(images);
        foreach (var invalid in new[]
        {
            scene with { Entries = [entry with { CityColors = new() { [8] = "green" } }] },
            scene with { Entries = [entry with { CityColors = new() { [7] = "unknown" } }] },
            scene with { Entries = [entry with { CategoryId = "unknown" }] },
            scene with { Entries = [entry, entry] },
            scene with { MarkerAssets = new() { ["green"] = "missing" } },
            scene with { Entries = [entry with { Month = 13 }] }
        })
            Assert.Throws<InvalidDataException>(() => invalid.Validate(images));
    }
}
