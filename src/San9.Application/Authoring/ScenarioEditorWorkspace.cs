using System.Text.Json;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Authoring;

/// <summary>One scenario draft, with shared defaults retained as references until composition.</summary>
public sealed class ScenarioEditorWorkspace
{
    private readonly ScenarioContentManifest _manifest;
    private readonly ScenarioDocument _opened;
    private readonly ScenarioPreviewEntry _openedPreview;
    private readonly SharedScenarioData _shared;
    private readonly Dictionary<string, byte[]?> _inputs;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private (ScenarioMapManifest Manifest, MapDefinition Definition)? _map;
    private ScenarioDocument _saved;
    private ScenarioPreviewEntry _savedPreview;
    public string ScenarioPath { get; }
    public string ManifestPath { get; }
    public string PlayerScenarioPath => PlayerDefinitionFiles.PlayerPath(ScenarioPath);
    public string PlayerManifestPath => PlayerDefinitionFiles.PlayerPath(ManifestPath);
    public ScenarioDocument Document { get; private set; }
    public ScenarioPreviewEntry Preview { get; private set; }
    public TextCatalog Text { get; }
    public ScenarioDefinitions Definitions => Document.DefinitionOverrides.Apply(_shared.Definitions);
    public IEnumerable<int> RegionIds => _shared.Geography.RegionNameKeys.Keys;
    public bool HasChanges => !ContentComparison.Equal(Document, _saved) || !ContentComparison.Equal(Preview, _savedPreview);

    private ScenarioEditorWorkspace(string scenarioPath, string manifestPath, ScenarioContentManifest manifest,
        ScenarioDocument document, SharedScenarioData shared, TextCatalog text, Dictionary<string, byte[]?> inputs)
    {
        ScenarioPath = scenarioPath; ManifestPath = manifestPath; _manifest = manifest; _shared = shared;
        Document = _saved = _opened = document;
        Preview = _savedPreview = _openedPreview = manifest.Preview!;
        Text = text; _inputs = inputs;
    }

