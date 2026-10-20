using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Client.Godot.Diagnostics;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Rendering;

namespace San9.Client.Godot;

public enum SessionReplacementKind { Startup, Load, NewGame }

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
    public MapMessageLog RecordsLog { get; private set; } = null!;
    public StrategyHud Hud { get; private set; } = null!;
    public bool IsSwitching => _pending is not null || _replacement is not null;
    public bool CanSave => !IsSwitching && Hud.CanQuickSave;
    public bool IsBlocked => IsSwitching || Hud.SystemMenu?.IsVisibleInTree() == true;
    private GameSession? _session;
    private Node? _sessionUi;
    private GameSession? _pending;
    private UserSettingsSession _settings = null!;
    private WindowDisplay _windowDisplay = null!;
    private SceneTransitionOverlay _transition = null!;
    private SceneReplacementTransition? _replacement;
    private bool _bound;
    private AutoSaveQueue _autoSaves = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private Label _autoSaveNotice = null!;
    private double _noticeRemaining;

    public override void _Ready()
    {
        _autoSaves = new(SaveStore);
        _autoSaveNotice = new Label { Position = new(16, 112), ZIndex = 1000,
            MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _autoSaveNotice.AddThemeConstantOverride("outline_size", 6);
        _autoSaveNotice.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(_autoSaveNotice);
        _transition = new() { ZIndex = 100, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_transition);
        _settings = new(new UserSettingsStore(ProjectSettings.GlobalizePath(UserSettingsStore.UserDataPath)));
        try { _settings.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException or InvalidDataException) { }
        Music.SetEnabled(_settings.Current.MusicEnabled);
        _windowDisplay = new(GetWindow());
        _windowDisplay.Apply(_settings.Current.FullscreenEnabled, _settings.Current.MouseConfined);
        Activate(InitialSession, SessionReplacementKind.Startup);
    }

    public void Activate(GameSession next, SessionReplacementKind kind)
    {
        if (IsSwitching) return;
        next.EnableXunStartCheckpoints();
        _pending = next;
        if (_sessionUi is not null) _sessionUi.ProcessMode = ProcessModeEnum.Disabled;
        _bound = false;
        if (_sessionUi is not null && kind == SessionReplacementKind.NewGame)
        {
            // Modern scene replacement: retain the old scene until it is fully covered.
            _replacement = new(1, .875);
            return;
        }
        BindPendingScene();
    }

    private void BindPendingScene()
    {
        try
        {
            World.BindSession(_pending!); _bound = true;
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
        if (_noticeRemaining > 0)
        {
            _noticeRemaining -= delta;
            if (_noticeRemaining <= 0) _autoSaveNotice.Hide();
        }
        if (!IsSwitching) return;
        if (_replacement is { } replacement)
        {
            replacement.Advance(delta); _transition.Sample(replacement.Opacity);
            if (replacement.Phase == SceneReplacementPhase.Complete)
            { _replacement = null; return; }
            if (replacement.Phase != SceneReplacementPhase.WaitingForScene) return;
            if (!_bound) BindPendingScene();
        }
        if (_pending is null || !_bound) return;
        if (World.LoadError is { } error) RestoreSession(error);
        else if (World.IsVisibleReady) CompleteSwitch();
    }

    private void RestoreSession(string error)
    {
        _pending = null;
        _replacement = null; _transition.Sample(0); _bound = false;
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
        RecordsLog = new();
        _sessionUi = new Node { Name = "SessionUi" }; AddChild(_sessionUi);
        var backdropHost = new Node(); _sessionUi.AddChild(backdropHost);
        var overlayHost = StrategyOverlayHost.Create();
        Hud = new StrategyHud
        {
            CommandPresentation = Content.Presentation, ContentRoot = Content.ContentRoot,
            SaveStore = SaveStore,
            StrategyReady = SavePendingCheckpoint,
            SaveSummary = note => SaveSlotSummaryFactory.Create(Session, Definition, Content.Text, note),
            ActivateSession = next => Activate(next, SessionReplacementKind.Load),
            StartNewGame = () => Callable.From(() => Activate(CreateNewSession(), SessionReplacementKind.NewGame)).CallDeferred(),
            Music = Music, OverlayHost = overlayHost,
            ReadSettings = () => _settings.Current,
            ApplySettings = draft =>
            {
                _settings.Apply(draft);
                _windowDisplay.Apply(draft.FullscreenEnabled, draft.MouseConfined);
                Music.SetEnabled(draft.MusicEnabled);
            },
            BackdropHost = backdropHost, World = World, Selection = Selection, Session = Session, RecordsLog = RecordsLog,
            Texts = Content.Text, Skin = Content.Skin, Textures = Textures
        };
        _sessionUi.AddChild(overlayHost);
        _sessionUi.AddChild(Hud);
        _sessionUi.MoveChild(overlayHost, _sessionUi.GetChildCount() - 1);
        var debug = new DebugPanel { World = World, Session = Session, Selection = Selection,
            CanIssueOrders = () => Hud.CanIssueOrders };
        _sessionUi.AddChild(debug);
        World.IsInspectionBlocked = p => IsSwitching || (debug.Visible && debug.GetRect().HasPoint(p));
        World.IsInputBlocked = p => IsSwitching || Hud.Overview.IsOpen ||
            (debug.Visible && debug.GetRect().HasPoint(p)) || Hud.IsBlockedPoint(p) || Hud.IsSpeechPoint(p);
        World.IsNavigationBlocked = p => IsSwitching ||
            (debug.Visible && debug.GetRect().HasPoint(p)) || Hud.IsNavigationBlocked(p) || Hud.IsSpeechPoint(p);
        World.CanNavigate = () => !IsSwitching && Hud.CanNavigateMap;
        World.CanIssueOrders = () => !IsSwitching && Hud.CanIssueOrders;
        _pending = null;
        _autoSaveNotice.Hide();
        SavePendingCheckpoint();
        _replacement?.RevealPreparedScene();
        if (!Session.RulesVersionWarnings.IsEmpty)
            Hud.SystemMenu.ShowLoadWarning(string.Join("\n", Session.RulesVersionWarnings.Select(w => w.Message)));
    }

    public void RequestQuickLoad() => Hud.SystemMenu.RequestLoad(() =>
        San9.Application.Persistence.SaveService.Load(ProjectSettings.GlobalizePath(QuickSaveShortcuts.SavePath), Definition));

    private async void SavePendingCheckpoint()
    {
        var session = Session;
        var token = _lifetime.Token;
        try
        {
            long? sequence = await _autoSaves.Enqueue(session, view => SaveSlotSummaryFactory.Create(view, Definition, Content.Text, "旬开始自动存档"));
            if (sequence is null || token.IsCancellationRequested || !ReferenceEquals(session, Session)) return;
            session.MarkSaved(sequence.Value);
        }
        catch (San9.Application.Persistence.SaveException error)
        {
            GD.PushWarning(error.ToString());
            if (token.IsCancellationRequested || !ReferenceEquals(session, Session)) return;
            _autoSaveNotice.Text = $"自动存档失败：{SaveFailureMessages.Describe(error)}";
            _autoSaveNotice.Show();
            _noticeRemaining = 5;
        }
    }

    public override void _Input(InputEvent input)
    {
        // Window input survives session replacement and remains available over modal pages.
        if (_windowDisplay.HandleInput(input)) GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        _lifetime.Cancel(); _lifetime.Dispose();
        _windowDisplay.Dispose();
        _pending = null;
        World.IsInputBlocked = null;
        World.IsNavigationBlocked = null;
        World.CanNavigate = null;
        World.CanIssueOrders = null;
    }
}
