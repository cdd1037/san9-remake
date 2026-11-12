using System.Text.Json;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Content.Tests;

public sealed class TextureDependencyTests
{
    [Fact]
    public void TextureMetadataStoresSourceFingerprintsWithoutImageDigests()
    {
        var image = new ImageAsset("portrait/0", "images/portraits/0.png", 240, 240, 3);
        var resources = new MapResources([], [], [], [], [], new int[288], new int[16], [], new byte[32]);
        var textures = new TexturePackage(TexturePackage.CurrentSchemaVersion, 7, "source",
            [new("G_FaceL.s9", 123, new string('0', 64))],
            resources, new(InterfaceSkin.CurrentSchemaVersion, [], [], [], []), new(3, [new("portrait", "portraits", [new(0, 0, image)])]),
            [new(0, image, new(0, 0), new(32d / 240, 30d / 240))]);
        string json = JsonSerializer.Serialize(textures, PackageFiles.JsonOptions);
        Assert.Contains("sha256", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastWriteTimeUtc", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<TexturePackage>(json, PackageFiles.JsonOptions)!;
        Assert.Equal(textures.Sources, restored.Sources);
        Assert.Equal(image, restored.Portraits[0].Image);
    }

    [Fact]
    public void DifferentMapInstancesShareTheSameResourceCatalogWithoutSerializingIt()
    {
        var image = new ImageAsset("bridge-0", "images/bridge.png", 1, 1);
        var resources = new MapResources([], [image], [], [], [], new int[288], new int[16], [], new byte[32])
            { TerrainStyles = [new(0, 1, true, [0], [new(1, Enumerable.Repeat(10006, 16).ToArray())])] };
        var first = new MapInstance(new(MapDefinition.CurrentSchemaVersion, "source", 2, 1,
            [new(0, 1, 0, null) { BridgeOrientation = 0 }, new(0, 1, 0, null)], []), []);
        var second = first with { Definition = first.Definition with { Cells =
            [new(0, 1, 0, null), new(0, 1, 0, null) { BridgeOrientation = 0 }] } };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "first", 184, 2, 1, null, [], [], [], []);
        var map1 = first.Resolve(resources, scenario);
        var map2 = second.Resolve(resources, scenario with { ScenarioId = "second", Month = 7 });
        Assert.Same(map1.Assets, map2.Assets);
        Assert.Equal("first", map1.ScenarioId);
        Assert.Equal("second", map2.ScenarioId);
        Assert.Equal("spring", map1.InitialSeason);
        Assert.Equal("autumn", map2.InitialSeason);
        Assert.Empty(map1.Sources);
        Assert.Equal(32, map1.Bridges[0].X);
        Assert.Equal(64, map2.Bridges[0].X);
        string json = JsonSerializer.Serialize(MapInstance.From(map1), PackageFiles.JsonOptions);
        Assert.DoesNotContain(".png", json);
        Assert.DoesNotContain("\"assets\"", json);
        Assert.DoesNotContain("\"seasons\"", json);
        var restored = JsonSerializer.Deserialize<MapInstance>(json, PackageFiles.JsonOptions)!.Resolve(resources, scenario);
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
            var map = new MapInstance(new(MapDefinition.CurrentSchemaVersion, "source", 200, 200, [], []), []);
            var scenario = new San9.Content.Scenarios.ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "first", 190, 1, 1, 0, [], [], [], []);
            var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, new("../textures"), map, scenario,
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
