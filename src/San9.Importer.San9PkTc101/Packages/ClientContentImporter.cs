using System.Security.Cryptography;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

public static class ClientContentImporter
{
    public static async Task<ClientContentPackage> ImportAsync(string gameDirectory, string scenarioFile,
        string textureRoot, string output, Action<string>? progress = null, bool messageReports = false, CancellationToken token = default, string? resourceRoot = null,
        SharedMessageLibrary? sharedMessages = null, bool publishSharedMessages = false,
        SharedMapDefinition? sharedMap = null, bool publishSharedMap = false,
        San9.Content.Interface.ScenarioPreviewEntry? preview = null)
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
        var buffers = new Dictionary<string, byte[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (buffers.TryGetValue(path, out var cached)) return cached;
            byte[] bytes = File.ReadAllBytes(path);
            buffers.Add(path, bytes);
            return bytes;
        }
        byte[] exe = Read(sourceExe), scenarioBytes = Read(scenarioPath);
        var map = MapImporter.Build(scenarioBytes, textures.Map,
            [new(Path.GetFileName(sourceExe), Convert.ToHexString(SHA256.HashData(exe))),
             new(Path.GetFileName(scenarioPath), Convert.ToHexString(SHA256.HashData(scenarioBytes)))]);
        var (scenario, text) = ScenarioContentCompiler.Compile(exe, scenarioBytes, Read(Path.Combine(dataDirectory, "M_MsgPK.s9")), Read(Path.Combine(dataDirectory, "M_RtdnPK.s9")));
        Dictionary<string, San9.Content.Text.MessageDefinition> messages = [];
        List<MessageContentAdaptation> adaptations = [];
        foreach (var (file, catalog) in new[] { ("M_MsgPK.s9", "messages"), ("M_RtdnPK.s9", "biographies") })
        {
            var converted = MessageArchiveCompiler.CompileForPublication(Read(Path.Combine(dataDirectory, file)), file, catalog);
            foreach (var item in converted.Messages) messages.Add(item.Key, item.Value);
            if (messageReports) adaptations.AddRange(converted.Adaptations);
        }
        var audioFiles = AudioAssetCompiler.Discover(sourceDirectory.Root);
        San9.Content.Interface.CommandPresentation presentation;
        using (var source = new MemoryStream(scenarioBytes))
        {
            var header = San9ScenarioArchive.ReadHeader(source);
            source.Position = 0;
            var people = San9ScenarioPersonArchive.ReadKnownRecords(source);
            presentation = CommandPresentationCompiler.Compile(people, audioFiles) with
            {
                Notices = new() { ["scroll-hover"] = "messages.8427", ["scroll-click"] = "messages.8428" }
            };
            messages = new GameplayMessageCompiler(scenario, people).Compile(messages);
            text = text with { MessageTags = ScenarioMessageTags.For(header.ScenarioId, header.ScenarioModeCode) };
        }
        messages = PublicationMessageCatalog.Compile(messages);
        San9.Content.Text.NameMessages.AddTo(messages);
        EventPresentationCompiler.AddMessages(messages);
        ChroniclePresentationCompiler.AddMessages(messages);
        text = text with { Messages = messages };
        var roles = InformationArtCompiler.Compile(scenarioBytes, scenario, textures.Library, textures.Portraits);
        var dependency = new TextureDependency(Path.GetRelativePath(output, textureRoot).Replace('\\','/'));
        var mapAudio = MapAudioCompiler.Compile(exe);
        var battle = BattlePresentationCompiler.Compile(exe, textures.Library);
        presentation = presentation with { MapAudio = mapAudio.Definition, Battle = battle.Definition,
            Diplomacy = DiplomacyPresentationCompiler.Compile(textures.Library), Ending = EndingPresentationCompiler.Compile(textures.Library) };
        foreach (var source in mapAudio.Sources.Concat(MapAudioCompiler.MusicSources).Concat(battle.Sources))
            presentation.Sounds.Add(source.Role, AudioAssetCompiler.Path(AudioAssetCompiler.Resolve(audioFiles, source.File)));
        Dictionary<string, byte[]> sounds = new(StringComparer.Ordinal);
        foreach (string file in audioFiles.Values)
            sounds.Add(AudioAssetCompiler.Path(file), Read(Path.Combine(sourceDirectory.Root, file)));
        foreach (string path in presentation.Sounds.Values.Distinct())
            if (!sounds.ContainsKey(path)) throw new InvalidDataException("Missing prepared audio asset: " + path);
        resourceRoot = Path.GetFullPath(resourceRoot ?? output);
        bool shared = resourceRoot != output;
        string audioPrefix = shared ? "shared/" : "";
        string PublishedAudio(string path) => audioPrefix + path["audio/".Length..];
        presentation = presentation with { Sounds = presentation.Sounds.ToDictionary(p => p.Key, p => PublishedAudio(p.Value), StringComparer.Ordinal) };
        sounds = sounds.ToDictionary(p => PublishedAudio(p.Key), p => p.Value, StringComparer.Ordinal);
        var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion,
            dependency, MapInstance.From(map), scenario, text, roles)
        { Presentation = presentation, Preview = preview };
        var loaded = ClientContentFiles.Resolve(content, textures, textureRoot) with { ContentRoot = resourceRoot };
        foreach (string key in messages.Keys)
            if (!San9.Content.Text.MessageGraph.ReferencesValid(key, messages))
                throw new InvalidDataException($"Invalid or cyclic message references: {key}.");
        foreach (var (path, bytes) in sounds)
        {
            string target = PackageFiles.Resolve(resourceRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                await File.WriteAllBytesAsync(target, bytes, token);
        }
        if (publishSharedMessages)
        {
            if (resourceRoot is null) throw new InvalidDataException("Publishing shared messages requires a content root.");
            sharedMessages = await MessageLibraryFiles.WriteAsync(resourceRoot, "shared/messages/library.json", messages,
                PublicationMessageCatalog.Groups, token);
        }
        if (publishSharedMap)
            sharedMap = await MapDefinitionFiles.WriteAsync(resourceRoot, "shared/maps/china/map.json", content.Map.Definition, token);
        await ScenarioContentFiles.WriteAsync(output, content, token, PublicationMessageCatalog.Groups, resourceRoot, sharedMessages,
            sharedMap: sharedMap);
        var currentAudio = sounds.Keys.Select(path => PackageFiles.Resolve(resourceRoot, path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in shared ? [] : new[] { "music", "sound-effects" }.SelectMany(directory =>
            Directory.EnumerateFiles(Path.Combine(output, directory), "*", SearchOption.AllDirectories)))
            if (San9.Content.Interface.AudioFileFormats.Identify(path) is not null && !currentAudio.Contains(path)) File.Delete(path);
        File.Delete(Path.Combine(output, "message-id-map.json"));
        if (messageReports)
        {
            var adaptedKeys = adaptations.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
            var messageAudit = GameplayMessageCompiler.Audit(messages)
                .Select(a => new
                {
                    a.Key,
                    SourceKey = PublicationMessageCatalog.Groups.ContainsKey(a.Key) ? a.Key : null,
                    SourceConfirmed = !adaptedKeys.Contains(a.Key) && a.SourceConfirmed,
                    a.RuntimeReady,
                    a.RequiredArguments
                }).ToArray();
            await File.WriteAllTextAsync(Path.Combine(output, "message-adaptations.json"),
                System.Text.Json.JsonSerializer.Serialize(adaptations.Select(a => new
                { a.Key, SourceKey = a.Key, a.SourceSha256, a.Decision, a.DiscardedHex }),
                    PackageFiles.JsonOptions), System.Text.Encoding.UTF8, token);
            await File.WriteAllTextAsync(Path.Combine(output, "message-readiness.json"),
                System.Text.Json.JsonSerializer.Serialize(messageAudit, PackageFiles.JsonOptions), System.Text.Encoding.UTF8, token);
        }
        else
        {
            File.Delete(Path.Combine(output, "message-adaptations.json"));
            File.Delete(Path.Combine(output, "message-readiness.json"));
        }
        progress?.Invoke($"Messages: {messages.Count} runtime-ready." +
            (messageReports ? " See message-readiness.json for arguments." : ""));
        progress?.Invoke($"Scenario: {scenario.Settlements.Length} settlements; references independent textures at {dependency.Path}.");
        return loaded;
    }
}
