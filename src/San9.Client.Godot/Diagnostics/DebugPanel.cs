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
    private readonly GameSession _debugSession = new(WorldState.Create(new(190, 1, XunIndex.Early), 190));
    private long _sequence;
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        { Visible = !Visible; GetViewport().SetInputAsHandled(); }
    }
    public override void _Process(double delta)
    {
        _debugStatus.Text = World.StatusText;
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
        box.AddChild(new Label { Text = "调试 · F3 关闭（不改变初始展示快照）" });
        _debugStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(400, 40) }; box.AddChild(_debugStatus);
        _season = new OptionButton(); foreach (string s in new[] { "春季预览", "夏季预览", "秋季预览", "冬季预览" }) _season.AddItem(s);
        _season.ItemSelected += i => World.PreviewSeason(new[] { "spring", "summer", "autumn", "winter" }[i]); box.AddChild(_season);
        _debugClock = new Label { Text = "独立模拟：190年1月上旬" }; box.AddChild(_debugClock);
        var advance = new Button { Text = "独立模拟推进一旬" }; box.AddChild(advance);
        advance.Pressed += () =>
        {
            var result = _debugSession.Submit(new WaitCommand(Guid.NewGuid(), new ForceId(5), _sequence++));
            if (!result.Accepted) { _debugClock.Text = $"拒绝：{result.Rejection}"; return; }
            var snapshot = _debugSession.AdvanceXun().Snapshot;
            _debugClock.Text = $"独立模拟：{snapshot.Clock.Year}年{snapshot.Clock.Month}月 第{(int)snapshot.Clock.Xun}旬";
        };
    }
}
