using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Client.Godot.Diagnostics;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Client.Godot;

/// <summary>Owns session UI replacement while retaining its last complete presentation.</summary>
public partial class SessionUiHost : Node
{
    public MapWorld World { get; init; } = null!;
    public TextureService Textures { get; init; } = null!;
    public ClientContentPackage Content { get; init; } = null!;
    public ScenarioInitialState Definition { get; init; } = null!;
    public SlotSaveStore SaveStore { get; init; } = null!;
    public SceneMusicPlayer Music { get; init; } = null!;
    public GameSession InitialSession { get; init; } = null!;
    public Func<GameSession> CreateNewSession { get; init; } = null!;
    public GameSession Session => _session ?? InitialSession;
    public StrategySelection Selection { get; private set; } = null!;
    public StrategyHud Hud { get; private set; } = null!;
    public bool IsSwitching => _pending is not null;
    public bool CanSave => !IsSwitching && Hud.CanQuickSave;
    public bool IsBlocked => IsSwitching || Hud.SystemMenu?.IsVisibleInTree() == true;
    private GameSession? _session;
    private Node? _sessionUi;
    private GameSession? _pending;
    private UserSettingsSession _settings = null!;
    private string? _settingsError;

    public override void _Ready()
    {
        _settings = new(new UserSettingsStore(ProjectSettings.GlobalizePath(UserSettingsStore.UserDataPath)));
        try { _settings.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException)
        {
            _settingsError = error.Message;
        }
        Music.SetEnabled(_settings.Current.MusicEnabled);
        Activate(InitialSession);
    }

    public void Activate(GameSession next)
    {
        if (IsSwitching) return;
        _pending = next;
        if (_sessionUi is not null) _sessionUi.ProcessMode = ProcessModeEnum.Disabled;
        try
        {
            World.BindSession(next);
        }
        catch (Exception error)
        {
            RestoreSession(error.Message);
            return;
        }
        // Initial UI subscribes to map readiness; replacements keep the complete old HUD until ready.
        if (_sessionUi is null || World.IsVisibleReady) CompleteSwitch();
    }

    public override void _Process(double delta)
    {
        if (_settingsError is { } settingsError && Hud?.SystemMenu is { } menu)
        {
            _settingsError = null;
            menu.ShowSettingsLoadFailure(settingsError);
        }
        if (!IsSwitching) return;
        if (World.LoadError is { } error) RestoreSession(error);
        else if (World.IsVisibleReady) CompleteSwitch();
    }

    private void RestoreSession(string error)
    {
        _pending = null;
        World.BindSession(Session);
        if (_sessionUi is not null)
        {
            _sessionUi.ProcessMode = ProcessModeEnum.Inherit;
            Hud.SystemMenu.ShowLoadFailure(error);
        }
        GD.PushError($"会话显示切换失败：{error}");
    }

    private void CompleteSwitch()
    {
        _sessionUi?.Free();
        _session = _pending!;
        Selection = new(Session);
        _sessionUi = new Node { Name = "SessionUi" }; AddChild(_sessionUi);
        var backdropHost = new Node(); _sessionUi.AddChild(backdropHost);
        var overlayHost = new StrategyOverlayHost();
        Hud = new StrategyHud
        {
            CommandPresentation = Content.Presentation, ContentRoot = Content.ContentRoot,
            SaveStore = SaveStore,
            SaveSummary = note => SaveSlotSummaryFactory.Create(Session, Definition, Content.Text, note),
            ActivateSession = Activate,
            StartNewGame = () => Callable.From(() => Activate(CreateNewSession())).CallDeferred(),
            Music = Music, OverlayHost = overlayHost,
            ReadSettings = () => _settings.Current,
            ApplySettings = draft =>
            {
                _settings.Apply(draft);
                Music.SetEnabled(draft.MusicEnabled);
            },
            BackdropHost = backdropHost, World = World, Selection = Selection, Session = Session,
            Texts = Content.Text, Skin = Content.Skin, Textures = Textures
        };
        _sessionUi.AddChild(overlayHost);
        _sessionUi.AddChild(Hud);
        _sessionUi.MoveChild(overlayHost, _sessionUi.GetChildCount() - 1);
        var debug = new DebugPanel { World = World, Session = Session, Selection = Selection,
            CanIssueOrders = () => Hud.CanIssueOrders };
        _sessionUi.AddChild(debug);
        World.IsInputBlocked = p => IsSwitching || Hud.Overview.IsOpen ||
            (debug.Visible && debug.GetRect().HasPoint(p)) || Hud.IsBlockedPoint(p);
        World.IsNavigationBlocked = p => IsSwitching ||
            (debug.Visible && debug.GetRect().HasPoint(p)) || Hud.IsNavigationBlocked(p);
        World.CanNavigate = () => !IsSwitching && Hud.CanNavigateMap;
        World.CanIssueOrders = () => !IsSwitching && Hud.CanIssueOrders;
        _pending = null;
    }

    public void RequestQuickLoad() => Hud.SystemMenu.RequestLoad(() =>
        San9.Application.Persistence.SaveService.Load(ProjectSettings.GlobalizePath(QuickSaveShortcuts.SavePath), Definition));

    public override void _ExitTree()
    {
        _pending = null;
        World.IsInputBlocked = null;
        World.IsNavigationBlocked = null;
        World.CanNavigate = null;
        World.CanIssueOrders = null;
    }
}
