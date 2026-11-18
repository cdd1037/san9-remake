using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Authoring;

/// <summary>Shared map and geography inputs and the selected scenario layout are edited in one transaction.</summary>
public sealed class MapEditorWorkspace
{
    private sealed record Draft(MapDefinition Map, ScenarioDocument Scenario, ScenarioGeography Geography);
    private readonly List<Draft> _undo = [], _redo = [];
    private readonly Dictionary<string, byte[]?> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string _catalogRoot;
    private readonly ScenarioContentManifest _manifest;
    private readonly ScenarioMapManifest _layout;
    private readonly ScenarioGeography _geographyInput;
    private MapDefinition? _effectiveMap;
    private readonly ScenarioDefinitions _definitions;
    private readonly OfficerDefaultsCatalog _officers;
    private readonly string _manifestPath, _scenarioPath, _mapPath, _mapManifestPath, _geographyPath;
    private readonly string _relativeMap;
    private Draft _draft, _saved;
    private Draft? _stroke;
    public MapDefinition Map => _effectiveMap ??= ActiveLayout.ApplyLayout(_draft.Map, Scenario, _definitions);
    private ScenarioMapManifest ActiveLayout => _layout.WithPositions(Scenario);
    public ScenarioDocument Scenario => _draft.Scenario;
    public ScenarioGeography Geography => _draft.Geography;
    public TextCatalog Text { get; }
    public string TextureRoot { get; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public long Revision { get; private set; }
    public bool HasMapChanges => !_draft.Map.Cells.AsSpan().SequenceEqual(_saved.Map.Cells) ||
        !ReferenceEquals(_draft.Geography, _saved.Geography) && !ContentComparison.Equal(_draft.Geography.RegionCityIds, _saved.Geography.RegionCityIds);
    public bool HasLayoutChanges => !ReferenceEquals(Scenario, _saved.Scenario) && !ContentComparison.Equal(Scenario, _saved.Scenario);
    public bool HasChanges => HasMapChanges || HasLayoutChanges;
    public MapBuildingPlacement[] Buildings => ActiveLayout.Buildings;

    private MapEditorWorkspace(string catalogRoot, string package, string root, ScenarioContentManifest manifest,
        ScenarioMapManifest layout, MapDefinition map, ScenarioDocument scenario, SharedScenarioData shared,
        ScenarioGeography geographyInput, TextCatalog text, Dictionary<string, byte[]?> inputs)
    {
        _catalogRoot = catalogRoot;
        _manifest = manifest; _layout = layout; _definitions = shared.Definitions; _officers = shared.Officers;
        _geographyInput = geographyInput;
        _draft = _saved = new(map, scenario, shared.Geography);
        Text = text; _inputs = inputs;
        TextureRoot = Path.GetFullPath(Path.Combine(package, manifest.Textures.Path));
        string relative = Path.GetRelativePath(root, package).Replace('\\', '/');
        string prefix = relative == "." ? "" : relative + "/";
        _relativeMap = prefix + "map.player.json";
        _manifestPath = PlayerDefinitionFiles.PlayerPath(Path.Combine(package, "content.json"));
        _scenarioPath = PlayerDefinitionFiles.PlayerPath(PackageFiles.Resolve(root, manifest.Scenario));
        _mapPath = PlayerDefinitionFiles.PlayerPath(PackageFiles.Resolve(root, layout.Definition));
        _mapManifestPath = PackageFiles.Resolve(root, _relativeMap);
        _geographyPath = PlayerDefinitionFiles.PlayerPath(PackageFiles.Resolve(root, manifest.Geography));
    }

    public static async Task<MapEditorWorkspace> OpenAsync(string contentRoot, string scenarioId, CancellationToken token = default)
    {
        var inputs = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        async Task<T> Read<T>(string path, bool player = false)
        {
            if (player)
            {
                string candidate = PlayerDefinitionFiles.PlayerPath(path);
                byte[]? active = await PlayerDefinitionFiles.ReadOptionalAsync(candidate, token);
                inputs[candidate] = active;
                if (active is not null) return Parse<T>(active);
            }
            byte[] bytes = await File.ReadAllBytesAsync(path, token); inputs[path] = bytes;
            return Parse<T>(bytes);
        }
        contentRoot = Path.GetFullPath(contentRoot);
        var catalog = await Read<ContentCatalog>(Path.Combine(contentRoot, "catalog.json"));
        ContentCatalogFiles.Validate(contentRoot, catalog);
        string package = PackageFiles.Resolve(contentRoot, catalog.RequireScenario(scenarioId).Package);
        var manifest = await Read<ScenarioContentManifest>(Path.Combine(package, "content.json"), true);
        if (manifest.SchemaVersion != ScenarioContent.CurrentSchemaVersion || manifest.Preview?.Id != scenarioId)
            throw new InvalidDataException("剧本身份或内容版本无效。");
        string root = ScenarioContentFiles.ResolveResourceRoot(package, manifest.ResourceRoot);
        var mapManifest = await Read<ScenarioMapManifest>(PackageFiles.Resolve(root, manifest.Map));
        var map = await Read<MapDefinition>(PackageFiles.Resolve(root, mapManifest.Definition), true);
        map.Validate();
        var scenario = await Read<ScenarioDocument>(PackageFiles.Resolve(root, manifest.Scenario), true);
        if (scenario.ScenarioId != scenarioId) throw new InvalidDataException("剧本身份与目录不一致。");
        var definitions = await Read<ScenarioDefinitions>(PackageFiles.Resolve(root, manifest.Definitions), true);
        var officers = await Read<OfficerDefaultsCatalog>(PackageFiles.Resolve(root, manifest.OfficerDefaults), true);
        var geographyInput = await Read<ScenarioGeography>(PackageFiles.Resolve(root, manifest.Geography), true);
        var geography = MapGeographyCompiler.Resolve(mapManifest, mapManifest.ApplyLayout(map, scenario, definitions), geographyInput, scenario, definitions, officers, token);
        var text = await Read<StaticTextContent>(PackageFiles.Resolve(root, manifest.Text));
        var shared = new SharedScenarioData(manifest.Definitions, manifest.OfficerDefaults, manifest.Geography, definitions, officers, geography);
        shared.Validate();
        var workspace = new MapEditorWorkspace(contentRoot, package, root, manifest, mapManifest, map, scenario,
            shared, geographyInput, new(text.SchemaVersion, text.Entries) { MessagePeople = text.MessagePeople, MessageTags = text.MessageTags }, inputs);
        foreach (string target in new[] { workspace._manifestPath, workspace._scenarioPath, workspace._mapPath,
                     workspace._mapManifestPath, workspace._geographyPath })
            if (!inputs.ContainsKey(target)) inputs[target] = await PlayerDefinitionFiles.ReadOptionalAsync(target, token);
        return workspace;
    }

    private static T Parse<T>(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var value = JsonSerializer.Deserialize<T>(stream, ScenarioContentFiles.DataJsonOptions)
            ?? throw new InvalidDataException("内容文件为空。");
        ContentStructure.Validate(value); return value;
    }

    public string RegionName(int id) => Geography.RegionNameKeys.TryGetValue(id, out string? key) ? Name(key) : $"地域 {id}";
    public string Name(string key) => Text.Entries.GetValueOrDefault(key, key);
    public MapDefinition Snapshot() => Map with { Cells = (MapCellDefinition[])Map.Cells.Clone() };
    private static bool Equal(Draft a, Draft b) => a.Map.Cells.AsSpan().SequenceEqual(b.Map.Cells) &&
        (ReferenceEquals(a.Scenario, b.Scenario) || ContentComparison.Equal(a.Scenario, b.Scenario)) &&
        (ReferenceEquals(a.Geography, b.Geography) || ContentComparison.Equal(a.Geography, b.Geography));

    public void BeginStroke()
    {
        if (_stroke is not null) throw new InvalidOperationException("Finish the active map stroke first.");
        _stroke = _draft;
        // Only an active stroke mutates its owned cell array. Completed drafts are shared by history and saves.
        _draft = _draft with { Map = _draft.Map with { Cells = (MapCellDefinition[])_draft.Map.Cells.Clone() } };
        _effectiveMap = null;
    }

    public int Paint(MapCell from, MapCell to, int radius, Func<MapCellDefinition, MapCellDefinition> change,
        IReadOnlyDictionary<int, TerrainStyle> styles, bool paintBlocking = false)
    {
        if (_stroke is null) throw new InvalidOperationException("Begin a map stroke first.");
        Dictionary<int, MapCellDefinition> changes = [];
        var effective = Map;
        foreach (int index in BrushIndices(from, to, radius))
        {
            var next = change(effective.Cells[index]);
            if (!styles.TryGetValue(next.Terrain, out var style) || !style.Supports(next))
                throw new InvalidDataException($"格 ({index % Map.Width}, {index / Map.Width}) 的地形、高度和变体组合不受素材支持。");
            if (next.RegionId is int region && !Geography.RegionNameKeys.ContainsKey(region))
                throw new InvalidDataException("未知地域引用。");
            var visible = effective.Cells[index];
            if (next == visible && !paintBlocking) continue;
            var basis = _draft.Map.Cells[index];
            bool movement = paintBlocking || next.MovementBlocked != visible.MovementBlocked;
            bool influence = paintBlocking || next.InfluenceBlocked != visible.InfluenceBlocked;
            var authored = next with
            {
                RegionId = next.RegionId == visible.RegionId ? basis.RegionId : next.RegionId,
                MovementBlocked = movement ? next.MovementBlocked : basis.MovementBlocked,
                InfluenceBlocked = influence ? next.InfluenceBlocked : basis.InfluenceBlocked,
                MovementSource = movement ? BlockingSource.Manual : basis.MovementSource,
                InfluenceSource = influence ? BlockingSource.Manual : basis.InfluenceSource
            };
            if (authored != basis) changes[index] = authored;
        }
        int[] terrainChanges = changes.Where(p => p.Value.Terrain != _draft.Map.Cells[p.Key].Terrain).Select(p => p.Key).ToArray();
        foreach (var (index, cell) in changes) _draft.Map.Cells[index] = cell;
        if (changes.Count > 0) _effectiveMap = null;
        if (terrainChanges.Length > 0) FillBlocking(terrainChanges);
        if (changes.Count > 0) Revision++;
        return changes.Count;
    }

    private HashSet<int> BrushIndices(MapCell from, MapCell to, int radius)
    {
        if (radius < 0 || radius > Math.Max(Map.Width, Map.Height)) throw new ArgumentOutOfRangeException(nameof(radius));
        HashSet<int> indices = [];
        int steps = Math.Max(1, Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)));
        for (int step = 0; step <= steps; step++)
        {
            int cx = (int)Math.Round(from.X + (to.X - from.X) * (double)step / steps);
            int cy = (int)Math.Round(from.Y + (to.Y - from.Y) * (double)step / steps);
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(Map.Height - 1, cy + radius); y++)
            for (int x = Math.Max(0, cx - radius); x <= Math.Min(Map.Width - 1, cx + radius); x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
                indices.Add(y * Map.Width + x);
            }
        }
        return indices;
    }

    public int RegenerateBlocking()
    {
        EndStroke();
        var before = _draft;
        int changed = FillBlocking(null);
        if (changed > 0) Record(before);
        return changed;
    }

    private int FillBlocking(IEnumerable<int>? indices)
    {
        var basis = _draft.Map;
        var cells = MapBlockingGenerator.Generate(basis, [], indices);
        int changed = cells.Where((cell, index) => cell != basis.Cells[index]).Count();
        if (changed > 0)
        {
            _draft = _draft with { Map = basis with { Cells = cells } };
            _effectiveMap = null; Revision++;
        }
        return changed;
    }

    public void EndStroke()
    {
        if (_stroke is null) return;
        if (!Equal(_stroke, _draft)) Record(_stroke);
        _stroke = null;
    }

    public void CancelStroke()
    {
        if (_stroke is null) return;
        _draft = _stroke; _stroke = null; _effectiveMap = null; Revision++;
    }

    private void Record(Draft before)
    {
        _undo.Add(before); if (_undo.Count > 50) _undo.RemoveAt(0); _redo.Clear();
    }

    public void Undo()
    {
        EndStroke(); if (_undo.Count == 0) return;
        _redo.Add(_draft); _draft = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _effectiveMap = null; Revision++;
    }

    public void Redo()
    {
        EndStroke(); if (_redo.Count == 0) return;
        _undo.Add(_draft); _draft = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _effectiveMap = null; Revision++;
    }

    public void SetRegionCity(int region, int city)
    {
        EndStroke();
        if (!Geography.RegionNameKeys.ContainsKey(region) || !Scenario.Settlements.Any(s => s.Id == city && s.Kind == SettlementKind.City))
            throw new InvalidDataException("地域或关联都市引用无效。");
        if (Geography.RegionCityIds.GetValueOrDefault(region, -1) == city) return;
        Record(_draft);
        _draft = _draft with { Geography = Geography with { RegionCityIds = new(Geography.RegionCityIds) { [region] = city } } };
        Revision++;
    }

    public void MoveBuilding(int id, MapCell target)
    {
        EndStroke();
        if (target.X < 0 || target.Y < 0 || target.X >= Map.Width || target.Y >= Map.Height)
            throw new InvalidDataException("据点坐标超出地图。");
        var settlement = Scenario.Settlements.Single(s => s.Id == id);
        if (settlement.Cell == target) return;
        var place = Scenario.MilitaryPlaces.Single(p => p.Id == id);
        var position = MapCoordinates.Center(target);
        var area = new MapOccupancy(position, place.Footprint);
        int scale = MapCoordinates.UnitsPerCell;
        if (area.Left < 0 || area.Top < 0 || area.Right > (long)Map.Width * scale || area.Bottom > (long)Map.Height * scale ||
            Scenario.MilitaryPlaces.Where(p => p.Id != id).Any(p => area.Overlaps(new(p.Position, p.Footprint))) ||
            Scenario.UnbuiltPlaces.Any(p => area.Contains(p.Position)))
            throw new InvalidDataException("据点占地超出地图或与其他地点重叠。");
        Record(_draft);
        _draft = _draft with
        {
            Scenario = Scenario with
            {
                Settlements = Scenario.Settlements.Select(s => s.Id == id ? s with { X = target.X, Y = target.Y } : s).ToArray(),
                MilitaryPlaces = Scenario.MilitaryPlaces.Select(p => p.Id == id ? p with { Position = position } : p).ToImmutableArray()
            }
        };
        _effectiveMap = null;
        Revision++;
    }

    public ScenarioInitialState PreviewScenario() => Scenario.Compile(_definitions, _officers.Officers, Geography);

    private void ValidateAnchors(MapDefinition map, ScenarioDocument scenario, MapDefinition? editedBase = null)
    {
        var anchors = scenario.Settlements.Select(s => (s.Id, s.Cell)).Concat(
            scenario.UnbuiltPlaces.Select(p => (p.Id, MapCoordinates.Cell(p.Position))));
        foreach (var (id, cell) in anchors)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= map.Width || cell.Y >= map.Height)
                throw new InvalidDataException($"地点 {id} 的锚点超出地图。");
            int i = cell.Y * map.Width + cell.X;
            if (map.Cells[i].RegionId != id || editedBase is not null && editedBase.Cells[i].RegionId != id &&
                editedBase.Cells[i].RegionId != _saved.Map.Cells[i].RegionId)
                throw new InvalidDataException($"地点 {id} 的锚点须属于同名地域。");
        }
    }

    private async Task ValidateOtherScenarios(Draft draft, bool mapChanged, bool geographyChanged, MapResources resources,
        Dictionary<string, byte[]?> dependencies, CancellationToken token)
    {
        async Task<T> Read<T>(string path, bool active = false)
        {
            if (active || path.EndsWith(".player.json", StringComparison.OrdinalIgnoreCase))
            {
                string player = PlayerDefinitionFiles.PlayerPath(path);
                if (!dependencies.TryGetValue(player, out var bytes))
                    dependencies[player] = bytes = await PlayerDefinitionFiles.ReadOptionalAsync(player, token);
                if (bytes is not null) return Parse<T>(bytes);
            }
            return await PackageFiles.ReadAsync<T>(path, token);
        }
        var catalog = await Read<ContentCatalog>(Path.Combine(_catalogRoot, "catalog.json"));
        foreach (var entry in catalog.Scenarios.Where(e => e.Id != draft.Scenario.ScenarioId))
        {
            try
            {
                string package = PackageFiles.Resolve(_catalogRoot, entry.Package);
                var manifest = await Read<ScenarioContentManifest>(Path.Combine(package, "content.json"), true);
                string root = ScenarioContentFiles.ResolveResourceRoot(package, manifest.ResourceRoot);
                string Resolve(string path) => PackageFiles.Resolve(root, path);
                var layout = await Read<ScenarioMapManifest>(Resolve(manifest.Map));
                bool sameMap = StringComparer.OrdinalIgnoreCase.Equals(_mapPath, PlayerDefinitionFiles.PlayerPath(Resolve(layout.Definition)));
                bool sameGeography = StringComparer.OrdinalIgnoreCase.Equals(_geographyPath, PlayerDefinitionFiles.PlayerPath(Resolve(manifest.Geography)));
                if (!(mapChanged && sameMap || geographyChanged && sameGeography)) continue;
                var scenario = await Read<ScenarioDocument>(Resolve(manifest.Scenario), true);
                var definitions = await Read<ScenarioDefinitions>(Resolve(manifest.Definitions), true);
                var officers = await Read<OfficerDefaultsCatalog>(Resolve(manifest.OfficerDefaults), true);
                var map = sameMap ? draft.Map : await Read<MapDefinition>(Resolve(layout.Definition), true);
                map = layout.ApplyLayout(map, scenario, definitions);
                var geo = sameGeography ? _geographyInput with { RegionCityIds = draft.Geography.RegionCityIds }
                    : await Read<ScenarioGeography>(Resolve(manifest.Geography), true);
                ValidateAnchors(map, scenario, sameMap ? draft.Map : null);
                geo = MapGeographyCompiler.Resolve(layout, map, geo, scenario, definitions, officers, token);
                var state = scenario.Compile(definitions, officers.Officers, geo);
                _ = new ScenarioSessionDefinition(state).CreateWorld(0);
                string textures = Path.GetFullPath(Path.Combine(package, manifest.Textures.Path));
                var theme = StringComparer.OrdinalIgnoreCase.Equals(textures, TextureRoot) ? resources :
                    (await TexturePackageFiles.LoadAsync(textures, token)).Map;
                MapPackageFiles.Validate(MapLayoutCompiler.Compile(map, layout.Buildings, theme, state));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidDataException($"共享修改无法用于剧本 {entry.Id}：{error.Message}", error);
            }
        }
    }

    public async Task<ScenarioPreviewEntry> SaveAsync(MapResources resources, CancellationToken token = default)
    {
        EndStroke(); await _saveGate.WaitAsync(token);
        try
        {
            var draft = _draft;
            var layout = _layout.WithPositions(draft.Scenario);
            var effective = layout.ApplyLayout(draft.Map, draft.Scenario, _definitions);
            var geography = await Task.Run(() => MapGeographyCompiler.Compile(effective, draft.Geography,
                draft.Scenario, _definitions, _officers, token), token);
            var scenario = draft.Scenario.Compile(_definitions, _officers.Officers, geography);
            ValidateAnchors(effective, draft.Scenario, draft.Map);
            var display = MapLayoutCompiler.Compile(effective, layout.Buildings, resources, scenario);
            MapPackageFiles.Validate(display);
            scenario.Validate();
            _ = new ScenarioSessionDefinition(scenario).CreateWorld(0);
            var preview = _manifest.Preview!;
            foreach (var (path, expected) in _inputs)
            {
                byte[]? current = await PlayerDefinitionFiles.ReadOptionalAsync(path, token);
                if (expected is null ? current is not null : current is null || !current.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("内容已被其他操作修改，请重新打开地图编辑器。当前草稿已保留。");
            }
            List<ScenarioDataFiles.PlayerDocument> documents = [];
            bool mapChanged = !draft.Map.Cells.AsSpan().SequenceEqual(_saved.Map.Cells);
            bool geographyChanged = !ContentComparison.Equal(draft.Geography.RegionCityIds, _saved.Geography.RegionCityIds);
            if (mapChanged)
                documents.Add(new(_mapPath, draft.Map, _inputs[_mapPath]));
            if (geographyChanged)
                documents.Add(new(_geographyPath, _geographyInput with { RegionCityIds = draft.Geography.RegionCityIds }, _inputs[_geographyPath]));
            if (!ContentComparison.Equal(draft.Scenario, _saved.Scenario))
            {
                documents.Add(new(_mapManifestPath, layout with { GeographyInputHash = null }, _inputs[_mapManifestPath]));
                documents.Add(new(_scenarioPath, draft.Scenario, _inputs[_scenarioPath]));
                if (_inputs[_manifestPath] is null || _manifest.Map != _relativeMap)
                    documents.Add(new(_manifestPath, _manifest with { Map = _relativeMap }, _inputs[_manifestPath]));
            }
            var dependencies = _inputs.Where(p => p.Key.EndsWith(".player.json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (mapChanged || geographyChanged)
                await ValidateOtherScenarios(draft, mapChanged, geographyChanged, resources, dependencies, token);
            var published = await ScenarioDataFiles.WritePlayerDocumentsAsync(documents, token, dependencies);
            foreach (var (path, bytes) in published) _inputs[path] = bytes;
            _saved = draft;
            return preview;
        }
        finally { _saveGate.Release(); }
    }
}
