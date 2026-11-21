using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Authoring;

/// <summary>An editor visit over shared catalogs and person defaults, published as complete player documents.</summary>
public sealed partial class SharedDataEditorWorkspace
{
    private readonly ScenarioDefinitions _baseDefinitions;
    private readonly Dictionary<int, OfficeDefinition> _officeBaselines;
    private Dictionary<int, int> _officeOrderBaseline;
    private ScenarioDefinitions _saved;
    private byte[]? _expectedPlayer, _expectedOfficers;
    private string? _createdOfficersPath;
    private byte[]? _expectedCreated;
    private HashSet<int> _sharedOfficerIds = [];
    private long _sharedNextOfficerId;
    private readonly HashSet<int> _changedOfficers = [];
    private readonly HashSet<int> _baseOfficerIds;
    private readonly long _baseOfficerIdentityFloor;
    private readonly Dictionary<int, OfficerDefaults> _officerBaselines;
    private OfficerDefaultsCatalog _savedOfficers;
    public OfficerDefaultsCatalog OfficerCatalog { get; private set; }
    public string OfficerPath { get; }
    public string PlayerOfficerPath => PlayerDefinitionFiles.PlayerPath(OfficerPath);
    private readonly Dictionary<string, byte[]> _inputs;
    private sealed record ScenarioReference(string Id, string Root, ScenarioContentManifest Manifest);
    private readonly ScenarioReference[] _scenarios;
    public ScenarioDefinitions Definitions { get; private set; }
    public string DefinitionPath { get; }
    public string PlayerDefinitionPath => PlayerDefinitionFiles.PlayerPath(DefinitionPath);
    public bool HasPlayerCopy => _expectedPlayer is not null || _expectedOfficers is not null || _expectedCreated is not null;
    public TreasureIllustrationWorkspace? Illustrations { get; private set; }
    public IReadOnlyDictionary<int, string> FacilityNames { get; private set; } = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string>? _regionNames;
    public IReadOnlyDictionary<int, string> RegionNames => _regionNames ??= ReadRegionNames();

    private IReadOnlyDictionary<int, string> ReadRegionNames()
    {
        var reference = _scenarios.Single(e => e.Id == ReferenceScenarioId);
        string path = PackageFiles.Resolve(reference.Root, reference.Manifest.Geography);
        using var stream = File.OpenRead(PlayerDefinitionFiles.ActivePath(path));
        var geography = JsonSerializer.Deserialize<ScenarioGeography>(stream, ScenarioContentFiles.DataJsonOptions)
            ?? throw new InvalidDataException("缺少地域资料。");
        using var textStream = File.OpenRead(PackageFiles.Resolve(reference.Root, reference.Manifest.Text));
        var text = JsonSerializer.Deserialize<StaticTextContent>(textStream, ScenarioContentFiles.DataJsonOptions)
            ?? throw new InvalidDataException("缺少地域名称资料。");
        return geography.RegionNameKeys.ToDictionary(p => p.Key, p => text.Entries.GetValueOrDefault(p.Value, p.Value));
    }

    public string ReferenceScenarioId { get; }
    public IReadOnlyDictionary<int, string> FormationNames => Catalog.FormationNames;
    public ImmutableArray<FormationRule> Formations => Catalog.Formations;
    public IReadOnlyList<OfficeDefinition> Offices => Definitions.Information.Offices;
    public IReadOnlyList<RankDefinition> Ranks => Definitions.Information.Ranks;
    public IReadOnlyList<OfficerDefaults> Officers => OfficerCatalog.Officers;
    public ImmutableArray<TacticRule> Tactics => Catalog.Tactics;
    public int ScenarioCount => _scenarios.Length;
    public void SetContentSetId(string value) => OfficerCatalog = OfficerCatalog with { ContentSetId = value.Trim() };
    public bool HasChanges => OfficerCatalog.ContentSetId != _savedOfficers.ContentSetId || !ContentComparison.Equal(Definitions, _saved) || _changedOfficers.Count != 0 || OfficerCatalog.NextOfficerId != _savedOfficers.NextOfficerId || Illustrations?.HasChanges == true;

