using Godot;
using San9.Application;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot.Diagnostics;

public partial class DebugPanel : PanelContainer
{
    public MapWorld World { get; init; } = null!;
    private Label _debugStatus = null!, _debugClock = null!;
    private OptionButton _season = null!;
    public GameSession Session { get; init; } = null!;
    public San9.Application.Presentation.StrategySelection Selection { get; init; } = null!;
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        { Visible = !Visible; GetViewport().SetInputAsHandled(); }
    }
    public override void _Process(double delta)
    {
        _debugStatus.Text = World.StatusText + "\n" + World.Textures.Statistics + $"\n地形GPU估算 {World.CacheBytes / 1048576.0:F1} MiB CPU合成源 {World.CpuSourceBytes / 1048576.0:F1} MiB";
        _season.Disabled = World.IsLoading || World.Package is null;
        if (!World.IsLoading) _season.Select(Array.IndexOf(new[] { "spring", "summer", "autumn", "winter" }, World.Season));
        if (World.LoadError is not null) Visible = true;
    }
    public override void _Ready()
    {
        Position = new(16, 116); Size = new(430, 160); Visible = false;
        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.055f, 0.07f, 0.96f), ContentMarginLeft = 12,
            ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 };
        AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); AddChild(box);
        box.AddChild(new Label { Text = "调试 · F3 关闭（内政操作改变当前运行状态）" });
        _debugStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(400, 40) }; box.AddChild(_debugStatus);
        _season = new OptionButton(); foreach (string s in new[] { "春季预览", "夏季预览", "秋季预览", "冬季预览" }) _season.AddItem(s);
        _season.ItemSelected += i => World.PreviewSeason(new[] { "spring", "summer", "autumn", "winter" }[i]); box.AddChild(_season);
        _debugClock = new Label { Text = "选择己方城市后可执行单人内政" }; box.AddChild(_debugClock);
        foreach (var activity in new[] { DomesticActivity.Commerce, DomesticActivity.Cultivation })
        {
            var button = new Button { Text = activity == DomesticActivity.Commerce ? "商业（单人）" : "开垦（单人）" }; box.AddChild(button);
            button.Pressed += () =>
            {
                if (Selection.BuildingId is not int city) { _debugClock.Text="请先选择城市"; return; }
                var officer=Session.Snapshot.Officers.Where(o => o.SettlementId==city && o.OwnerId==Session.View.PlayerForceId && !o.Acted)
                    .OrderByDescending(o => o.Abilities.Politics).ThenBy(o => o.Id).FirstOrDefault();
                if(officer is null) { _debugClock.Text="没有可行动武将"; return; }
                var result=Session.Develop(new(Session.View.PlayerForceId,city,officer.Id,activity));
                _debugClock.Text=result.Accepted ? $"增加 {result.Increment}，支出 {result.Cost}" : $"拒绝：{result.Rejection}";
            };
        }
    }
}
