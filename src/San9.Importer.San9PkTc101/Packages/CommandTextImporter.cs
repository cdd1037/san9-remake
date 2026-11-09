using San9.Content.Packages;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Rebuilds command-interface roles without compiling worlds, gameplay messages or display resources.</summary>
public static class CommandTextImporter
{
    public static async Task RebuildAsync(string gameDirectory, string output, Action<string>? progress = null,
        CancellationToken token = default)
    {
        var source = await San9ImportSource.OpenAsync(gameDirectory, token);
        using var operation = ImportOperation.Acquire(output);
        var catalog = await ContentCatalogFiles.LoadAsync(output, token);
        using var reader = new ScenarioTextReader(source.Read("M_MsgPK.s9"), source.Executable);
        reader.ReadCommandText();
        int changed = await ScenarioContentFiles.WriteCommandTextAsync(catalog.Scenarios
            .Select(entry => PackageFiles.Resolve(output, entry.Package)).ToArray(), reader.Catalog.Entries, token);
        progress?.Invoke($"Updated command text: {changed} files; worlds, gameplay messages and resources preserved.");
    }
}
