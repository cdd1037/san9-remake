using San9.Content.Text;

namespace San9.Content.Packages;

/// <summary>The shared command entries referenced by scenario static text documents.</summary>
public sealed record SharedCommandText(string Path, IReadOnlyDictionary<string, string> Entries)
{
    public static bool IsCommandKey(string key) => key.StartsWith("ui.commands.", StringComparison.Ordinal);

    internal static void Validate(IReadOnlyDictionary<string, string> entries)
    {
        if (!entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(CommandText.Parameters.Keys.Select(CommandText.Key)))
            throw new InvalidDataException("Command text must contain exactly the current command roles.");
        CommandText.Validate(new(TextCatalog.CurrentSchemaVersion, new(entries)));
    }

    public async Task<bool> StageAsync(string resourceRoot, ContentPublication publication, CancellationToken token)
    {
        Validate(Entries);
        string path = PackageFiles.Resolve(resourceRoot, Path);
        if (File.Exists(path))
        {
            var existing = await PackageFiles.ReadAsync<Dictionary<string, string>>(path, token);
            if (existing.Count == Entries.Count && Entries.All(p => existing.TryGetValue(p.Key, out var value) && value == p.Value))
                return false;
        }
        await publication.StageAsync(path, Entries, ScenarioContentFiles.DataJsonOptions, token);
        return true;
    }
}
