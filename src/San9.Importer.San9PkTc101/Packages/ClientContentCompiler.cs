using System.Security.Cryptography;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

internal sealed record CompiledScenario(ScenarioContent Content, MessageContentAdaptation[] Adaptations, ClientContentPackage Package);

internal static class ClientContentCompiler
{
    internal static CompiledScenario Compile(San9ImportSource source, string scenarioFile, string textureRoot,
        TexturePackage textures, string output, bool messageReports, CancellationToken token,
        San9.Content.Interface.ScenarioPreviewEntry? preview = null)
    {
        token.ThrowIfCancellationRequested();
        var scenarioSource = source.Scenario(scenarioFile);
        byte[] exe = source.Executable, scenarioBytes = scenarioSource.Bytes;
        string sourceExe = source.Directory.Executable;
        var map = MapImporter.Build(scenarioSource, textures.Map,
            [new(Path.GetFileName(sourceExe), Convert.ToHexString(SHA256.HashData(exe))),
             new(Path.GetFileName(scenarioFile), Convert.ToHexString(SHA256.HashData(scenarioBytes)))]);
        var (scenario, text) = source.World(scenarioFile, token);
        Dictionary<string, San9.Content.Text.MessageDefinition> messages = [];
        List<MessageContentAdaptation> adaptations = [];
        foreach (var (file, catalog) in new[] { ("M_MsgPK.s9", "messages"), ("M_RtdnPK.s9", "biographies") })
        {
            var converted = source.Messages(file, catalog, token);
            foreach (var item in converted.Messages) messages.Add(item.Key, item.Value);
            if (messageReports) adaptations.AddRange(converted.Adaptations);
        }
        var audioFiles = source.Audio;
        San9.Content.Interface.CommandPresentation presentation;
        {
            var header = scenarioSource.Read(San9ScenarioArchive.ReadHeader);
            var people = scenarioSource.Read(San9ScenarioPersonArchive.ReadKnownRecords);
            presentation = CommandPresentationCompiler.Compile(people, audioFiles) with
            {
                OpeningMovie = MovieLibraryImporter.ScenarioOpeningRole(header.ScenarioId),
                Notices = new() { ["scroll-hover"] = "messages.8427", ["scroll-click"] = "messages.8428" }
            };
            messages = new GameplayMessageCompiler(scenario, people).Compile(messages, token);
            text = text with { MessageTags = ScenarioMessageTags.For(header.ScenarioId, header.ScenarioModeCode) };
        }
        messages = PublicationMessageCatalog.Compile(messages);
        San9.Content.Text.NameMessages.AddTo(messages);
        EventPresentationCompiler.AddMessages(messages);
        ChroniclePresentationCompiler.AddMessages(messages);
        ExecutionPresentationCompiler.AddMessages(messages);
        text = text with { Messages = messages };
        var roles = InformationArtCompiler.Compile(exe, scenarioSource, scenario, textures.Library, textures.Portraits);
        var dependency = new TextureDependency(Path.GetRelativePath(output, textureRoot).Replace('\\','/'));
        var mapAudio = MapAudioCompiler.Compile(exe);
        var battle = BattlePresentationCompiler.Compile(exe, textures.Library);
        presentation = presentation with { MapAudio = mapAudio.Definition, Battle = battle.Definition,
            Diplomacy = DiplomacyPresentationCompiler.Compile(textures.Library), Ending = EndingPresentationCompiler.Compile(textures.Library) };
        foreach (var sound in mapAudio.Sources.Concat(MapAudioCompiler.MusicSources).Concat(battle.Sources))
            presentation.Sounds.Add(sound.Role, AudioAssetCompiler.Path(AudioAssetCompiler.Resolve(audioFiles, sound.File)));
        var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion,
            dependency, MapInstance.From(map), scenario, text, roles)
        { Presentation = presentation, Preview = preview };
        var package = ClientContentFiles.Resolve(content, textures, textureRoot);
        foreach (string key in messages.Keys)
            if (!San9.Content.Text.MessageGraph.ReferencesValid(key, messages))
                throw new InvalidDataException($"Invalid or cyclic message references: {key}.");
        return new(content, adaptations.ToArray(), package);
    }
}
