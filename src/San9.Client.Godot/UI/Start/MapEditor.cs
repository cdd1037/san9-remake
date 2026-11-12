using Godot;
using San9.Application.Authoring;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Packages;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class MapEditor : ModalView
{
    public string ContentRoot { get; init; } = "";
    public string? ScenarioId { get; init; }
    public Action Closed { get; init; } = null!;
    public Action<ScenarioPreviewEntry>? Published { get; init; }
    private readonly CancellationTokenSource _lifetime = new();
    private MapEditorWorkspace? _workspace;
    private TexturePackage? _resources;
    private MapEditorCanvas? _canvas;
    private ScenarioPreviewEntry[] _entries = [];
    private Dictionary<int, TerrainStyle> _styles = [];
    private OptionButton _scenarios = null!;
    private VBoxContainer _properties = null!;
    private Label _status = null!, _position = null!, _title = null!;
    private Button _save = null!, _undo = null!, _redo = null!, _close = null!;
    private bool _busy, _previousAutoQuit;
    private int _selectedScenario, _tool, _terrain, _height = 1, _variant, _radius, _region, _bridge = -1;
    private int? _selectedBuilding;
    private bool _paintTerrain = true, _paintHeight = true, _paintVariant = true, _hideName, _movementBlocked, _influenceBlocked;
    private ConstructionSurface _construction = ConstructionSurface.General;
    private MapCell? _last, _moveTarget;

    public override void _Ready()
    {
        Theme = ContentEditorStyle.Create(GetThemeFont("font"));
        _previousAutoQuit = GetTree().AutoAcceptQuit; GetTree().AutoAcceptQuit = false;
        GetWindow().CloseRequested += RequestClose;
        BuildLayout();
        LoadCatalog();
    }

    private async void LoadCatalog()
    {
        Busy(true); Status("正在读取剧本目录…");
        try
        {
            var catalog = await ContentCatalogFiles.LoadAsync(ContentRoot, _lifetime.Token);
            _entries = await ContentCatalogFiles.LoadPreviewsAsync(ContentRoot, catalog, _lifetime.Token);
            foreach (var entry in _entries) _scenarios.AddItem($"{entry.Year}年　{entry.Title}　[{entry.Id}]");
            int index = ScenarioId is null ? 0 : Array.FindIndex(_entries, e => e.Id == ScenarioId);
            if (index < 0) throw new InvalidDataException("找不到指定剧本。");
            await LoadScenario(index);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) Status("读取失败：" + error.Message, true); }
        finally { if (!_lifetime.IsCancellationRequested) Busy(false); }
    }

    private async Task LoadScenario(int index)
    {
        Busy(true); Status("正在读取地图与素材…");
        try
        {
            var workspace = await Task.Run(() => MapEditorWorkspace.OpenAsync(ContentRoot, _entries[index].Id, _lifetime.Token), _lifetime.Token);
            var resources = await Task.Run(() => TexturePackageFiles.LoadAsync(workspace.TextureRoot, _lifetime.Token), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _canvas?.QueueFree();
            if (_canvas is not null) _mapArea.RemoveChild(_canvas);
            _workspace = workspace; _resources = resources; _styles = resources.Map.TerrainStyles.ToDictionary(s => s.Id);
            _selectedScenario = index; _scenarios.Select(index); _selectedBuilding = workspace.Buildings.FirstOrDefault()?.Id;
            var first = workspace.Map.Cells[0]; _terrain = first.Terrain; _height = first.Elevation; _variant = first.TextureVariant;
            _region = workspace.Geography.RegionNameKeys.Keys.Order().First();
            _canvas = new MapEditorCanvas { Name = "MapEditorCanvas", Workspace = workspace, Resources = resources,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
                Season = Seasons[_season.Selected], ShowGrid = _gridToggle.ButtonPressed,
                ShowRegions = _regionToggle.ButtonPressed, ShowBuildings = _buildingToggle.ButtonPressed,
                ZoomChanged = zoom => _zoomLabel.Text = $"{zoom:P0}",
                BrushRadius = _tool == 5 ? 0 : _radius, SelectedBuilding = _selectedBuilding, Pressed = StartStroke, Dragged = DragStroke,
                Released = FinishStroke, Hovered = Hover, Failed = message => Status("预览失败：" + message, true) };
            _mapArea.AddChild(_canvas);
            Hover(null);
            BuildProperties(); Refresh(); Status("修改用于当前剧本的新局。保存时统一重建地貌、地域连接与建设位置。");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) { _scenarios.Select(_selectedScenario); Status("读取失败：" + error.Message, true); } }
        finally { if (!_lifetime.IsCancellationRequested) Busy(false); }
    }

    private void BuildProperties()
    {
        foreach (Node child in _properties.GetChildren()) { _properties.RemoveChild(child); child.QueueFree(); }
        if (_workspace is null) return;
        _canvas!.BrushRadius = _tool == 5 ? 0 : _radius;
        _brushControls.Visible = _tool != 5;
        _propertyTitle.Text = ToolNames[_tool];
        switch (_tool)
        {
            case 0: TerrainProperties(); break;
            case 1: RegionProperties(); break;
            case 2:
                ContentEditorControls.Toggle(_properties, "阻止部队通行", _movementBlocked, value => _movementBlocked = value);
                ContentEditorControls.Toggle(_properties, "阻止都市影响传播", _influenceBlocked, value => _influenceBlocked = value);
                var regenerate = new Button { Text = "按规则重新生成全图", TooltipText = "覆盖全图的部队阻断和影响阻断，可撤销。" };
                regenerate.Pressed += () =>
                {
                    if (_busy) return;
                    FinishStroke();
                    try
                    {
                        int changed = _workspace.RegenerateBlocking();
                        Refresh();
                        Status($"已重新生成 {changed} 格的阻断，可撤销。");
                    }
                    catch (Exception error) { Status(error.Message, true); }
                };
                _properties.AddChild(regenerate);
                break;
            case 3:
                foreach (var (surface, title) in new[] { (ConstructionSurface.General, "允许普通设施"),
                    (ConstructionSurface.Horizontal, "允许横向设施"), (ConstructionSurface.Vertical, "允许纵向设施") })
                    ContentEditorControls.Toggle(_properties, title, (_construction & surface) != 0,
                        value => _construction = value ? _construction | surface : _construction & ~surface);
                break;
            case 4:
                ContentEditorControls.Choices(_properties, "桥锚点", ["清除", "方向 0", "方向 1"], _bridge + 1, index => _bridge = index - 1);
                _properties.AddChild(new Label { Text = "桥锚点与地貌分别编辑，可配合桥面及桥岸地形使用。",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart });
                break;
            case 5: BuildingProperties(); break;
        }
    }

    private void TerrainProperties()
    {
        int[] ids = _styles.Keys.Order().ToArray();
        var terrainRow = PaintField("地形", _paintTerrain, value => _paintTerrain = value);
        terrainRow.AddChild(new Label { Text = TerrainLabel(_terrain), VerticalAlignment = VerticalAlignment.Center });
        var terrains = new HFlowContainer { Name = "MapEditorTerrainChoices" };
        terrains.AddThemeConstantOverride("h_separation", 2);
        terrains.AddThemeConstantOverride("v_separation", 2);
        _properties.AddChild(terrains);
        var group = new ButtonGroup();
        foreach (int id in ids)
        {
            var button = new Button { Text = _workspace!.Text.Resolve($"terrain.{id}"), TooltipText = TerrainLabel(id),
                ToggleMode = true, ButtonGroup = group, ButtonPressed = id == _terrain };
            terrains.AddChild(button);
            CompanionPanelStyle.Toggle(button);
            ContentEditorStyle.Compact(button);
            button.CustomMinimumSize = new(44, 24);
            button.Pressed += () =>
            {
                _terrain = id; var style = _styles[id];
                _height = Math.Clamp(_height, style.MinimumElevation, style.MaximumElevation);
                _variant = Math.Clamp(_variant, 0, style.VariantCount - 1); BuildProperties();
            };
        }
        var current = _styles[_terrain];
        PaintField("高度", _paintHeight, value => _paintHeight = value);
        NumberChoices(current.Levels.Select(level => level.Elevation).Order(), _height,
            value => _height = value, "MapEditorHeightChoices");
        PaintField("变体", _paintVariant, value => _paintVariant = value);
        NumberChoices(Enumerable.Range(0, current.VariantCount), _variant,
            value => _variant = value, "MapEditorVariantChoices");
    }

    private void RegionProperties()
    {
        int[] ids = _workspace!.Geography.RegionNameKeys.Keys.Order().ToArray();
        ContentEditorControls.Choices(_properties, "地域", new[] { "无地域" }.Concat(ids.Select(id => $"{_workspace.RegionName(id)} [{id}]")).ToArray(),
            _region < 0 ? 0 : Array.IndexOf(ids, _region) + 1,
            index => { _region = index == 0 ? -1 : ids[index - 1]; BuildProperties(); });
        ContentEditorControls.Toggle(_properties, "隐藏地域名称", _hideName, value => _hideName = value);
        if (_region < 0) return;
        var cities = _workspace.Scenario.Settlements.Where(s => s.Kind == SettlementKind.City).ToArray();
        ContentEditorControls.Choices(_properties, "关联都市", cities.Select(s => _workspace.Name(s.NameKey)).ToArray(),
            Array.FindIndex(cities, s => s.Id == _workspace.Geography.RegionCityIds.GetValueOrDefault(_region, -1)), index =>
            {
                _workspace.SetRegionCity(_region, cities[index].Id); Refresh();
            });
        var point = _workspace.Map.Cells.Select((c, i) => (c, i)).FirstOrDefault(p => p.c.RegionId == _region);
        if (point.c is not null) ContentEditorControls.ActionButton(_properties, "定位地域", () => _canvas!.Focus(new(point.i % _workspace.Map.Width, point.i / _workspace.Map.Width)));
    }

    private void BuildingProperties()
    {
        var buildings = _workspace!.Buildings;
        ContentEditorControls.Choices(_properties, "据点", buildings.Select(b => _workspace.Name(_workspace.Scenario.Settlements.Single(s => s.Id == b.Id).NameKey)).ToArray(),
            Array.FindIndex(buildings, b => b.Id == _selectedBuilding), index =>
            {
                _selectedBuilding = buildings[index].Id; _canvas!.SelectedBuilding = _selectedBuilding;
                _canvas.Focus(new(buildings[index].X, buildings[index].Y)); BuildProperties();
            });
        if (_selectedBuilding is not int id) return;
        var selected = buildings.Single(b => b.Id == id); int x = selected.X, y = selected.Y;
        var coordinates = FieldRow(_properties);
        coordinates.AddChild(new Label { Text = "X" });
        ContentEditorControls.NumericInput(coordinates, x, 0, _workspace.Map.Width - 1, value => x = (int)value).CustomMinimumSize = new(84, 34);
        coordinates.AddChild(new Label { Text = "Y" });
        ContentEditorControls.NumericInput(coordinates, y, 0, _workspace.Map.Height - 1, value => y = (int)value).CustomMinimumSize = new(84, 34);
        ContentEditorControls.ActionButton(_properties, "应用位置", () => MoveBuilding(new(x, y)));
        var shape = _workspace.Scenario.MilitaryPlaces.Single(p => p.Id == id).Footprint;
        _properties.AddChild(new Label { Text = $"占地 {shape.Width} × {shape.Height} 精细单位\n从据点所在格拖到目标格后松开，或输入坐标应用位置。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void StartStroke(MapCell cell, bool sample)
    {
        if (_busy || _workspace is null) return;
        if (sample) { Sample(cell); return; }
        if (_tool == 5)
        {
            var building = _workspace.Buildings.FirstOrDefault(b => b.X == cell.X && b.Y == cell.Y);
            if (building is null) return;
            _selectedBuilding = building.Id; _canvas!.SelectedBuilding = building.Id;
            _moveTarget = cell; _canvas.PlacementTarget = cell;
            BuildProperties(); return;
        }
        _workspace.BeginStroke(); _last = cell; Paint(cell);
    }

    private void DragStroke(MapCell cell)
    {
        if (_busy) return;
        if (_tool == 5 && _moveTarget is not null) { _moveTarget = cell; _canvas!.PlacementTarget = cell; }
        else if (_last is not null) Paint(cell);
    }

    private void Paint(MapCell cell)
    {
        try
        {
            _workspace!.Paint(_last!.Value, cell, _radius, current => _tool switch
            {
                0 => current with { Terrain = _paintTerrain ? _terrain : current.Terrain,
                    Elevation = _paintHeight ? _height : current.Elevation, TextureVariant = _paintVariant ? _variant : current.TextureVariant },
                1 => current with { RegionId = _region < 0 ? null : _region, HideRegionName = _hideName },
                2 => current with { MovementBlocked = _movementBlocked, InfluenceBlocked = _influenceBlocked },
                3 => current with { Construction = _construction },
                4 => current with { BridgeOrientation = _bridge < 0 ? null : _bridge },
                _ => current
            }, _styles);
            _last = cell; Refresh();
        }
        catch (Exception error) { _workspace!.CancelStroke(); _last = null; Refresh(); Status("本笔已撤回：" + error.Message, true); }
    }

    private void FinishStroke()
    {
        _workspace?.EndStroke(); _last = null;
        if (_moveTarget is { } target)
        {
            _moveTarget = null; _canvas!.PlacementTarget = null;
            MoveBuilding(target);
        }
        Refresh();
    }

    private void MoveBuilding(MapCell target)
    {
        if (_selectedBuilding is not int id || _workspace is null) return;
        try { _workspace.MoveBuilding(id, target); BuildProperties(); Refresh(); }
        catch (Exception error) { Status(error.Message, true); }
    }

    private void Sample(MapCell cell)
    {
        var value = _workspace!.Map.Cells[cell.Y * _workspace.Map.Width + cell.X];
        _terrain = value.Terrain; _height = value.Elevation; _variant = value.TextureVariant;
        _region = value.RegionId ?? -1;
        _hideName = value.HideRegionName; _movementBlocked = value.MovementBlocked; _influenceBlocked = value.InfluenceBlocked;
        _construction = value.Construction; _bridge = value.BridgeOrientation ?? -1;
        BuildProperties(); Status("已吸取当前格属性。");
    }

    private void Hover(MapCell? cell)
    {
        if (cell is not { } point || _workspace is null)
        {
            _position.Text = "将鼠标移到地图上查看格数据";
            return;
        }
        var value = _workspace.Map.Cells[point.Y * _workspace.Map.Width + point.X];
        string regionName = value.RegionId is int region ? $"{_workspace.RegionName(region)} [{region}]" : "无地域";
        _position.Text = $"当前格 ({point.X}, {point.Y})  {TerrainLabel(value.Terrain)}\n高度 {value.Elevation}  变体 {value.TextureVariant}\n{regionName}\n" +
            $"部队{(value.MovementBlocked ? "阻断" : "可通行")}  影响{(value.InfluenceBlocked ? "阻断" : "可传播")}";
    }

    private string TerrainLabel(int id) => $"{_workspace!.Text.Resolve($"terrain.{id}")} [{id}]";

    private void History(bool redo)
    {
        if (_busy || _workspace is null) return;
        FinishStroke(); if (redo) _workspace.Redo(); else _workspace.Undo();
        BuildProperties(); Refresh();
    }

    private async Task<bool> Save()
    {
        if (_workspace is null || _resources is null || _busy) return false;
        FinishStroke(); Busy(true); Status("正在校验地图、重建领域地理并保存…");
        try
        {
            var preview = await Task.Run(() => _workspace.SaveAsync(_resources.Map, _lifetime.Token), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested(); Published?.Invoke(preview);
            Status("地图已保存，重新开局后使用当前地图。"); return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) Status("保存失败：" + error.Message, true); return false; }
        finally { if (!_lifetime.IsCancellationRequested) { Busy(false); Refresh(); } }
    }

    private void Refresh()
    {
        if (_workspace is null) return;
        _title.Text = _workspace.HasChanges ? "地图编辑器 *" : "地图编辑器";
        _title.TooltipText = _workspace.HasChanges ? "有未保存的修改" : "当前修改已保存";
        _mapSize.Text = $"{_workspace.Map.Width} × {_workspace.Map.Height}";
        _undo.Disabled = _busy || !_workspace.CanUndo; _redo.Disabled = _busy || !_workspace.CanRedo;
    }

    private void Busy(bool busy)
    {
        _busy = busy; _save.Disabled = busy || _workspace is null; _close.Disabled = busy;
        _scenarios.Disabled = busy;
        foreach (var button in _toolButtons) button.Disabled = busy;
        _brushSize.Editable = !busy;
        _properties.MouseBehaviorRecursive = _viewControls.MouseBehaviorRecursive =
            busy ? MouseBehaviorRecursiveEnum.Disabled : MouseBehaviorRecursiveEnum.Inherited;
        if (_canvas is not null) _canvas.EditingEnabled = !busy;
        Refresh();
    }

    private void Status(string message, bool error = false)
    { _status.Text = _status.TooltipText = message; _status.Modulate = error ? new Color("ffb4a8") : ContentEditorStyle.Muted; }

    private void RequestClose() { if (!_busy) { FinishStroke(); ContinueAfterChanges(Closed); } }

    private void ContinueAfterChanges(Action continuation)
    {
        if (_workspace?.HasChanges != true) { continuation(); return; }
        var dialog = new ConfirmationDialog { Title = "未保存的地图", DialogText = "是否保存地图修改？",
            OkButtonText = "保存并继续", CancelButtonText = "继续编辑" };
        dialog.AddButton("放弃修改并继续", false, "discard");
        dialog.Confirmed += async () => { dialog.QueueFree(); if (await Save()) continuation(); };
        dialog.CustomAction += action => { if (action == "discard") { dialog.QueueFree(); continuation(); } };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(520, 180));
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key || _busy) return;
        if (key.CtrlPressed && key.Keycode == Key.S) { GetViewport().SetInputAsHandled(); _ = Save(); }
        else if (key.CtrlPressed && key.Keycode == Key.Z) { GetViewport().SetInputAsHandled(); History(key.ShiftPressed); }
        else if (key.CtrlPressed && key.Keycode == Key.Y) { GetViewport().SetInputAsHandled(); History(true); }
        else if (!key.CtrlPressed && !key.AltPressed && GetViewport().GuiGetFocusOwner() is not LineEdit &&
            key.Keycode >= Key.Key1 && key.Keycode <= Key.Key6)
        { GetViewport().SetInputAsHandled(); SelectTool((int)(key.Keycode - Key.Key1)); }
        else if (!key.CtrlPressed && key.Keycode == Key.F && GetViewport().GuiGetFocusOwner() is not LineEdit)
        { GetViewport().SetInputAsHandled(); _canvas?.Fit(); }
        else if (key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); RequestClose(); }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= LayoutViewport;
        GetWindow().CloseRequested -= RequestClose; GetTree().AutoAcceptQuit = _previousAutoQuit;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
