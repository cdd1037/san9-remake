using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

public static class ContentCatalogImporter
{
    public static async Task ImportAsync(string gameDirectory, string output, Action<string>? progress = null, CancellationToken token = default,
        bool openingOnly = false)
    {
        output = Path.GetFullPath(output);
        var source = new San9SourceDirectory(gameDirectory);
        var identity = await San9PkTc101Detector.DetectAsync(source.Executable, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        string textureRoot = PackageFiles.Resolve(output, "shared/textures");
        var textures = await TexturePackageFiles.LoadAsync(textureRoot, token);
        byte[] exe = await File.ReadAllBytesAsync(source.Executable, token);
        var messages = MessageArchiveCompiler.CompileForPublication(File.ReadAllBytes(source.File("M_MsgPK.s9")), "M_MsgPK.s9", "messages").Messages;
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, []) { Messages = messages.ToDictionary(p => p.Key, p => new MessageDefinition(MessageGraph.Simplify(p.Value.Nodes))) };
        var headers = Enumerable.Range(1, 20).Select(i =>
        {
            using var file = File.OpenRead(source.File($"D_Sce{i:D3}.S9"));
            return San9ScenarioArchive.ReadHeader(file);
        }).ToArray();
        var map = OpeningMapCompiler.Compile(exe, File.ReadAllBytes(source.File("G_MMap.s9")), textures.Library);
        var selection = ScenarioSelectionPresentationCompiler.Compile(exe, textures.Library, text, headers, map);
        List<ScenarioCatalogEntry> entries = [];
        SharedMessageLibrary? sharedMessages = null;
        SharedMapDefinition? sharedMap = null;
        foreach (var preview in selection.Entries)
        {
            var header = headers.Single(h => $"scenario-{h.ScenarioId}" == preview.Id);
            string path = $"scenarios/{preview.CategoryId}/{preview.Id}";
            entries.Add(new(preview.Id, path));
            if (openingOnly)
            {
                await ScenarioContentFiles.WritePreviewAsync(PackageFiles.Resolve(output, path), preview, token);
                progress?.Invoke($"Refreshed scenario preview: {preview.Id}.");
                continue;
            }
            var package = await ClientContentImporter.ImportAsync(gameDirectory, $"D_Sce{header.ScenarioId + 1:D3}.S9", textureRoot,
                PackageFiles.Resolve(output, path), progress, token: token, resourceRoot: output,
                sharedMessages: sharedMessages, publishSharedMessages: sharedMessages is null,
                sharedMap: sharedMap, publishSharedMap: sharedMap is null, preview: preview);
            sharedMessages ??= new("shared/messages/library.json", package.Text.Messages);
            sharedMap ??= new("shared/maps/china/map.json", MapDefinition.From(package.Map));
            progress?.Invoke($"Published {preview.Id}: {preview.Year}/{preview.Month} ({preview.CategoryId}).");
        }
        var opening = new OpeningContent(StartMenuPresentationCompiler.Compile(textures.Library, text), selection with { Entries = [] });
        await Publish("shared/opening.json", opening);
        await Publish("catalog.json", new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "shared/textures", "shared/opening.json", entries.ToArray()));

        async Task Publish<T>(string path, T value)
        {
            string target = PackageFiles.Resolve(output, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string pending = target + ".pending";
            try
            {
                await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(value, PackageFiles.JsonOptions), System.Text.Encoding.UTF8, token);
                File.Move(pending, target, true);
            }
            finally { File.Delete(pending); }
        }
    }

}
