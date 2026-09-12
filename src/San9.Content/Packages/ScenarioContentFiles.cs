using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Packages;

public sealed record ScenarioContentManifest(int SchemaVersion, ContentSource[] Sources,
    TextureDependency Textures, string Map, string Scenario, string Text, string Messages, string Illustrations);

public sealed record StaticTextContent(int SchemaVersion, Dictionary<string, TextEntry> Entries);

/// <summary>Disk contract for a scenario; the assembled model remains independent of file layout.</summary>
public static class ScenarioContentFiles
{
    public static JsonSerializerOptions DataJsonOptions { get; } = CreateDataOptions();

    private static JsonSerializerOptions CreateDataOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            // These constructor parameters explicitly default to null. Required nullable
            // state fields must still be written, including when their value is null.
            foreach (var property in type.Properties)
                if (type.Type == typeof(MessageExpression) && property.Name is "binding" or "number" or "arguments"
                    || type.Type == typeof(MessageValue) && property.Name == "format")
                    property.ShouldSerialize = (_, value) => value is not null;
        });
        return new(PackageFiles.JsonOptions) { WriteIndented = false, TypeInfoResolver = resolver };
    }

    public static async Task<ScenarioContent> LoadAsync(string root, CancellationToken token = default)
    {
        // Check the version before interpreting the layout, including older monolithic files.
        await using var stream = File.OpenRead(Path.Combine(root, "content.json"));
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var version)
            || !version.TryGetInt32(out int number) || number != 25)
            throw new InvalidDataException("Unsupported scenario content version; reconvert the package.");
        var manifest = document.Deserialize<ScenarioContentManifest>(PackageFiles.JsonOptions)
            ?? throw new InvalidDataException("Empty scenario manifest.");
        async Task<T> Read<T>(string relative)
        {
            await using var file = File.OpenRead(PackageFiles.Resolve(root, relative));
            return await JsonSerializer.DeserializeAsync<T>(file, DataJsonOptions, token)
                ?? throw new InvalidDataException($"Empty content file: {relative}");
        }
        var map = await Read<MapInstance>(manifest.Map);
        var scenario = await Read<ScenarioInitialState>(manifest.Scenario);
        var text = await Read<StaticTextContent>(manifest.Text);
        var messages = await Read<Dictionary<string, MessageDefinition>>(manifest.Messages);
        var illustrations = await Read<Dictionary<string, string>>(manifest.Illustrations);
        return new(25, manifest.Sources, manifest.Textures, map, scenario,
            new(text.SchemaVersion, text.Entries) { Messages = messages }, illustrations);
    }

    public static async Task WriteAsync(string root, ScenarioContent content, CancellationToken token = default)
    {
        if (content.SchemaVersion != 25) throw new InvalidDataException("Unsupported scenario content version.");
        Directory.CreateDirectory(root);
        var manifest = new ScenarioContentManifest(25, content.Sources, content.Textures,
            "map.json", "scenario.json", "text.json", "messages.json", "illustrations.json");
        string manifestPath = Path.Combine(root, "content.json");
        string[] previousFiles = [];
        if (File.Exists(manifestPath))
        {
            await using var previousStream = File.OpenRead(manifestPath);
            using var previous = await JsonDocument.ParseAsync(previousStream, cancellationToken: token);
            if (previous.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() == 25)
            {
                var old = previous.Deserialize<ScenarioContentManifest>(PackageFiles.JsonOptions)!;
                previousFiles = new[] { old.Map, old.Scenario, old.Text, old.Messages, old.Illustrations }
                    .Select(path => PackageFiles.Resolve(root, path)).ToArray();
            }
        }
        List<(string Pending, string Target)> files = [];
        async Task Stage<T>(string name, T value, JsonSerializerOptions options)
        {
            string target = PackageFiles.Resolve(root, name);
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".pending";
            files.Add((pending, target));
            await using var file = File.Create(pending);
            await JsonSerializer.SerializeAsync(file, value, options, token);
        }
        try
        {
            await Stage(manifest.Map, content.Map, DataJsonOptions);
            await Stage(manifest.Scenario, content.Scenario, DataJsonOptions);
            await Stage(manifest.Text, new StaticTextContent(content.Text.SchemaVersion, content.Text.Entries), DataJsonOptions);
            await Stage(manifest.Messages, content.Text.Messages, DataJsonOptions);
            await Stage(manifest.Illustrations, content.IllustrationRoles, DataJsonOptions);
            await Stage("content.json", manifest, PackageFiles.JsonOptions);
            token.ThrowIfCancellationRequested();
            foreach (var (pending, target) in files) File.Move(pending, target, true);
            var currentFiles = files.Select(file => file.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string obsolete in previousFiles.Distinct(StringComparer.OrdinalIgnoreCase))
                if (!currentFiles.Contains(obsolete)) File.Delete(obsolete);
        }
        finally
        {
            foreach (var (pending, _) in files) File.Delete(pending);
        }
    }
}
