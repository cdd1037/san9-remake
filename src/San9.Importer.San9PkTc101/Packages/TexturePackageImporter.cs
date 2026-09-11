using System.Text.Json;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Images;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

public static class TexturePackageImporter
{
    public const int ConversionVersion = 7;

    public static async Task<TexturePackage> ImportAsync(string sourceExe, string dataDirectory, string output,
        Action<string>? progress = null, CancellationToken token = default)
    {
        var identity = await San9PkTc101Detector.DetectAsync(sourceExe, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        TextureLibraryCompiler.ValidateInventory(dataDirectory);
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "textures.json");
        bool reusable = false;
        if (File.Exists(manifest))
        {
            using var header = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, token));
            reusable = header.RootElement.GetProperty("schemaVersion").GetInt32() == 3 &&
                header.RootElement.TryGetProperty("conversionVersion", out var version) && version.GetInt32() == ConversionVersion;
        }
        if (reusable)
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
        var cpuIds = map.Masks.SelectMany(m => new[] { m.WeightAsset, m.CoverageAsset }).Append("cloud-fill").ToHashSet();
        var animationGroups = new Dictionary<string, HashSet<string>>();
        foreach (var clip in map.Clips)
        {
            // Equivalent seasonal frame sets have the same lifetime; fallback Frames is not another group.
            var frameSets = clip.SeasonFrames.Values.Append(clip.Frames).Where(frames => frames.Length != 0)
                .Select(frames => frames.Distinct().Order(StringComparer.Ordinal).ToArray())
                .GroupBy(frames => string.Join("|", frames), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal);
            int set = 0;
            foreach (var frames in frameSets)
            {
                string group = TextureGroups.MapAnimation(clip.Id, set++);
                foreach (string id in frames.First())
                {
                    if (!animationGroups.TryGetValue(id, out var groups)) animationGroups[id] = groups = [];
                    groups.Add(group);
                }
            }
        }
        string MapCategory(ImageAsset asset)
        {
            if (asset.Id.StartsWith("mask-", StringComparison.Ordinal) || asset.Id.StartsWith("coverage-", StringComparison.Ordinal)) return "map/cpu/masks";
            if (asset.Id == "cloud-fill") return "map/cpu/clouds";
            if (animationGroups.TryGetValue(asset.Id, out var groups)) return groups.Count == 1 ? groups.Single() : "map/animations/shared";
            string prefix = asset.Id.Split('-')[0];
            return "map/objects/" + prefix;
        }
        var mapSources = map.Assets.Select(a => new TextureAtlasCompiler.Source(a, cpuIds.Contains(a.Id) ? null :
            animationGroups.TryGetValue(a.Id, out var groups) ? groups.Count == 1 ? groups.Single() : null : MapCategory(a), MapCategory(a)));
        var rolesByAsset = skin.Roles.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Select(p => TextureGroups.Interface(p.Key)).Distinct().ToArray());
        var uiSources = skin.Assets.Select(a =>
        {
            string category = rolesByAsset.TryGetValue(a.Id, out var groups) && groups.Length == 1 ? groups[0] : "shared";
            string folder = category == "portrait" ? "portraits" : "interface/" + category;
            return new TextureAtlasCompiler.Source(a, category is "portrait" or "shared" ? null : category, folder);
        });
        var landSources = map.Seasons.SelectMany(s => s.Textures.Select(a => new TextureAtlasCompiler.Source(
            a with { Id = $"terrain/{s.Id}/{a.Id}" }, null, $"map/cpu/terrain/{s.Id}")));
        var librarySources = library.Archives.SelectMany(a => a.Variants.Select(v =>
            new TextureAtlasCompiler.Source(v.Image,
                a.Category.StartsWith("portraits/", StringComparison.Ordinal) || a.Category == "illustrations"
                    ? null : $"{a.Category}/{a.Id}/palette-{v.PaletteIndex}",
                a.Id == "g_still" && v.ImageIndex is >= 172 and < 272 ? "illustrations/treasures" : a.Category,
                a.Id == "g_still" && v.ImageIndex is >= 172 and < 272 ? v.ImageIndex - 172 : null)));
        var requests = mapSources.Concat(uiSources).Concat(landSources).Concat(librarySources)
            .Concat(portraits.Select(p => new TextureAtlasCompiler.Source(p.Image, null, "portraits", p.Id))).ToArray();
        // Resolve consumers to their declared source before packing. No pixel comparison occurs.
        // Display/CPU definitions own placement; the complete library references those definitions.
        var owners = requests.GroupBy(s => s.Asset.File).Select(g => g
            .OrderBy(s => s.Asset.Id.StartsWith("library/", StringComparison.Ordinal))
            .ThenBy(s => s.Asset.Id, StringComparer.Ordinal).First()).ToArray();
        var pages = TextureAtlasCompiler.Compile(output, owners, "images", token).ToDictionary(a => a.Id);
        var bySource = owners.ToDictionary(s => s.Asset.File, s => pages[s.Asset.Id]);
        var packed = requests.ToDictionary(s => s.Asset.Id, s => bySource[s.Asset.File] with { Id = s.Asset.Id });
        map = map with { Assets = map.Assets.Select(a => packed[a.Id]).ToArray(),
            Seasons = map.Seasons.Select(s => s with { Textures = s.Textures.Select(a => packed[$"terrain/{s.Id}/{a.Id}"] with { Id = a.Id }).ToArray() }).ToArray() };
        skin = skin with { Assets = skin.Assets.Select(a => packed[a.Id]).ToArray() };
        library = library with { Archives = library.Archives.Select(a => a with {
            Variants = a.Variants.Select(v => v with { Image = packed[v.Image.Id] }).ToArray() }).ToArray() };
        portraits = portraits.Select(p => p with { Image = packed[p.Image.Id] }).ToArray();
        var package = new TexturePackage(3, ConversionVersion, San9PkTc101Detector.SourceBuildId,
            sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), map, skin, library, portraits);
        package.Validate();
        await ImageAssetFiles.VerifyAsync(output, package.Assets, token);
        string pending = manifest + ".pending";
        await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(package, PackageFiles.JsonOptions), token);
        File.Move(pending, manifest, true);
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
        progress?.Invoke($"Independent textures: {package.Assets.Count()} logical images, {package.Assets.Select(a => a.File).Distinct().Count()} physical files.");
        return package;
    }
}
