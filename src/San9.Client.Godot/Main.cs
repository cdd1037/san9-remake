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
        var world = GetNode<MapWorld>("%MapWorld");
        var ui = GetNode<Control>("%UiRoot");
        try
        {
            var startup = System.Diagnostics.Stopwatch.StartNew();
            string contentRoot = OS.HasFeature("editor")
                ? Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../local/generated/content")
                : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "content");
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), contentRoot);
            var loaded = await Task.Run(async () =>
            {
                var package = await ClientContentFiles.LoadAsync(options.PackageRoot, token);
                double validationMs = startup.Elapsed.TotalMilliseconds;
                return (Content: package, ValidationMs: validationMs);
            });
            token.ThrowIfCancellationRequested();
            var textures = new TextureService(); textures.Initialize(loaded.Content, loaded.Content.TextureRoot); AddChild(textures);
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
                ui.AddChild(new TextVerification { Content = loaded.Content, Service = textures,
                    Output = Path.GetFullPath(Path.Combine(options.PackageRoot,"../../verification/text")) });
                return;
            }
            var content = loaded.Content;
            GD.Print($"CLIENT_PREPARE content_ms={loaded.ValidationMs:F2} ui_prepare_ms={startup.Elapsed.TotalMilliseconds - loaded.ValidationMs:F2}");
            void OnFirstMapReady() { GD.Print($"CLIENT_READY total_ms={startup.Elapsed.TotalMilliseconds:F2}"); world.MapReady -= OnFirstMapReady; }
            world.MapReady += OnFirstMapReady;
            var initial = content.Scenario;
            if (options.RuntimeSmoke) initial = ClientVerificationScenario.Create(initial);
            San9.Application.GameSession CreateNewSession()
            {
                ulong seed = options.RequiresWorldVerification ? 190UL
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
                return San9.Application.GameSession.Start(initial, seed, options.ComputerDamageBonusPercent, enableXunStartCheckpoints: true);
            }
            var session = CreateNewSession();
            var dataPaths = new Persistence.ClientDataPaths(OS.HasFeature("template"), OS.GetName(),
                OS.GetExecutablePath(), ProjectSettings.GlobalizePath("user://"));
            var saveStore = new Persistence.SlotSaveStore(dataPaths.SaveSlots, initial);
            var music = new SceneMusicPlayer { ContentRoot = content.ContentRoot, Tracks = content.Presentation.Sounds };
            AddChild(music);
            world.Session = session;
            var initialCity = session.View.Settlements.FirstOrDefault(s => session.View.PlayerForceId is int player &&
                s.OwnerId == player && s.Kind == San9.Core.Domain.SettlementKind.City);
            var initialCell = options.ResolveMapCenter(content.Map.Width, content.Map.Height, initialCity is null ? null : new(initialCity.X, initialCity.Y));
            var center = MapGeometry.Center(initialCell.X, initialCell.Y, content.Map.Height);
            world.FocusWorld(new(center.X, center.Y)); world.SetZoom(options.Zoom);
            world.LoadMap(content.Map, content.TextureRoot, options.Season, options.AnimationTime);
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
                AddChild(new ClientVerification { World = world, Selection = sessions.Selection, Hud = sessions.Hud, Options = options });
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
}
