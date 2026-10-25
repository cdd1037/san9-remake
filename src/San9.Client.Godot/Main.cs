using Godot;
using San9.Client.Godot.Diagnostics;
using San9.Client.Godot.Persistence;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class Main : Node
{
    private const double ViewFadeDuration = .5;
    private Node? _scene;
    private SceneTransitionOverlay _transition = null!;
    private bool _switching;
    private ClientOptions _options = null!;
    private readonly CancellationTokenSource _startup = new();
    public override void _ExitTree()
    {
        _startup.Cancel(); _startup.Dispose();
    }
    private static async Task NextFrame(SceneTree tree, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Frame() => completion.TrySetResult();
        tree.ProcessFrame += Frame;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try { await completion.Task; }
        finally { if (GodotObject.IsInstanceValid(tree)) tree.ProcessFrame -= Frame; }
    }
    public override async void _Ready()
    {
        var token = _startup.Token;
        WindowStartup.Apply(GetWindow());
        var transitionLayer = new CanvasLayer { Name = "SceneTransition", Layer = 100 };
        _transition = new SceneTransitionOverlay { Color = new(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(transitionLayer); transitionLayer.AddChild(_transition);
        var ui = GetNode<Control>("%UiRoot");
        try
        {
            string contentRoot = OS.HasFeature("editor")
                ? Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../local/generated/content")
                : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "content");
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), contentRoot);
            _options = options;
            if (options.PackageRoot is null && options.ScenarioId is null)
            {
                if (options.RequiresWorldVerification || options.TextSmoke)
                    throw new ArgumentException("World verification requires an explicit --scenario or --content-package.");
                await SwitchScene(PrepareOpening, transition: false);
                return;
            }
            await SwitchScene(() => PrepareWorld(options), transition: false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            GD.PushError($"客户端内容加载失败：{e}");
            ui.AddChild(new Label { Text = $"内容加载失败：{e.Message}\n请运行 San9.Tools import-client。", Position = new(24, 120), Size = new(970, 300), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (OS.GetCmdlineUserArgs().Contains("--map-quit")) GetTree().Quit(1);
        }
    }
    private ClientDataPaths DataPaths => new(_options.UserDataRoot is null && OS.HasFeature("template"), OS.GetName(),
        OS.GetExecutablePath(), _options.UserDataRoot ?? ProjectSettings.GlobalizePath("user://"));

    public override void _Input(InputEvent input)
    {
        if (_switching) GetViewport().SetInputAsHandled();
    }

    private async Task<Node> PrepareOpening()
    {
        var opening = new StartMenuPreview { ContentRoot = _options.ContentRoot, DataPaths = DataPaths, InitiallyVisible = false,
            BeginGame = (id, players, settings, damage) => SwitchScene(() => PrepareWorld(
                _options with { PackageRoot = null, ScenarioId = id, ComputerDamageBonusPercent = damage }, players, settings)),
            LoadGame = document => LoadGame(document, transition: true) };
        GetNode<Control>("%UiRoot").AddChild(opening);
        try { await opening.Prepared; return opening; }
        catch { if (IsInstanceValid(opening)) opening.Free(); throw; }
    }

    private Task LoadGame(SlotSaveDocument document, bool transition) => SwitchScene(() => PrepareWorld(_options with
        { PackageRoot = null, ScenarioId = document.Header.Summary.ScenarioId }, save: document), transition);

    private async Task SwitchScene(Func<Task<Node>> prepare, bool transition = true)
    {
        if (_switching) throw new InvalidOperationException("A scene switch is already in progress.");
        _switching = true;
        var token = _startup.Token;
        var previous = _scene;
        var replacement = new SceneReplacementTransition(ViewFadeDuration, ViewFadeDuration);
        Node? incoming = null;
        if (previous is not null) previous.ProcessMode = ProcessModeEnum.Disabled;
        async Task Animate(SceneReplacementPhase phase)
        {
            long sampledAt = System.Diagnostics.Stopwatch.GetTimestamp();
            while (replacement.Phase == phase)
            {
                await NextFrame(GetTree(), token);
                token.ThrowIfCancellationRequested();
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                replacement.Advance(System.Diagnostics.Stopwatch.GetElapsedTime(sampledAt, now).TotalSeconds);
                sampledAt = now;
                _transition.Sample(replacement.Opacity);
            }
        }
        try
        {
            var darkening = transition ? Animate(SceneReplacementPhase.Darkening) : Task.CompletedTask;
            try
            {
                incoming = await prepare();
                incoming.ProcessMode = ProcessModeEnum.Disabled;
            }
            finally { await darkening; }
            if (incoming is StartMenuPreview opening) opening.Activate();
            else
            {
                incoming.GetNode<CanvasLayer>("UiLayer").Show();
                var world = incoming.GetNode<MapWorld>("%MapWorld");
                world.Show(); world.SetProcessInput(true); world.SetProcessUnhandledInput(true);
            }
            _scene = incoming;
            previous?.Free();
            if (transition)
            {
                replacement.RevealPreparedScene();
                await Animate(SceneReplacementPhase.Revealing);
            }
            incoming.ProcessMode = ProcessModeEnum.Inherit;
        }
        catch
        {
            if (incoming is not null && _scene != incoming && IsInstanceValid(incoming)) incoming.Free();
            if (!token.IsCancellationRequested && _scene == previous && IsInstanceValid(previous))
            {
                if (transition && replacement.Phase == SceneReplacementPhase.WaitingForScene)
                {
                    replacement.RevealPreparedScene();
                    await Animate(SceneReplacementPhase.Revealing);
                }
                previous!.ProcessMode = ProcessModeEnum.Inherit;
            }
            throw;
        }
        finally
        {
            if (!token.IsCancellationRequested) _transition.Sample(0);
            _switching = false;
        }
    }

    private async Task<Node> PrepareWorld(ClientOptions options, int[]? players = null, San9.Core.Domain.NewGameSettings? settings = null,
        SlotSaveDocument? save = null)
    {
        var token = _startup.Token;
        var startup = System.Diagnostics.Stopwatch.StartNew();
        var runtime = GD.Load<PackedScene>("res://GameRuntime.tscn").Instantiate();
        var world = runtime.GetNode<MapWorld>("%MapWorld");
        var ui = runtime.GetNode<Control>("%UiRoot");
        var layer = runtime.GetNode<CanvasLayer>("UiLayer");
        layer.Hide();
        world.Hide();
        AddChild(runtime);
        world.SetProcessInput(false);
        world.SetProcessUnhandledInput(false);
        try
        {
            San9.Application.GameSession CreateNewSession(San9.Content.Scenarios.ScenarioInitialState definition)
            {
                ulong seed = options.RequiresWorldVerification ? 190UL
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
                return San9.Application.GameSession.Start(definition, seed, options.ComputerDamageBonusPercent,
                    enableXunStartCheckpoints: true, settings: settings, playerForceIds: players);
            }
            string packageRoot = options.PackageRoot ?? PackageFiles.Resolve(options.ContentRoot,
                (await ContentCatalogFiles.LoadAsync(options.ContentRoot, token)).RequireScenario(options.ScenarioId!).Package);
            var loaded = await Task.Run(async () =>
            {
                var package = await ClientContentFiles.LoadAsync(packageRoot, token);
                double validationMs = startup.Elapsed.TotalMilliseconds;
                token.ThrowIfCancellationRequested();
                if (options.ScenarioId is { } scenarioId && package.Scenario.ScenarioId != scenarioId)
                    throw new InvalidDataException("所选剧本与内容包的身份不符。");
                var initial = options.PlayerForceId is int player ? package.Scenario with { PlayerForceId = player } : package.Scenario;
                if (options.RuntimeSmoke) initial = ClientVerificationScenario.Create(initial);
                var session = save is null ? CreateNewSession(initial) : SlotSaveStore.Restore(save, initial);
                token.ThrowIfCancellationRequested();
                return (Content: package, Definition: initial, Session: session, ValidationMs: validationMs,
                    SessionMs: startup.Elapsed.TotalMilliseconds - validationMs);
            }, token);
            token.ThrowIfCancellationRequested();
            var textures = new TextureService(); textures.Initialize(loaded.Content, loaded.Content.TextureRoot); runtime.AddChild(textures);
            runtime.MoveChild(textures, 0);
            world.Textures = textures;
            using var startupTextures = textures.CreateScope();
            // Prewarm shared UI skins; active pages retain their own texture scopes.
            startupTextures.Request(InterfaceSkin.RequiredUiAssets.Where(k => !TextureGroups.IsPortrait(k)));
            while (!startupTextures.IsReady)
            {
                if (startupTextures.Error is { } error) throw error;
                await NextFrame(GetTree(), token);
                token.ThrowIfCancellationRequested();
            }
            if (options.TextSmoke)
            {
                layer.Show();
                ui.AddChild(new TextVerification { Content = loaded.Content, Service = textures,
                    Output = Path.GetFullPath(Path.Combine(options.ContentRoot,"../verification/text")) });
                return runtime;
            }
            var content = loaded.Content;
            GD.Print($"CLIENT_PREPARE content_ms={loaded.ValidationMs:F2} session_ms={loaded.SessionMs:F2} ui_prepare_ms={startup.Elapsed.TotalMilliseconds - loaded.ValidationMs - loaded.SessionMs:F2}");
            var initial = loaded.Definition;
            var session = loaded.Session;
            var dataPaths = DataPaths;
            var saveStore = new SlotSaveStore(dataPaths.SaveSlots);
            var music = new SceneMusicPlayer { ContentRoot = content.ContentRoot, Tracks = content.Presentation.Sounds };
            runtime.AddChild(music);
            runtime.MoveChild(music, 1);
            world.Session = session;
            var initialPlace = session.View.PlayerRulerServicePlace;
            var initialCell = options.ResolveMapCenter(content.Map.Width, content.Map.Height, initialPlace is null ? null : new(initialPlace.X, initialPlace.Y));
            var center = MapGeometry.Center(initialCell.X, initialCell.Y, content.Map.Height);
            world.FocusWorld(new(center.X, center.Y)); world.SetZoom(options.Zoom);
            world.LoadMap(content.Map, content.TextureRoot, options.Season, options.AnimationTime);
            while (!world.IsVisibleReady)
            {
                if (world.LoadError is { } error) throw new InvalidDataException(error);
                await NextFrame(GetTree(), token);
                token.ThrowIfCancellationRequested();
            }
            GD.Print($"CLIENT_READY total_ms={startup.Elapsed.TotalMilliseconds:F2}");
            ui.AddChild(new MapAnnotations { World = world, Texts = content.Text, UiFont = ui.GetThemeFont("font") });
            var sessions = new SessionUiHost
            {
                World = world, Textures = textures, Content = content, Definition = initial,
                DataPaths = dataPaths, SaveStore = saveStore, Music = music, InitialSession = session,
                CreateNewSession = () => CreateNewSession(initial), InitialSessionKind = save is null ? SessionReplacementKind.Startup : SessionReplacementKind.Load,
                ReturnToOpening = () => SwitchScene(PrepareOpening),
                LoadGame = document => LoadGame(document, transition: false)
            };
            ui.AddChild(sessions);
            if (!options.RequiresWorldVerification)
                ui.AddChild(new Persistence.QuickSaveShortcuts
                {
                    SavePath = dataPaths.QuickSave,
                    CurrentSession = () => sessions.Session, CanSave = () => sessions.CanSave,
                    IsBlocked = () => sessions.IsBlocked, LoadRequested = sessions.RequestQuickLoad
                });
            if (options.RequiresWorldVerification)
                ui.AddChild(new ClientVerification { World = world, Selection = sessions.Selection, Hud = sessions.Hud, Options = options });
            return runtime;
        }
        catch
        {
            if (IsInstanceValid(runtime)) runtime.Free();
            throw;
        }
    }
}
