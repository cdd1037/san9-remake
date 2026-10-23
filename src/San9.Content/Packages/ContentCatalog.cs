using San9.Content.Interface;

namespace San9.Content.Packages;

public sealed record ScenarioCatalogEntry(string Id, string Package);

/// <summary>Package discovery only; previews and world data belong to individual scenarios.</summary>
public sealed record ContentCatalog(int SchemaVersion, string Textures, string Opening,
    ScenarioCatalogEntry[] Scenarios)
{
    public const int CurrentSchemaVersion = 3;
    public ScenarioCatalogEntry RequireScenario(string id) => Scenarios.SingleOrDefault(s => s.Id == id)
        ?? throw new InvalidDataException($"Unknown scenario: {id}.");
}

public sealed record OpeningContent(StartMenuPresentation Menu, ScenarioSelectionPresentation Selection);

public static class ContentCatalogFiles
{
    public static async Task<ContentCatalog> LoadAsync(string root, CancellationToken token = default)
    {
        var catalog = await PackageFiles.ReadAsync<ContentCatalog>(Path.Combine(root, "catalog.json"), token);
        if (catalog.SchemaVersion != ContentCatalog.CurrentSchemaVersion || catalog.Scenarios.Length == 0 ||
            catalog.Scenarios.Select(s => s.Id).Distinct().Count() != catalog.Scenarios.Length ||
            catalog.Scenarios.Any(s => string.IsNullOrWhiteSpace(s.Id)))
            throw new InvalidDataException("Invalid content catalog.");
        _ = PackageFiles.Resolve(root, catalog.Textures);
        _ = PackageFiles.Resolve(root, catalog.Opening);
        foreach (var scenario in catalog.Scenarios) _ = PackageFiles.Resolve(root, scenario.Package);
        return catalog;
    }

    public static async Task<ScenarioPreviewEntry[]> LoadPreviewsAsync(string root, ContentCatalog catalog, CancellationToken token = default)
    {
        var previews = await Task.WhenAll(catalog.Scenarios.Select(async entry =>
        {
            string package = PackageFiles.Resolve(root, entry.Package);
            var preview = await ScenarioContentFiles.LoadPreviewAsync(package, token);
            if (preview.Id != entry.Id) throw new InvalidDataException($"Scenario preview identity differs from catalog: {entry.Id}.");
            return preview;
        }));
        return previews;
    }

    public static async Task<OpeningContent> LoadOpeningAsync(string root, ContentCatalog catalog, CancellationToken token = default)
    {
        var opening = await PackageFiles.ReadAsync<OpeningContent>(PackageFiles.Resolve(root, catalog.Opening), token);
        return opening with { Selection = opening.Selection with { Entries = await LoadPreviewsAsync(root, catalog, token) } };
    }
}
