using System.Reflection.PortableExecutable;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Explicit rule corrections rebuild a selected unit from source and retain other published definitions.</summary>
internal static class TexturePackageRebuilder
{
    internal static InterfaceSkin ReplaceCursors(InterfaceSkin skin, Dictionary<string, CursorDefinition> cursors, ImageAsset[] assets)
    {
        var replaced = skin.Cursors.Values.Select(c => c.Asset).Concat(assets.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
        return skin with { Cursors = cursors, Assets = skin.Assets.Where(a => !replaced.Contains(a.Id)).Concat(assets).ToArray() };
    }

    internal static void ValidateScope(string scope)
    {
        if (scope is "cursors" or "skin" or "map" or "portraits") return;
        if (scope.StartsWith("library/", StringComparison.Ordinal) &&
            TextureLibraryCompiler.ArchiveIds.Contains(scope[8..], StringComparer.Ordinal)) return;
        throw new ArgumentException($"Unknown texture rebuild unit '{scope}'. Use all, cursors, skin, map, portraits or library/ARCHIVE (for example library/g_common).");
    }

    public static async Task<TexturePackage> RebuildAsync(string gameDirectory, string output, string scope,
        Action<string>? progress, CancellationToken token)
    {
        ValidateScope(scope);
        var source = new San9SourceDirectory(gameDirectory);
        var identity = await San9PkTc101Detector.DetectAsync(source.Executable, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        output = Path.GetFullPath(output);
        // Validate the completed package after replacing the selected unit. The old unit can be
        // precisely what is missing a newly required role or contains an incorrect definition.
        var previous = await PackageFiles.ReadAsync<TexturePackage>(Path.Combine(output, "textures.json"), token);
        if (previous.SchemaVersion != 5 || previous.SourceBuild != San9PkTc101Detector.SourceBuildId)
            throw new InvalidDataException("Targeted rebuilding requires a matching texture package schema and source build.");
        var sources = previous.Sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes = File.ReadAllBytes(path);
            var info = new FileInfo(path);
            sources[info.Name] = new(info.Name, info.Length, info.LastWriteTimeUtc);
            return bytes;
        }
        Dictionary<string, ImageAsset> images = new(StringComparer.Ordinal);
        ImageAsset Save(string id, string sourcePath, PngImage image)
        {
            token.ThrowIfCancellationRequested();
            if (images.TryGetValue(sourcePath, out var existing)) return existing with { Id = id };
            string relative = $".sources/{images.Count}.png";
            string path = PackageFiles.Resolve(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, image.Encode());
            var asset = new ImageAsset(id, relative, image.Width, image.Height, image.Channels);
            images.Add(sourcePath, asset);
            return asset;
        }
        progress?.Invoke($"Rebuilding {scope} from source using current conversion rules.");
        var package = previous;
        switch (scope)
        {
            case "cursors":
                using (var pe = new PEReader(new MemoryStream(Read(source.Executable))))
                {
                    List<ImageAsset> assets = [];
                    var cursors = ExecutableCursorCompiler.Compile(pe, assets, Save);
                    package = previous with { Skin = ReplaceCursors(previous.Skin, cursors, assets.ToArray()) };
                }
                break;
            case "skin":
                package = previous with { Skin = InterfaceSkinCompiler.Compile(Read(source.Executable), source.Root, Read, Save) };
                break;
            case "map":
                package = previous with { Map = MapResourceCompiler.Compile(Read(source.Executable), source.Root, Read, Save, progress, token) };
                break;
            case "portraits":
                package = previous with { Portraits = PortraitCompiler.Compile(source.Root, Read, Save) };
                break;
            default:
                string archiveId = scope[8..];
                var converted = TextureLibraryCompiler.Compile(Read(source.Executable), source.Root, Read, Save, progress, token, archiveId);
                package = previous with { Library = previous.Library with {
                    Archives = previous.Library.Archives.Where(a => a.Id != archiveId).Concat(converted.Archives).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray() } };
                break;
        }
        package = package with { Sources = sources.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray() };
        // Other definitions may still reference the old atlas. Never overwrite it during a local correction.
        package = TexturePackagePacking.Pack(output, package, "images/rebuilt/" + scope, token);
        await TexturePackageImporter.PublishAsync(output, package, token);
        progress?.Invoke($"Rebuilt {scope}: {images.Count} source images; retained other published resource definitions.");
        return package;
    }
}
