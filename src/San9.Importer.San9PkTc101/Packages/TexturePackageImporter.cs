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
        TexturePackage? previous = null;
        if (!force && File.Exists(manifest))
        {
            await using var stream = File.OpenRead(manifest);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var header = document.RootElement;
            if (header.GetProperty("schemaVersion").GetInt32() == TexturePackage.CurrentSchemaVersion &&
                header.TryGetProperty("conversionVersion", out var version) && version.GetInt32() == ConversionVersion)
                previous = TexturePackageFiles.Read(header);
        }
        if (previous is not null)
        {
            bool unchanged = await SourcesMatchAsync(dataDirectory, previous.Sources, token);
            if (unchanged)
            {
                RequireImageFiles(output, previous.Assets, token);
                progress?.Invoke("Reusing unchanged independent texture package; image files present."); return previous;
            }
        }
        Dictionary<string, TextureSourceStamp> sources = new(StringComparer.OrdinalIgnoreCase);
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested(); var bytes = source.Read(Path.GetFileName(path));
            var info = new FileInfo(path);
            if (!sources.ContainsKey(info.Name)) sources.Add(info.Name, new(info.Name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))); return bytes;
        }
        var sourceImages = new TextureImageSources(token);
        ImageAsset Save(string id, string path, TextureSourceImage image) => sourceImages.Add(id, path, image);
        byte[] exe = Read(sourceExe);
        var map = MapResourceCompiler.Compile(exe, dataDirectory, Read, Save, progress, token);
        var skin = InterfaceSkinCompiler.Compile(exe, dataDirectory, Read, Save);
        var portraits = PortraitCompiler.Compile(dataDirectory, Read, Save);
        var library = TextureLibraryCompiler.Compile(exe, dataDirectory, Read, Save, progress, token);
        var package = new TexturePackage(TexturePackage.CurrentSchemaVersion, ConversionVersion, San9PkTc101Detector.SourceBuildId,
            sources.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), map, skin, library, portraits);
        package = TexturePackagePacking.Pack(output, package, "images", token, TexturePublication.Write, sourceImages.Load);
        Publish(output, package, token);
        progress?.Invoke($"Independent textures: {package.Assets.Count()} logical images, {package.Assets.Select(a => a.File).Distinct().Count()} physical files.");
        return package;
    }

    internal static async Task<bool> SourcesMatchAsync(string root, IEnumerable<TextureSourceStamp> stamps, CancellationToken token = default)
    {
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

    internal static void RequireImageFiles(string output, IEnumerable<ImageAsset> assets, CancellationToken token)
    {
        foreach (string path in assets.Select(a => a.Path).Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var file = new FileInfo(PackageFiles.Resolve(output, path));
            if (!file.Exists || file.Length == 0) throw new InvalidDataException($"Missing or empty texture file: {path}.");
        }
    }

    internal static void Publish(string output, TexturePackage package, CancellationToken token)
    {
        string manifest = Path.Combine(output, "textures.json");
        string staging = Path.Combine(output, ".sources");
        package.Validate();
        RequireImageFiles(output, package.Assets, token);
        token.ThrowIfCancellationRequested();
        TexturePublication.Write(manifest, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(package, PackageFiles.JsonOptions)));
        // Retire unused outputs only after the new manifest is published.
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
