using System.Text.Json;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

public static class TexturePackageImporter
{
    public const int ConversionVersion = 51;

    public static async Task<TexturePackage> ImportAsync(string gameDirectory, string output,
        Action<string>? progress = null, CancellationToken token = default, string? rebuild = null)
    {
        if (rebuild is not null && rebuild != "all")
            return await TexturePackageRebuilder.RebuildAsync(gameDirectory, output, rebuild, progress, token);
        var sourceDirectory = new San9SourceDirectory(gameDirectory);
        string sourceExe = sourceDirectory.Executable, dataDirectory = sourceDirectory.Root;
        var identity = await San9PkTc101Detector.DetectAsync(sourceExe, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        TextureLibraryCompiler.ValidateInventory(dataDirectory);
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "textures.json");
        bool reusable = false;
        if (File.Exists(manifest))
        {
            using var header = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, token));
            reusable = header.RootElement.GetProperty("schemaVersion").GetInt32() == 5 &&
                header.RootElement.TryGetProperty("conversionVersion", out var version) && version.GetInt32() == ConversionVersion;
        }
        if (reusable && rebuild != "all")
        {
            var previous = await TexturePackageFiles.LoadAsync(output, token);
            bool unchanged = previous.ConversionVersion == ConversionVersion;
            foreach (var source in previous.Sources)
            {
                string path = source.Name == Path.GetFileName(sourceExe) ? sourceExe : Path.Combine(dataDirectory, source.Name);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != source.Length || info.LastWriteTimeUtc != source.LastWriteTimeUtc)
                { unchanged = false; break; }
            }
            if (unchanged)
            {
                await ImageAssetFiles.VerifyAsync(output, previous.Assets, token);
                progress?.Invoke("Reusing verified independent texture package."); return previous;
            }
        }
        List<TextureSourceStamp> sources = [];
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested(); var bytes = File.ReadAllBytes(path);
            var info = new FileInfo(path);
            sources.Add(new(info.Name, info.Length, info.LastWriteTimeUtc)); return bytes;
        }
        string staging = Path.Combine(output, ".sources");
        int sourceNumber = 0;
        Dictionary<string, ImageAsset> sourceImages = new(StringComparer.Ordinal);
        ImageAsset Save(string id, string path, PngImage image)
        {
            if (sourceImages.TryGetValue(path, out var existing)) return existing with { Id = id };
            var bytes = image.Encode();
            string relative = $".sources/{sourceNumber++}.png";
            string target = PackageFiles.Resolve(output, relative); Directory.CreateDirectory(staging);
            File.WriteAllBytes(target, bytes);
            var asset = new ImageAsset(id, relative, image.Width, image.Height, image.Channels);
            sourceImages.Add(path, asset); return asset;
        }
        byte[] exe = Read(sourceExe);
        var map = MapResourceCompiler.Compile(exe, dataDirectory, Read, Save, progress, token);
        var skin = InterfaceSkinCompiler.Compile(exe, dataDirectory, Read, Save);
        var portraits = PortraitCompiler.Compile(dataDirectory, Read, Save);
        var library = TextureLibraryCompiler.Compile(exe, dataDirectory, Read, Save, progress, token);
        var package = new TexturePackage(5, ConversionVersion, San9PkTc101Detector.SourceBuildId,
            sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), map, skin, library, portraits);
        package = TexturePackagePacking.Pack(output, package, "images", token);
        await PublishAsync(output, package, token);
        progress?.Invoke($"Independent textures: {package.Assets.Count()} logical images, {package.Assets.Select(a => a.File).Distinct().Count()} physical files.");
        return package;
    }

    internal static async Task PublishAsync(string output, TexturePackage package, CancellationToken token)
    {
        string manifest = Path.Combine(output, "textures.json");
        string staging = Path.Combine(output, ".sources");
        package.Validate();
        await ImageAssetFiles.VerifyAsync(output, package.Assets, token);
        string pending = manifest + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(package, PackageFiles.JsonOptions), token);
            token.ThrowIfCancellationRequested();
            File.Move(pending, manifest, true);
        }
        finally { File.Delete(pending); }
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
