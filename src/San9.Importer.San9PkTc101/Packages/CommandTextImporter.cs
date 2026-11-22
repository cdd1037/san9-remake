using San9.Content.Packages;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Rebuilds command-interface roles without compiling worlds, gameplay messages or display resources.</summary>
public static class CommandTextImporter
{
    internal const string LibraryPath = "shared/messages/commands.json";

    public static async Task RebuildAsync(string gameDirectory, string output, Action<string>? progress = null,
        CancellationToken token = default, bool originalTextEdits = true)
    {
        var source = await San9ImportSource.OpenAsync(gameDirectory, token, originalTextEdits);
        using var operation = ImportOperation.Acquire(output);
        var catalog = await ContentCatalogFiles.LoadBaseAsync(output, token);
        using var reader = new ScenarioTextReader(source.Read("M_MsgPK.s9"), source.Executable, source.OriginalTextEdits);
        reader.ReadCommandText();
        int changed = await ScenarioContentFiles.WriteCommandTextAsync(catalog.Scenarios
            .Select(entry => PackageFiles.Resolve(output, entry.Package)).ToArray(), reader.Catalog.Entries, token, output, LibraryPath);
        progress?.Invoke($"Updated command text: {changed} files; worlds, gameplay messages and resources preserved.");
    }
}
