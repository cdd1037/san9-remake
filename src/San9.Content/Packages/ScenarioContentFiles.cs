using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using San9.Content.Scenarios;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Packages;

public sealed record ScenarioContentManifest(int SchemaVersion,
    TextureDependency Textures, string Map, string Scenario, string Text, string[] Messages, string Illustrations)
{
    public ScenarioPreviewEntry? Preview { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public string ResourceRoot { get; init; } = ".";
    [System.Text.Json.Serialization.JsonRequired] public string Presentation { get; init; } = "presentation.json";
    [System.Text.Json.Serialization.JsonRequired] public string? SharedMessages { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public string[] MessageOverrides { get; init; } = [];
}

public sealed record StaticTextContent(int SchemaVersion, Dictionary<string, string> Entries)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<int, MessagePersonProfile> MessagePeople { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public string[] MessageTags { get; init; } = [];
}

/// <summary>Disk contract for a scenario; the assembled model remains independent of file layout.</summary>
public static class ScenarioContentFiles
{
    public static JsonSerializerOptions DataJsonOptions { get; } = CreateDataOptions();

    internal static JsonSerializerOptions MessageJsonOptions { get; } = new(DataJsonOptions)
    { WriteIndented = true, Converters = { new MessageFileJsonConverter() } };

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

    /// <summary>Reads only the lightweight package entry, without following its data dependencies.</summary>
    public static async Task<ScenarioPreviewEntry> LoadPreviewAsync(string root, CancellationToken token = default)
    {
        var manifest = await ReadManifestAsync(root, token);
        return manifest.Preview ?? throw new InvalidDataException("Scenario package has no selection preview.");
    }

    /// <summary>Refreshes selection metadata without loading or rewriting the world dependencies.</summary>
    public static async Task WritePreviewAsync(string root, ScenarioPreviewEntry preview, CancellationToken token = default)
    {
        var manifest = await ReadManifestAsync(root, token);
        string target = Path.Combine(root, "content.json"), pending = target + ".pending";
        try
        {
            await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(manifest with { Preview = preview }, PackageFiles.JsonOptions),
                System.Text.Encoding.UTF8, token);
            token.ThrowIfCancellationRequested();
            Publish([(pending, target)]);
            File.Delete(Path.Combine(root, "preview.json"));
        }
        finally { File.Delete(pending); }
    }

    /// <summary>Reads the authored state needed for force selection without unrelated rules or presentation data.</summary>
    public static async Task<ScenarioSelectionContent> LoadSelectionAsync(string root, CancellationToken token = default)
    {
        var manifest = await ReadManifestAsync(root, token);
        return await ReadSelectionAsync(manifest, ResolveResourceRoot(root, manifest.ResourceRoot), token);
    }

    private static async Task<ScenarioContentManifest> ReadManifestAsync(string root, CancellationToken token)
    {
        // Check the version before interpreting the layout, including older monolithic files.
        await using var stream = File.OpenRead(Path.Combine(root, "content.json"));
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var version)
            || !version.TryGetInt32(out int number) || number != ScenarioContent.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported scenario content version; reconvert the package.");
        var manifest = document.Deserialize<ScenarioContentManifest>(PackageFiles.JsonOptions)
            ?? throw new InvalidDataException("Empty scenario manifest.");
        ContentStructure.Validate(manifest);
        return manifest;
    }

    private static async Task<T> ReadDataAsync<T>(string resourceRoot, string relative, CancellationToken token)
    {
        await using var file = File.OpenRead(PackageFiles.Resolve(resourceRoot, relative));
        var value = await JsonSerializer.DeserializeAsync<T>(file, DataJsonOptions, token)
            ?? throw new InvalidDataException($"Empty content file: {relative}");
        ContentStructure.Validate(value);
        return value;
    }

    private static async Task<ScenarioSelectionContent> ReadSelectionAsync(ScenarioContentManifest manifest, string resourceRoot, CancellationToken token)
    {
        var scenario = await ReadDataAsync<ScenarioSelectionState>(resourceRoot, manifest.Scenario, token);
        if (scenario.SchemaVersion != ScenarioInitialState.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported scenario state version; reconvert the package.");
        var text = await ReadDataAsync<StaticTextContent>(resourceRoot, manifest.Text, token);
        var illustrations = await ReadDataAsync<Dictionary<string, string>>(resourceRoot, manifest.Illustrations, token);
        return new(scenario, new(text.SchemaVersion, text.Entries)
            { MessagePeople = text.MessagePeople, MessageTags = text.MessageTags }, illustrations);
    }

    public static async Task<ScenarioContent> LoadAsync(string root, CancellationToken token = default)
    {
        var manifest = await ReadManifestAsync(root, token);
        string resourceRoot = ResolveResourceRoot(root, manifest.ResourceRoot);
        Task<T> Read<T>(string relative) => ReadDataAsync<T>(resourceRoot, relative, token);
        var mapManifest = await Read<ScenarioMapManifest>(manifest.Map);
        var mapDefinition = await Read<MapDefinition>(mapManifest.Definition);
        if (mapDefinition.SchemaVersion != MapDefinition.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported map definition version; reconvert the package.");
        var map = new MapInstance(mapDefinition, mapManifest.Buildings);
        var scenario = await Read<ScenarioInitialState>(manifest.Scenario);
        var text = await Read<StaticTextContent>(manifest.Text);
        var illustrations = await Read<Dictionary<string, string>>(manifest.Illustrations);
        Dictionary<string, MessageDefinition> messages = new(StringComparer.Ordinal);
        foreach (string path in manifest.Messages)
        foreach (var (key, definition) in await Read<Dictionary<string, MessageDefinition>>(path))
            if (!messages.TryAdd(key, definition))
                throw new InvalidDataException($"Duplicate message key {key} in {path}.");
        var shared = manifest.SharedMessages is { } library
            ? await MessageLibraryFiles.LoadAsync(PackageFiles.Resolve(resourceRoot, library), token) : [];
        messages = ComposeMessages(shared, messages, manifest.MessageOverrides);
        return new(ScenarioContent.CurrentSchemaVersion, manifest.Textures, map, scenario,
            new(text.SchemaVersion, text.Entries) { Messages = messages, MessagePeople = text.MessagePeople, MessageTags = text.MessageTags }, illustrations) { Presentation = await Read<San9.Content.Interface.CommandPresentation>(manifest.Presentation), ResourceRoot = resourceRoot, Preview = manifest.Preview };
    }

    public static async Task WriteAsync(string root, ScenarioContent content, CancellationToken token = default,
        IReadOnlyDictionary<string, string>? messageGroups = null, string? resourceRoot = null,
        SharedMessageLibrary? sharedMessages = null, IReadOnlyCollection<string>? messageOverrides = null,
        SharedMapDefinition? sharedMap = null)
    {
        if (content.SchemaVersion != ScenarioContent.CurrentSchemaVersion) throw new InvalidDataException("Unsupported scenario content version.");
        if (content.Map.Definition.SchemaVersion != MapDefinition.CurrentSchemaVersion) throw new InvalidDataException("Unsupported map definition version.");
        if (messageGroups is not null && messageGroups.Keys.Any(key => !content.Text.Messages.ContainsKey(key)))
            throw new InvalidDataException("Message grouping contains an unknown message key.");
        root = Path.GetFullPath(root);
        resourceRoot = Path.GetFullPath(resourceRoot ?? root);
        string relativeRoot = Path.GetRelativePath(root, resourceRoot).Replace('\\', '/');
        _ = ResolveResourceRoot(root, relativeRoot);
        string ownPrefix = resourceRoot == root ? "" : Path.GetRelativePath(resourceRoot, root).Replace('\\', '/') + "/";
        if (sharedMap is not null && JsonSerializer.Serialize(sharedMap.Definition, DataJsonOptions)
            != JsonSerializer.Serialize(content.Map.Definition, DataJsonOptions))
            throw new InvalidDataException("Scenario geography differs from its explicitly referenced shared map.");
        string mapDefinitionPath = sharedMap?.Path ?? ownPrefix + "map-definition.json";
        var overrides = (messageOverrides ?? []).ToHashSet(StringComparer.Ordinal);
        var privateMessages = content.Text.Messages.Where(p => sharedMessages is null || !sharedMessages.Messages.ContainsKey(p.Key) || overrides.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (sharedMessages is not null)
        {
            var composed = ComposeMessages(sharedMessages.Messages, privateMessages, messageOverrides ?? []);
            if (JsonSerializer.Serialize(composed.OrderBy(p => p.Key, StringComparer.Ordinal), DataJsonOptions)
                != JsonSerializer.Serialize(content.Text.Messages.OrderBy(p => p.Key, StringComparer.Ordinal), DataJsonOptions))
                throw new InvalidDataException("Scenario differs from its shared message library; declare message overrides explicitly.");
        }
        else if (overrides.Count != 0) throw new InvalidDataException("Message overrides require a shared library.");
        var messageFiles = privateMessages.GroupBy(pair =>
            ValidateCategory(messageGroups?.GetValueOrDefault(pair.Key) ?? Category(pair.Key)), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => $"{ownPrefix}messages/{group.Key}.json", group => group.ToDictionary(pair => pair.Key, pair => pair.Value));
        Directory.CreateDirectory(root);
        var manifest = new ScenarioContentManifest(ScenarioContent.CurrentSchemaVersion, content.Textures,
            ownPrefix + "map.json", ownPrefix + "scenario.json", ownPrefix + "messages/text.json", messageFiles.Keys.ToArray(), ownPrefix + "illustrations.json")
            { Presentation = ownPrefix + "presentation.json", ResourceRoot = relativeRoot, Preview = content.Preview,
                SharedMessages = sharedMessages?.Path, MessageOverrides = overrides.Order(StringComparer.Ordinal).ToArray() };
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
                string oldRoot = previous.RootElement.TryGetProperty("resourceRoot", out var oldResourceRoot)
                    ? ResolveResourceRoot(root, oldResourceRoot.GetString()!) : root;
                if (previous.RootElement.TryGetProperty("map", out var oldMap))
                {
                    string oldMapPath = PackageFiles.Resolve(oldRoot, oldMap.GetString()!);
                    if (File.Exists(oldMapPath))
                    {
                        await using var oldMapStream = File.OpenRead(oldMapPath);
                        using var oldMapDocument = await JsonDocument.ParseAsync(oldMapStream, cancellationToken: token);
                        if (oldMapDocument.RootElement.TryGetProperty("definition", out var definition))
                            paths.Add(definition.GetString()!);
                    }
                }
                previousFiles = paths.Select(path => PackageFiles.Resolve(oldRoot, path))
                    .Where(path => IsInside(root, path)).ToArray();
            }
        }
        List<(string Pending, string Target)> files = [];
        async Task Stage<T>(string name, T value, JsonSerializerOptions options)
        {
            string target = name == "content.json" ? manifestPath : PackageFiles.Resolve(resourceRoot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string pending = target + ".pending";
            files.Add((pending, target));
            await using var file = File.Create(pending);
            await JsonSerializer.SerializeAsync(file, value, options, token);
        }
        try
        {
            if (sharedMap is null) await Stage(mapDefinitionPath, content.Map.Definition, DataJsonOptions);
            await Stage(manifest.Map, new ScenarioMapManifest(mapDefinitionPath, content.Map.Buildings), DataJsonOptions);
            await Stage(manifest.Scenario, content.Scenario, DataJsonOptions);
            await Stage(manifest.Text, new StaticTextContent(content.Text.SchemaVersion, content.Text.Entries)
                { MessagePeople = content.Text.MessagePeople, MessageTags = content.Text.MessageTags }, DataJsonOptions);
            foreach (var (path, messages) in messageFiles) await Stage(path, messages, MessageJsonOptions);
            await Stage(manifest.Illustrations, content.IllustrationRoles, DataJsonOptions);
            await Stage(manifest.Presentation, content.Presentation, DataJsonOptions);
            await Stage("content.json", manifest, PackageFiles.JsonOptions);
            token.ThrowIfCancellationRequested();
            Publish(files);
            File.Delete(Path.Combine(root, "preview.json"));
            var currentFiles = files.Select(file => file.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string obsolete in previousFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (currentFiles.Contains(obsolete)) continue;
                File.Delete(obsolete);
                string? directory = Path.GetDirectoryName(obsolete);
                while (directory is not null && IsInside(root, directory) && !directory.Equals(root, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    directory = Path.GetDirectoryName(directory);
                }
            }
        }
        finally
        {
            foreach (var (pending, target) in files)
            {
                File.Delete(pending);
            }
        }
    }

    internal static void Publish(List<(string Pending, string Target)> files)
    {
        List<(string Target, string? Backup)> replaced = [];
        List<string> backups = [];
        bool complete = false;
        try
        {
            foreach (var (pending, target) in files)
            {
                string? backup = File.Exists(target) ? target + ".previous" : null;
                if (backup is not null)
                {
                    File.Copy(target, backup, true);
                    backups.Add(backup);
                }
                File.Move(pending, target, true);
                replaced.Add((target, backup));
            }
            complete = true;
        }
        catch
        {
            foreach (var (target, backup) in replaced.AsEnumerable().Reverse())
                if (backup is null) File.Delete(target);
                else File.Move(backup, target, true);
            complete = true;
            throw;
        }
        finally
        {
            if (complete) foreach (string backup in backups) File.Delete(backup);
        }
    }

    public static async Task<ScenarioInitialState> LoadScenarioAsync(string root, CancellationToken token = default)
    {
        var manifest = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(root, "content.json"), token);
        if (manifest.SchemaVersion != ScenarioContent.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported scenario content version; reconvert the package.");
        string resourceRoot = ResolveResourceRoot(root, manifest.ResourceRoot);
        await using var stream = File.OpenRead(PackageFiles.Resolve(resourceRoot, manifest.Scenario));
        var scenario = await JsonSerializer.DeserializeAsync<ScenarioInitialState>(stream, DataJsonOptions, token)
            ?? throw new InvalidDataException("Empty scenario state.");
        ContentStructure.Validate(scenario);
        return scenario;
    }

    public static string ResolveResourceRoot(string scenarioRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid resource root.");
        string root = Path.GetFullPath(Path.Combine(scenarioRoot, relative));
        if (!IsInside(root, Path.GetFullPath(scenarioRoot))) throw new InvalidDataException("Resource root must contain its scenario package.");
        return root;
    }

    private static bool IsInside(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Category(string key)
    {
        string category = key.Contains('.') ? key[..key.IndexOf('.')] : "general";
        return ValidateCategory(category);
    }

    internal static string ValidateCategory(string category)
    {
        if (category is "text" or "library") throw new InvalidDataException($"Message category '{category}' is reserved.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(category, "^[a-z][a-z0-9-]*$"))
            throw new InvalidDataException($"Invalid message category: {category}.");
        return category;
    }

    private static Dictionary<string, MessageDefinition> ComposeMessages(IReadOnlyDictionary<string, MessageDefinition> shared,
        IReadOnlyDictionary<string, MessageDefinition> local, IReadOnlyCollection<string> overrides)
    {
        var declared = overrides.ToHashSet(StringComparer.Ordinal);
        if (declared.Count != overrides.Count || declared.Any(k => !shared.ContainsKey(k) || !local.ContainsKey(k)))
            throw new InvalidDataException("Invalid message override declaration.");
        Dictionary<string, MessageDefinition> result = new(shared, StringComparer.Ordinal);
        foreach (var (key, message) in local)
        {
            if (result.ContainsKey(key) && !declared.Contains(key))
                throw new InvalidDataException($"Private message collides with shared key without an explicit override: {key}.");
            result[key] = message;
        }
        MessageGraph.Validate(result);
        foreach (string key in result.Keys)
            if (!MessageGraph.ReferencesValid(key, result)) throw new InvalidDataException($"Invalid or cyclic message reference: {key}.");
        return result;
    }
}
