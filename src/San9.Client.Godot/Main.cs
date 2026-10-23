using Godot;
using San9.Client.Godot.Diagnostics;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class Main : Node
{
    private TextureScope? _startupTextures;
    private readonly CancellationTokenSource _startup = new();
    public override void _ExitTree()
    {
        _startup.Cancel(); _startup.Dispose(); _startupTextures?.Dispose();
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
        var ui = GetNode<Control>("%UiRoot");
        try
        {
            string contentRoot = OS.HasFeature("editor")
                ? Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../local/generated/content")
                : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "content");
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), contentRoot);
            if (options.PackageRoot is null && options.ScenarioId is null)
            {
                if (options.RequiresWorldVerification || options.TextSmoke)
                    throw new ArgumentException("World verification requires an explicit --scenario or --content-package.");
                StartMenuPreview opening = null!;
                opening = new StartMenuPreview { ContentRoot = options.ContentRoot,
                    BeginGame = async (id, players, settings, damage) =>
                    {
                        await StartWorld(options with { ScenarioId = id, ComputerDamageBonusPercent = damage }, players, settings);
                        opening.QueueFree();
                    } };
                ui.AddChild(opening);
                return;
            }
            await StartWorld(options);
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
    private async Task StartWorld(ClientOptions options, int[]? players = null, San9.Core.Domain.NewGameSettings? settings = null)
    {
        var token = _startup.Token;
        var startup = System.Diagnostics.Stopwatch.StartNew();
        var runtime = GD.Load<PackedScene>("res://GameRuntime.tscn").Instantiate();
        var world = runtime.GetNode<MapWorld>("%MapWorld");
        var ui = runtime.GetNode<Control>("%UiRoot");
        var layer = runtime.GetNode<CanvasLayer>("UiLayer");
        layer.Hide();
        AddChild(runtime);
        world.SetProcessInput(false);
        world.SetProcessUnhandledInput(false);
        try
        {
            string packageRoot = options.PackageRoot ?? PackageFiles.Resolve(options.ContentRoot,
                (await ContentCatalogFiles.LoadAsync(options.ContentRoot, token)).RequireScenario(options.ScenarioId!).Package);
            var loaded = await Task.Run(async () =>
            {
                var package = await ClientContentFiles.LoadAsync(packageRoot, token);
                double validationMs = startup.Elapsed.TotalMilliseconds;
                return (Content: package, ValidationMs: validationMs);
            });
            token.ThrowIfCancellationRequested();
            var textures = new TextureService(); textures.Initialize(loaded.Content, loaded.Content.TextureRoot); runtime.AddChild(textures);
            runtime.MoveChild(textures, 0);
            world.Textures = textures;
            _startupTextures = textures.CreateScope();
            // Keep shared UI skins resident; portraits and map resources remain demand-loaded.
            _startupTextures.Request(InterfaceSkin.RequiredUiAssets.Where(k => !TextureGroups.IsPortrait(k)));
            while (!_startupTextures.IsReady)
            {
                if (_startupTextures.Error is { } error) throw error;
                await NextFrame(GetTree(), token);
                token.ThrowIfCancellationRequested();
            }
            if (options.TextSmoke)
            {
                layer.Show();
                ui.AddChild(new TextVerification { Content = loaded.Content, Service = textures,
                    Output = Path.GetFullPath(Path.Combine(options.ContentRoot,"../verification/text")) });
                return;
            }
            var content = loaded.Content;
            GD.Print($"CLIENT_PREPARE content_ms={loaded.ValidationMs:F2} ui_prepare_ms={startup.Elapsed.TotalMilliseconds - loaded.ValidationMs:F2}");
            var initial = options.PlayerForceId is int player ? content.Scenario with { PlayerForceId = player } : content.Scenario;
            if (options.RuntimeSmoke) initial = ClientVerificationScenario.Create(initial);
            San9.Application.GameSession CreateNewSession()
            {
                ulong seed = options.RequiresWorldVerification ? 190UL
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
                return San9.Application.GameSession.Start(initial, seed, options.ComputerDamageBonusPercent, enableXunStartCheckpoints: true, settings: settings, playerForceIds: players);
            }
            var session = CreateNewSession();
            var dataPaths = new Persistence.ClientDataPaths(options.UserDataRoot is null && OS.HasFeature("template"), OS.GetName(),
                OS.GetExecutablePath(), options.UserDataRoot ?? ProjectSettings.GlobalizePath("user://"));
            var saveStore = new Persistence.SlotSaveStore(dataPaths.SaveSlots, initial);
            var music = new SceneMusicPlayer { ContentRoot = content.ContentRoot, Tracks = content.Presentation.Sounds };
            runtime.AddChild(music);
            runtime.MoveChild(music, 1);
            world.Session = session;
            var initialCity = session.View.Settlements.FirstOrDefault(s => session.View.PlayerForceId is int player &&
                s.OwnerId == player && s.Kind == San9.Core.Domain.SettlementKind.City);
            var initialCell = options.ResolveMapCenter(content.Map.Width, content.Map.Height, initialCity is null ? null : new(initialCity.X, initialCity.Y));
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
                CreateNewSession = CreateNewSession
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
            layer.Show();
            world.SetProcessInput(true);
            world.SetProcessUnhandledInput(true);
        }
        catch
        {
            _startupTextures?.Dispose(); _startupTextures = null;
            if (IsInstanceValid(runtime)) runtime.Free();
            throw;
        }
    }
}
