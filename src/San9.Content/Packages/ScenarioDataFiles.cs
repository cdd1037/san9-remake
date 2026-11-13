using System.Text.Json;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Packages;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficerDefaultsCatalog(int SchemaVersion, OfficerDefaults[] Officers)
{
    public const int CurrentSchemaVersion = 4;
    // Allocation history is authoring state: deleting an item must not recycle its identity.
    [System.Text.Json.Serialization.JsonRequired]
    public long NextOfficerId { get; init; } = (long)Officers.Select(p => p.Id).DefaultIfEmpty(-1).Max() + 1;
}

/// <summary>Explicitly owned reusable inputs; no automatic discovery or content-based relocation.</summary>
public sealed record SharedScenarioData(string DefinitionsPath, string OfficersPath, string GeographyPath,
    ScenarioDefinitions Definitions, OfficerDefaultsCatalog Officers, ScenarioGeography Geography)
{
    public static SharedScenarioData From(ScenarioInitialState scenario, string definitionsPath, string officersPath, string geographyPath, TextCatalog texts) =>
        new(definitionsPath, officersPath, geographyPath, ScenarioDefinitions.From(scenario),
            new(OfficerDefaultsCatalog.CurrentSchemaVersion, scenario.Officers.Select(p => OfficerDefaults.From(p, texts)).ToArray()),
            ScenarioGeography.From(scenario));

    public ScenarioDocument Describe(ScenarioInitialState scenario, TextCatalog texts)
    {
        Validate();
        ContentStructure.Validate(scenario);
        if (scenario.SchemaVersion != ScenarioInitialState.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported compiled scenario state version.");
        if (!ContentComparison.Equal(Geography, ScenarioGeography.From(scenario)))
            throw new InvalidDataException("Scenario geography differs from its explicitly referenced shared geography.");
        var document = ScenarioDocument.From(scenario, Definitions, Officers.Officers, texts);
        if (!ContentComparison.Equal(scenario, document.Compile(Definitions, Officers.Officers, Geography)))
            throw new InvalidDataException("Authored scenario composition changed the compiled initial state.");
        return document;
    }

    public void Validate()
    {
        ContentStructure.Validate(this);
        if (Definitions.SchemaVersion != ScenarioDefinitions.CurrentSchemaVersion ||
            Officers.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion ||
            Geography.SchemaVersion != ScenarioGeography.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported shared scenario data version; reconvert the package.");
        OfficerComposition.Compile([], Officers.Officers);
    }

    internal string[] ResolvePaths(string resourceRoot)
    {
        var paths = new[] { DefinitionsPath, OfficersPath, GeographyPath }.Select(p => PackageFiles.Resolve(resourceRoot, p)).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new InvalidDataException("Shared scenario inputs must use distinct paths.");
        return paths;
    }
}

public static class ScenarioDataFiles
{
    public sealed record PlayerDocument(string Path, object Value, byte[]? Expected);

    /// <summary>Publishes complete player documents as one rollback-protected transaction.</summary>
    public static async Task<IReadOnlyDictionary<string, byte[]>> WritePlayerDocumentsAsync(
        IReadOnlyList<PlayerDocument> documents, CancellationToken token = default)
    {
        if (documents.Select(d => Path.GetFullPath(d.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != documents.Count)
            throw new InvalidDataException("Duplicate player document path.");
        var prepared = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var staged = new List<(string Pending, string Target)>();
        var locks = new List<FileStream>();
        string transaction = Guid.NewGuid().ToString("N");
        try
        {
            // Every writer locks the same target set in a canonical order, across processes.
            foreach (string path in documents.Select(d => Path.GetFullPath(d.Path)).Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try { locks.Add(new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
                catch (IOException error) { throw new IOException("无法取得保存锁，其他进程可能正在保存；请稍后重试。", error); }
            }
            foreach (var document in documents)
            {
                ContentStructure.Validate(document.Value);
                string pending = document.Path + "." + transaction + ".pending";
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document.Value,
                    document.Value.GetType(), ScenarioContentFiles.DataJsonOptions));
                staged.Add((pending, document.Path));
                await File.WriteAllBytesAsync(pending, bytes, token);
                prepared.Add(document.Path, bytes);
            }
            foreach (var document in documents)
            {
                byte[]? current = await PlayerDefinitionFiles.ReadOptionalAsync(document.Path, token);
                if (document.Expected is null ? current is not null : current is null || !current.AsSpan().SequenceEqual(document.Expected))
                    throw new InvalidDataException("Player content changed since editing began.");
            }
            token.ThrowIfCancellationRequested();
            ContentPublication.PublishFiles(staged);
            return prepared;
        }
        finally
        {
            try { foreach (var (pending, _) in staged) File.Delete(pending); }
            finally { foreach (var handle in locks) handle.Dispose(); }
            // Keep lock files: unlinking them would let writers acquire different file objects.
        }
    }

    public static async Task<SharedScenarioData> LoadAsync(string resourceRoot, ScenarioContentManifest manifest, CancellationToken token = default,
        bool usePlayerDefinitions = false)
    {
        string path = PackageFiles.Resolve(resourceRoot, manifest.Definitions);
        var definitions = await PackageFiles.ReadAsync<ScenarioDefinitions>(usePlayerDefinitions ? PlayerDefinitionFiles.ActivePath(path) : path, token);
        var officers = await PackageFiles.ReadAsync<OfficerDefaultsCatalog>(
            usePlayerDefinitions ? PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(resourceRoot, manifest.OfficerDefaults))
                : PackageFiles.Resolve(resourceRoot, manifest.OfficerDefaults), token);
        var geography = await ReadAsync<ScenarioGeography>(resourceRoot, manifest.Geography, token);
        var result = new SharedScenarioData(manifest.Definitions, manifest.OfficerDefaults, manifest.Geography, definitions, officers, geography);
        result.Validate();
        return result;
    }

    public static async Task<ScenarioInitialState> LoadScenarioAsync(string resourceRoot, ScenarioContentManifest manifest, CancellationToken token = default)
        => (await LoadScenarioWithProfilesAsync(resourceRoot, manifest, token)).Scenario;

    internal static async Task<(ScenarioInitialState Scenario, OfficerDefaults[] Profiles)> LoadScenarioWithProfilesAsync(
        string resourceRoot, ScenarioContentManifest manifest, CancellationToken token)
    {
        var document = await ReadAsync<ScenarioDocument>(resourceRoot, manifest.Scenario, token);
        var shared = await LoadAsync(resourceRoot, manifest, token, usePlayerDefinitions: true);
        var scenario = document.Compile(shared.Definitions, shared.Officers.Officers, shared.Geography);
        ContentStructure.Validate(scenario);
        return (scenario, OfficerComposition.Profiles(document.Officers, [.. shared.Officers.Officers, .. document.AdditionalOfficerDefaults]));
    }

    // Selection deliberately skips rule and map payloads, while sharing the exact person composition.
    public static async Task<ScenarioSelectionState> LoadSelectionAsync(string resourceRoot, ScenarioContentManifest manifest, CancellationToken token = default)
        => (await LoadSelectionWithProfilesAsync(resourceRoot, manifest, token)).Scenario;

    internal static async Task<(ScenarioSelectionState Scenario, OfficerDefaults[] Profiles)> LoadSelectionWithProfilesAsync(
        string resourceRoot, ScenarioContentManifest manifest, CancellationToken token)
    {
        var document = await ReadAsync<SelectionDocument>(resourceRoot, manifest.Scenario, token);
        var definitions = await PackageFiles.ReadAsync<SelectionDefinitions>(
            PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(resourceRoot, manifest.Definitions)), token);
        var officers = await PackageFiles.ReadAsync<OfficerDefaultsCatalog>(
            PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(resourceRoot, manifest.OfficerDefaults)), token);
        if (document.SchemaVersion != ScenarioDocument.CurrentSchemaVersion || definitions.SchemaVersion != ScenarioDefinitions.CurrentSchemaVersion ||
            officers.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported authored scenario data version; reconvert the package.");
        var catalog = document.DefinitionOverrides.Information is { } change ? change.Value : definitions.Information;
        ContentStructure.Validate(catalog);
        return (new ScenarioSelectionState(ScenarioInitialState.CurrentSchemaVersion, document.Year, document.Month, document.Day,
            document.Forces, document.Settlements, OfficerComposition.Compile(document.Officers, [.. officers.Officers, .. document.AdditionalOfficerDefaults]),
            document.Units, catalog.Compile(document.Information), document.Alliance)
            { PersonalPeerageRegimes = document.DefinitionOverrides.PersonalPeerageRegimes is { } changeRegimes ? changeRegimes.Value : definitions.PersonalPeerageRegimes },
            OfficerComposition.Profiles(document.Officers, [.. officers.Officers, .. document.AdditionalOfficerDefaults]));
    }

    public static async Task WriteSharedAsync(string resourceRoot, SharedScenarioData shared, CancellationToken token = default,
        ContentPublication? publication = null)
    {
        shared.Validate();
        using var own = publication is null ? new ContentPublication() : null;
        var batch = publication ?? own!;
        await batch.StageAsync(PackageFiles.Resolve(resourceRoot, shared.DefinitionsPath), shared.Definitions, ScenarioContentFiles.DataJsonOptions, token);
        await batch.StageAsync(PackageFiles.Resolve(resourceRoot, shared.OfficersPath), shared.Officers, ScenarioContentFiles.DataJsonOptions, token);
        await batch.StageAsync(PackageFiles.Resolve(resourceRoot, shared.GeographyPath), shared.Geography, ScenarioContentFiles.DataJsonOptions, token);
        own?.Commit(token);
    }

    private static Task<T> ReadAsync<T>(string root, string path, CancellationToken token) =>
        PackageFiles.ReadAsync<T>(PackageFiles.Resolve(root, path), token);

    private sealed record SelectionDefinitions(int SchemaVersion, InformationCatalog Information)
    {
        public San9.Core.Domain.PersonalPeerageRegimeDefinition[]? PersonalPeerageRegimes { get; init; }
    }
    private sealed record SelectionOverrides(ContentOverride<InformationCatalog>? Information = null,
        ContentOverride<San9.Core.Domain.PersonalPeerageRegimeDefinition[]?>? PersonalPeerageRegimes = null);
    private sealed record SelectionDocument(int SchemaVersion, int Year, int Month, int Day,
        Scenarios.ForceInitialState[] Forces, Scenarios.SettlementInitialState[] Settlements, ScenarioOfficerState[] Officers,
        OfficerDefaults[] AdditionalOfficerDefaults, San9.Core.Domain.UnitState[] Units, ScenarioInformationState Information,
        San9.Core.Domain.AllianceState? Alliance, SelectionOverrides DefinitionOverrides);
}
