using System.Text.Json;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class TextureDependencyTests
{
    [Fact]
    public void DifferentMapInstancesShareTheSameResourceCatalogWithoutSerializingIt()
    {
        var image = new ImageAsset("building-5663-key", "images/buildings/one.png", new string('A', 64), 1, 1);
        var resources = new MapResources([], [image], [], [], [], new int[288], new int[16], [], new byte[32]);
        var first = new MapInstance("source", "first", "spring", 200, 200, 13056, 6656,
            [], [], [new(image.Id, 10, 20)], [], [], [], [], [], [], [], []);
        var second = first with { ScenarioId = "second", Bridges = [new(image.Id, 30, 40)] };
        var map1 = first.Resolve(resources);
        var map2 = second.Resolve(resources);
        Assert.Same(map1.Assets, map2.Assets);
        Assert.Equal(10, map1.Bridges[0].X);
        Assert.Equal(30, map2.Bridges[0].X);
        string json = JsonSerializer.Serialize(MapInstance.From(map1), PackageFiles.JsonOptions);
        Assert.DoesNotContain(".png", json);
        Assert.DoesNotContain("\"assets\"", json);
        Assert.DoesNotContain("\"seasons\"", json);
        var restored = JsonSerializer.Deserialize<MapInstance>(json, PackageFiles.JsonOptions)!.Resolve(resources);
        Assert.Same(image, restored.Assets[0]);
        Assert.Equal(map1.Bridges, restored.Bridges);
    }

    [Fact]
    public async Task ChangedTextureDependencyFailsBeforeInstallingContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "scenario"));
        Directory.CreateDirectory(Path.Combine(root, "textures"));
        try
        {
            // Dependency checking must reject a changed manifest before any texture or scenario is loaded.
            await File.WriteAllTextAsync(Path.Combine(root, "textures", "textures.json"), "changed");
            var map = new MapInstance("source", "first", "spring", 200, 200, 13056, 6656,
                [], [], [], [], [], [], [], [], [], [], []);
            var scenario = new San9.Content.Scenarios.ScenarioInitialState(11, "first", 190, 1, 1, 0, [], [], [], []);
            var content = new ScenarioContent(22, [], new("../textures", new string('0', 64)), map, scenario,
                new(3, []), []);
            await File.WriteAllTextAsync(Path.Combine(root, "scenario", "content.json"),
                JsonSerializer.Serialize(content, PackageFiles.JsonOptions));
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => ClientContentFiles.LoadAsync(Path.Combine(root, "scenario")));
            Assert.Contains("dependency changed", error.Message);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "textures")));
        }
        finally { Directory.Delete(root, true); }
    }
}
