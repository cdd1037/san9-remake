using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Packages;

public static class ClientContentImporter
{
    public static async Task<ClientContentPackage> ImportAsync(string sourceExe, string dataDirectory, string scenarioPath,
        string output, Action<string>? progress = null, CancellationToken token = default)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; choose a new package directory.");
        string staging = output + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var map = await MapImporter.ImportAsync(sourceExe, dataDirectory, scenarioPath, Path.Combine(staging, "images", "map"), progress, token);
            ImageAsset MapPath(ImageAsset asset) => asset with { Path = "images/map/" + asset.Path };
            map = map with { Assets = map.Assets.Select(MapPath).ToArray(),
                Seasons = map.Seasons.Select(s => s with { Textures = s.Textures.Select(MapPath).ToArray() }).ToArray() };
            File.Delete(Path.Combine(staging, "images", "map", "map.json"));
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
                string target = PackageFiles.Resolve(staging, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                byte[] bytes = image.Encode(); File.WriteAllBytes(target, bytes);
                return new(id, path, Convert.ToHexString(SHA256.HashData(bytes)), image.Width, image.Height);
            }
            var skin = InterfaceSkinCompiler.Compile(exe, dataDirectory, Read, Save);
            var content = new ClientContentPackage(4, sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), map, scenario, text, skin);
            content.ValidateLinks();
            await File.WriteAllTextAsync(Path.Combine(staging, "content.json"), JsonSerializer.Serialize(content, PackageFiles.JsonOptions), token);
            await ClientContentFiles.LoadAsync(staging, token);
            _ = ImageAssetFiles.Load(staging, skin.Assets, token);
            Directory.Move(staging, output);
            progress?.Invoke($"Client content: map, {scenario.Settlements.Length} settlements, {text.Entries.Count} texts, {skin.Assets.Length} interface images.");
            return content;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
