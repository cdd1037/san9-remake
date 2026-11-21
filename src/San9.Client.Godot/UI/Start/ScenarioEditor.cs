using Godot;
using San9.Application.Authoring;
using San9.Content.Interface;
using San9.Content.Packages;

namespace San9.Client.Godot;

public partial class ScenarioEditor : ModalView
{
    public string? CreatedOfficersPath { get; init; }
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string, string> CategoryLabels { get; init; } = new Dictionary<string, string>();
    public string? ScenarioId { get; init; }
    public Action Closed { get; init; } = null!;
    public Action<ScenarioPreviewEntry[]>? Published { get; init; }
    private readonly CancellationTokenSource _lifetime = new();
    private ScenarioEditorWorkspace? _workspace;
    private ScenarioPreviewEntry[] _entries = [];
    private OptionButton _scenarios = null!;
    private TabContainer _pages = null!;
    private Label _status = null!, _title = null!;
    private Button _saveAs = null!, _save = null!, _close = null!, _restore = null!;
    private TextEdit _json = null!;
    private bool _jsonDirty, _busy, _building, _previousAutoQuit;
    private int _selected, _lastTab, _dataTab;
    private int? _browsedForce;
    private San9.Content.Scenarios.ScenarioDocument? _displayedDocument;
    private bool HasChanges => _jsonDirty || _workspace?.HasChanges == true;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Stop;
        Theme = ContentEditorStyle.Create(GetThemeFont("font"));
        _previousAutoQuit = GetTree().AutoAcceptQuit; GetTree().AutoAcceptQuit = false;
        GetWindow().CloseRequested += RequestClose;
        var surface = new PanelContainer(); surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        surface.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new("1b222c") }); AddChild(surface);
        var margin = new MarginContainer(); surface.AddChild(margin);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 16);
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 10); margin.AddChild(layout);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); layout.AddChild(header);
        _title = new Label { Text = "剧本编辑器", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 20); header.AddChild(_title);
        ContentEditorControls.ActionButton(header, "检查剧本", CheckScenario, "CheckScenario");
        _restore = ContentEditorControls.ActionButton(header, "恢复打开时的值", Restore, "RestoreScenario");
        _save = ContentEditorControls.ActionButton(header, "保存剧本", () => _ = Save(), "SaveScenario");
        _saveAs = ContentEditorControls.ActionButton(header, "另存为新剧本", ShowSaveAs, "SaveScenarioAs");
        _save.TooltipText = "Ctrl+S"; CompanionPanelStyle.Primary(_save);
        _close = ContentEditorControls.ActionButton(header, "关闭", RequestClose, "CloseScenarioEditor");
        _scenarios = new OptionButton { Name = "EditorScenarioList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        layout.AddChild(_scenarios); _scenarios.ItemSelected += index => SelectScenario((int)index);
        _pages = new TabContainer { Name = "ScenarioEditorPages", SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.AddChild(_pages); _pages.TabChanged += TabChanged;
        _status = new Label { Name = "ScenarioEditorStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 14); layout.AddChild(_status);
        LoadCatalog();
    }

    private async void LoadCatalog()
    {
        Busy(true); Status("正在读取剧本目录…");
        try
        {
            var catalog = await ContentCatalogFiles.LoadAsync(ContentRoot, _lifetime.Token);
            _entries = await ContentCatalogFiles.LoadPreviewsAsync(ContentRoot, catalog, _lifetime.Token);
            await LoadCityMap(catalog, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            RefreshScenarioList();
            _selected = ScenarioId is null ? 0 : Array.FindIndex(_entries, e => e.Id == ScenarioId);
            if (_selected < 0) throw new InvalidDataException("找不到指定剧本：" + ScenarioId);
            await LoadScenario(_selected);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) Status("读取失败：" + error.Message, true); }
        finally { if (!_lifetime.IsCancellationRequested) Busy(false); }
    }

    private void RefreshScenarioList()
    {
        _scenarios.Clear();
        foreach (var entry in _entries) _scenarios.AddItem($"{entry.Year}年　{entry.Title}　[{entry.Id}]");
        _scenarios.Select(_selected);
    }

    private async Task LoadScenario(int index, string status = "修改在保存后用于新局。人物所属、据点所属和军团引用须保持一致。")
    {
        Busy(true); Status("正在读取剧本…");
        try
        {
            var workspace = await Task.Run(() => ScenarioEditorWorkspace.OpenAsync(ContentRoot, _entries[index].Id, _lifetime.Token, CreatedOfficersPath), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _selectedCity = null; _browsedForce = null; _forceTab = 0; _diplomacyTab = 0;
            _workspace = workspace; _selected = index; _scenarios.Select(index); _jsonDirty = false;
            BuildPages(); Status(status);
            _title.TooltipText = $"基础剧本：{workspace.ScenarioPath}\n玩家副本：{workspace.PlayerScenarioPath}";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_lifetime.IsCancellationRequested) { _scenarios.Select(_selected); Status("读取失败：" + error.Message, true); }
        }
        finally { if (!_lifetime.IsCancellationRequested) Busy(false); }
    }

    private void SelectScenario(int index)
    {
        if (_busy || index == _selected) return;
        _scenarios.Select(_selected);
        ContinueAfterChanges(() => _ = LoadScenario(index));
    }

    private void BuildPages(int selectedTab = 0, int? selectedId = null)
    {
        _building = true;
        ContentEditorControls.ClearChildren(_pages);
        int? Selection() => _pages.GetTabCount() == selectedTab ? selectedId : null;
        BuildOverview();
        var forces = BuildListPage("势力", ForceChoices, BuildForce, Selection() ?? _browsedForce, scrollForm: false);
        ContentEditorControls.ActionButton(forces, "建立势力", NewForceDialog);
        BuildSettlementPage(Selection());
        BuildOfficerPage(Selection());
        BuildListPage("宝物", () => _workspace!.Definitions.Information.Treasures.Select(t => (t.Id, t.Name)), BuildTreasure, selectedId: Selection());
        BuildDiplomacyPage(Selection());
        _dataTab = _pages.GetTabCount();
        var data = new VBoxContainer { Name = "完整初始状态" }; _pages.AddChild(data);
        ContentEditorControls.ActionButton(data, "应用完整数据", () =>
        {
            if (CommitJson()) { BuildPages(_dataTab); Status("完整数据已应用到草稿，尚未保存。"); }
        });
        _json = new TextEdit { Name = "ScenarioJson", Text = _workspace!.ExportJson(), SizeFlagsVertical = SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.None };
        data.AddChild(_json); _json.TextChanged += () => { if (!_building) _jsonDirty = true; };
        _displayedDocument = _workspace!.Document;
        _jsonDirty = false; _pages.CurrentTab = selectedTab; _lastTab = selectedTab; _building = false;
    }

    private void TabChanged(long index)
    {
        if (_building) return;
        if (_lastTab == _dataTab && _jsonDirty)
        {
            if (!CommitJson()) { _building = true; _pages.CurrentTab = _dataTab; _building = false; return; }
            BuildPages((int)index);
        }
        else if (index != _dataTab && !ReferenceEquals(_displayedDocument, _workspace!.Document)) BuildPages((int)index);
        else if (index == _dataTab) { _building = true; _json.Text = _workspace!.ExportJson(); _building = false; }
        _lastTab = (int)index;
    }

    private bool CommitJson()
    {
        if (!_jsonDirty) return true;
        try { _workspace!.ApplyJson(_json.Text); _jsonDirty = false; return true; }
        catch (Exception error)
        {
            _building = true; _pages.CurrentTab = _dataTab; _building = false;
            Status("完整数据无效：" + error.Message, true); return false;
        }
    }

    private async Task<bool> Save((string Title, string Description, string Category)? copy = null)
    {
        if (_workspace is null || _busy) return false;
        if (_jsonDirty)
        {
            if (!CommitJson()) return false;
            BuildPages(_dataTab);
        }
        Busy(true); Status("正在校验并保存…");
        try
        {
            if (copy is { } details)
            {
                var preview = await Task.Run(() => _workspace.SaveAsAsync(details.Title, details.Description, details.Category, _lifetime.Token), _lifetime.Token);
                _entries = [.. _entries, preview];
            }
            else
            {
                await Task.Run(() => _workspace.SaveAsync(_lifetime.Token), _lifetime.Token);
                _entries[_selected] = _workspace.Preview;
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            RefreshScenarioList();
            Published?.Invoke([.. _entries]);
            if (copy is null) Status("已保存，重新开局后生效。");
            else await LoadScenario(_entries.Length - 1, "已创建独立剧本，当前正在编辑副本。");
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) ShowIssue(error); return false; }
        finally { if (!_lifetime.IsCancellationRequested) Busy(false); }
    }

    private void ShowSaveAs()
    {
        if (_busy || _workspace is null) return;
        var preview = _workspace.Preview;
        var dialog = new ConfirmationDialog { Title = "另存为新剧本", OkButtonText = "创建剧本", CancelButtonText = "取消" };
        var fields = new VBoxContainer();
        dialog.AddChild(fields);
        ContentEditorControls.Caption(fields, "标题");
        var title = new LineEdit { Text = preview.Title + "（副本）" };
        fields.AddChild(title);
        ContentEditorControls.Caption(fields, "简介");
        var description = new TextEdit { Text = preview.Description, CustomMinimumSize = new(480, 120),
            WrapMode = TextEdit.LineWrappingMode.Boundary };
        fields.AddChild(description);
        ContentEditorControls.Caption(fields, "分类");
        var categories = _entries.Select(e => e.CategoryId).Distinct().ToArray();
        var category = new OptionButton();
        foreach (var id in categories)
            category.AddItem(CategoryLabels.GetValueOrDefault(id, id));
        category.Select(Array.IndexOf(categories, preview.CategoryId));
        fields.AddChild(category);
        void CheckFields() => dialog.GetOkButton().Disabled =
            string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(description.Text);
        title.TextChanged += _ => CheckFields();
        description.TextChanged += CheckFields;
        dialog.Confirmed += () =>
        {
            _ = Save((title.Text, description.Text, categories[category.Selected]));
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new(540, 340)); title.GrabFocus(); title.SelectAll();
    }

    private void Restore()
    {
        if (_workspace is null || _busy) return;
        var dialog = new ConfirmationDialog { Title = "恢复剧本", DialogText = "恢复本次打开剧本时的全部初始状态和标题简介？",
            OkButtonText = "恢复", CancelButtonText = "继续编辑" };
        dialog.Confirmed += () => { _workspace.RestoreOpened(); BuildPages(); Status("已恢复，尚未保存。"); dialog.QueueFree(); };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(570, 190));
    }

    private void RequestClose() { if (!_busy) ContinueAfterChanges(Closed); }

    private void ContinueAfterChanges(Action continuation)
    {
        if (!HasChanges) { continuation(); return; }
        var dialog = new ConfirmationDialog { Title = "未保存的修改", DialogText = "是否保存当前剧本的修改？",
            OkButtonText = "保存并继续", CancelButtonText = "继续编辑" };
        dialog.AddButton("放弃修改并继续", false, "discard");
        dialog.Confirmed += async () => { dialog.QueueFree(); if (await Save()) continuation(); };
        dialog.CustomAction += action => { if (action == "discard") { dialog.QueueFree(); continuation(); } };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(570, 190));
    }

    private void Busy(bool busy)
    {
        _busy = busy; _save.Disabled = _restore.Disabled = _saveAs.Disabled = busy || _workspace is null;
        _close.Disabled = busy; _scenarios.Disabled = busy;
        _pages.ProcessMode = busy ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
    }

    private void Status(string text, bool error = false)
    {
        _status.Text = text; _status.Modulate = error ? new Color("ffb4a8") : ContentEditorStyle.Muted;
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.CtrlPressed && key.Keycode == Key.S) { GetViewport().SetInputAsHandled(); _ = Save(); }
        else if (key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); RequestClose(); }
    }

    public override void _ExitTree()
    {
        GetWindow().CloseRequested -= RequestClose; GetTree().AutoAcceptQuit = _previousAutoQuit;
        _lifetime.Cancel(); _lifetime.Dispose();
        _cityTextures?.Dispose();
    }
}
