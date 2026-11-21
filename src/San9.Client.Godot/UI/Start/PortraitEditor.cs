using Godot;
using San9.Application.Authoring;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Independent portrait authoring page. Saving never publishes character drafts.</summary>
public partial class PortraitEditor : ModalView
{
    public string? CreatedOfficersPath { get; init; }
    public string ContentRoot { get; init; } = "";
    public string PackRoot { get; init; } = "";
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private PortraitEditorWorkspace? _workspace;
    private PortraitEditorImage? _image;
    private string? _current;
    private ILookup<string, string> _savedReferences = Array.Empty<(string Key, string Name)>().ToLookup(p => p.Key, p => p.Name);
    private readonly CancellationTokenSource _lifetime = new();
    private Tree _list = null!;
    private LineEdit _search = null!, _name = null!;
    private CheckBox _modified = null!;
    private Label _framingHint = null!, _identity = null!, _references = null!;
    private AcceptDialog _errorDialog = null!;
    private global::Godot.Timer _saveFeedbackTimer = null!;
    private PortraitEditorCanvas _canvas = null!;
    private PortraitEditorPreviews _previews = null!;
    private SpinBox _x = null!, _y = null!;
    private Button _faceView = null!, _bustView = null!;
    private Button _automatic = null!;
    private MenuButton _more = null!;
    private readonly PortraitFaceDetector _detector = new();
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _importCancellation;
    private int _analysisVersion;
    private string _textureRoot = "", _officerDefaultsPath = "";
    private string? _candidateKey;
    private Button _save = null!, _import = null!, _undo = null!, _redo = null!;
    private bool _refreshing, _previousAutoQuit, _xDirty, _yDirty, _nameDirty;

