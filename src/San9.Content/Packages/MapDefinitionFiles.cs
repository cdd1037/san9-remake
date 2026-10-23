using System.Text.Json;
using San9.Content.Maps;

namespace San9.Content.Packages;

public sealed record ScenarioMapManifest(string Definition, MapBuildingPlacement[] Buildings);
public sealed record SharedMapDefinition(string Path, MapDefinition Definition);

public static class MapDefinitionFiles
{
    public static async Task<SharedMapDefinition> WriteAsync(string resourceRoot, string path, MapDefinition definition,
        CancellationToken token = default)
    {
        if (definition.SchemaVersion != MapDefinition.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported map definition version.");
        string target = PackageFiles.Resolve(resourceRoot, path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        string pending = target + ".pending";
        try
        {
            await using (var stream = File.Create(pending))
                await JsonSerializer.SerializeAsync(stream, definition, ScenarioContentFiles.DataJsonOptions, token);
            token.ThrowIfCancellationRequested();
            ScenarioContentFiles.Publish([(pending, target)]);
        }
        finally { File.Delete(pending); }
        return new(path, definition);
    }
}
