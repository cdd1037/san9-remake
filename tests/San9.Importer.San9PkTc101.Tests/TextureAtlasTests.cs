using San9.Content.Images;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TextureAtlasTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-atlas-" + Guid.NewGuid().ToString("N"));
    public TextureAtlasTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void NumberedStandaloneFilesStayStableAcrossPixelChangesAndRejectCollisions()
    {
        var first = Save("portrait", 4, 4, 20, null) with { Category = "portraits", FileId = 9 };
        var old = Assert.Single(TextureAtlasCompiler.Compile(_root, [first], "images"));
        Assert.Equal("images/portraits/9.png", old.Path);
        var changed = Save("portrait", 4, 4, 60, null) with { Category = "portraits", FileId = 9 };
        var current = Assert.Single(TextureAtlasCompiler.Compile(_root, [changed], "images"));
        Assert.Equal(old.Path, current.Path);
        Assert.Equal(old.File, current.File);
        Assert.Equal(ImageAssetFiles.LoadFile(_root, changed.Asset.File).Pixels,
            ImageAssetFiles.LoadFile(_root, current.File).Pixels);
        var equal = Save("equal", 4, 4, 60, null) with { Category = "portraits", FileId = 10 };
        var separate = TextureAtlasCompiler.Compile(_root, [changed, equal], "images");
        Assert.Equal(new[] { "images/portraits/9.png", "images/portraits/10.png" }, separate.Select(a => a.Path));
        var collision = Save("other", 4, 4, 90, null) with { Category = "portraits", FileId = 9 };
        Assert.Throws<InvalidDataException>(() => TextureAtlasCompiler.Compile(_root, [changed, collision], "images"));
    }
    private TextureAtlasCompiler.Source Save(string id, int width, int height, byte color, string? group)
    {
        var image = new PngImage(width, height, 4, Enumerable.Range(0, width * height * 4).Select(i => (byte)(color + i % 19)).ToArray());
        byte[] png = image.Encode(); string path = id + ".png"; File.WriteAllBytes(Path.Combine(_root, path), png);
        return new(new(id, path, width, height), group);
    }
    [Fact]
    public void AtlasRestoresEveryPixelAndExtrudesEdgesWithoutRotating()
    {
        var sources = new[] { Save("a", 7, 3, 20, "hud"), Save("b", 3, 9, 60, "hud"), Save("c", 4, 4, 90, "hud") };
        var original = ImageAssetFiles.Load(_root, sources.Select(s => s.Asset));
        var packed = TextureAtlasCompiler.Compile(_root, sources, "packed");
        Assert.Single(packed.Select(a => a.File).Distinct());
        var restored = ImageAssetFiles.Load(_root, packed);
        foreach (var asset in packed)
        {
            Assert.Equal(original[asset.Id].Pixels, restored[asset.Id].Pixels);
            Assert.Equal(original[asset.Id].Width, asset.Width);
            var page = ImageAssetFiles.LoadFile(_root, asset.File);
            for (int y = -2; y < asset.Height + 2; y++) for (int x = -2; x < asset.Width + 2; x++)
            {
                int from = (Math.Clamp(y, 0, asset.Height - 1) * asset.Width + Math.Clamp(x, 0, asset.Width - 1)) * 4;
                int to = ((asset.Y + y) * page.Width + asset.X + x) * 4;
                Assert.Equal(original[asset.Id].Pixels.AsSpan(from, 4).ToArray(), page.Pixels.AsSpan(to, 4).ToArray());
            }
        }
        var again = TextureAtlasCompiler.Compile(_root, sources.Reverse(), "packed");
        Assert.Equal(packed.OrderBy(a => a.Id), again.OrderBy(a => a.Id));
    }
    [Fact]
    public void DistinctResourcesKeepTheirFilesEvenWhenPixelsMatch()
    {
        ImageAsset Asset(string id, int channels, byte[] pixels)
        {
            var png = new PngImage(1, 1, channels, pixels).Encode();
            File.WriteAllBytes(Path.Combine(_root, id + ".png"), png);
            return new(id, id + ".png", 1, 1, channels);
        }
        var display = Asset("display", 4, [10, 20, 30, 255]);
        var catalog = Asset("catalog", 3, [10, 20, 30]);
        var translucent = Asset("translucent", 4, [10, 20, 30, 128]);
        var shared = TextureAtlasCompiler.Compile(_root, new[] { display, catalog, translucent }.Select(a => new TextureAtlasCompiler.Source(a, null)), "packed").ToDictionary(a => a.Id);
        Assert.NotEqual(shared[display.Id].File, shared[catalog.Id].File);
        Assert.Equal(catalog.Id, shared[catalog.Id].Id);
        Assert.NotEqual(shared[display.Id].File, shared[translucent.Id].File);
        ImageAssetFiles.ValidateCatalog(shared.Values);
    }

    [Fact]
    public void SmallAnimationGroupsDoNotSpreadAcrossAnAlmostEmptyMaximumPage()
    {
        var sources = Enumerable.Range(0, 64).Select(i => Save("frame" + i, 68, 52, (byte)i, "flags")).ToArray();
        var packed = TextureAtlasCompiler.Compile(_root, sources, "packed");
        var file = Assert.Single(packed.Select(a => a.File).Distinct());
        long paddedArea = 64L * 72 * 56;
        Assert.True((long)file.Width * file.Height <= paddedArea * 1.25, $"Sparse page: {file.Width}x{file.Height}, padded area {paddedArea}");
        ImageAssetFiles.ValidateCatalog(packed);
    }

    [Fact]
    public void PackingFillsVerticalGapsAndKeepsCategoryDirectories()
    {
        var sources = new[] { Save("tall", 1196, 1796, 20, "effects/dynamic"),
            Save("top", 796, 896, 60, "effects/dynamic"), Save("bottom", 796, 896, 90, "effects/dynamic") };
        var packed = TextureAtlasCompiler.Compile(_root, sources, "packed");
        Assert.Single(packed.Select(a => a.File).Distinct());
        Assert.All(packed, a => Assert.StartsWith("packed/effects/dynamic/", a.Path));
        ImageAssetFiles.ValidateCatalog(packed);
        var originals = ImageAssetFiles.Load(_root, sources.Select(s => s.Asset));
        var restored = ImageAssetFiles.Load(_root, packed);
        foreach (var asset in packed) Assert.Equal(originals[asset.Id].Pixels, restored[asset.Id].Pixels);
    }

    [Fact]
    public void CategoriesPortraitsAndOversizedImagesStayIndependent()
    {
        var sources = new[] { Save("a", 8, 8, 20, "hud"), Save("alias", 8, 8, 20, "window"),
            Save("portrait", 8, 8, 70, null), Save("wide", 2045, 1, 80, "hud"), Save("small", 4, 4, 90, "hud") };
        var packed = TextureAtlasCompiler.Compile(_root, sources, "packed").ToDictionary(a => a.Id);
        Assert.NotEqual(packed["a"].File, packed["alias"].File);
        foreach (string id in new[] { "portrait", "wide" })
        { Assert.Equal(0, packed[id].X); Assert.Equal(packed[id].Width, packed[id].File.Width); }
        Assert.Equal(packed["a"].File, packed["small"].File);
        ImageAssetFiles.ValidateCatalog(packed.Values);
    }
    [Fact]
    public void PagesSplitWithinMaximumDimensionsWithoutMergingEqualPixels()
    {
        var sources = new[] { Save("a", 1200, 1100, 20, "map"), Save("b", 1200, 1100, 60, "map"), Save("alias", 1200, 1100, 20, "map") };
        var packed = TextureAtlasCompiler.Compile(_root, sources, "packed");
        Assert.Equal(3, packed.Select(a => a.File).Distinct().Count());
        Assert.NotEqual(packed[0].File, packed[2].File);
        Assert.All(packed, a => { Assert.InRange(a.File.Width, 1, 2048); Assert.InRange(a.File.Height, 1, 2048); a.Validate(); });
    }
    public void Dispose() => Directory.Delete(_root, true);
}
