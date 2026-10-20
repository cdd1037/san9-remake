using Godot;

namespace San9.Client.Godot.Diagnostics;

public partial class DebugPanel : PanelContainer
{
    public MapWorld World { get; init; } = null!;
    private Label _debugStatus = null!;
    private OptionButton _season = null!;
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
    }
}
