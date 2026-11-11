using System.Text.Json;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class TreasureIllustrationPackTests
{
    [Theory]
    [InlineData(-1, 6, 4)]
    [InlineData(0, 6, 4)]
    [InlineData(8, 0, 4)]
    [InlineData(8, 6, 1)]
    [InlineData(16385, 6, 4)]
    public void ImagesMustHaveValidDimensionsAndColorChannels(int width, int height, int channels)
    {
        var entry = new TreasureIllustration("test", "插画", new("source.png", width, height, channels));
        Assert.Throws<InvalidDataException>(() => (TreasureIllustrationPack.Empty with { Images = [entry] }).Validate());
    }

    [Fact]
    public void ResourcesHaveIndependentKeysAndRejectAmbiguousIdentitiesAndUnsafePaths()
    {
        var image = new TreasureIllustration("test", "插画", new("source.png", 8, 6, 4));
        var pack = TreasureIllustrationPack.Empty with { Images = [image, image with { Id = "other" }] };
        var catalog = pack.Resolve(Path.GetTempPath());
        Assert.NotEqual(catalog.Assets[0].Id, catalog.Assets[1].Id);
        Assert.Equal(catalog.Assets[0].File, catalog.Assets[1].File);
        Assert.Single(catalog.ImageSources);
        foreach (var invalid in new[] { pack with { Images = [image, image] },
            pack with { Images = [image with { Id = "../outside" }] },
            pack with { Images = [image with { Image = image.Image with { Path = "../outside.png" } }] },
            pack with { Images = [image with { Name = " " }] } })
            Assert.Throws<InvalidDataException>(invalid.Validate);
    }

    [Fact]
    public async Task TextureLoadingMergesAvatarAndTreasureImageSourcesWithoutChangingTheBaseManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-illustrations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ui = new ImageAsset("ui", "frame.png", 32, 32);
            var colors = Enumerable.Range(0, 52).Select(i => $"city-marker-{i}")
                .Concat(Enumerable.Range(0, 51).Select(i => $"overview-force-{i}"))
                .Concat(["roster-available", "roster-unavailable", "map-record-normal", "map-record-hover", "map-record-pressed", "map-speech-text", "map-speech-name-shadow"])
                .ToDictionary(s => s, _ => "F0F0F0");
            var skin = new InterfaceSkin(InterfaceSkin.CurrentSchemaVersion, [ui], InterfaceSkin.RequiredUiAssets.ToDictionary(s => s, _ => "ui"),
                colors, CursorDefinition.EdgeScrollRoles.ToDictionary(s => s, _ => new CursorDefinition("ui", 0, 0)));
            var textures = new TexturePackage(TexturePackage.CurrentSchemaVersion, 1, "test", [],
                new([], [], [], [], [], new int[288], new int[16], [], new byte[32]), skin, new(3, []), []);
            string manifest = Path.Combine(root, "textures.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(textures, PackageFiles.JsonOptions));
            var art = new TreasureIllustration("one", "插画", new("art.png", 8, 6, 4));
            var pack = TreasureIllustrationPack.Empty with { Images = [art] };
            await File.WriteAllBytesAsync(Path.Combine(root, "art.png"), new PngImage(8, 6, 4, new byte[8 * 6 * 4]).Encode());
            await File.WriteAllTextAsync(Path.Combine(root, "illustrations.json"), JsonSerializer.Serialize(pack, PackageFiles.JsonOptions));
            var avatar = new AuthoredPortrait("avatar", "头像", new("face.png", 240, 240, 3), new(0, 0), new(.1, .1));
            await File.WriteAllBytesAsync(Path.Combine(root, "face.png"), new PngImage(240, 240, 3, new byte[240 * 240 * 3]).Encode());
            await File.WriteAllTextAsync(Path.Combine(root, "portraits.json"), JsonSerializer.Serialize(PortraitPack.Empty with { Portraits = [avatar] }, PackageFiles.JsonOptions));
            byte[] baseline = await File.ReadAllBytesAsync(manifest);
            var loaded = await TexturePackageFiles.LoadAsync(root, portraitPackRoot: root, treasureIllustrationRoot: root);
            Assert.Equal(2, loaded.ImageSources.Count);
            Assert.Single(loaded.Portraits);
            var source = Assert.Single(loaded.TreasureIllustrations.Assets);
            Assert.Equal(art.Key, source.Id);
            Assert.Equal(Path.Combine(root, "art.png"), loaded.ImageSources[source.Path]);
            Assert.Equal(8, source.Width);
            Assert.Equal(6, source.Height);
            Assert.Equal(0, source.X);
            Assert.Equal(0, source.Y);
            Assert.Equal(baseline, await File.ReadAllBytesAsync(manifest));
        }
        finally { Directory.Delete(root, true); }
    }
}
