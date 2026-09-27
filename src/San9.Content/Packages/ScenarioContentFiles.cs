using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Packages;

public sealed record ScenarioContentManifest(int SchemaVersion, ContentSource[] Sources,
    TextureDependency Textures, string Map, string Scenario, string Text, string[] Messages, string Illustrations)
{
    [System.Text.Json.Serialization.JsonRequired] public string Presentation { get; init; } = "presentation.json";
}

public sealed record StaticTextContent(int SchemaVersion, Dictionary<string, string> Entries)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<int, MessagePersonProfile> MessagePeople { get; init; } = [];
}

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
            || !version.TryGetInt32(out int number) || number != ScenarioContent.CurrentSchemaVersion)
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
        Dictionary<string, MessageDefinition> messages = new(StringComparer.Ordinal);
        foreach (string path in manifest.Messages)
        foreach (var (key, definition) in await Read<Dictionary<string, MessageDefinition>>(path))
            if (!messages.TryAdd(key, definition))
                throw new InvalidDataException($"Duplicate message key {key} in {path}.");
        MessageGraph.Validate(messages);
        var illustrations = await Read<Dictionary<string, string>>(manifest.Illustrations);
        return new(ScenarioContent.CurrentSchemaVersion, manifest.Sources, manifest.Textures, map, scenario,
            new(text.SchemaVersion, text.Entries) { Messages = messages, MessagePeople = text.MessagePeople }, illustrations) { Presentation = await Read<San9.Content.Interface.CommandPresentation>(manifest.Presentation) };
    }

    public static async Task WriteAsync(string root, ScenarioContent content, CancellationToken token = default)
    {
        if (content.SchemaVersion != ScenarioContent.CurrentSchemaVersion) throw new InvalidDataException("Unsupported scenario content version.");
        var messageFiles = content.Text.Messages.GroupBy(pair => Category(pair.Key), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => $"messages/{group.Key}.json", group => group.ToDictionary(pair => pair.Key, pair => pair.Value));
        Directory.CreateDirectory(root);
        var manifest = new ScenarioContentManifest(ScenarioContent.CurrentSchemaVersion, content.Sources, content.Textures,
            "map.json", "scenario.json", "text.json", messageFiles.Keys.ToArray(), "illustrations.json");
        string manifestPath = Path.Combine(root, "content.json");
        string[] previousFiles = [];
        if (File.Exists(manifestPath))
        {
            await using var previousStream = File.OpenRead(manifestPath);
            using var previous = await JsonDocument.ParseAsync(previousStream, cancellationToken: token);
            // Read only old file references for retirement, never old content as a runtime contract.
            // This also retires the monolithic messages.json when reconverting an older package.
            if (previous.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() <= ScenarioContent.CurrentSchemaVersion)
            {
                List<string> paths = [];
                foreach (string field in new[] { "map", "scenario", "text", "messages", "illustrations", "presentation" })
                {
                    if (!previous.RootElement.TryGetProperty(field, out var value)) continue;
                    if (value.ValueKind == JsonValueKind.String) paths.Add(value.GetString()!);
                    else if (field == "messages" && value.ValueKind == JsonValueKind.Array)
                        paths.AddRange(value.EnumerateArray().Select(item => item.GetString()!));
                }
                previousFiles = paths.Select(path => PackageFiles.Resolve(root, path)).ToArray();
            }
        }
        List<(string Pending, string Target)> files = [];
        async Task Stage<T>(string name, T value, JsonSerializerOptions options)
        {
            string target = PackageFiles.Resolve(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".pending";
            files.Add((pending, target));
            await using var file = File.Create(pending);
            await JsonSerializer.SerializeAsync(file, value, options, token);
        }
        try
        {
            await Stage(manifest.Map, content.Map, DataJsonOptions);
            await Stage(manifest.Scenario, content.Scenario, DataJsonOptions);
            await Stage(manifest.Text, new StaticTextContent(content.Text.SchemaVersion, content.Text.Entries) { MessagePeople = content.Text.MessagePeople }, DataJsonOptions);
            foreach (var (path, messages) in messageFiles) await Stage(path, messages, DataJsonOptions);
            await Stage(manifest.Illustrations, content.IllustrationRoles, DataJsonOptions);
            await Stage(manifest.Presentation, content.Presentation, DataJsonOptions);
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

    private static string Category(string key)
    {
        string category = key.Contains('.') ? key[..key.IndexOf('.')] : "general";
        if (!System.Text.RegularExpressions.Regex.IsMatch(category, "^[a-z][a-z0-9-]*$"))
            throw new InvalidDataException($"Invalid message category: {key}.");
        return category;
    }
}
