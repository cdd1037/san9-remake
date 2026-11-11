using San9.Application.Authoring;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Application.Tests;

public sealed partial class TreasureDataEditorWorkspaceTests
{
    private static PngImage IllustrationPixels()
    {
        var pixels = Enumerable.Range(0, 8 * 6 * 4).Select(i => (byte)(i % 256)).ToArray();
        pixels[3] = 0; pixels[7] = 127;
        return new(8, 6, 4, pixels);
    }

    [Fact]
    public async Task ImagesAndDefinitionsSaveTogetherAndReplacementPreservesTheCopiedReference()
    {
        await Create();
        string imagesRoot = Path.Combine(_root, "user-images");
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        var images = workspace.Illustrations!;
        var pixels = IllustrationPixels();
        var image = images.Add("插画", pixels);
        workspace.SetTreasure(workspace.GetTreasure(901) with { ImageKey = image.Key });
        Assert.False(File.Exists(images.ManifestPath));
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        var copy = await workspace.CreateTreasureAsync(901);
        var replacement = new PngImage(3, 9, 4, new byte[3 * 9 * 4]);
        var edited = images.Add("替换插画", replacement);
        workspace.SetTreasure(copy with { ImageKey = edited.Key });
        Assert.NotEqual(image.Key, edited.Key);
        Assert.NotEqual(image.Image, edited.Image);
        Assert.Equal(image.Key, workspace.GetTreasure(901).ImageKey);
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        Assert.Equal(edited.Key, reopened.GetTreasure(copy.Id).ImageKey);
        Assert.Equal(replacement.Pixels, reopened.Illustrations!.ReadImage(edited.Key).Pixels);
        Assert.Equal(pixels.Pixels, reopened.Illustrations.ReadImage(image.Key).Pixels);
        Assert.Equal(4, reopened.Illustrations.ReadImage(image.Key).Channels);
        Assert.Equal(2, reopened.Illustrations.Pack.Images.Length);
        Assert.Equal(2, Directory.GetFiles(imagesRoot, "*.png", SearchOption.AllDirectories).Length);
        var catalog = (await TreasureIllustrationPackFiles.LoadAsync(imagesRoot)).Resolve(imagesRoot);
        ImageAssetFiles.ValidateCatalog(catalog.Assets);
        var full = catalog.Assets.Single(a => a.Id == edited.Key);
        Assert.Equal(3, full.Width);
        Assert.Equal(9, full.Height);
        Assert.Equal(0, full.X);
        Assert.Equal(0, full.Y);
        var content = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"));
        Assert.Equal(edited.Key, content.Scenario.Information.Treasures.Single(t => t.Id == copy.Id).ImageKey);
        workspace.RestoreTreasure(copy.Id);
        Assert.Equal(image.Key, workspace.GetTreasure(copy.Id).ImageKey);
        workspace.SetTreasure(workspace.GetTreasure(901) with { ImageKey = null });
        await workspace.SaveAsync();
        Assert.Null((await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot)).GetTreasure(901).ImageKey);
    }

    [Fact]
    public async Task FailedImageManifestPublicationRollsBackPlayerFilesAndNewImagesAndCanRetry()
    {
        await Create();
        string imagesRoot = Path.Combine(_root, "user-images");
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        var images = workspace.Illustrations!;
        var entry = images.Add("插画", IllustrationPixels());
        workspace.SetTreasure(workspace.GetTreasure(901) with { ImageKey = entry.Key });
        Directory.CreateDirectory(images.ManifestPath);
        var error = await Record.ExceptionAsync(() => workspace.SaveAsync());
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(workspace.HasChanges);
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.False(File.Exists(workspace.PlayerOfficerPath));
        Assert.Empty(Directory.GetFiles(imagesRoot, "*.png", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.pending", SearchOption.AllDirectories));
        Directory.Delete(images.ManifestPath);
        await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        Assert.Single((await TreasureIllustrationPackFiles.LoadAsync(imagesRoot)).Images);
    }

    [Fact]
    public async Task ConcurrentImagePublicationRejectsTheStaleDraftAndKeepsThePublishedPack()
    {
        await Create();
        string imagesRoot = Path.Combine(_root, "user-images");
        var first = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        var second = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        var a = first.Illustrations!.Add("甲", IllustrationPixels());
        var b = second.Illustrations!.Add("乙", IllustrationPixels());
        first.SetTreasure(first.GetTreasure(901) with { ImageKey = a.Key });
        second.SetTreasure(second.GetTreasure(901) with { ImageKey = b.Key });
        await first.SaveAsync();
        byte[] expected = await File.ReadAllBytesAsync(first.PlayerDefinitionPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => second.SaveAsync());
        Assert.Equal(expected, await File.ReadAllBytesAsync(first.PlayerDefinitionPath));
        Assert.Equal(a.Key, Assert.Single((await TreasureIllustrationPackFiles.LoadAsync(imagesRoot)).Images).Key);
        Assert.Single(Directory.GetFiles(imagesRoot, "*.png", SearchOption.AllDirectories));
        Assert.Equal(b.Key, second.GetTreasure(901).ImageKey);
        Assert.True(second.HasChanges);
    }

    [Fact]
    public async Task InvalidSavedImagePreventsPublicationAndKeepsTheDraft()
    {
        await Create();
        string imagesRoot = Path.Combine(_root, "user-images");
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        var images = workspace.Illustrations!;
        var entry = images.Add("插画", IllustrationPixels());
        workspace.SetTreasure(workspace.GetTreasure(901) with { ImageKey = entry.Key });
        await workspace.SaveAsync();
        byte[] published = await File.ReadAllBytesAsync(workspace.PlayerDefinitionPath);

        var replacement = new PngImage(3, 9, 4, new byte[3 * 9 * 4]);
        await File.WriteAllBytesAsync(PackageFiles.Resolve(imagesRoot, entry.Image.Path), replacement.Encode());
        workspace.SetTreasure(workspace.GetTreasure(901) with { Name = "修改后的宝物" });

        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.Equal(published, await File.ReadAllBytesAsync(workspace.PlayerDefinitionPath));
        Assert.Equal("修改后的宝物", workspace.GetTreasure(901).Name);
        Assert.True(workspace.HasChanges);
    }

    [Fact]
    public async Task RetryingWithAnIdenticalPreparedImageReusesTheImmutableFile()
    {
        await Create();
        string imagesRoot = Path.Combine(_root, "user-images");
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root, treasureIllustrationRoot: imagesRoot);
        var pixels = IllustrationPixels();
        var entry = workspace.Illustrations!.Add("插画", pixels);
        string file = PackageFiles.Resolve(imagesRoot, entry.Image.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, pixels.Encode());
        workspace.SetTreasure(workspace.GetTreasure(901) with { ImageKey = entry.Key });
        await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        Assert.Equal(entry.Key, Assert.Single((await TreasureIllustrationPackFiles.LoadAsync(imagesRoot)).Images).Key);
    }
}