    private SharedDataEditorWorkspace(string path, string reference, ScenarioDefinitions definitions,
        string officerPath, OfficerDefaultsCatalog officers, Dictionary<string, byte[]> inputs, byte[]? expectedPlayer,
        byte[]? expectedOfficers, ScenarioReference[] scenarios, OfficerDefaultsCatalog baseOfficers, ScenarioDefinitions baseDefinitions)
    {
        DefinitionPath = path; ReferenceScenarioId = reference;
        _saved = Definitions = definitions;
        OfficerPath = officerPath; OfficerCatalog = _savedOfficers = officers;
        _baseOfficerIds = baseOfficers.Officers.Select(p => p.Id).ToHashSet();
        _baseOfficerIdentityFloor = Math.Max(baseOfficers.NextOfficerId, (long)_baseOfficerIds.DefaultIfEmpty(-1).Max() + 1);
        _officerBaselines = officers.Officers.ToDictionary(p => p.Id);
        _baseDefinitions = baseDefinitions;
        _officeBaselines = Offices.ToDictionary(o => o.Id);
        _treasureBaselines = Treasures.ToDictionary(t => t.Id);
        _officeOrderBaseline = Offices.ToDictionary(o => o.Id, o => o.GovernorPriority);
        _rankBaselines = Ranks.ToDictionary(r => r.Id);
        _rankOrderBaseline = Ranks.ToDictionary(r => r.Id, r => r.Precedence);
        _formationBaselines = Formations.ToDictionary(f => f.Id, f => (f, GetFormationCatalog(f.Id)));
        _tacticBaselines = Tactics.ToDictionary(t => t.Id, t => (t, GetTacticCatalog(t.Id)));
        _expectedOfficers = expectedOfficers;
        _inputs = inputs; _expectedPlayer = expectedPlayer; _scenarios = scenarios;
    }

