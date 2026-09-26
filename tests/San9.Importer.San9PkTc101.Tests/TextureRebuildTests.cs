using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TextureRebuildTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "san9-rebuild-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CursorReplacementRestoresMissingDefinitionsAndRemovesObsoleteAssets()
    {
        var unrelated = new ImageAsset("button", "images/button.png", 1, 1);
        var orphan = unrelated with { Id = "cursor-up", File = new("images/up.png", 1, 1, 4) };
        var obsolete = unrelated with { Id = "old-cursor", File = new("images/old.png", 1, 1, 4) };
        var corrected = orphan with { File = new(".sources/new.png", 2, 2, 4), Width = 2, Height = 2 };
        var skin = new InterfaceSkin(25, [unrelated, orphan, obsolete], [], [], new() { ["old"] = new(obsolete.Id, 0, 0) });
        var replaced = TexturePackageRebuilder.ReplaceCursors(skin, new() { ["up"] = new(corrected.Id, 1, 1) }, [corrected]);
        Assert.Equal(new[] { unrelated, corrected }, replaced.Assets);
        Assert.Equal(new CursorDefinition(corrected.Id, 1, 1), Assert.Single(replaced.Cursors).Value);
        Assert.Equal(skin.Roles, replaced.Roles);
        Assert.Equal(skin.Colors, replaced.Colors);
    }

    [Fact]
    public void SelectedImagesArePackedWithoutChangingOtherConsumersOfTheOldAtlas()
    {
        Directory.CreateDirectory(Path.Combine(root, "images"));
        Directory.CreateDirectory(Path.Combine(root, ".sources"));
        byte[] oldBytes = new PngImage(2, 1, 3, [10, 20, 30, 40, 50, 60]).Encode();
        File.WriteAllBytes(Path.Combine(root, "images", "shared.png"), oldBytes);
        File.WriteAllBytes(Path.Combine(root, ".sources", "0.png"), new PngImage(1, 1, 3, [90, 80, 70]).Encode());
        var first = new ImageAsset("library/first", new ImageFile("images/shared.png", 2, 1, 3), 0, 0, 1, 1);
        var second = first with { Id = "library/second", X = 1 };
        var replacement = new ImageAsset("ui-test", ".sources/0.png", 1, 1, 3);
        var package = new TexturePackage(5, 23, "test", [],
            new([], [], [], [], [], new int[288], new int[16], [], new byte[32]),
            new(25, [replacement], new() { ["test"] = replacement.Id }, [], []),
            new(3, [new("original", "interface", [new(0, 0, first), new(1, 0, second)])]), []);
        var result = TexturePackagePacking.Pack(root, package, "images/rebuilt/skin", default);
        Assert.Equal(new[] { first, second }, result.Library.Assets);
        Assert.Equal(oldBytes, File.ReadAllBytes(Path.Combine(root, "images", "shared.png")));
        var rebuilt = Assert.Single(result.Skin.Assets);
        Assert.StartsWith("images/rebuilt/skin/", rebuilt.Path);
        Assert.Equal(new byte[] { 90, 80, 70 }, ImageAssetFiles.Load(root, [rebuilt])[rebuilt.Id].Pixels);
        // A new invocation must read newly decoded pixels even when the source filename is unchanged.
        File.WriteAllBytes(Path.Combine(root, ".sources", "0.png"), new PngImage(1, 1, 3, [1, 2, 3]).Encode());
        var again = TexturePackagePacking.Pack(root, package, "images/rebuilt/skin", default);
        Assert.Equal(new byte[] { 1, 2, 3 }, ImageAssetFiles.Load(root, again.Skin.Assets)[rebuilt.Id].Pixels);
        Assert.Equal(oldBytes, File.ReadAllBytes(Path.Combine(root, "images", "shared.png")));
    }

    [Fact]
    public void FullBuildStillSharesAnExplicitSourceBetweenConsumers()
    {
        Directory.CreateDirectory(Path.Combine(root, ".sources"));
        File.WriteAllBytes(Path.Combine(root, ".sources", "0.png"), new PngImage(1, 1, 3, [10, 20, 30]).Encode());
        var image = new ImageAsset("ui-test", ".sources/0.png", 1, 1, 3);
        var package = new TexturePackage(5, 23, "test", [],
            new([], [], [], [], [], new int[288], new int[16], [], new byte[32]),
            new(25, [image], new() { ["test"] = image.Id }, [], []),
            new(3, [new("original", "interface", [new(0, 0, image with { Id = "library/test" })])]), []);
        var result = TexturePackagePacking.Pack(root, package, "images", default);
        Assert.Equal(Assert.Single(result.Skin.Assets).File, Assert.Single(result.Library.Assets).File);
        Assert.Equal(Assert.Single(result.Skin.Assets).X, Assert.Single(result.Library.Assets).X);
    }

    [Theory]
    [InlineData("skin")]
    [InlineData("cursors")]
    [InlineData("map")]
    [InlineData("portraits")]
    [InlineData("library/g_common")]
    public void RecognizesExplicitRebuildUnits(string scope) => TexturePackageRebuilder.ValidateScope(scope);

    [Theory]
    [InlineData("")]
    [InlineData("library/../../escape")]
    [InlineData("library/unknown")]
    [InlineData("skin/missing")]
    public void RejectsUnknownUnitsBeforeReadingOrWritingPackages(string scope) =>
        Assert.Throws<ArgumentException>(() => TexturePackageRebuilder.ValidateScope(scope));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
