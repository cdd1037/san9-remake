using Godot;
using San9.Application.Authoring;
using San9.Content.Authoring;
using San9.Client.Godot.Diagnostics;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;
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
    private OpeningMoviePlayback? _openingMovie;
    private ClientOptions _options = null!;
    private ClientDirectories _directories = null!;
    private readonly CancellationTokenSource _startup = new();
    private UiPreferencesSession _preferences = new();
    private WindowDisplay? _windowDisplay;
    private bool _rememberWindow;

    public override void _Process(double delta) => RememberWindow();

    private void RememberWindow()
    {
        if (_rememberWindow && _windowDisplay?.Capture() is { } placement && placement != _preferences.Current.Window)
            _preferences.Update(p => p with { Window = placement });
    }

    public override void _ExitTree()
    {
        RememberWindow();
        _windowDisplay?.Dispose();
        try { _preferences.Save(); }
        catch (Exception error) { GD.PushWarning($"界面偏好保存失败：{error.Message}"); }
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
            _directories = ClientDirectories.Resolve(OS.HasFeature("template"), OS.GetName(), OS.GetExecutablePath(),
                ProjectSettings.GlobalizePath("res://"), ProjectSettings.GlobalizePath("user://"), OS.GetSystemDir(OS.SystemDir.Documents));
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), _directories.ContentRoot);
            _options = options;
            _directories = _directories.ApplyOverrides(options);
            var preferenceStore = new UiPreferencesStore(DataPaths.UiPreferences);
            var preferences = UiPreferences.Default;
            try { preferences = preferenceStore.Load(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException or InvalidDataException)
            { GD.PushWarning($"界面偏好读取失败，使用默认值：{error.Message}"); }
            _preferences = new(preferences, preferenceStore.Save);
            _windowDisplay = new(GetWindow());
            var arguments = OS.GetCmdlineArgs().TakeWhile(argument => argument != "--").ToArray();
            _rememberWindow = DisplayServer.GetName() != "headless" && !GetWindow().IsEmbedded() &&
                !arguments.Any(argument => argument is "--resolution" or "--position" or "--fullscreen" or "-f" or
                    "--maximized" or "-m" or "--windowed" or "-w" or "--embedded" or "--wid");
            if (_rememberWindow)
            {
                if (preferences.Window is { } placement) _windowDisplay.Restore(placement);
                try
                {
                    var settings = new UserSettingsStore(DataPaths.Settings).Load();
                    _windowDisplay.Apply(settings.FullscreenEnabled, false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
                    System.Text.DecoderFallbackException or InvalidDataException)
                { GD.PushWarning($"窗口显示设置读取失败：{error.Message}"); }
            }
            if (options.MapEditor)
            {
                GetWindow().Title = "San9R · 地图编辑器";
                ui.AddChild(new MapEditor { ContentRoot = options.ContentRoot, ScenarioId = options.ScenarioId,
                    Closed = () => GetTree().Quit() });
                return;
            }
            if (options.ScenarioEditor)
            {
                GetWindow().Title = "San9R · 剧本编辑器";
                ui.AddChild(new ScenarioEditor { ContentRoot = options.ContentRoot, ScenarioId = options.ScenarioId,
                    Closed = () => GetTree().Quit() });
                return;
            }
            if (options.SharedDataEditor)
            {
                GetWindow().Title = "San9R · 游戏资料编辑器";
                ui.AddChild(new SharedDataEditor { DataPaths = DataPaths, ContentRoot = options.ContentRoot, ReferenceScenarioId = options.ScenarioId,
                    Closed = () => GetTree().Quit() });
                return;
            }
            if (options.OfficerEditor)
            {
                ui.AddChild(new CreatedOfficerEditor { PortraitPackRoot = DataPaths.PortraitPack, Store = new(DataPaths.CreatedOfficers), ContentRoot = options.ContentRoot,
                    Preferences = _preferences, Closed = () => GetTree().Quit() });
                return;
            }
            await StartInitialContent();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            GD.PushError($"客户端内容加载失败：{e}");
            string? contentRoot = _options?.PackageRoot ?? _directories?.ContentRoot;
            if (contentRoot is not null && _options is not null)
            {
                ui.AddChild(new ContentImportPanel
                {
                    ContentRoot = contentRoot, Failure = e.Message,
                    ImportAvailable = _options.PackageRoot is null && ContentImportService.Available,
                    Continue = StartInitialContent
                });
            }
            else ui.AddChild(new Label { Text = $"启动失败：{e.Message}", Position = new(24, 120),
                Size = new(970, 300), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (OS.GetCmdlineUserArgs().Contains("--map-quit")) GetTree().Quit(1);
        }
    }
    private async Task StartInitialContent()
    {
        if (_options.PackageRoot is null && _options.ScenarioId is null)
        {
            if (_options.RequiresWorldVerification || _options.TextSmoke)
                throw new ArgumentException("World verification requires an explicit --scenario or --content-package.");
            await PlayStartupMovies();
            await SwitchScene(PrepareOpening, transition: false);
        }
        else await StartNewGame(_options, transition: false);
    }

    private ClientDataPaths DataPaths => _directories.UserData;

    public override void _Input(InputEvent input)
    {
        if (_switching && _openingMovie is null) GetViewport().SetInputAsHandled();
    }

    private async Task<PreparedScene> PrepareOpening()
    {
        var opening = new StartMenuPreview { ContentRoot = _options.ContentRoot, DataPaths = DataPaths, Preferences = _preferences, InitiallyVisible = false,
            BeginGame = (id, players, settings, damage, setup) => StartNewGame(
                _options with { PackageRoot = null, ScenarioId = id, ComputerDamageBonusPercent = damage }, players, settings, setup),
            LoadGame = document => LoadGame(document, transition: true) };
        GetNode<Control>("%UiRoot").AddChild(opening);
        try { await opening.Prepared; return new(opening); }
        catch { if (IsInstanceValid(opening)) opening.Free(); throw; }
    }

    private Task LoadGame(SlotSaveDocument document, bool transition) => SwitchScene(() => PrepareWorld(_options with
        { PackageRoot = null, ScenarioId = document.Header.Summary.ScenarioId }, save: document), transition);

    private Task StartNewGame(ClientOptions options, int[]? players = null, San9.Core.Domain.NewGameSettings? settings = null,
        NewGameSetup? setup = null, bool transition = true) => SwitchScene(
            () => PrepareWorld(options, players, settings, setup: setup), transition,
            () => PrepareOpeningMovie(options));

    private async Task<OpeningMoviePlayback?> PrepareOpeningMovie(ClientOptions options)
    {
        var token = _startup.Token;
        var settings = ReadMovieSettings();
        if (!settings.MoviesEnabled) return null;
        string packageRoot = options.PackageRoot ?? PackageFiles.Resolve(options.ContentRoot,
            (await ContentCatalogFiles.LoadAsync(options.ContentRoot, token)).RequireScenario(options.ScenarioId!).Package);
        var manifest = await ScenarioContentFiles.ReadManifestAsync(packageRoot, token);
        var presentation = await ScenarioContentFiles.LoadPresentationAsync(packageRoot, manifest, token);
        if (presentation.OpeningMovie is not { } role) return null;
        string root = ScenarioContentFiles.ResolveResourceRoot(packageRoot, manifest.ResourceRoot);
        var catalog = await MovieCatalogFiles.LoadAsync(root, token);
        return CreateMovieLayer(root, catalog, role, settings);
    }

    private UserSettings ReadMovieSettings()
    {
        try { return new UserSettingsStore(DataPaths.Settings).Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException or InvalidDataException)
        { GD.PushWarning($"影片设置读取失败，使用默认值：{error.Message}"); return UserSettings.Default; }
    }

    private OpeningMoviePlayback? CreateMovieLayer(string root, MovieCatalog catalog, string role, UserSettings settings)
    {
        if (!settings.MoviesEnabled || !catalog.Movies.TryGetValue(role, out string? path)) return null;
        return new OpeningMoviePlayback(this, PackageFiles.Resolve(root, path), settings.SoundEnabled, _startup.Token);
    }

    private async Task PlayStartupMovies()
    {
        var settings = ReadMovieSettings();
        if (!settings.MoviesEnabled) return;
        var token = _startup.Token;
        var catalog = await MovieCatalogFiles.LoadAsync(_options.ContentRoot, token);
        _switching = true;
        _transition.Sample(1);
        try
        {
            foreach (string role in new[] { MovieCatalog.StartupThemeRole, MovieCatalog.StartupOpeningRole })
            {
                token.ThrowIfCancellationRequested();
                using var movie = CreateMovieLayer(_options.ContentRoot, catalog, role, settings);
                _openingMovie = movie;
                if (movie is not null) await movie.Finished;
                _openingMovie = null;
            }
        }
        finally
        {
            _openingMovie = null;
            _switching = false;
            if (!token.IsCancellationRequested) _transition.Sample(0);
        }
    }

    private async Task SwitchScene(Func<Task<PreparedScene>> prepare, bool transition = true, Func<Task<OpeningMoviePlayback?>>? prepareMovie = null)
    {
        if (_switching) throw new InvalidOperationException("A scene switch is already in progress.");
        _switching = true;
        var token = _startup.Token;
        var previous = _scene;
        var replacement = new SceneReplacementTransition(ViewFadeDuration, ViewFadeDuration);
        PreparedScene? prepared = null;
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
                if (prepareMovie is not null)
                {
                    await darkening;
                    _openingMovie = await prepareMovie();
                    if (_openingMovie is not null) _transition.Sample(1);
                }
                incoming = await ScenePreparation.PrepareAsync(_openingMovie?.Presented ?? Task.CompletedTask,
                    _openingMovie?.Finished ?? Task.CompletedTask, async () =>
                    {
                        prepared = await prepare();
                        incoming = prepared.Node;
                        incoming.ProcessMode = ProcessModeEnum.Disabled;
                        return incoming;
                    }, token);
            }
            finally { await darkening; }
            _openingMovie?.Dispose();
            _openingMovie = null;
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
            _openingMovie?.Dispose();
            _openingMovie = null;
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
            prepared?.Dispose();
            _openingMovie?.Dispose();
            _openingMovie = null;
            if (!token.IsCancellationRequested) _transition.Sample(0);
            _switching = false;
        }
    }

    private sealed record PreparedScene(Node Node, IDisposable? PreparationSilence = null) : IDisposable
    {
        public void Dispose() => PreparationSilence?.Dispose();
    }

    private async Task<PreparedScene> PrepareWorld(ClientOptions options, int[]? players = null, San9.Core.Domain.NewGameSettings? settings = null,
        SlotSaveDocument? save = null, NewGameSetup? setup = null)
    {
        setup ??= NewGameSetup.Empty;
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
        IDisposable? preparationSilence = null;
        try
        {
            San9.Application.GameSession CreateNewSession(San9.Content.Scenarios.ScenarioInitialState definition)
            {
                ulong seed = options.RequiresWorldVerification ? 190UL
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
                return San9.Application.GameSession.Start(definition, seed, options.ComputerDamageBonusPercent,
                    enableXunStartCheckpoints: true, settings: settings, playerForceIds: players, setup: setup);
            }
            string packageRoot = options.PackageRoot ?? PackageFiles.Resolve(options.ContentRoot,
                (await ContentCatalogFiles.LoadAsync(options.ContentRoot, token)).RequireScenario(options.ScenarioId!).Package);
            var loaded = await Task.Run(async () =>
            {
                var package = await ClientContentFiles.LoadAsync(packageRoot, token, DataPaths.PortraitPack, DataPaths.TreasureIllustrations);
                double validationMs = startup.Elapsed.TotalMilliseconds;
                token.ThrowIfCancellationRequested();
                if (options.ScenarioId is { } scenarioId && package.Scenario.ScenarioId != scenarioId)
                    throw new InvalidDataException("所选剧本与内容包的身份不符。");
                var initial = options.PlayerForceId is int player ? package.Scenario with { PlayerForceId = player } : package.Scenario;
                if (options.RuntimeSmoke) initial = ClientVerificationScenario.Create(initial);
                var session = save is null ? CreateNewSession(initial) : SlotSaveStore.Restore(save, initial);
                token.ThrowIfCancellationRequested();
                var definitions = session.Definitions;
                if (!definitions.Officers.IsEmpty)
                {
                    package = package with { Definitions = definitions, Text = definitions.AddText(package.Text),
                        Skin = package.Skin with { Roles = definitions.AddPortraits(package.Skin.Roles) } };
                    package.ValidateLinks();
                }
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
                return new(runtime);
            }
            var content = loaded.Content;
            GD.Print($"CLIENT_PREPARE content_ms={loaded.ValidationMs:F2} session_ms={loaded.SessionMs:F2} ui_prepare_ms={startup.Elapsed.TotalMilliseconds - loaded.ValidationMs - loaded.SessionMs:F2}");
            var initial = loaded.Definition;
            var session = loaded.Session;
            var dataPaths = DataPaths;
            var saveStore = new SlotSaveStore(dataPaths.SaveSlots);
            var music = new SceneMusicPlayer { ContentRoot = content.ContentRoot, Tracks = content.Presentation.Sounds };
            preparationSilence = music.Suspend();
            runtime.AddChild(music);
            runtime.MoveChild(music, 1);
            world.Session = session;
            var sessions = new SessionUiHost
            {
                WindowDisplay = _windowDisplay!,
                World = world, Textures = textures, Content = content, Definition = initial,
                DataPaths = dataPaths, SaveStore = saveStore, Music = music, InitialSession = session,
                Preferences = _preferences,
                InitialSessionKind = save is null ? SessionReplacementKind.Startup : SessionReplacementKind.Load,
                ReturnToOpening = () => SwitchScene(PrepareOpening),
                LoadGame = document => LoadGame(document, transition: false)
            };
            ui.AddChild(sessions);
            // Session settings can change the window mode. Let the viewport and HUD
            // anchors update before requesting the first map frame at their final size.
            await NextFrame(GetTree(), token);
            token.ThrowIfCancellationRequested();
            var initialPlace = session.View.PlayerRulerServicePlace;
            var initialCell = options.ResolveMapCenter(content.Map.Width, content.Map.Height, initialPlace is null ? null : new(initialPlace.X, initialPlace.Y));
            var center = MapGeometry.Center(initialCell.X, initialCell.Y, content.Map.Height);
            world.FocusWorld(new(center.X, center.Y)); world.SetZoom(options.Zoom);
            world.LoadMap(content.Map, content.TextureRoot, options.Season, options.AnimationTime);
            ui.AddChild(new MapAnnotations { World = world, Texts = content.Text, UiFont = ui.GetThemeFont("font") });
            while (!world.IsVisibleReady)
            {
                if (world.LoadError is { } error) throw new InvalidDataException(error);
                await NextFrame(GetTree(), token);
                token.ThrowIfCancellationRequested();
            }
            GD.Print($"CLIENT_READY total_ms={startup.Elapsed.TotalMilliseconds:F2}");
            if (!options.RequiresWorldVerification)
                ui.AddChild(new Persistence.QuickSaveShortcuts
                {
                    SavePath = dataPaths.QuickSave,
                    CurrentSession = () => sessions.Session, CanSave = () => sessions.CanSave,
                    IsBlocked = () => sessions.IsBlocked, LoadRequested = sessions.RequestQuickLoad
                });
            if (options.RequiresWorldVerification)
                ui.AddChild(new ClientVerification { World = world, Selection = sessions.Selection, Hud = sessions.Hud, Options = options });
            return new(runtime, preparationSilence);
        }
        catch
        {
            if (IsInstanceValid(runtime)) runtime.Free();
            preparationSilence?.Dispose();
            throw;
        }
    }
}
