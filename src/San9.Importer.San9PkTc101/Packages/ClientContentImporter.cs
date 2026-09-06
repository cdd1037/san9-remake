using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Packages;

public static class ClientContentImporter
{
    public static async Task<ClientContentPackage> ImportAsync(string sourceExe, string dataDirectory, string scenarioPath,
        string output, Action<string>? progress = null, CancellationToken token = default)
    {
        var identity = await San9PkTc101Detector.DetectAsync(sourceExe, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "content.json");
        MapPackage? map = null;
        if (File.Exists(manifest))
        {
            // The map contract is independent of the scenario and interface contracts.
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, token));
            map = document.RootElement.GetProperty("map").Deserialize<MapPackage>(PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("Missing map package.");
            MapPackageFiles.Validate(map);
            foreach (var source in map.Sources)
            {
                string path = source.Name == Path.GetFileName(sourceExe) ? sourceExe :
                    source.Name == Path.GetFileName(scenarioPath) ? scenarioPath : Path.Combine(dataDirectory, source.Name);
                if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token))) != source.Sha256)
                { map = null; break; }
            }
            if (map is not null)
            {
                _ = ImageAssetFiles.Load(output, map.Assets, token);
                foreach (var season in map.Seasons) _ = ImageAssetFiles.Load(output, season.Textures, token);
                progress?.Invoke("Reusing verified unchanged map assets.");
            }
        }
        if (map is null)
        {
            map = MapImporter.Build(sourceExe, dataDirectory, scenarioPath, Path.Combine(output, "images", "map"), progress, token);
            ImageAsset MapPath(ImageAsset asset) => asset with { Path = "images/map/" + asset.Path };
            map = map with { Assets = map.Assets.Select(MapPath).ToArray(),
                Seasons = map.Seasons.Select(s => s with { Textures = s.Textures.Select(MapPath).ToArray() }).ToArray() };
        }
        MapPackageFiles.Validate(map);
        List<ContentSource> sources = [.. map.Sources];
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested(); var bytes = File.ReadAllBytes(path);
            sources.Add(new(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)))); return bytes;
        }
        byte[] exe = Read(sourceExe), scenarioBytes = Read(scenarioPath);
        var (scenario, text) = ScenarioContentCompiler.Compile(exe, scenarioBytes, Read(Path.Combine(dataDirectory, "M_MsgPK.s9")));
        ImageAsset Save(string id, string path, PngImage image)
        {
            path = "images/interface/" + Path.GetFileName(path);
            string target = PackageFiles.Resolve(output, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            byte[] bytes = image.Encode();
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(target, bytes);
            return new(id, path, Convert.ToHexString(SHA256.HashData(bytes)), image.Width, image.Height);
        }
        var skin = InterfaceSkinCompiler.Compile(exe, dataDirectory, Read, Save);
        var content = new ClientContentPackage(7, sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), map, scenario, text, skin);
        content.ValidateLinks();
        _ = ImageAssetFiles.Load(output, skin.Assets, token);
        string pending = manifest + ".pending";
        try
        {
            await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(content, PackageFiles.JsonOptions), token);
            File.Move(pending, manifest, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        var retained = map.Assets.Concat(map.Seasons.SelectMany(s => s.Textures)).Concat(skin.Assets)
            .Select(a => PackageFiles.Resolve(output, a.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in new[] { "images/map", "images/interface" })
        {
            string root = PackageFiles.Resolve(output, directory);
            if (Directory.Exists(root))
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    if (!retained.Contains(Path.GetFullPath(file))) File.Delete(file);
        }
        progress?.Invoke($"Client content: map, {scenario.Settlements.Length} settlements, {text.Entries.Count} texts, {skin.Assets.Length} interface images.");
        return content;
    }
}
