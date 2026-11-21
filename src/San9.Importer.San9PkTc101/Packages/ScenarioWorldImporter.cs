using System.Globalization;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Recompiles source worlds while preserving separately owned presentation and shared resources.</summary>
public static class ScenarioWorldImporter
{
    public static async Task RebuildAsync(string gameDirectory, string output, Action<string>? progress = null,
        CancellationToken token = default, bool originalTextEdits = true)
    {
        var source = await San9ImportSource.OpenAsync(gameDirectory, token, originalTextEdits);
        using var operation = ImportOperation.Acquire(output);
        output = Path.GetFullPath(output);
        var catalog = await ContentCatalogFiles.LoadBaseAsync(output, token);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(source.Directory.Root, "D_Sce*.S9"))
        {
            string id = source.Scenario(Path.GetFileName(path)).Read(San9ScenarioArchive.ReadHeader).ScenarioId.ToString(CultureInfo.InvariantCulture);
            if (!sources.TryAdd(id, path)) throw new InvalidDataException($"Duplicate source scenario identity: {id}.");
        }
        List<(ScenarioCatalogEntry Entry, ScenarioInitialState Scenario, San9.Content.Text.TextCatalog Text)> worlds = [];
        foreach (var entry in catalog.Scenarios)
        {
            token.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(entry.Id, out var path)) throw new InvalidDataException($"Missing source scenario: {entry.Id}.");
            var (scenario, texts) = source.World(Path.GetFileName(path), token);
            worlds.Add((entry, scenario, texts));
        }
        var baseline = worlds.Single(w => w.Entry.Id == "0");
        var shared = ContentCatalogImporter.CreateSharedScenario(baseline.Scenario, baseline.Text);
        var sites = new SharedMapSites([], []);
        foreach (var world in worlds) SharedPlaceImporter.Collect(sites, world.Scenario, world.Text);
        await ScenarioContentFiles.WriteWorldsAsync(output, shared, worlds.Select(w => (w.Entry.Package, w.Scenario, w.Text)).ToArray(), token, SharedPlaceImporter.Reference, sites);
        foreach (var (entry, _, _) in worlds) progress?.Invoke($"Rebuilt scenario world: {entry.Id}.");
    }
}
