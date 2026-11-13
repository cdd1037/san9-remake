using System.Security.Cryptography;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ImportOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-import-operation-" + Guid.NewGuid().ToString("N"));
    public ImportOperationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task StandaloneMediaEntriesRespectOutputOwnershipAndReleaseOnFailure()
    {
        using (ImportOperation.Acquire(_root))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => AudioLibraryImporter.ImportAsync(_root, _root, AudioLibraryGroup.Effects));
            await Assert.ThrowsAnyAsync<IOException>(() => MovieLibraryImporter.ImportAsync(_root, _root));
        }
        Directory.CreateDirectory(Path.Combine(_root, "SE"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "SE", "broken.wav"), []);
        await Assert.ThrowsAnyAsync<IOException>(() => AudioLibraryImporter.ImportAsync(_root, _root, AudioLibraryGroup.Effects));
        using var reacquired = ImportOperation.Acquire(_root);
    }

    [Fact]
    public void ParentAndNestedOutputsRequireExplicitSharedOwnership()
    {
        string child = Path.Combine(_root, "shared", "textures");
        using (var parent = ImportOperation.Acquire(_root))
        {
            Assert.ThrowsAny<IOException>(() => ImportOperation.Acquire(child));
            using var nested = ImportOperation.Acquire(child, parent);
        }
        using (ImportOperation.Acquire(child))
            Assert.ThrowsAny<IOException>(() => ImportOperation.Acquire(_root));
        using var reacquired = ImportOperation.Acquire(_root);
    }

    [Fact]
    public async Task FingerprintsIgnoreTouchButDetectEqualLengthEditsWithPreservedTime()
    {
        Assert.False(await TexturePackageImporter.SourcesMatchAsync(_root, []));
        string file = Path.Combine(_root, "source.s9");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        var stamp = new TextureSourceStamp("source.s9", 3, Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-1));
        Assert.True(await TexturePackageImporter.SourcesMatchAsync(_root, [stamp]));
        DateTime originalTime = File.GetLastWriteTimeUtc(file);
        await File.WriteAllBytesAsync(file, [3, 2, 1]);
        File.SetLastWriteTimeUtc(file, originalTime);
        Assert.False(await TexturePackageImporter.SourcesMatchAsync(_root, [stamp]));
    }

    [Fact]
    public void ParsedScenarioSectionsAreReusedWithinTheirSourceOnly()
    {
        int calls = 0;
        object Parse(Stream stream) { calls++; return new object(); }
        var first = new ScenarioSource([1, 2, 3]);
        Assert.Same(first.Read(Parse), first.Read(Parse));
        var second = new ScenarioSource([1, 2, 3]);
        Assert.NotSame(first.Read(Parse), second.Read(Parse));
        Assert.Equal(2, calls);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
