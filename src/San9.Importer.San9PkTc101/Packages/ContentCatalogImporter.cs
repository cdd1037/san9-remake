using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

public static class ContentCatalogImporter
{
    internal static SharedScenarioData CreateSharedScenario(San9.Content.Scenarios.ScenarioInitialState baseline, TextCatalog text) =>
        SharedScenarioData.From(baseline, "shared/definitions.json", "shared/officer-defaults.json", "shared/maps/china/geography.json", text,
            minimumNextOfficerId: San9ScenarioPersonArchive.RecordCount, minimumNextTreasureId: San9ScenarioItemArchive.RecordCount);

    public static async Task ImportAsync(string gameDirectory, string output, Action<string>? progress = null, CancellationToken token = default,
        bool openingOnly = false, bool originalTextEdits = true)
    {
        var source = await San9ImportSource.OpenAsync(gameDirectory, token, originalTextEdits);
        using var operation = ImportOperation.Acquire(output);
        await ImportCoreAsync(operation, source, output, progress, token, openingOnly);
    }

    internal static async Task ImportCoreAsync(ImportOperation operation, San9ImportSource source, string output, Action<string>? progress,
        CancellationToken token, bool openingOnly = false)
    {
        operation.RequireOutput(output);
        output = Path.GetFullPath(output);
        using var publication = new ContentPublication();
        string textureRoot = PackageFiles.Resolve(output, "shared/textures");
        var textures = await source.LoadTexturesAsync(textureRoot, token);
        byte[] exe = source.Executable;
        byte[] messageBytes = source.Read("M_MsgPK.s9");
        var messages = source.Messages("M_MsgPK.s9", "messages", token).Messages;
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, []) { Messages = messages.ToDictionary(p => p.Key, p => new MessageDefinition(MessageGraph.Simplify(p.Value.Nodes))) };
        var headers = Enumerable.Range(1, 20).Select(i =>
        {
            token.ThrowIfCancellationRequested();
            return source.Scenario($"D_Sce{i:D3}.S9").Read(San9ScenarioArchive.ReadHeader);
        }).ToArray();
        var map = OpeningMapCompiler.Compile(exe, source.Read("G_MMap.s9"), textures.Library);
        var selection = ScenarioSelectionPresentationCompiler.Compile(exe, textures.Library, text, headers, map);
        List<ScenarioCatalogEntry> entries = [];
        var sites = new SharedMapSites([], []);
        SharedCommandText? sharedCommands = null;
        SharedMessageLibrary? sharedMessages = null;
        SharedMapDefinition? sharedMap = null;
        SharedScenarioData? sharedScenario = null;
        if (!openingOnly)
        {
            // TC101 explicitly chooses scenario 0 as its publication baseline. Every
            // other scenario retains its differences; no majority-value inference.
            var (baseline, baselineText) = source.World("D_Sce001.S9", token);
            sharedScenario = CreateSharedScenario(baseline, baselineText);
            await ScenarioDataFiles.WriteSharedAsync(output, sharedScenario, token, publication);
        }
        foreach (var preview in selection.Entries)
        {
            var header = headers.Single(h => h.ScenarioId.ToString(System.Globalization.CultureInfo.InvariantCulture) == preview.Id);
            string path = $"scenarios/{preview.CategoryId}/scenario-{preview.Id}";
            entries.Add(new(preview.Id, path));
            if (openingOnly)
            {
                await ScenarioContentFiles.WritePreviewAsync(PackageFiles.Resolve(output, path), preview, token, publication);
                progress?.Invoke($"Refreshed scenario preview: {preview.Id}.");
                continue;
            }
            var compiled = ClientContentCompiler.Compile(source, $"D_Sce{header.ScenarioId + 1:D3}.S9", textureRoot,
                textures, PackageFiles.Resolve(output, path), false, token, preview);
            SharedPlaceImporter.Collect(sites, compiled.Content.Scenario, compiled.Content.Text);
            if (sharedCommands is null)
            {
                sharedCommands = new(CommandTextImporter.LibraryPath, compiled.Content.Text.Entries.Where(p => SharedCommandText.IsCommandKey(p.Key)).ToDictionary());
                await sharedCommands.StageAsync(output, publication, token);
            }
            sharedMessages ??= await MessageLibraryFiles.WriteAsync(output, "shared/messages/library.json", compiled.Content.Text.Messages,
                PublicationMessageCatalog.Groups, token, publication);
            sharedMap ??= await MapDefinitionFiles.WriteAsync(output, "shared/maps/china/map.json", compiled.Content.Map.Definition, token, publication);
            await ClientContentImporter.StageAsync(PackageFiles.Resolve(output, path), compiled, publication, false, progress, token,
                new(output, sharedMessages, sharedMap, sharedScenario!, sharedCommands));
            source.ReleaseScenario($"D_Sce{header.ScenarioId + 1:D3}.S9");
            progress?.Invoke($"Prepared {preview.Id}: {preview.Year}/{preview.Month} ({preview.CategoryId}).");
        }
        var preparedAudio = source.Audio;
        Dictionary<string, string> sounds = [];
        foreach (var (role, file) in new[] { ("setup", "BGM/BGM_02"), ("game-start", "SE/SE_S001") })
        {
            string original = AudioAssetCompiler.Resolve(preparedAudio, file);
            string path = AudioAssetCompiler.Path(original);
            sounds.Add(role, path);
        }
        var opening = new OpeningContent(StartMenuPresentationCompiler.Compile(textures.Library, text), selection with { Entries = [] })
            { Audio = new("setup", "game-start", sounds) };
        opening.Audio.Validate(output);
        await Publish("shared/opening.json", opening);
        using (var names = new ScenarioTextReader(messageBytes, exe, source.OriginalTextEdits))
        {
            var people = source.Scenario("D_Sce001.S9").Read(San9ScenarioPersonArchive.ReadKnownRecords);
            var flags = OfficerFlagCatalogCompiler.Compile(people, names.Surname);
            flags.ValidateReferences(textures.Map.Clips.Select(c => c.Id).ToHashSet());
            await Publish("shared/officer-flags.json", flags);
        }
        var sharedMapRef = openingOnly ? (await ContentCatalogFiles.LoadBaseAsync(output, token)).SharedMap : SharedPlaceImporter.Reference;
        if (!openingOnly) await Publish(SharedPlaceImporter.Reference.Locations, sites);
        await Publish("catalog.json", new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "shared/textures", "shared/opening.json", entries.ToArray())
            { SharedMap = sharedMapRef, OfficerFlags = "shared/officer-flags.json", Definitions = "shared/definitions.json", OfficerDefaults = "shared/officer-defaults.json" });

        publication.Commit(token);
        progress?.Invoke("Published complete scenario catalog.");

        Task Publish<T>(string path, T value) => publication.StageAsync(PackageFiles.Resolve(output, path), value, PackageFiles.JsonOptions, token);
    }
}
