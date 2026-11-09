using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using San9.Content.Scenarios;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Packages;

public sealed record ScenarioContentManifest(int SchemaVersion,
    TextureDependency Textures, string Map, string Scenario, string Text, string[] Messages, string Illustrations)
{
    [System.Text.Json.Serialization.JsonRequired] public string Definitions { get; init; } = "definitions.json";
    [System.Text.Json.Serialization.JsonRequired] public string OfficerDefaults { get; init; } = "officer-defaults.json";
    [System.Text.Json.Serialization.JsonRequired] public string Geography { get; init; } = "geography.json";
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
    public static async Task WritePreviewAsync(string root, ScenarioPreviewEntry preview, CancellationToken token = default,
        ContentPublication? publication = null)
    {
        using var own = publication is null ? new ContentPublication() : null;
        var batch = publication ?? own!;
        var manifest = await ReadManifestAsync(root, token);
        await batch.StageAsync(Path.Combine(root, "content.json"), manifest with { Preview = preview }, PackageFiles.JsonOptions, token);
        batch.AfterCommit(() => File.Delete(Path.Combine(root, "preview.json")));
        own?.Commit(token);
    }

    /// <summary>Replaces only command-interface text in matching-format packages; all other data stays published as-is.</summary>
    public static async Task<int> WriteCommandTextAsync(IReadOnlyList<string> roots,
        IReadOnlyDictionary<string, string> entries, CancellationToken token = default)
    {
        if (!entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(CommandText.Parameters.Keys.Select(CommandText.Key)))
            throw new InvalidDataException("Command text replacement must contain exactly the current command roles.");
        CommandText.Validate(new(TextCatalog.CurrentSchemaVersion, new(entries)));
        using var publication = new ContentPublication();
        var written = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        int changed = 0;
        foreach (string root in roots)
        {
            token.ThrowIfCancellationRequested();
            var manifest = await ReadManifestAsync(root, token);
            string path = PackageFiles.Resolve(ResolveResourceRoot(root, manifest.ResourceRoot), manifest.Text);
            if (!written.Add(path)) continue;
            var text = await PackageFiles.ReadAsync<StaticTextContent>(path, token);
            if (text.SchemaVersion != TextCatalog.CurrentSchemaVersion)
                throw new InvalidDataException("Unsupported text version; reconvert the package.");
            var previous = text.Entries.Where(p => p.Key.StartsWith("ui.commands.", StringComparison.Ordinal)).ToArray();
            if (previous.Length == entries.Count && previous.All(p => entries.TryGetValue(p.Key, out var value) && p.Value == value)) continue;
            var replacement = text.Entries.Where(p => !p.Key.StartsWith("ui.commands.", StringComparison.Ordinal))
                .Concat(entries).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            await publication.StageAsync(path, text with { Entries = replacement }, DataJsonOptions, token);
            changed++;
        }
        publication.Commit(token);
        return changed;
    }

    /// <summary>Publishes shared inputs and rebuilt worlds together, preserving map and presentation resources.</summary>
    public static async Task WriteWorldsAsync(string resourceRoot, SharedScenarioData shared,
        IReadOnlyList<(string Package, ScenarioInitialState Scenario, TextCatalog Text)> worlds, CancellationToken token = default)
    {
        shared.Validate();
        resourceRoot = Path.GetFullPath(resourceRoot);
        using var publication = new ContentPublication();
        List<(string Path, ScenarioContentManifest Manifest)> manifests = [];
        await ScenarioDataFiles.WriteSharedAsync(resourceRoot, shared, token, publication);
        foreach (var (package, scenario, texts) in worlds)
        {
            string root = PackageFiles.Resolve(resourceRoot, package);
            // Read publication metadata only; the old world is never interpreted.
            var layout = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "content.json"), token))!.AsObject();
            layout["definitions"] = shared.DefinitionsPath;
            layout["officerDefaults"] = shared.OfficersPath;
            layout["geography"] = shared.GeographyPath;
            var manifest = layout.Deserialize<ScenarioContentManifest>(PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("Empty scenario manifest.");
            scenario.Validate();
            if (ResolveResourceRoot(root, manifest.ResourceRoot) != resourceRoot)
                throw new InvalidDataException("Rebuilt scenario uses a different publication root.");
            if (manifest.Preview is { } preview && (preview.Id != scenario.ScenarioId || preview.Year != scenario.Year || preview.Month != scenario.Month))
                throw new InvalidDataException("Rebuilt world differs from scenario selection metadata.");
            await publication.StageAsync(PackageFiles.Resolve(resourceRoot, manifest.Scenario), shared.Describe(scenario, texts), DataJsonOptions, token);
            manifests.Add((Path.GetRelativePath(resourceRoot, Path.Combine(root, "content.json")),
                manifest with { SchemaVersion = ScenarioContent.CurrentSchemaVersion }));
        }
        // All dependency and world files precede every entry point. Publish rolls
        // back the whole batch if replacing any file fails.
        foreach (var (path, manifest) in manifests)
            await publication.StageAsync(PackageFiles.Resolve(resourceRoot, path), manifest, PackageFiles.JsonOptions, token);
        token.ThrowIfCancellationRequested();
        publication.Commit(token);
    }

    /// <summary>Reads the authored state needed for force selection without unrelated rules or presentation data.</summary>
    public static async Task<ScenarioSelectionContent> LoadSelectionAsync(string root, CancellationToken token = default)
    {
        var manifest = await ReadManifestAsync(root, token);
        return await ReadSelectionAsync(manifest, ResolveResourceRoot(root, manifest.ResourceRoot), token);
    }

    public static async Task<ScenarioContentManifest> ReadManifestAsync(string root, CancellationToken token = default)
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

    /// <summary>Reads presentation bindings without loading world, map or texture dependencies.</summary>
    public static Task<CommandPresentation> LoadPresentationAsync(string root, ScenarioContentManifest manifest,
        CancellationToken token = default) => ReadDataAsync<CommandPresentation>(
            ResolveResourceRoot(root, manifest.ResourceRoot), manifest.Presentation, token);

    private static async Task<ScenarioSelectionContent> ReadSelectionAsync(ScenarioContentManifest manifest, string resourceRoot, CancellationToken token)
    {
        var (scenario, profiles) = await ScenarioDataFiles.LoadSelectionWithProfilesAsync(resourceRoot, manifest, token);
        if (scenario.SchemaVersion != ScenarioInitialState.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported scenario state version; reconvert the package.");
        var text = await ReadDataAsync<StaticTextContent>(resourceRoot, manifest.Text, token);
        var illustrations = await ReadDataAsync<Dictionary<string, string>>(resourceRoot, manifest.Illustrations, token);
        return new(scenario, OfficerComposition.ProjectText(new(text.SchemaVersion, text.Entries)
            { MessagePeople = text.MessagePeople, MessageTags = text.MessageTags }, profiles), OfficerComposition.ProjectPortraits(illustrations, profiles));
    }

    public static async Task<ScenarioContent> LoadAsync(string root, CancellationToken token = default) =>
        await LoadAsync(root, await ReadManifestAsync(root, token), token);

    internal static async Task<ScenarioContent> LoadAsync(string root, ScenarioContentManifest manifest, CancellationToken token)
    {
        string resourceRoot = ResolveResourceRoot(root, manifest.ResourceRoot);
        Task<T> Read<T>(string relative) => ReadDataAsync<T>(resourceRoot, relative, token);
        // Four independent branches keep CPU-heavy parsing off the caller thread without
        // creating one worker for every small message file. Composition follows all branches.
        var mapTask = Task.Run(async () =>
        {
            var mapManifest = await Read<ScenarioMapManifest>(manifest.Map);
            var definition = await Read<MapDefinition>(mapManifest.Definition);
            if (definition.SchemaVersion != MapDefinition.CurrentSchemaVersion)
                throw new InvalidDataException("Unsupported map definition version; reconvert the package.");
            return new MapInstance(definition, mapManifest.Buildings);
        }, token);
        var scenarioTask = Task.Run(() => ScenarioDataFiles.LoadScenarioWithProfilesAsync(resourceRoot, manifest, token), token);
        var displayTask = Task.Run(async () =>
        {
            var text = await Read<StaticTextContent>(manifest.Text);
            var illustrations = await Read<Dictionary<string, string>>(manifest.Illustrations);
            var presentation = await LoadPresentationAsync(root, manifest, token);
            return (Text: text, Illustrations: illustrations, Presentation: presentation);
        }, token);
        var messagesTask = Task.Run(async () =>
        {
            Dictionary<string, MessageDefinition> messages = new(StringComparer.Ordinal);
            foreach (string path in manifest.Messages)
            foreach (var (key, definition) in await Read<Dictionary<string, MessageDefinition>>(path))
                if (!messages.TryAdd(key, definition))
                    throw new InvalidDataException($"Duplicate message key {key} in {path}.");
            var shared = manifest.SharedMessages is { } library
                ? await MessageLibraryFiles.LoadAsync(PackageFiles.Resolve(resourceRoot, library), token) : [];
            return ComposeMessages(shared, messages, manifest.MessageOverrides);
        }, token);
        // Observe every branch before returning, including when one fails or is cancelled.
        await Task.WhenAll(mapTask, scenarioTask, displayTask, messagesTask);
        var display = await displayTask;
        var text = display.Text;
        var (scenario, profiles) = await scenarioTask;
        var presentation = display.Presentation;
        if (presentation.Battle is { } battle)
            presentation = presentation with { Battle = battle.BindCatalog(scenario.Information) };
        return new(ScenarioContent.CurrentSchemaVersion, manifest.Textures, await mapTask, scenario,
            OfficerComposition.ProjectText(new(text.SchemaVersion, text.Entries) { Messages = await messagesTask,
                MessagePeople = text.MessagePeople, MessageTags = text.MessageTags }, profiles), OfficerComposition.ProjectPortraits(display.Illustrations, profiles))
            { Presentation = presentation, ResourceRoot = resourceRoot, Preview = manifest.Preview };
    }

    public static async Task WriteAsync(string root, ScenarioContent content, CancellationToken token = default,
        IReadOnlyDictionary<string, string>? messageGroups = null, string? resourceRoot = null,
        SharedMessageLibrary? sharedMessages = null, IReadOnlyCollection<string>? messageOverrides = null,
        SharedMapDefinition? sharedMap = null, SharedScenarioData? sharedScenario = null, ContentPublication? publication = null)
    {
        using var own = publication is null ? new ContentPublication() : null;
        var batch = publication ?? own!;
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
        var scenarioData = sharedScenario ?? SharedScenarioData.From(content.Scenario, ownPrefix + "definitions.json",
            ownPrefix + "officer-defaults.json", ownPrefix + "geography.json", content.Text);
        var reusablePaths = scenarioData.ResolvePaths(resourceRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (reusablePaths.Contains(PackageFiles.Resolve(resourceRoot, mapDefinitionPath)))
            throw new InvalidDataException("Map display and scenario inputs must use distinct paths.");
        var scenarioDocument = scenarioData.Describe(content.Scenario, content.Text);
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
                SharedMessages = sharedMessages?.Path, MessageOverrides = overrides.Order(StringComparer.Ordinal).ToArray(),
                Definitions = scenarioData.DefinitionsPath, OfficerDefaults = scenarioData.OfficersPath, Geography = scenarioData.GeographyPath };
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
                // Only canonical standalone inputs belong to this writer. Referenced
                // shared inputs can live beneath the same root and belong to their publisher.
                previousFiles = paths.Select(path => PackageFiles.Resolve(oldRoot, path))
                    .Append(Path.Combine(root, "map-definition.json"))
                    .Append(Path.Combine(root, "definitions.json"))
                    .Append(Path.Combine(root, "officer-defaults.json"))
                    .Append(Path.Combine(root, "geography.json"))
                    .Where(path => IsInside(root, path)).ToArray();
            }
        }
        HashSet<string> ownedPaths = new(StringComparer.OrdinalIgnoreCase);
        async Task Stage<T>(string name, T value, JsonSerializerOptions options)
        {
            string target = name == "content.json" ? manifestPath : PackageFiles.Resolve(resourceRoot, name);
            if (ownedPaths.Contains(target) ||
                sharedScenario is not null && reusablePaths.Contains(target))
                throw new InvalidDataException($"Scenario publication collides with another content file: {name}.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ownedPaths.Add(target);
            await batch.StageAsync(target, value, options, token);
        }
        if (sharedMap is null) await Stage(mapDefinitionPath, content.Map.Definition, DataJsonOptions);
        await Stage(manifest.Map, new ScenarioMapManifest(mapDefinitionPath, content.Map.Buildings), DataJsonOptions);
        if (sharedScenario is null)
        {
            await Stage(scenarioData.DefinitionsPath, scenarioData.Definitions, DataJsonOptions);
            await Stage(scenarioData.OfficersPath, scenarioData.Officers, DataJsonOptions);
            await Stage(scenarioData.GeographyPath, scenarioData.Geography, DataJsonOptions);
        }
        await Stage(manifest.Scenario, scenarioDocument, DataJsonOptions);
        var ownedPeople = OfficerComposition.Profiles(scenarioDocument.Officers,
            [.. scenarioData.Officers.Officers, .. scenarioDocument.AdditionalOfficerDefaults]);
        var ownedKeys = ownedPeople.SelectMany(p => new[] { p.NameKey, p.CourtesyNameKey, p.BiographyKey,
            $"officer-surname.{p.Id}", $"officer-given-name.{p.Id}" }.OfType<string>()).ToHashSet(StringComparer.Ordinal);
        await Stage(manifest.Text, new StaticTextContent(content.Text.SchemaVersion,
            content.Text.Entries.Where(p => !ownedKeys.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value))
            { MessagePeople = content.Text.MessagePeople, MessageTags = content.Text.MessageTags }, DataJsonOptions);
        foreach (var (path, messages) in messageFiles) await Stage(path, messages, MessageJsonOptions);
        await Stage(manifest.Illustrations, content.IllustrationRoles, DataJsonOptions);
        await Stage(manifest.Presentation, content.Presentation, DataJsonOptions);
        await Stage("content.json", manifest, PackageFiles.JsonOptions);
        token.ThrowIfCancellationRequested();
        batch.AfterCommit(() =>
        {
            File.Delete(Path.Combine(root, "preview.json"));
            var currentFiles = ownedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            currentFiles.Add(PackageFiles.Resolve(resourceRoot, mapDefinitionPath));
            foreach (string path in new[] { scenarioData.DefinitionsPath, scenarioData.OfficersPath, scenarioData.GeographyPath })
                currentFiles.Add(PackageFiles.Resolve(resourceRoot, path));
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
        });
        own?.Commit(token);
    }

    public static async Task<ScenarioInitialState> LoadScenarioAsync(string root, CancellationToken token = default)
    {
        var manifest = await ReadManifestAsync(root, token);
        return await ScenarioDataFiles.LoadScenarioAsync(ResolveResourceRoot(root, manifest.ResourceRoot), manifest, token);
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

    public static Dictionary<string, MessageDefinition> ComposeMessages(IReadOnlyDictionary<string, MessageDefinition> shared,
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
