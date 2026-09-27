using System.Text.Json;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class TextureDependencyTests
{
    [Fact]
    public void TextureMetadataSerializesWithoutDigests()
    {
        var image = new ImageAsset("portrait/0", "images/portraits/0.png", 240, 240, 3);
        var resources = new MapResources([], [], [], [], [], new int[288], new int[16], [], new byte[32]);
        var textures = new TexturePackage(5, 7, "source",
            [new("G_FaceL.s9", 123, new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc))],
            resources, new(33, [], [], [], []), new(3, [new("portrait", "portraits", [new(0, 0, image)])]),
            [new(0, image, 0, 0)]);
        string json = JsonSerializer.Serialize(textures, PackageFiles.JsonOptions);
        Assert.DoesNotContain("sha256", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<TexturePackage>(json, PackageFiles.JsonOptions)!;
        Assert.Equal(textures.Sources, restored.Sources);
        Assert.Equal(image, restored.Portraits[0].Image);
    }

    [Fact]
    public void DifferentMapInstancesShareTheSameResourceCatalogWithoutSerializingIt()
    {
        var image = new ImageAsset("building-5663-key", "images/buildings/one.png", 1, 1);
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
    public async Task InvalidTextureManifestFailsBeforeInstallingContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "scenario"));
        Directory.CreateDirectory(Path.Combine(root, "textures"));
        try
        {
            // References contain only a directory; malformed dependency content still fails loading.
            await File.WriteAllTextAsync(Path.Combine(root, "textures", "textures.json"), "changed");
            var map = new MapInstance("source", "first", "spring", 200, 200, 13056, 6656,
                [], [], [], [], [], [], [], [], [], [], []);
            var scenario = new San9.Content.Scenarios.ScenarioInitialState(11, "first", 190, 1, 1, 0, [], [], [], []);
            var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, [], new("../textures"), map, scenario,
                new(3, []), []);
            await ScenarioContentFiles.WriteAsync(Path.Combine(root, "scenario"), content);
            await Assert.ThrowsAsync<JsonException>(() => ClientContentFiles.LoadAsync(Path.Combine(root, "scenario")));
            using var dependency = JsonDocument.Parse(JsonSerializer.Serialize(content.Textures, PackageFiles.JsonOptions));
            Assert.Equal("path", Assert.Single(dependency.RootElement.EnumerateObject()).Name);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "textures")));
        }
        finally { Directory.Delete(root, true); }
    }
}
