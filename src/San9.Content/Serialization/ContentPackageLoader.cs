using System.Text.Json;

namespace San9.Content.Serialization;

public static class ContentPackageLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<ContentPackage> LoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string manifestPath = Path.Combine(directory, "manifest.json");
        string scenarioPath = Path.Combine(directory, "scenario.json");
        ContentManifest manifest = await DeserializeAsync<ContentManifest>(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        ScenarioDefinition scenario = await DeserializeAsync<ScenarioDefinition>(scenarioPath, cancellationToken)
            .ConfigureAwait(false);
        return new ContentPackage(manifest, scenario);
    }

    private static async Task<T> DeserializeAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return value ?? throw new InvalidDataException($"Content file '{path}' contained no {typeof(T).Name} value.");
    }
}
