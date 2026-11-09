using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Explicit rule corrections rebuild a selected unit from source and retain other published definitions.</summary>
internal static class TexturePackageRebuilder
{
    internal static TexturePackage ReplaceUnit<T>(JsonObject manifest, string unit, T replacement)
    {
        // The selected unit is regenerated in full; its old schema is not an input contract.
        return TexturePackageFiles.ReplaceUnit(manifest, unit, replacement);
    }

    internal static InterfaceSkin ReplaceCursors(InterfaceSkin skin, Dictionary<string, CursorDefinition> cursors, ImageAsset[] assets)
    {
        var replaced = skin.Cursors.Values.Select(c => c.Asset).Concat(assets.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
        return skin with { Cursors = cursors, Assets = skin.Assets.Where(a => !replaced.Contains(a.Id)).Concat(assets).ToArray() };
    }

    internal static void ValidateScope(string scope)
    {
        if (scope is "cursors" or "skin" or "map" or "map/formation-appearance" or "portraits") return;
        if (scope.StartsWith("library/", StringComparison.Ordinal) &&
            TextureLibraryCompiler.ArchiveIds.Contains(scope[8..], StringComparer.Ordinal)) return;
        throw new ArgumentException($"Unknown texture rebuild unit '{scope}'. Use all, cursors, skin, map, map/formation-appearance, portraits or library/ARCHIVE (for example library/g_common).");
    }

    public static async Task<TexturePackage> RebuildAsync(string gameDirectory, string output, string scope,
        Action<string>? progress, CancellationToken token)
    {
        ValidateScope(scope);
        output = Path.GetFullPath(output);
        // Validate the completed package after replacing the selected unit. The old unit can be
        // precisely what is missing a newly required role or contains an incorrect definition.
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(output, "textures.json"), token))?.AsObject()
            ?? throw new InvalidDataException("Empty texture package.");
        if (manifest["schemaVersion"]?.GetValue<int>() != TexturePackage.CurrentSchemaVersion || manifest["sourceBuild"]?.GetValue<string>() != San9PkTc101Detector.SourceBuildId)
            throw new InvalidDataException("Targeted rebuilding requires a matching texture package schema and source build.");
        if (scope == "map/formation-appearance")
        {
            var units = manifest["map"]?["units"]?.AsObject()
                ?? throw new InvalidDataException("Missing unit visual catalog.");
            units["formationAppearance"] = JsonSerializer.SerializeToNode(
                MapUnitCompiler.CompileFormationAppearance(), PackageFiles.JsonOptions);
            var rebuilt = manifest.Deserialize<TexturePackage>(PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("Empty texture package.");
            TexturePackageImporter.Publish(output, rebuilt, token);
            progress?.Invoke("Rebuilt map/formation-appearance; retained all images and other resource definitions.");
            return rebuilt;
        }
        var imported = await San9ImportSource.OpenAsync(gameDirectory, token);
        var source = imported.Directory;
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested();
            return imported.Read(Path.GetFileName(path));
        }
        var images = new TextureImageSources(token);
        ImageAsset Save(string id, string sourcePath, TextureSourceImage image) => images.Add(id, sourcePath, image);
        progress?.Invoke($"Rebuilding {scope} from source using current conversion rules.");
        TexturePackage package;
        switch (scope)
        {
            case "cursors":
                using (var pe = new PEReader(new MemoryStream(Read(source.Executable))))
                {
                    List<ImageAsset> assets = [];
                    var cursors = ExecutableCursorCompiler.Compile(pe, assets, Save);
                    var skin = TexturePackageFiles.ReadUnit<InterfaceSkin>(manifest, "skin");
                    package = ReplaceUnit(manifest, "skin", ReplaceCursors(skin, cursors, assets.ToArray()));
                }
                break;
            case "skin":
                package = ReplaceUnit(manifest, "skin", InterfaceSkinCompiler.Compile(Read(source.Executable), source.Root, Read, Save));
                break;
            case "map":
                package = ReplaceUnit(manifest, "map", MapResourceCompiler.Compile(Read(source.Executable), source.Root, Read, Save, progress, token));
                break;
            case "portraits":
                package = ReplaceUnit(manifest, "portraits", PortraitCompiler.Compile(source.Root, Read, Save));
                break;
            default:
                string archiveId = scope[8..];
                var converted = TextureLibraryCompiler.Compile(Read(source.Executable), source.Root, Read, Save, progress, token, archiveId);
                var library = TexturePackageFiles.ReadUnit<TextureLibrary>(manifest, "library");
                package = ReplaceUnit(manifest, "library", library with {
                    Archives = library.Archives.Where(a => a.Id != archiveId).Concat(converted.Archives).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray() });
                break;
        }
        // Sources certifies the entire package. A local rebuild cannot certify retained units.
        // Retained consumers continue referencing their existing files.
        // A fresh directory keeps retained references intact without reading or backing up old PNGs.
        string directory = "images/rebuilt/" + scope + "/" + Guid.NewGuid().ToString("N");
        package = TexturePackagePacking.Pack(output, package, directory, token, TexturePublication.Write, images.Load);
        TexturePackageImporter.Publish(output, package, token);
        progress?.Invoke($"Rebuilt {scope}: {images.Count} source images; retained other published resource definitions.");
        return package;
    }
}
