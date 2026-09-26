using System.Security.Cryptography;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

public static class ClientContentImporter
{
    public static async Task<ClientContentPackage> ImportAsync(string gameDirectory, string scenarioFile,
        string textureRoot, string output, Action<string>? progress = null, CancellationToken token = default)
    {
        var sourceDirectory = new San9SourceDirectory(gameDirectory);
        string sourceExe = sourceDirectory.Executable, dataDirectory = sourceDirectory.Root;
        string scenarioPath = sourceDirectory.File(scenarioFile);
        var identity = await San9PkTc101Detector.DetectAsync(sourceExe, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        textureRoot = Path.GetFullPath(textureRoot); output = Path.GetFullPath(output);
        if (textureRoot == output) throw new InvalidDataException("Scenario and texture package directories must differ.");
        var textures = await TexturePackageFiles.LoadAsync(textureRoot, token);
        if (textures.SourceBuild != San9PkTc101Detector.SourceBuildId) throw new InvalidDataException("Texture source build differs.");
        List<ContentSource> sources = [];
        var buffers = new Dictionary<string, byte[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (buffers.TryGetValue(path, out var cached)) return cached;
            byte[] bytes = File.ReadAllBytes(path);
            buffers.Add(path, bytes);
            sources.Add(new(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes))));
            return bytes;
        }
        byte[] exe = Read(sourceExe), scenarioBytes = Read(scenarioPath);
        var map = MapImporter.Build(scenarioBytes, textures.Map, sources.ToArray());
        var (scenario, text) = ScenarioContentCompiler.Compile(exe, scenarioBytes, Read(Path.Combine(dataDirectory, "M_MsgPK.s9")), Read(Path.Combine(dataDirectory, "M_RtdnPK.s9")));
        Dictionary<string, San9.Content.Text.MessageDefinition> messages = [];
        List<MessageContentAdaptation> adaptations = [];
        foreach (var (file, catalog) in new[] { ("M_MsgPK.s9", "messages"), ("M_RtdnPK.s9", "biographies") })
        {
            var converted = MessageArchiveCompiler.Compile(Read(Path.Combine(dataDirectory, file)), file, catalog);
            foreach (var item in converted.Messages) messages.Add(item.Key, item.Value);
            var table = new San9MessageTable(Read(Path.Combine(dataDirectory, file)));
            for (int i = 0; i < table.Count; i++)
                if (MessageContentPolicy.Apply($"{catalog}.{i}", table.Record(i), catalog, messages) is { } adaptation)
                    adaptations.Add(adaptation);
        }
        using (var source = new MemoryStream(scenarioBytes))
        {
            var header = San9ScenarioArchive.ReadInitialScenario190Header(source);
            source.Position = 0;
            var people = San9ScenarioPersonArchive.ReadKnownRecords(source);
            messages = new GameplayMessageCompiler(scenario, people, header.ScenarioId, header.ScenarioModeCode).Compile(messages);
        }
        San9.Content.Text.NameMessages.AddTo(messages);
        text = text with { Messages = messages };
        var roles = InformationArtCompiler.Compile(scenarioBytes, scenario, textures.Library, textures.Portraits);
        var dependency = new TextureDependency(Path.GetRelativePath(output, textureRoot).Replace('\\','/'));
        var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, sources.Distinct().OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(),
            dependency, MapInstance.From(map), scenario, text, roles);
        var loaded = ClientContentFiles.Resolve(content, textures, textureRoot);
        await ScenarioContentFiles.WriteAsync(output, content, token);
        var adaptedKeys = adaptations.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        var messageAudit = GameplayMessageCompiler.Audit(messages)
            .Select(a => adaptedKeys.Contains(a.Key) ? a with { SourceConfirmed = false } : a).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output, "message-adaptations.json"),
            System.Text.Json.JsonSerializer.Serialize(adaptations, PackageFiles.JsonOptions), System.Text.Encoding.UTF8, token);
        await File.WriteAllTextAsync(Path.Combine(output, "message-readiness.json"),
            System.Text.Json.JsonSerializer.Serialize(messageAudit, PackageFiles.JsonOptions), System.Text.Encoding.UTF8, token);
        progress?.Invoke($"Messages: {messageAudit.Count(m => m.RuntimeReady)}/{messageAudit.Length} runtime-ready; see message-readiness.json for arguments and exclusions.");
        progress?.Invoke($"Scenario: {scenario.Settlements.Length} settlements; references independent textures at {dependency.Path}.");
        return loaded;
    }
}
