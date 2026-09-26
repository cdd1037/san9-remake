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
    public override void _ExitTree() => _startupTextures?.Dispose();
    public override async void _Ready()
    {
        WindowStartup.Apply(GetWindow());
        var world = GetNode<MapWorld>("MapWorld");
        var ui = GetNode<Control>("UiLayer/UiRoot");
        try
        {
            var startup = System.Diagnostics.Stopwatch.StartNew();
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), ProjectSettings.GlobalizePath("res://"));
            var loaded = await Task.Run(async () =>
            {
                var package = await ClientContentFiles.LoadAsync(options.PackageRoot);
                double validationMs = startup.Elapsed.TotalMilliseconds;
                return (Content: package, ValidationMs: validationMs);
            });
            var textures = new TextureService(); textures.Initialize(loaded.Content, loaded.Content.TextureRoot); AddChild(textures);
            world.Textures = textures;
            _startupTextures = textures.CreateScope();
            // Keep shared UI skins resident; portraits and map resources remain demand-loaded.
            _startupTextures.Request(textures.Roles.Keys.Where(k => !TextureGroups.IsPortrait(k)));
            while (!_startupTextures.IsReady)
            {
                if (_startupTextures.Error is { } error) throw error;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
            // TEMP: local visual preview requested by the user; remove after acceptance.
            var previewHome = initial.Settlements.Single(s => s.Id == 15);
            int previewId = initial.Units.Select(u => u.Id).DefaultIfEmpty(-1).Max() + 1;
            int previewCommander = initial.Forces.Single(f => f.Id == previewHome.OwnerId).LeaderId!.Value;
            initial = initial with
            {
                Officers = initial.Officers.Select(o => o.State.Id == previewCommander ?
                    o with { State = o.State with { SettlementId = null, RegionId = null, IsCommander = false, UnitId = previewId } } : o).ToArray(),
                Units = initial.Units.Append(new San9.Core.Domain.UnitState(
                    previewId,
                    previewHome.OwnerId!.Value,
                    San9.Core.Domain.MapCoordinates.Center(new(previewHome.X + 1, previewHome.Y + 5)), 18000)
                {
                    CommanderId = previewCommander,
                    MemberRanks = System.Collections.Immutable.ImmutableDictionary<int, San9.Core.Domain.BattleRank>.Empty
                        .Add(previewCommander, San9.Core.Domain.BattleRank.Front),
                    FormationId = 0, NavalFormationId = initial.Military!.BasicNavalFormationId, Morale = 85, CorpsId = previewHome.CorpsId, ServicePlaceId = previewHome.Id,
                    OrderClass = San9.Core.Domain.UnitOrderClass.Military,
                    Condition = San9.Core.Domain.UnitCondition.Normal
                }).ToArray()
            };
            // A controlled, in-memory boundary fixture for GPU verification; never changes the content package.
            if (options.RuntimeSmoke) initial = initial with { Settlements = initial.Settlements.Select(s => s.Id == 15 ? s with { Commerce = 300, Cultivation = 150 } : s).ToArray() };
            var session = San9.Application.GameSession.Start(initial, 190, options.ComputerDamageBonusPercent);
            world.Session = session;
            var selection = new StrategySelection(session);
            var center = MapGeometry.Center(options.CenterX, options.CenterY);
            world.FocusWorld(new(center.X, center.Y)); world.SetZoom(options.Zoom);
            world.LoadMap(content.Map, content.TextureRoot, options.Season, options.AnimationTime);
            ui.AddChild(new MapAnnotations { World = world, Texts = content.Text, UiFont = ui.GetThemeFont("font") });
            var hud = new StrategyHud { World = world, Selection = selection, Session = session, Texts = content.Text, Skin = content.Skin, Textures = textures };
            ui.AddChild(hud);
            var debug = new DebugPanel { World = world, Session = session, Selection = selection, CanIssueOrders = () => hud.CanIssueOrders }; ui.AddChild(debug);
            world.IsInputBlocked = p => hud.Overview.IsOpen || (debug.Visible && debug.GetRect().HasPoint(p)) || hud.IsBlockedPoint(p);
            world.CanIssueOrders = () => hud.CanIssueOrders;
            if (options.RequiresWorldVerification)
                AddChild(new ClientVerification { World = world, Selection = selection, Hud = hud, Options = options });
        }
        catch (Exception e)
        {
            GD.PushError($"客户端内容加载失败：{e}");
            ui.AddChild(new Label { Text = $"内容加载失败：{e.Message}\n请运行 San9.Tools import-client。", Position = new(24, 120), Size = new(970, 300), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (OS.GetCmdlineUserArgs().Contains("--map-quit")) GetTree().Quit(1);
        }
    }
}
