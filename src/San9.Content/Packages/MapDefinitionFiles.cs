using System.Text.Json;
using San9.Content.Maps;

namespace San9.Content.Packages;

public sealed record ScenarioMapManifest(string Definition, MapBuildingPlacement[] Buildings)
{
    public string? GeographyInputHash { get; init; }
}
public sealed record SharedMapDefinition(string Path, MapDefinition Definition);

public static class MapDefinitionFiles
{
    public static async Task<SharedMapDefinition> WriteAsync(string resourceRoot, string path, MapDefinition definition,
        CancellationToken token = default, ContentPublication? publication = null)
    {
        if (definition.SchemaVersion != MapDefinition.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported map definition version.");
        using var own = publication is null ? new ContentPublication() : null;
        await (publication ?? own!).StageAsync(PackageFiles.Resolve(resourceRoot, path), definition, ScenarioContentFiles.DataJsonOptions, token);
        own?.Commit(token);
        return new(path, definition);
    }
}
