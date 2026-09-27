using Godot;
using San9.Application.Presentation;
using San9.Client.Godot.Diagnostics;
using San9.Content.Images;
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
        var world = GetNode<MapWorld>("MapWorld");
        var ui = GetNode<Control>("UiLayer/UiRoot");
        try
        {
            var startup = System.Diagnostics.Stopwatch.StartNew();
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), ProjectSettings.GlobalizePath("res://"));
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
            _startupTextures.Request(textures.Roles.Keys.Where(k => !TextureGroups.IsPortrait(k)));
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
            var session = San9.Application.GameSession.Start(initial, 190, options.ComputerDamageBonusPercent);
            world.Session = session;
            var selection = new StrategySelection(session);
            var center = MapGeometry.Center(options.CenterX, options.CenterY);
            world.FocusWorld(new(center.X, center.Y)); world.SetZoom(options.Zoom);
            world.LoadMap(content.Map, content.TextureRoot, options.Season, options.AnimationTime);
            ui.AddChild(new MapAnnotations { World = world, Texts = content.Text, UiFont = ui.GetThemeFont("font") });
            var backdropHost = new Node(); ui.AddChild(backdropHost);
            var overlayHost = new StrategyOverlayHost();
            var hud = new StrategyHud { OverlayHost = overlayHost, BackdropHost = backdropHost, World = world, Selection = selection, Session = session, Texts = content.Text, Skin = content.Skin, Textures = textures };
            ui.AddChild(overlayHost);
            ui.AddChild(hud);
            ui.MoveChild(overlayHost, ui.GetChildCount() - 1);
            var debug = new DebugPanel { World = world, Session = session, Selection = selection, CanIssueOrders = () => hud.CanIssueOrders }; ui.AddChild(debug);
            world.IsInputBlocked = p => hud.Overview.IsOpen || (debug.Visible && debug.GetRect().HasPoint(p)) || hud.IsBlockedPoint(p);
            world.IsNavigationBlocked = p => (debug.Visible && debug.GetRect().HasPoint(p)) || hud.IsNavigationBlocked(p);
            world.CanNavigate = () => hud.CanNavigateMap;
            world.CanIssueOrders = () => hud.CanIssueOrders;
            if (options.RequiresWorldVerification)
                AddChild(new ClientVerification { World = world, Selection = selection, Hud = hud, Options = options });
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