    public static async Task<ScenarioEditorWorkspace> OpenAsync(string contentRoot, string scenarioId, CancellationToken token = default)
    {
        var inputs = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        async Task<T> Read<T>(string path)
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, token);
            inputs[path] = bytes;
            var value = JsonSerializer.Deserialize<T>(bytes, ScenarioContentFiles.DataJsonOptions)
                ?? throw new InvalidDataException($"内容文件为空：{path}");
            ContentStructure.Validate(value);
            return value;
        }
        async Task<T> ReadActive<T>(string path)
        {
            string player = PlayerDefinitionFiles.PlayerPath(path);
            byte[]? bytes = await PlayerDefinitionFiles.ReadOptionalAsync(player, token);
            inputs[player] = bytes;
            if (bytes is null) return await Read<T>(path);
            var value = JsonSerializer.Deserialize<T>(bytes, ScenarioContentFiles.DataJsonOptions)
                ?? throw new InvalidDataException($"玩家内容为空：{player}");
            ContentStructure.Validate(value);
            return value;
        }
        contentRoot = Path.GetFullPath(contentRoot);
        var catalog = await Read<ContentCatalog>(Path.Combine(contentRoot, "catalog.json"));
        ContentCatalogFiles.Validate(contentRoot, catalog);
        string package = PackageFiles.Resolve(contentRoot, catalog.RequireScenario(scenarioId).Package);
        string manifestPath = Path.Combine(package, "content.json");
        var manifest = await ReadActive<ScenarioContentManifest>(manifestPath);
        if (manifest.SchemaVersion != ScenarioContent.CurrentSchemaVersion || manifest.Preview?.Id != scenarioId)
            throw new InvalidDataException("剧本入口版本或身份无效，请重建内容包。");
        string root = ScenarioContentFiles.ResolveResourceRoot(package, manifest.ResourceRoot);
        string scenarioPath = PackageFiles.Resolve(root, manifest.Scenario);
        var document = await ReadActive<ScenarioDocument>(scenarioPath);
        if (document.ScenarioId != scenarioId) throw new InvalidDataException("剧本身份与目录不一致。");
        var definitions = await ReadActive<ScenarioDefinitions>(PackageFiles.Resolve(root, manifest.Definitions));
        var officers = await ReadActive<OfficerDefaultsCatalog>(PackageFiles.Resolve(root, manifest.OfficerDefaults));
        var geography = await Read<ScenarioGeography>(PackageFiles.Resolve(root, manifest.Geography));
        (ScenarioMapManifest Manifest, MapDefinition Definition)? authoredMap = null;
        if (geography.Military is not null)
        {
            var mapManifest = await Read<ScenarioMapManifest>(PackageFiles.Resolve(root, manifest.Map));
            var map = await Read<MapDefinition>(PackageFiles.Resolve(root, mapManifest.Definition));
            geography = San9.Content.Maps.MapGeographyCompiler.Resolve(mapManifest, map, geography, document, definitions, officers, token);
            authoredMap = (mapManifest with
            {
                GeographyInputHash = San9.Content.Maps.MapGeographyCompiler.InputHash(map, geography, document, definitions)
            }, map);
        }
        var shared = new SharedScenarioData(manifest.Definitions, manifest.OfficerDefaults, manifest.Geography, definitions, officers, geography);
        shared.Validate();
        document.Compile(definitions, officers.Officers, geography);
        var text = await Read<StaticTextContent>(PackageFiles.Resolve(root, manifest.Text));
        var catalogText = new TextCatalog(text.SchemaVersion, text.Entries) { MessagePeople = text.MessagePeople, MessageTags = text.MessageTags };
        return new(scenarioPath, manifestPath, manifest, document, shared, catalogText, inputs) { _map = authoredMap };
    }

    public string OfficerName(int id)
    {
        var defaults = _shared.Officers.Officers.Concat(Document.AdditionalOfficerDefaults).SingleOrDefault(o => o.Id == id);
        if (defaults is null) return $"未知武将 #{id}";
        var placement = Document.Officers.SingleOrDefault(o => o.Id == id);
        return placement?.Overrides.Apply(defaults).Name ?? defaults.Name;
    }

    public string Name(string key) => Text.Entries.GetValueOrDefault(key, key);
    public string RegionName(int id) => _shared.Geography.RegionNameKeys.TryGetValue(id, out string? key) ? Name(key) : $"地域 {id}";

    public TreasurePlacement? GetTreasurePlacement(int id)
    {
        if (!Definitions.Information.Treasures.Any(t => t.Id == id)) throw new InvalidDataException("找不到宝物定义。");
        return Document.Information.TreasureStates.SingleOrDefault(t => t.Id == id)?.Placement;
    }

    public void SetTreasurePlacement(int id, TreasurePlacement? placement)
    {
        if (!Definitions.Information.Treasures.Any(t => t.Id == id)) throw new InvalidDataException("找不到宝物定义。");
        var treasure = placement is null ? null : new TreasureState(id, placement);
        if (treasure is not null && !treasure.IsValid) throw new InvalidDataException("宝物安置无效。");
        if (treasure?.HolderId is int holder && !Document.Officers.Any(o => o.Id == holder && o.Status != OfficerStatus.Dead))
            throw new InvalidDataException("宝物持有者必须是当前剧本中存活的武将。");
        if (treasure?.RegionId is int region && !_shared.Geography.RegionNameKeys.ContainsKey(region))
            throw new InvalidDataException("宝物埋藏地点必须引用当前地图中的地域。");
        var states = Document.Information.TreasureStates.Where(t => t.Id != id);
        if (treasure is not null) states = states.Append(treasure);
        Update(Document with { Information = Document.Information with { TreasureStates = states.OrderBy(t => t.Id).ToArray() } });
    }

    public void Update(ScenarioDocument document)
    {
        ContentStructure.Validate(document);
        if (document.SchemaVersion != ScenarioDocument.CurrentSchemaVersion || document.ScenarioId != _opened.ScenarioId)
            throw new InvalidDataException("剧本身份与格式版本不能修改。");
        // Display placements and geography are authored by the map tools.
        if (!document.Settlements.Select(s => (s.Id, s.X, s.Y, s.Kind, s.Appearance)).OrderBy(s => s.Id)
                .SequenceEqual(_opened.Settlements.Select(s => (s.Id, s.X, s.Y, s.Kind, s.Appearance)).OrderBy(s => s.Id)) ||
            !ContentComparison.Equal(document.MilitaryPlaces, _opened.MilitaryPlaces))
            throw new InvalidDataException("首版剧本编辑器保留地图布局、据点类型与外观，请在原有据点上配置初始状态。");
        Document = document;
    }

    public void SetDescription(string title, string description) => Preview = Preview with { Title = title, Description = description };
    public void RestoreOpened() { Document = _opened; Preview = _openedPreview; }
    public string ExportJson() => JsonSerializer.Serialize(Document, new JsonSerializerOptions(ScenarioContentFiles.DataJsonOptions) { WriteIndented = true });
    public void ApplyJson(string json)
    {
        var document = JsonSerializer.Deserialize<ScenarioDocument>(json,
            new JsonSerializerOptions(ScenarioContentFiles.DataJsonOptions) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("剧本数据不能为空。");
        Validate(document);
        Update(document);
    }

    private void Validate(ScenarioDocument document)
    {
        var geography = _map is { } map ? San9.Content.Maps.MapGeographyCompiler.Resolve(map.Manifest, map.Definition,
            _shared.Geography, document, _shared.Definitions, _shared.Officers) : _shared.Geography;
        var scenario = document.Compile(_shared.Definitions, _shared.Officers.Officers, geography);
        var profiles = OfficerComposition.Profiles(document.Officers, [.. _shared.Officers.Officers, .. document.AdditionalOfficerDefaults]);
        var text = OfficerComposition.ProjectText(Text, profiles);
        text.Validate();
        foreach (string key in scenario.Forces.Select(f => f.NameKey).Concat(scenario.Settlements.Select(s => s.NameKey))) _ = text.Resolve(key);
        if (scenario.Day is not (1 or 11 or 21)) throw new InvalidDataException("开局日期必须位于旬首（1、11或21日）。");
        // Exercise the same domain construction used by new-game startup.
        _ = new ScenarioSessionDefinition(scenario).CreateWorld(0);
    }

    public async Task SaveAsync(CancellationToken token = default)
    {
        await _saveGate.WaitAsync(token);
        try
        {
            if (!HasChanges) return;
            var document = Document;
            var previewAtStart = Preview;
            var preview = previewAtStart;
            if (string.IsNullOrWhiteSpace(preview.Title) || string.IsNullOrWhiteSpace(preview.Description))
                throw new InvalidDataException("剧本标题和简介不能为空。");
            MessageStyles.ValidateMarkup(preview.Description);
            Validate(document);
            preview = preview with
            {
                Year = document.Year, Month = document.Month,
                CityColors = document.Settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant && s.OwnerId is not null)
                    .ToDictionary(s => s.Id, s => $"color-{document.Forces.Single(f => f.Id == s.OwnerId).ColorCode}")
            };
            foreach (var (path, expected) in _inputs)
            {
                byte[]? current = await PlayerDefinitionFiles.ReadOptionalAsync(path, token);
                if (expected is null ? current is not null : current is null || !current.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("内容已被其他操作修改，请重新打开编辑器。当前草稿已保留。");
            }
            var published = await ScenarioDataFiles.WritePlayerDocumentsAsync([
                new(PlayerScenarioPath, document, _inputs[PlayerScenarioPath]),
                new(PlayerManifestPath, _manifest with { Preview = preview }, _inputs[PlayerManifestPath])], token);
            _inputs[PlayerScenarioPath] = published[PlayerScenarioPath];
            _inputs[PlayerManifestPath] = published[PlayerManifestPath];
            _saved = document; _savedPreview = preview;
            // Callers may continue editing while the asynchronous publication is in flight.
            if (ReferenceEquals(Preview, previewAtStart)) Preview = preview;
        }
        finally { _saveGate.Release(); }
    }
}
