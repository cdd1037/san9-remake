using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class LegacyTextureSkinUpgradeTests
{
    [Fact]
    public async Task RejectsInputAndOutputOverlapBeforeReadingOrWriting()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-legacy-overlap-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<ArgumentException>(() => LegacyTextureSkinUpgrade.UpgradeAsync(root, Path.Combine(root, "output")));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void RebuildsRolesFromConvertedImagesWithoutChangingInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-legacy-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "atlas.png"), new PngImage(48, 20, 4, new byte[48 * 20 * 4]).Encode());
            JsonObject Variant(int index, int palette) => new()
            {
                ["imageIndex"] = index,
                ["paletteIndex"] = palette,
                ["image"] = JsonSerializer.SerializeToNode(new ImageAsset($"image-{index}", "atlas.png", 48, 20), PackageFiles.JsonOptions)
            };
            var common = new JsonArray();
            foreach (int id in Enumerable.Range(475, 12)) common.Add(Variant(id, 65));
            var source = new JsonObject
            {
                ["schemaVersion"] = 5,
                ["sources"] = new JsonArray(),
                ["map"] = new JsonObject { ["units"] = new JsonObject() },
                ["portraits"] = new JsonArray(),
                ["skin"] = new JsonObject { ["schemaVersion"] = 62, ["assets"] = new JsonArray(), ["roles"] = new JsonObject() },
                ["library"] = new JsonObject { ["archives"] = new JsonArray(
                    new JsonObject { ["id"] = "g_common", ["variants"] = common },
                    new JsonObject { ["id"] = "g_marker", ["variants"] = new JsonArray(Variant(33, 4)) }) }
            };

            var prepared = LegacyTextureSkinUpgrade.Prepare(source, root);

            Assert.Equal(22, prepared.Images.Count);
            Assert.Equal(12, PngImage.Decode(prepared.Images["images/legacy-skin/map-object-row-left-3.png"]).Width);
            Assert.Equal(5, (int)prepared.Manifest["schemaVersion"]!);
            Assert.Equal(62, (int)source["skin"]!["schemaVersion"]!);
            Assert.Empty(source["skin"]!["assets"]!.AsArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
