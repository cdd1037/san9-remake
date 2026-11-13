using Godot;
using San9.Application.Authoring;
using San9.Content.Authoring;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Owns the shared draft and the single save transaction for every content subpage.</summary>
public partial class SharedDataEditor : ModalView
{
    public Persistence.ClientDataPaths DataPaths { get; init; } = null!;
    public TextureService? Service { get; init; }
    private TextureService? _appearanceService;
    private OfficerFlagCatalog? _flags;
    public string ContentRoot { get; init; } = "";
    public string? ReferenceScenarioId { get; init; }
    public Action Closed { get; init; } = null!;
    private SharedDataEditorWorkspace? _workspace;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<SharedDataEditorPage> _editors = [];
    private TabContainer _pages = null!;
    private Label _title = null!, _status = null!;
    private Button _save = null!, _close = null!;
    private bool _busy, _previousAutoQuit;
    private bool HasChanges => _workspace?.HasChanges == true || _editors.Any(p => p.HasPendingChanges);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Stop;
        Theme = ContentEditorStyle.Create(GetThemeFont("font"));
        _previousAutoQuit = GetTree().AutoAcceptQuit; GetTree().AutoAcceptQuit = false;
        GetWindow().CloseRequested += RequestClose;
        var surface = new PanelContainer(); surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        surface.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new("1b222c") }); AddChild(surface);
        var margin = new MarginContainer(); surface.AddChild(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 10); margin.AddChild(layout);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); layout.AddChild(header);
        _title = new Label { Text = "游戏资料编辑器", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Pass, TooltipText = "正在读取共享数据…" };
        _title.AddThemeFontSizeOverride("font_size", 20); header.AddChild(_title);
        _save = AddButton(header, "保存全部修改", () => _ = Save(), "SaveSharedData");
        CompanionPanelStyle.Primary(_save); _save.TooltipText = "Ctrl+S"; _save.Disabled = true;
        _close = AddButton(header, "关闭", RequestClose, "CloseSharedDataEditor");
        _pages = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Name = "SharedDataPages" }; layout.AddChild(_pages);
        _status = new Label { Visible = false, Name = "SharedDataStatus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 14); layout.AddChild(_status);
        Load();
    }

    private async void Load()
    {
        try
        {
            _workspace = await Task.Run(() => SharedDataEditorWorkspace.OpenAsync(ContentRoot, ReferenceScenarioId, _lifetime.Token), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            string? appearanceError = await LoadAppearance();
            AddPage(new BasicRulesEditorPage { Name = "基础规则", Workspace = _workspace, ReportStatus = Status });
            AddPage(new RankEditorPage { Name = "势力官爵", Workspace = _workspace, ReportStatus = Status });
            AddPage(new OfficeEditorPage { Name = "官职", Workspace = _workspace, ReportStatus = Status });
            AddPage(new OfficerEditorPage { Name = "武将", Workspace = _workspace, ReportStatus = Status, PortraitService = _appearanceService, Flags = _flags });
            AddPage(new FormationEditorPage { Name = "阵形", Workspace = _workspace, ReportStatus = Status });
            AddPage(new TacticEditorPage { Name = "兵法", Workspace = _workspace, ReportStatus = Status });
            _pages.TabChanged += index => _editors[(int)index].Activate();
            UpdateTitleTooltip(); _save.Disabled = false;
            Status(appearanceError ?? "", appearanceError is not null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) Status("读取失败：" + error.Message, true); }
    }

    private async Task<string?> LoadAppearance()
    {
        _appearanceService = Service;
        try
        {
            var catalog = await ContentCatalogFiles.LoadAsync(ContentRoot, _lifetime.Token);
            _flags = await ContentCatalogFiles.LoadOfficerFlagsAsync(ContentRoot, catalog, _lifetime.Token);
            if (_appearanceService is null)
            {
                string root = PackageFiles.Resolve(ContentRoot, catalog.Textures);
                var textures = await Task.Run(() => TexturePackageFiles.LoadAsync(root, _lifetime.Token, DataPaths?.PortraitPack), _lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
                _appearanceService = new TextureService(); _appearanceService.Initialize(textures, root); AddChild(_appearanceService);
            }
            return null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { return "外观资源不可用：" + error.Message; }
    }

    private void AddPage(SharedDataEditorPage page) { _editors.Add(page); _pages.AddChild(page); }

    private void UpdateTitleTooltip()
    {
        if (_workspace is not { } workspace) return;
        _title.TooltipText = $"{(workspace.HasPlayerCopy ? "玩家配置" : "基础配置")}\n势力官爵 {workspace.Ranks.Count} 项 · 官职 {workspace.Offices.Count} 项 · 武将 {workspace.Officers.Count} 项 · 阵形 {workspace.Formations.Length} 项 · 兵法 {workspace.Tactics.Length} 项\n\n基础：{workspace.DefinitionPath}\n玩家副本：{workspace.PlayerDefinitionPath}\n人物资料：{workspace.PlayerOfficerPath}";
    }

    private async Task<bool> Save()
    {
        if (_workspace is null || _busy) return false;
        for (int i = 0; i < _editors.Count; i++)
            if (!_editors[i].CommitPending()) { _pages.CurrentTab = i; return false; }
        _busy = true; _save.Disabled = _close.Disabled = true; _pages.ProcessMode = ProcessModeEnum.Disabled;
        Status("正在保存…");
        try
        {
            await Task.Run(() => _workspace.SaveAsync(_lifetime.Token), _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return false;
            foreach (var page in _editors) page.Saved();
            UpdateTitleTooltip(); Status("已保存，重新开局后生效。"); return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception error) { Status("保存失败：" + error.Message, true); return false; }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            { _busy = false; _save.Disabled = _close.Disabled = false; _pages.ProcessMode = ProcessModeEnum.Inherit; }
        }
    }

    private void RequestClose()
    {
        if (_busy) return;
        if (!HasChanges) { Closed(); return; }
        var dialog = new ConfirmationDialog { Title = "未保存的修改", DialogText = "是否保存修改？",
            OkButtonText = "保存并关闭", CancelButtonText = "继续编辑" };
        dialog.AddButton("不保存并关闭", false, "discard");
        dialog.Confirmed += async () => { dialog.QueueFree(); if (await Save()) Closed(); };
        dialog.CustomAction += action => { if (action == "discard") { dialog.QueueFree(); Closed(); } };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(570, 190));
    }

    private void Status(string message, bool error = false)
    {
        _status.Text = message;
        _status.Visible = !string.IsNullOrEmpty(message);
        _status.Modulate = error ? new Color("ffb4a8") : ContentEditorStyle.Muted;
    }

    private static Button AddButton(Node parent, string title, Action action, string name) =>
        ContentEditorControls.ActionButton(parent, title, action, name);

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
    }
}
