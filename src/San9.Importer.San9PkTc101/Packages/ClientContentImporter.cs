using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

public static class ClientContentImporter
{
    public static async Task<ClientContentPackage> ImportAsync(string sourceExe, string dataDirectory, string scenarioPath,
        string textureRoot, string output, Action<string>? progress = null, CancellationToken token = default)
    {
        var identity = await San9PkTc101Detector.DetectAsync(sourceExe, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        textureRoot = Path.GetFullPath(textureRoot); output = Path.GetFullPath(output);
        if (textureRoot == output) throw new InvalidDataException("Scenario and texture package directories must differ.");
        var textures = await TexturePackageFiles.LoadAsync(textureRoot, token);
        if (textures.SourceBuild != San9PkTc101Detector.SourceBuildId) throw new InvalidDataException("Texture source build differs.");
        var map = MapImporter.Build(sourceExe, scenarioPath, textures.Map, progress, token);
        List<ContentSource> sources = [.. map.Sources];
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested(); var bytes = File.ReadAllBytes(path);
            sources.Add(new(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)))); return bytes;
        }
        byte[] exe = Read(sourceExe), scenarioBytes = Read(scenarioPath);
        var (scenario, text) = ScenarioContentCompiler.Compile(exe, scenarioBytes, Read(Path.Combine(dataDirectory, "M_MsgPK.s9")), Read(Path.Combine(dataDirectory, "M_RtdnPK.s9")));
        Dictionary<string, San9.Content.Text.MessageDefinition> messages = [];
        foreach (var (file, catalog) in new[] { ("M_MsgPK.s9", "messages"), ("M_RtdnPK.s9", "biographies"), ("M_Msg.s9", "base-messages"), ("M_Rtdn.s9", "base-biographies") })
        {
            var converted = MessageArchiveCompiler.Compile(Read(Path.Combine(dataDirectory, file)), file, catalog);
            foreach (var item in converted.Messages) messages.Add(item.Key, item.Value);
        }
        San9.Content.Text.NameMessages.AddTo(messages);
        text = text with { Messages = messages };
        var roles = InformationArtCompiler.Compile(scenarioBytes, scenario, textures.Library, textures.Portraits);
        var dependency = new TextureDependency(Path.GetRelativePath(output, textureRoot).Replace('\\','/'));
        var content = new ScenarioContent(23, sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(),
            dependency, MapInstance.From(map), scenario, text, roles);
        var loaded = ClientContentFiles.Resolve(content, textures, textureRoot);
        Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "content.json"), pending = manifest + ".pending";
        await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(content, PackageFiles.JsonOptions), token);
        File.Move(pending, manifest, true);
        progress?.Invoke($"Scenario: {scenario.Settlements.Length} settlements; references independent textures at {dependency.Path}.");
        return loaded;
    }
}
