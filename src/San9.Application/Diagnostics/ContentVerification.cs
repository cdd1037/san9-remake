using San9.Application.Presentation;
using San9.Content.Packages;

namespace San9.Application.Diagnostics;

public static class ContentVerification
{
    public static async Task VerifyAsync(string root, Action<string>? progress = null, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        var catalog = await ContentCatalogFiles.LoadAsync(root, token);
        _ = await MovieCatalogFiles.LoadAsync(root, token);
        var textures = await TexturePackageFiles.LoadAsync(PackageFiles.Resolve(root, catalog.Textures), token);
        var flags = await ContentCatalogFiles.LoadOfficerFlagsAsync(root, catalog, token);
        flags.ValidateReferences(textures.Map.Clips.Select(c => c.Id).ToHashSet());
        var opening = await ContentCatalogFiles.LoadOpeningAsync(root, catalog, token);
        opening.Menu.Validate(textures.Assets); opening.Selection.Validate(textures.Assets);
        // LoadOpeningAsync already checks audio at the catalog root.
        var audioRoots = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal) { root };
        foreach (var entry in catalog.Scenarios)
        {
            token.ThrowIfCancellationRequested();
            var scenario = await ScenarioContentFiles.LoadAsync(PackageFiles.Resolve(root, entry.Package), token);
            if (scenario.Scenario.ScenarioId != entry.Id)
                throw new InvalidDataException($"Scenario identity differs from catalog: {entry.Id}.");
            _ = ClientContentFiles.Resolve(scenario, textures, PackageFiles.Resolve(root, catalog.Textures));
            var preview = opening.Selection.Entries.Single(p => p.Id == entry.Id);
            if (scenario.Scenario.Year != preview.Year || scenario.Scenario.Month != preview.Month)
                throw new InvalidDataException($"Scenario preview date differs from world: {entry.Id}");
            var forces = ScenarioForceSelection.Read(
                scenario.Scenario, scenario.Text, scenario.IllustrationRoles);
            if (preview.CityColors.Any(p => !forces.CityColors.TryGetValue(p.Key, out var color) || color != p.Value))
                throw new InvalidDataException($"Scenario preview ownership differs from world: {entry.Id}; rebuild opening content.");
            if (audioRoots.Add(scenario.ResourceRoot)) _ = AudioCatalogFiles.Load(scenario.ResourceRoot);
            progress?.Invoke($"Verified {entry.Id}: {scenario.Scenario.Forces.Length} forces; {scenario.Scenario.Settlements.Length} settlements; {scenario.Scenario.Officers.Length} officers; package and references ready.");
        }
        // File presence belongs to package integrity; decoding every PNG is the explicit verify-textures job.
        string textureRoot = PackageFiles.Resolve(root, catalog.Textures);
        foreach (string path in textures.Assets.Select(a => a.File.Path).Distinct())
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(PackageFiles.Resolve(textureRoot, path)))
                throw new FileNotFoundException("Missing texture file.", path);
        }
    }
}