    public static async Task<SharedDataEditorWorkspace> OpenAsync(string contentRoot, string? referenceScenarioId = null,
        CancellationToken token = default, string? treasureIllustrationRoot = null, string? createdOfficersPath = null)
    {
        contentRoot = Path.GetFullPath(contentRoot);
        var files = new ContentReadSession();
        var catalog = await files.ReadAsync<ContentCatalog>(Path.Combine(contentRoot, "catalog.json"), token);
        ContentCatalogFiles.Validate(contentRoot, catalog);
        var reference = referenceScenarioId is null ? catalog.Scenarios.FirstOrDefault()
            : catalog.Scenarios.SingleOrDefault(s => s.Id == referenceScenarioId);
        if (reference is null) throw new InvalidDataException("找不到共享数据编辑的参考剧本。");
        var entries = new List<ScenarioReference>();
        foreach (var entry in catalog.Scenarios)
        {
            string package = PackageFiles.Resolve(contentRoot, entry.Package);
            string manifestPath = Path.Combine(package, "content.json"), active = PlayerDefinitionFiles.ActivePath(manifestPath);
            var manifest = await files.ReadAsync<ScenarioContentManifest>(active, token);
            if (active != manifestPath && manifest.Locations is null)
                manifest = manifest with { Locations = (await files.ReadAsync<ScenarioContentManifest>(manifestPath, token)).Locations };
            if (manifest.SchemaVersion != ScenarioContent.CurrentSchemaVersion)
                throw new InvalidDataException("内容包版本不符，请先重建世界内容。");
            entries.Add(new(entry.Id, ScenarioContentFiles.ResolveResourceRoot(package, manifest.ResourceRoot), manifest));
        }
        var selected = entries.Single(e => e.Id == reference.Id);
        string path = PackageFiles.Resolve(selected.Root, selected.Manifest.Definitions);
        var baseDefinitions = await files.ReadAsync<ScenarioDefinitions>(path, token);
        string activePath = PlayerDefinitionFiles.ActivePath(path);
        var definitions = await files.ReadAsync<ScenarioDefinitions>(activePath, token);
        var inputs = files.Snapshots();
        byte[]? expectedPlayer = activePath == path ? null : inputs[activePath];
        if (definitions.SchemaVersion != ScenarioDefinitions.CurrentSchemaVersion || baseDefinitions.SchemaVersion != ScenarioDefinitions.CurrentSchemaVersion)
            throw new InvalidDataException("共享定义版本不符，请先重建世界内容。");
        if (definitions.NextFormationId < 0 || definitions.NextTacticId < 0 || definitions.NextOfficeId < 0 || definitions.NextRankId < 0 || definitions.NextTreasureId < 0 ||
            baseDefinitions.NextFormationId < 0 || baseDefinitions.NextTacticId < 0 || baseDefinitions.NextOfficeId < 0 || baseDefinitions.NextRankId < 0 || baseDefinitions.NextTreasureId < 0)
            throw new InvalidDataException("共享资料身份分配记录无效。");
        string officerPath = PackageFiles.Resolve(selected.Root, selected.Manifest.OfficerDefaults);
        var baseOfficers = await files.ReadAsync<OfficerDefaultsCatalog>(officerPath, token);
        string activeOfficers = PlayerDefinitionFiles.ActivePath(officerPath);
        var officers = await files.ReadAsync<OfficerDefaultsCatalog>(activeOfficers, token);
        if (officers.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion || baseOfficers.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion)
            throw new InvalidDataException("人物资料版本不符，请重建内容包。");
        if (officers.NextOfficerId < 0 || baseOfficers.NextOfficerId < 0)
            throw new InvalidDataException("武将身份分配记录无效。请检查人物资料。");
        byte[]? expectedOfficers = activeOfficers == officerPath ? null : files.Snapshots()[activeOfficers];
        var personal = createdOfficersPath is null ? null : await PlayerDefinitionFiles.ReadOptionalAsync(createdOfficersPath, token);
        var sharedOfficers = officers;
        officers = PlayerOfficerFiles.Merge(sharedOfficers, personal);
        var scenarios = entries.Where(e => PackageFiles.Resolve(e.Root, e.Manifest.Definitions)
            .Equals(path, StringComparison.OrdinalIgnoreCase) || PackageFiles.Resolve(e.Root, e.Manifest.OfficerDefaults)
            .Equals(officerPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        var workspace = new SharedDataEditorWorkspace(path, reference.Id, definitions, officerPath, officers,
            files.Snapshots(), expectedPlayer, expectedOfficers, scenarios, baseOfficers, baseDefinitions)
        {
            _createdOfficersPath = createdOfficersPath, _expectedCreated = personal,
            _sharedOfficerIds = sharedOfficers.Officers.Select(p => p.Id).ToHashSet(),
            _sharedNextOfficerId = sharedOfficers.NextOfficerId
        };
        if (definitions.FacilityNameSuffixKeys.Count > 0)
        {
            var text = await files.ReadAsync<StaticTextContent>(PackageFiles.Resolve(selected.Root, selected.Manifest.Text), token);
            workspace.FacilityNames = definitions.FacilityNameSuffixKeys.Where(p => text.Entries.ContainsKey(p.Value))
                .ToDictionary(p => p.Key, p => text.Entries[p.Value]);
        }
        if (treasureIllustrationRoot is not null)
            workspace.Illustrations = await TreasureIllustrationWorkspace.OpenAsync(treasureIllustrationRoot, token);
        return workspace;
    }

    private async Task<ContentReadSession> ValidateDraftAsync(ScenarioDefinitions draft, CancellationToken token)
    {
        ContentStructure.Validate(draft);
        if (string.IsNullOrWhiteSpace(OfficerCatalog.ContentSetId)) throw new InvalidDataException("请填写资料集标识。");
        ValidateLocalRules(draft);
        ValidateProfiles(draft, Officers, "共享武将");
        foreach (var treasure in draft.Information.Treasures)
            if (treasure.DefaultPlacement is OfficerTreasurePlacement held && !Officers.Any(o => o.Id == held.OfficerId))
                throw new InvalidDataException($"宝物「{treasure.Name}」的默认持有人 {held.OfficerId} 不在共享武将目录中。");
        foreach (var formation in draft.Military?.Formations ?? [])
            if ((!formation.Positions.IsEmpty || formation.Attack is not null) && formation.Positions.Length != formation.OfficerCapacity)
                throw new InvalidDataException($"{FormationNames.GetValueOrDefault(formation.Id, formation.Id.ToString())}：武将上限 {formation.OfficerCapacity} 与阵位数 {formation.Positions.Length} 不一致，请在阵位页调整。");
        var deletedOfficers = _savedOfficers.Officers.Select(p => p.Id).Except(Officers.Select(p => p.Id)).ToHashSet();
        if (deletedOfficers.Count > 0)
            ValidateDeletedOfficerReferences(PersonReferences(Officers, "共享武将").Concat(RuleReferences(draft, "共享规则")), deletedOfficers);
        // Each validation owns a fresh read session. Shared geography and person defaults are
        // parsed once for this operation, even when many scenarios reference them.
        var files = new ContentReadSession();
        if (!RequiresScenarioValidation(draft)) return files;
        var validation = new ScenarioValidationContext();
        bool validateMessages = CatalogIdentitiesChanged(draft);
        foreach (var entry in _scenarios)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var document = await files.ReadScenarioAsync(entry.Root, entry.Manifest, token);
                string officersPath = PackageFiles.Resolve(entry.Root, entry.Manifest.OfficerDefaults);
                var officers = officersPath.Equals(OfficerPath, StringComparison.OrdinalIgnoreCase) ? OfficerCatalog
                    : await files.ReadAsync<OfficerDefaultsCatalog>(PlayerDefinitionFiles.ActivePath(officersPath), token);
                string definitionsPath = PackageFiles.Resolve(entry.Root, entry.Manifest.Definitions);
                var definitions = definitionsPath.Equals(DefinitionPath, StringComparison.OrdinalIgnoreCase) ? draft
                    : await files.ReadAsync<ScenarioDefinitions>(PlayerDefinitionFiles.ActivePath(definitionsPath), token);
                string geographyPath = PackageFiles.Resolve(entry.Root, entry.Manifest.Geography);
                var geography = await files.ReadAsync<ScenarioGeography>(PlayerDefinitionFiles.ActivePath(geographyPath), token);
                if (geography.Military is not null)
                {
                    var mapManifest = await files.ReadAsync<ScenarioMapManifest>(PackageFiles.Resolve(entry.Root, entry.Manifest.Map), token);
                    var map = await files.ReadAsync<MapDefinition>(PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(entry.Root, mapManifest.Definition)), token);
                    if (entry.Manifest.Locations is { } locationPath)
                    {
                        var sites = await files.ReadAsync<SharedMapSites>(PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(entry.Root, locationPath)), token);
                        mapManifest = mapManifest with { Buildings = sites.Buildings(document.Settlements.Select(s => s.Id)) };
                    }
                    map = mapManifest.ApplyLayout(map, document, definitions);
                    geography = San9.Content.Maps.MapGeographyCompiler.Resolve(mapManifest, map, geography, document, definitions, officers, token);
                }
                var effectiveDefinitions = document.DefinitionOverrides.Apply(definitions);
                var missingActiveTreasures = document.Information.TreasureStates.Where(t => !effectiveDefinitions.Information.Treasures.Any(d => d.Id == t.Id)).Select(t => t.Id).ToArray();
                if (missingActiveTreasures.Length > 0)
                    throw new InvalidDataException($"宝物 {string.Join('、', missingActiveTreasures)} 仍有剧本安置，请先在剧本编辑器中取消投放。");
                if (officers.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion)
                    throw new InvalidDataException("人物默认资料版本不符。");
                if (deletedOfficers.Count > 0 && officersPath.Equals(OfficerPath, StringComparison.OrdinalIgnoreCase))
                    ValidateDeletedOfficerReferences(ScenarioReferences(document, effectiveDefinitions, $"剧本 {entry.Id}"), deletedOfficers);
                var scenario = document.Compile(definitions, officers.Officers, geography);
                foreach (var person in scenario.Officers)
                    if (person.State.OfficeId is int office && !scenario.Information.Offices.Any(o => o.Id == office))
                        throw new InvalidDataException($"武将 {person.State.Id} 的初始官职引用不存在的官职 {office}。");
                scenario.Validate(validation);
                var profiles = OfficerComposition.Profiles(document.Officers, [.. officers.Officers, .. document.AdditionalOfficerDefaults]);
                ValidateProfiles(effectiveDefinitions, profiles, $"剧本 {entry.Id}");
                if (scenario.Forces.Any(f => f.ProvidedTactics.Any(id => scenario.Military?.Tactics.Any(t => t.Id == id) != true)))
                    throw new InvalidDataException("势力授予兵法引用不存在的兵法。");
                if (definitionsPath.Equals(DefinitionPath, StringComparison.OrdinalIgnoreCase) && draft.Military is { } military &&
                    geography.Military is { } militaryGeography)
                    validation.ValidateMilitary(military.Compile(militaryGeography, []));
                if (validateMessages)
                {
                    var messages = await ReadMessagesAsync(entry, files, token);
                    MessageGraph.ValidateCatalogReferences(messages, scenario.Information);
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or OverflowException)
            { throw new InvalidDataException($"剧本 {entry.Id} 的共享数据校验失败：{error.Message}", error); }
        }
        return files;
    }

    private static async Task<Dictionary<string, MessageDefinition>> ReadMessagesAsync(ScenarioReference entry,
        ContentReadSession files, CancellationToken token)
    {
        Dictionary<string, MessageDefinition> shared = new(StringComparer.Ordinal), own = new(StringComparer.Ordinal);
        if (entry.Manifest.SharedMessages is { } path)
        {
            string full = PackageFiles.Resolve(entry.Root, path);
            var library = await files.ReadAsync<MessageLibraryManifest>(full, token);
            if (library.SchemaVersion != MessageLibraryManifest.CurrentSchemaVersion) throw new InvalidDataException("消息库版本不符。");
            foreach (string relative in library.Files)
            foreach (var (key, value) in await files.ReadAsync<Dictionary<string, MessageDefinition>>(PackageFiles.Resolve(Path.GetDirectoryName(full)!, relative), token))
                if (!shared.TryAdd(key, value)) throw new InvalidDataException($"重复消息：{key}");
        }
        foreach (string relative in entry.Manifest.Messages)
        foreach (var (key, value) in await files.ReadAsync<Dictionary<string, MessageDefinition>>(PackageFiles.Resolve(entry.Root, relative), token))
            if (!own.TryAdd(key, value)) throw new InvalidDataException($"重复消息：{key}");
        return ScenarioContentFiles.ComposeMessages(shared, own, entry.Manifest.MessageOverrides);
    }

    public async Task SaveAsync(CancellationToken token = default)
    {
        if (!HasChanges) return;
        await EnsureUnchangedAsync(_inputs, token);
        var validation = await ValidateDraftAsync(Definitions, token);
        // Worlds are read at save time; check that both the editor source and that
        // validation snapshot are still current before replacing the player copy.
        await EnsureUnchangedAsync(_inputs, token);
        await EnsureUnchangedAsync(validation.Snapshots(), token);
        var documents = new List<ScenarioDataFiles.PlayerDocument>();
        if (_createdOfficersPath is null)
        {
            documents.Add(new(PlayerDefinitionPath, Definitions, _expectedPlayer));
            documents.Add(new(PlayerOfficerPath, OfficerCatalog, _expectedOfficers));
        }
        else
        {
            if (!ContentComparison.Equal(Definitions, _saved))
                documents.Add(new(PlayerDefinitionPath, Definitions, _expectedPlayer));
            if (_changedOfficers.Any(_sharedOfficerIds.Contains) || OfficerCatalog.ContentSetId != _savedOfficers.ContentSetId)
            {
                var shared = OfficerCatalog with { Officers = Officers.Where(p => _sharedOfficerIds.Contains(p.Id)).ToArray(), NextOfficerId = _sharedNextOfficerId };
                documents.Add(new(PlayerOfficerPath, shared, _expectedOfficers));
            }
            if (_changedOfficers.Any(id => !_sharedOfficerIds.Contains(id)) || OfficerCatalog.NextOfficerId != _savedOfficers.NextOfficerId)
            {
                var personal = OfficerCatalog with { Officers = Officers.Where(p => !_sharedOfficerIds.Contains(p.Id)).ToArray() };
                documents.Add(new(_createdOfficersPath, personal, _expectedCreated));
            }
        }
        var createdImages = new List<string>();
        IReadOnlyDictionary<string, byte[]> published;
        try
        {
            if (Illustrations is { } illustrations)
            {
                illustrations.PrepareImages(createdImages, token);
                documents.Add(illustrations.Document);
            }
            var dependencies = new Dictionary<string, byte[]?>
            {
                [PlayerDefinitionPath] = _expectedPlayer,
                [PlayerOfficerPath] = _expectedOfficers
            };
            if (_createdOfficersPath is not null) dependencies[_createdOfficersPath] = _expectedCreated;
            published = await ScenarioDataFiles.WritePlayerDocumentsAsync(documents, token, dependencies);
        }
        catch
        {
            foreach (string path in createdImages) File.Delete(path);
            throw;
        }
        if (Illustrations is { } savedIllustrations) savedIllustrations.Saved(published[savedIllustrations.ManifestPath]);
        if (published.TryGetValue(PlayerDefinitionPath, out var definitions)) _inputs[PlayerDefinitionPath] = _expectedPlayer = definitions;
        if (published.TryGetValue(PlayerOfficerPath, out var officers)) _inputs[PlayerOfficerPath] = _expectedOfficers = officers;
        if (_createdOfficersPath is not null && published.TryGetValue(_createdOfficersPath, out var personalFile)) _expectedCreated = personalFile;
        _saved = Definitions; _savedOfficers = OfficerCatalog; _changedOfficers.Clear();
    }

    private static async Task EnsureUnchangedAsync(IReadOnlyDictionary<string, byte[]> inputs, CancellationToken token)
    {
        foreach (var (path, expected) in inputs)
            if (!(await File.ReadAllBytesAsync(path, token)).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("内容文件已被其他操作修改，请重新打开编辑器或重试保存。当前草稿已保留。");
    }

    /// <summary>Operation-local reuse only. Nothing survives into a later save.</summary>
    private sealed class ContentReadSession
    {
        private sealed class Entry(byte[] bytes)
        {
            public byte[] Bytes { get; } = bytes;
            public Dictionary<Type, object> Values { get; } = [];
        }
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public async Task<T> ReadAsync<T>(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (!_entries.TryGetValue(path, out var entry))
            {
                entry = new(await File.ReadAllBytesAsync(path, token));
                _entries.Add(path, entry);
            }
            if (entry.Values.TryGetValue(typeof(T), out var parsed)) return (T)parsed;
            using var stream = new MemoryStream(entry.Bytes, writable: false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, ScenarioContentFiles.DataJsonOptions, token)
                ?? throw new InvalidDataException($"内容文件为空：{path}");
            ContentStructure.Validate(value);
            entry.Values.Add(typeof(T), value);
            return value;
        }

        public async Task<ScenarioDocument> ReadScenarioAsync(string root, ScenarioContentManifest manifest, CancellationToken token)
        {
            var path = PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(root, manifest.Scenario));
            if (manifest.Locations is null) return await ReadAsync<ScenarioDocument>(path, token);
            var sites = await ReadAsync<SharedMapSites>(PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(root, manifest.Locations)), token);
            return sites.Resolve<ScenarioDocument>(await ReadAsync<JsonElement>(path, token));
        }

        public Dictionary<string, byte[]> Snapshots() => _entries.ToDictionary(e => e.Key, e => e.Value.Bytes, StringComparer.OrdinalIgnoreCase);
    }

}
