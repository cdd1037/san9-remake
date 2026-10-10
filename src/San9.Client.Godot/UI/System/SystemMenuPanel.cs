using Godot;
using San9.Client.Godot.Persistence;
using San9.Application;
using San9.Application.Persistence;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Strategy overlay owner; opening and closing releases its page and texture leases together.</summary>
public partial class SystemMenuPanel : Control
{
    public TextureService Service { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public SlotSaveStore Store { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public Func<string, SaveSlotSummary> CreateSummary { get; init; } = null!;
    public Action<GameSession> ActivateSession { get; init; } = null!;
    public Func<Settings.UserSettings> ReadSettings { get; init; } = null!;
    public Action<Settings.UserSettings> ApplySettings { get; init; } = null!;
    public SaveLoadBrowser Browser { get; } = new();
    private TextureScope? _textures;
    private Control? _page;
    public InformationMenuButton? HoverMenuButton => (_page as FunctionsMenuView)?.HoverButton;
    private Control? _busyOverlay;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;

    public override void _Ready()
    {
        Name = "SystemMenuPanel"; Size = new(1024, 768); ZIndex = 30;
        MouseFilter = MouseFilterEnum.Stop; Hide();
    }
    public void OpenMenu()
    {
        if (_busy) return;
        Browser.Close();
        ReplacePage(null);
        _textures ??= Service.CreateScope(owner: this);
        _textures.RequestPrefixes("information-menu-", "save-load-", "message-");
        Show();
        ReplacePage(new FunctionsMenuView { Textures = _textures, Texts = Texts, Open = Open, OpenEnvironment = OpenEnvironment, Closed = Close });
    }
    private void OpenEnvironment()
    {
        _textures!.RequestPrefixes("environment-", "save-load-", "dialog-");
        ReplacePage(new EnvironmentSettingsView { Textures = _textures, Initial = ReadSettings(), Apply = ApplySettings, Closed = Close });
    }
    public void ShowSettingsLoadFailure(string message)
    {
        _textures ??= Service.CreateScope(owner: this);
        _textures.RequestPrefixes("dialog-"); Show();
        var notice = MapDialog();
        notice.SetNotice($"环境设定读取失败，已使用默认值：\n{message}");
        notice.FocusDefaultButton();
    }
    public async void Open(SaveLoadMode mode)
    {
        if (_busy) return;
        var token = _lifetime.Token;
        _busy = true;
        try
        {
            var catalog = await Task.Run(() => Store.ReadCatalog(Browser.SlotCount), token);
            if (token.IsCancellationRequested) return;
            Browser.SetEntries(catalog);
            Browser.Open(mode);
            ReplacePage(new SaveLoadView { State = Browser, Textures = _textures!, Service = Service, Closed = Close, Requested = Request });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { if (!token.IsCancellationRequested) HideBusy(); }
    }
    public void Close()
    {
        if (_busy) return;
        Browser.Close(); Hide(); ReplacePage(null);
        _textures?.Dispose(); _textures = null;
    }
    private void Request(SaveSlotRequest request)
    {
        if (request.Mode == SaveLoadMode.Save) Save(request);
        else RequestLoad(() => Store.Load(request.Slot));
    }
    public void RequestLoad(Func<GameSession> read)
    {
        if (_busy) return;
        Browser.Close();
        ReplacePage(null);
        _textures ??= Service.CreateScope(owner: this);
        _textures.RequestPrefixes("dialog-");
        Show();
        if (!Session.HasUnsavedChanges) { Load(read); return; }
        var dialog = MapDialog();
        dialog.SetMessage("不储存现在的资料，\n而读取其他的进度。\n您确定吗？");
        dialog.Confirmed += () => Load(read);
        dialog.Dismissed += Close;
        dialog.FocusDefaultButton();
    }
    private GameDialog MapDialog()
    {
        var dialog = new GameDialog { Textures = _textures!, Size = Size };
        dialog.Closed += Close;
        ReplacePage(dialog);
        return dialog;
    }
    public void ShowLoadFailure(string message)
    {
        HideBusy();
        _textures ??= Service.CreateScope(owner: this);
        _textures.RequestPrefixes("dialog-");
        Show();
        var notice = MapDialog();
        notice.SetNotice($"载入失败：\n{message}");
        notice.FocusDefaultButton();
    }
    private async void Load(Func<GameSession> read)
    {
        if (_busy) return;
        var token = _lifetime.Token;
        _busy = true;
        ReplacePage(null);
        bool activating = false;
        try
        {
            var loaded = await Task.Run(read, token);
            if (token.IsCancellationRequested) return;
            activating = true;
            Callable.From(() => { if (!token.IsCancellationRequested) ActivateSession(loaded); }).CallDeferred();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SaveException error)
        {
            if (!token.IsCancellationRequested) ShowLoadFailure(SaveFailureMessages.Describe(error));
            GD.PushWarning(error.ToString());
        }
        finally { if (!token.IsCancellationRequested && !activating) _busy = false; }
    }
    private async void Save(SaveSlotRequest request)
    {
        if (_busy) return;
        var token = _lifetime.Token;
        bool saved = false;
        ShowBusy("正在储存…");
        try
        {
            // Capture on the main thread; the worker only owns immutable bytes and disk I/O.
            long sequence = Session.NextOperationSequence;
            byte[] bytes = SlotSaveStore.Encode(Session, CreateSummary(request.Note));
            await Task.Run(() => Store.Write(request.Slot, bytes), token);
            if (token.IsCancellationRequested) return;
            Session.MarkSaved(sequence);
            saved = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SaveException error)
        {
            if (!token.IsCancellationRequested)
                ((SaveLoadView)_page!).ShowNotice($"储存失败：\n{SaveFailureMessages.Describe(error)}");
            GD.PushWarning(error.ToString());
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                HideBusy();
                if (saved) Close();
            }
        }
    }
    private void ShowBusy(string message)
    {
        _busy = true;
        _busyOverlay = new ColorRect { Color = new(0, 0, 0, .25f), Size = Size, ZIndex = 5, MouseFilter = MouseFilterEnum.Stop };
        var label = new Label { Text = message, Position = new(212, 350), Size = new(600, 64),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeConstantOverride("outline_size", 4);
        _busyOverlay.AddChild(label); AddChild(_busyOverlay);
    }
    private void HideBusy()
    {
        _busy = false;
        if (_busyOverlay is not null) { RemoveChild(_busyOverlay); _busyOverlay.QueueFree(); _busyOverlay = null; }
    }
    private void ReplacePage(Control? page)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = page;
        if (page is not null) AddChild(page);
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (_busy) { GetViewport().SetInputAsHandled(); return; }
        if (_page is EnvironmentSettingsView environment && input is InputEventMouseButton { ButtonIndex: MouseButton.Right } right)
        {
            GetViewport().SetInputAsHandled();
            if (right.Pressed) environment.Back();
            return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        {
            GetViewport().SetInputAsHandled();
            if (_page is SaveLoadView view) view.Back();
            else if (_page is EnvironmentSettingsView settings) settings.Back();
            else Close();
        }
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); _textures?.Dispose(); }
}