    private async void Load()
    {
        try
        {
            var token = _lifetime.Token;
            var catalog = await ContentCatalogFiles.LoadAsync(ContentRoot, token);
            _textureRoot = PackageFiles.Resolve(ContentRoot, catalog.Textures);
            _officerDefaultsPath = PackageFiles.Resolve(ContentRoot, catalog.OfficerDefaults);
            var textures = await TexturePackageFiles.LoadAsync(_textureRoot, token);
            _workspace = await PortraitEditorWorkspace.OpenAsync(PackRoot, textures.Portraits, token);
            token.ThrowIfCancellationRequested();
            _savedReferences = await Task.Run(() => ReadCurrentUsers().ToLookup(p => p.Key, p => p.Name), token);
            token.ThrowIfCancellationRequested();
            _current = _workspace.Catalog.Portraits.FirstOrDefault()?.Image.Id;
            _save.Disabled = _import.Disabled = false; RefreshSelection();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) ShowError("读取失败：" + error.Message); }
    }

    private void RefreshList()
    {
        if (_workspace is null) return;
        _refreshing = true; _list.Clear();
        var root = _list.CreateItem();
        string filter = _search.Text.Trim();
        var modifiedKeys = _workspace.ModifiedKeys;
        foreach (var portrait in _workspace.Catalog.Portraits)
        {
            string key = portrait.Image.Id, name = _workspace.Name(key);
            var users = PortraitUsers(key);
            bool modified = modifiedKeys.Contains(key);
            if (_modified.ButtonPressed && !modified || !name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !users.Any(user => user.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
            var item = _list.CreateItem(root); item.SetMetadata(0, key);
            item.SetText(0, (modified ? "* " : "") + name); item.SetTooltipText(0, name);
            item.SetText(1, users.Length == 0 ? "未使用" : users.Length <= 2 ? string.Join("、", users) : $"{users.Length}人");
            item.SetTooltipText(1, users.Length == 0 ? "未使用" : string.Join("、", users));
            if (key == _current) item.Select(0);
        }
        _refreshing = false;
    }
    private string[] PortraitUsers(string key) => _savedReferences[key].ToArray();

    private void Select(string key)
    {
        FinishName(); CancelAnalysis(); _canvas.CancelGesture(); _current = key;
        _automatic.Disabled = false;
        _image?.Dispose(); _image = null;
        try
        {
            var (portrait, path, pending) = ImageSource(key);
            _image = new(Service, portrait, pending, path);
        }
        catch (Exception error) { ShowError("预览失败：" + error.Message); }
        _canvas.Source = _previews.Source = _image;
        _canvas.ResetView(); ShowCurrent();
    }
    private void RefreshSelection()
    {
        if (_workspace is null) return;
        var portraits = _workspace.Catalog.Portraits;
        if (!portraits.Any(p => p.Image.Id == _current)) _current = portraits.FirstOrDefault()?.Image.Id;
        RefreshList();
        if (_current is { } key) Select(key);
        else
        {
            _image?.Dispose(); _image = null; _canvas.Source = _previews.Source = null;
            _canvas.Portrait = _previews.Portrait = null; _identity.Text = "暂无头像";
            _name.Text = ""; _name.Editable = false; _references.Hide();
            _canvas.QueueRedraw(); _previews.QueueRedraw();
        }
        UpdateHistory();
    }
    private void ShowCurrent()
    {
        if (_workspace is null || _current is null) return;
        var portrait = _workspace.Portrait(_current);
        _canvas.Portrait = _previews.Portrait = portrait;
        if (!_nameDirty)
        {
            _refreshing = true; _name.Text = _workspace.Name(_current); _refreshing = false;
        }
        _name.Editable = _workspace.IsAuthored(_current);
        _identity.Text = $"{portrait.Image.Width}×{portrait.Image.Height}";
        var references = PortraitUsers(_current);
        _references.Visible = references.Length > 0;
        _references.Text = references.Length <= 2 ? "使用者：" + string.Join("、", references) : $"使用者：{references.Length}人";
        _references.TooltipText = string.Join("、", references.Take(12)) + (references.Length > 12 ? "…" : "");
        UpdateCoordinates(portrait); _canvas.QueueRedraw(); _previews.QueueRedraw(); UpdateHistory();
    }
    private void Restore(bool basis)
    {
        FinishName();
        CancelAnalysis(); _canvas.CancelGesture();
        if (_workspace is null || _current is null) return;
        try { _workspace.Restore(_current, basis); RefreshSelection(); }
        catch (InvalidDataException error) { ShowError(error.Message); }
    }
    private void UpdateHistory()
    {
        _undo.Disabled = _workspace?.CanUndo != true; _redo.Disabled = _workspace?.CanRedo != true;
        var menu = _more.GetPopup();
        bool authored = _current is not null && _workspace?.IsAuthored(_current) == true;
        menu.SetItemDisabled(menu.GetItemIndex(4), _current is null);
        menu.SetItemDisabled(menu.GetItemIndex(5), _current is null || authored);
        menu.SetItemDisabled(menu.GetItemIndex(6), _current is null || !authored);
        if (_workspace?.HasChanges == true) { _saveFeedbackTimer.Stop(); _save.Text = "保存"; }
    }
    private void FinishName()
    {
        if (!_nameDirty || _workspace is null || _current is null) return;
        _nameDirty = false;
        if (!_workspace.IsAuthored(_current)) return;
        try { _workspace.Rename(_current, _name.Text); }
        catch (InvalidDataException error) { ShowError(error.Message); }
        _refreshing = true; _name.Text = _workspace.Name(_current); _refreshing = false;
        RefreshList(); UpdateHistory();
    }
    private void ShowHelp()
    {
        var help = new AcceptDialog { Title = "取景操作", OkButtonText = "知道了",
            DialogText = "金框：半身；蓝框：面部。单选独立调整，同时选中则一起移动。\n拖动取景框或输入X、Y调整位置。\n方向键微调，Shift加速；Esc或右键取消拖动。\n滚轮缩放画布，中键平移。" };
        help.Confirmed += () => help.QueueFree(); help.Canceled += () => help.QueueFree();
        AddChild(help); help.PopupCentered(new(540, 210));
    }
    private bool Save()
    {
        FinishName();
        if (_workspace is null || _importCancellation is not null) return false;
        CancelAnalysis(); _canvas.CancelGesture();
        ApplyCoordinates();
        try
        {
            var removed = _workspace.RemovedKeys;
            var users = removed.Count == 0 ? null : ReadCurrentUsers().Select(p => p.Key).ToHashSet();
            _workspace.Save(users); Service.UpdatePortraitCatalog(_workspace.Catalog);
            RefreshList(); UpdateHistory(); _save.Text = "已保存"; _saveFeedbackTimer.Start(); return true;
        }
        catch (Exception error) { ShowError("保存失败，草稿已保留：" + error.Message); return false; }
    }
    private void FramingHint(string text)
    {
        _framingHint.Text = text;
        _framingHint.TooltipText = text;
    }
    private void ShowError(string text)
    {
        _errorDialog.DialogText = text;
        _errorDialog.PopupCentered(new(540, 180));
    }
    private void RequestClose()
    {
        if (_importCancellation is not null) { _importCancellation.Cancel(); return; }
        FinishName();
        CancelAnalysis(); _canvas.CancelGesture(); ApplyCoordinates();
        if (_workspace?.HasChanges != true) { Closed(); return; }
        var dialog = new ConfirmationDialog { Title = "未保存的头像", DialogText = "是否保存头像修改？",
            OkButtonText = "保存并关闭", CancelButtonText = "继续编辑" };
        dialog.AddButton("放弃并关闭", false, "discard");
        dialog.Confirmed += () => { dialog.QueueFree(); if (Save()) Closed(); };
        dialog.CustomAction += action => { if (action == "discard") { dialog.QueueFree(); Closed(); } };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(530, 180));
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_importCancellation is not null || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.CtrlPressed && key.Keycode == Key.S) { Save(); GetViewport().SetInputAsHandled(); }
        else if (key.Keycode == Key.Escape) { RequestClose(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree()
    {
        GetWindow().CloseRequested -= RequestClose; GetTree().AutoAcceptQuit = _previousAutoQuit;
        CancelAnalysis(); _lifetime.Cancel(); _lifetime.Dispose(); _image?.Dispose(); _detector.Dispose(); _workspace?.Dispose();
    }
}
