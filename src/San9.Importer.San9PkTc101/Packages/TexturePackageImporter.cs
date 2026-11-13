using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

public static class TexturePackageImporter
{
    public const int ConversionVersion = 66;

    public static async Task<TexturePackage> ImportAsync(string gameDirectory, string output,
        Action<string>? progress = null, CancellationToken token = default, string? rebuild = null)
    {
        if (rebuild is not null && rebuild != "all")
        {
            using var operation = ImportOperation.Acquire(output);
            return await TexturePackageRebuilder.RebuildAsync(gameDirectory, output, rebuild, progress, token);
        }
        var source = await San9ImportSource.OpenAsync(gameDirectory, token);
        return await ImportCoreAsync(source, output, progress, token, rebuild == "all");
    }

    internal static async Task<TexturePackage> ImportCoreAsync(San9ImportSource source, string output,
        Action<string>? progress, CancellationToken token, bool force = false, ImportOperation? parent = null)
    {
        using var operation = ImportOperation.Acquire(output, parent);
        string sourceExe = source.Directory.Executable, dataDirectory = source.Directory.Root;
        TextureLibraryCompiler.ValidateInventory(dataDirectory);
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "textures.json");
        bool reusable = false;
        if (File.Exists(manifest))
        {
            using var header = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, token));
            reusable = header.RootElement.GetProperty("schemaVersion").GetInt32() == TexturePackage.CurrentSchemaVersion &&
                header.RootElement.TryGetProperty("conversionVersion", out var version) && version.GetInt32() == ConversionVersion;
        }
        if (reusable && !force)
        {
            var previous = await TexturePackageFiles.LoadAsync(output, token);
            bool unchanged = await SourcesMatchAsync(dataDirectory, previous.Sources, token);
            if (unchanged)
            {
                await ImageAssetFiles.VerifyAsync(output, previous.Assets, token);
                progress?.Invoke("Reusing verified independent texture package."); return previous;
            }
        }
        List<TextureSourceStamp> sources = [];
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested(); var bytes = source.Read(Path.GetFileName(path));
            var info = new FileInfo(path);
            sources.Add(new(info.Name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))); return bytes;
        }
        string staging = Path.Combine(output, ".sources");
        int sourceNumber = 0;
        Dictionary<string, ImageAsset> sourceImages = new(StringComparer.Ordinal);
        ImageAsset Save(string id, string path, PngImage image)
        {
            if (sourceImages.TryGetValue(path, out var existing)) return existing with { Id = id };
            string relative = $".sources/{sourceNumber++}.png";
            string target = PackageFiles.Resolve(output, relative); Directory.CreateDirectory(staging);
            using (var stream = File.Create(target))
                image.Encode(stream, System.IO.Compression.CompressionLevel.Fastest);
            var asset = new ImageAsset(id, relative, image.Width, image.Height, image.Channels);
            sourceImages.Add(path, asset); return asset;
        }
        byte[] exe = Read(sourceExe);
        var map = MapResourceCompiler.Compile(exe, dataDirectory, Read, Save, progress, token);
        var skin = InterfaceSkinCompiler.Compile(exe, dataDirectory, Read, Save);
        var portraits = PortraitCompiler.Compile(dataDirectory, Read, Save);
        var library = TextureLibraryCompiler.Compile(exe, dataDirectory, Read, Save, progress, token);
        var package = new TexturePackage(TexturePackage.CurrentSchemaVersion, ConversionVersion, San9PkTc101Detector.SourceBuildId,
            sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), map, skin, library, portraits);
        using var publication = new TexturePublication();
        package = TexturePackagePacking.Pack(output, package, "images", token, publication.Write);
        await PublishAsync(output, package, token, publication);
        progress?.Invoke($"Independent textures: {package.Assets.Count()} logical images, {package.Assets.Select(a => a.File).Distinct().Count()} physical files.");
        return package;
    }

    internal static async Task<bool> SourcesMatchAsync(string root, IEnumerable<TextureSourceStamp> stamps, CancellationToken token = default)
    {
        if (!stamps.Any()) return false;
        foreach (var stamp in stamps)
        {
            token.ThrowIfCancellationRequested();
            string path = PackageFiles.Resolve(root, stamp.Name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != stamp.Length || stamp.Sha256 != await FingerprintAsync(path, token)) return false;
        }
        return true;
    }

    private static async Task<string> FingerprintAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    internal static async Task PublishAsync(string output, TexturePackage package, CancellationToken token, TexturePublication publication)
    {
        string manifest = Path.Combine(output, "textures.json");
        string staging = Path.Combine(output, ".sources");
        package.Validate();
        await ImageAssetFiles.VerifyAsync(output, package.Assets, token);
        token.ThrowIfCancellationRequested();
        publication.Write(manifest, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(package, PackageFiles.JsonOptions)));
        publication.Commit();
        publication.Dispose();
        // Only this builder owns these outputs. Cleanup follows a successfully published manifest.
        var retained = package.Assets.Select(a => PackageFiles.Resolve(output, a.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in new[] { PackageFiles.Resolve(output, "images"), staging })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                if (!retained.Contains(Path.GetFullPath(file))) File.Delete(file);
            foreach (string empty in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(empty).Any()) Directory.Delete(empty);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }
}
