using Godot;
using San9.Application;
using San9.Core.Simulation;

namespace San9.Client.Godot.Diagnostics;

public partial class DebugPanel : PanelContainer
{
    public MapWorld World { get; init; } = null!;
    public SessionUiHost Host { get; init; } = null!;
    private Label _debugStatus = null!;
    private OptionButton _season = null!;
    private OptionButton _forces = null!;
    private Button _handOver = null!, _takeOver = null!;
    private Label _controlStatus = null!;
    private int[] _forceIds = [];
    private long _controlRevision = -1;
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        { Visible = !Visible; GetViewport().SetInputAsHandled(); }
    }
    public override void _Process(double delta)
    {
        _debugStatus.Text = World.StatusText + "\n" + World.Textures.Statistics + $"\n地形GPU估算 {World.CacheBytes / 1048576.0:F1} MiB CPU合成源 {World.CpuSourceBytes / 1048576.0:F1} MiB";
        _season.Disabled = World.IsLoading || World.Package is null;
        if (!World.IsLoading) _season.Select(Array.IndexOf(new string?[] { null, "spring", "summer", "autumn", "winter" }, World.PreviewSeasonOverride));
        if (World.LoadError is not null) Visible = true;
        RefreshControl();
    }
    public override void _Ready()
    {
        Position = new(16, 116); Size = new(430, 160); Visible = false;
        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.055f, 0.07f, 0.96f), ContentMarginLeft = 12,
            ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 };
        AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); AddChild(box);
        box.AddChild(new Label { Text = "调试 · F3 关闭" });
        _debugStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(400, 40) }; box.AddChild(_debugStatus);
        _season = new OptionButton(); foreach (string s in new[] { "跟随日期", "春季预览", "夏季预览", "秋季预览", "冬季预览" }) _season.AddItem(s);
        _season.ItemSelected += i => World.PreviewSeason(new string?[] { null, "spring", "summer", "autumn", "winter" }[i]); box.AddChild(_season);
        box.AddChild(new Label { Text = "势力控制（接管时原玩家势力交给电脑）" });
        _forces = new OptionButton(); box.AddChild(_forces);
        var actions = new HBoxContainer(); box.AddChild(actions);
        _handOver = new Button { Text = "交给电脑" }; actions.AddChild(_handOver);
        _takeOver = new Button { Text = "接管电脑势力" }; actions.AddChild(_takeOver);
        _handOver.Pressed += () => ChangeControl(ControllerKind.Automatic);
        _takeOver.Pressed += () => ChangeControl(ControllerKind.Player);
        _controlStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(_controlStatus);
    }

    private void RefreshControl()
    {
        var session = Host.Session;
        if (_controlRevision != session.Snapshot.Revision)
        {
            int? selected = _forces.Selected >= 0 && _forces.Selected < _forceIds.Length ? _forceIds[_forces.Selected] : null;
            _forceIds = session.Snapshot.Forces.Where(f => !f.IsDormant && !f.IsEliminated && !f.IsBarbarian)
                .OrderBy(f => f.Id).Select(f => f.Id).ToArray();
            _forces.Clear();
            foreach (int id in _forceIds)
            {
                var force = session.View.Forces.Single(f => f.Id == id);
                bool player = session.Snapshot.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player);
                _forces.AddItem($"{Host.Content.Text.Resolve(force.NameKey)} · {(player ? "玩家" : "电脑")}");
            }
            int index = Array.IndexOf(_forceIds, selected ?? session.View.PlayerForceId);
            if (_forceIds.Length > 0) _forces.Select(Math.Max(0, index));
            _controlRevision = session.Snapshot.Revision;
        }
        var seat = _forces.Selected >= 0 && _forces.Selected < _forceIds.Length
            ? session.Snapshot.Simulation.Controls.FirstOrDefault(c => c.ForceId == _forceIds[_forces.Selected]) : null;
        bool ready = !Host.IsBlocked && Host.Hud.CanQuickSave && Host.Hud.CanIssueOrders &&
            session.CanChangeForceControl;
        _handOver.Disabled = !ready || seat?.Kind != ControllerKind.Player;
        _takeOver.Disabled = !ready || seat?.Kind != ControllerKind.Automatic;
    }

    private void ChangeControl(ControllerKind kind)
    {
        int id = _forceIds[_forces.Selected];
        var result = Host.Session.Apply(Host.Session.Request(new ForceControlOperation(id, kind)));
        if (result.Disposition == SessionDisposition.Rejected)
        {
            _controlStatus.Text = $"切换失败：{result.Rejection}";
            return;
        }
        // Rebuild presentation and selection from the new perspective after this input callback.
        Callable.From(() => Host.Activate(Host.Session, SessionReplacementKind.Load)).CallDeferred();
    }
}
