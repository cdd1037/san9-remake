using Godot;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

/// <summary>Edits a private draft; cancel and failed writes leave the active preferences intact.</summary>
public partial class EnvironmentSettingsView : Control
{
    public TextureScope Textures { get; init; } = null!;
    public UserSettings Initial { get; init; } = null!;
    public Action<UserSettings> Apply { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private UserSettings _draft = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<(EnvironmentOptionButton Button, Func<bool> Selected)> _options = [];
    private GameDialog _notice = null!;
    public bool HasNotice => _notice?.Visible == true;
    public void Back() { if (HasNotice) _notice.Hide(); else Closed(); }
    private static readonly Vector2 Origin = new(272, 204);
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _draft = Initial;
        LabelAt("设定环境", new(45, 16), new(120, 40), 24);
        string[] labels = ["讯息速度", "进行记录", "BGM", "音效"];
        for (int row = 0; row < labels.Length; row++) LabelAt(labels[row], new(22, 88 + row * 55), new(128, 40), 16);
        string[] speeds = ["普通", "快", "暂停"];
        MessageSpeed[] speedValues = [MessageSpeed.Normal, MessageSpeed.Fast, MessageSpeed.Pause];
        string[] reports = ["详细", "要约", "OFF"];
        ProgressReportMode[] reportValues = [ProgressReportMode.Detailed, ProgressReportMode.Summary, ProgressReportMode.Off];
        for (int i = 0; i < 3; i++)
        {
            var speed = speedValues[i]; var report = reportValues[i];
            Option(speeds[i], i, 0, () => _draft = _draft with { MessageSpeed = speed }, () => _draft.MessageSpeed == speed);
            Option(reports[i], i, 1, () => _draft = _draft with { ProgressReport = report }, () => _draft.ProgressReport == report);
        }
        for (int i = 0; i < 2; i++)
        {
            bool enabled = i == 0;
            Option(enabled ? "ON" : "OFF", i, 2, () => _draft = _draft with { MusicEnabled = enabled }, () => _draft.MusicEnabled == enabled);
            Option(enabled ? "ON" : "OFF", i, 3, () => _draft = _draft with { SoundEnabled = enabled }, () => _draft.SoundEnabled == enabled);
        }
        Button("yes", "执行", new(197, 300), new(112, 48), Confirm);
        Button("no", "中止", new(317, 300), new(112, 48), Closed);
        Button("close", "", new(435, 9), new(40, 32), Closed);
        _notice = new GameDialog { Textures = Textures, Size = Size, Visible = false, ZIndex = 2 };
        _notice.Closed += () => _notice.Hide(); AddChild(_notice);
        Refresh();
    }
    private void Confirm()
    {
        try { Apply(_draft); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _notice.SetNotice($"环境设定保存失败：\n{error.Message}");
            _notice.Show(); _notice.FocusDefaultButton(); return;
        }
        Closed();
    }
    private void Option(string title, int column, int row, Action choose, Func<bool> selected)
    {
        var button = new EnvironmentOptionButton { Textures = Textures, Title = title,
            Position = Origin + new Vector2(158 + column * 96, 88 + row * 55), Size = new(96, 40) };
        button.Pressed += () => { choose(); Refresh(); }; AddChild(button); _options.Add((button, selected));
    }
    private void Refresh()
    {
        foreach (var (button, selected) in _options) { button.Selected = selected(); button.QueueRedraw(); }
    }
    private void LabelAt(string value, Vector2 at, Vector2 size, int fontSize)
    {
        var label = new Label { Text = value, Position = Origin + at, Size = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, new(fontSize, Colors.White, GameTextEffect.Outline, Colors.Black));
    }
    private void Button(string skin, string title, Vector2 at, Vector2 size, Action action)
    {
        var button = new SaveLoadButton { Textures = Textures, Skin = skin, Title = title, FontSize = 24, Position = Origin + at, Size = size };
        button.Pressed += action; AddChild(button);
    }
    public override void _Draw()
    {
        var painter = new SaveLoadSkinPainter(this, Textures);
        painter.Background(new(Origin, new(479, 360))); painter.Frame(new(Origin, new(479, 360)), note: true);
        painter.Title(Origin + new Vector2(13, 12), 184);
        for (int row = 0; row < 4; row++)
            SkinPainter.Strip(this, Textures["environment-label-left"], Textures["environment-label-middle"], Textures["environment-label-right"],
                new(Origin + new Vector2(22, 88 + row * 55), new(128, 40)));
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() => _text.Dispose();
}

public partial class EnvironmentOptionButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string Title { get; init; } = "";
    public bool Selected { get; set; }
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        var label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, new(16, Colors.White, GameTextEffect.Outline, Colors.Black));
        MouseEntered += QueueRedraw; MouseExited += QueueRedraw; FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
        ButtonDown += QueueRedraw; ButtonUp += QueueRedraw;
    }
    public override void _Draw()
    {
        int frame = Selected || GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed ? 2 : IsHovered() || HasFocus() ? 1 : 0;
        string suffix = frame == 0 ? "" : "-" + frame;
        SkinPainter.Strip(this, Textures["environment-choice-left" + suffix], Textures["environment-choice-middle" + suffix],
            Textures["environment-choice-right" + suffix], new(Vector2.Zero, Size));
    }
    public override void _ExitTree() => _text.Dispose();
}
