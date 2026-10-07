using Godot;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot;

public partial class SaveSlotConfirmation : Control
{
    public SaveLoadBrowser State { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Cancelled { get; init; } = null!;
    public Action<string> Confirmed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private LineEdit _note = null!;
    private static readonly Vector2 Origin = new(284, 248);
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        int slot = State.PendingSlot!.Value;
        var title = new Label { Text = "备注", Position = Origin + new Vector2(45, 16), Size = new(120, 40),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(title); _text.Apply(title, new(24, Colors.White, GameTextEffect.Outline, Colors.Black));
        _note = new LineEdit { Text = State.Entry(slot)?.Note ?? "", Position = Origin + new Vector2(60, 90), Size = new(304, 32) };
        _note.AddThemeFontSizeOverride("font_size", 24);
        _note.AddThemeColorOverride("font_color", Colors.Black);
        _note.AddThemeColorOverride("caret_color", Colors.Black);
        foreach (string state in new[] { "normal", "focus" })
            _note.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = new("d4c8b4"), ContentMarginLeft = 4 });
        AddChild(_note); _note.GrabFocus();
        _note.TextSubmitted += _ => Confirmed(_note.Text);
        string prompt = !State.Occupied(slot) ? $"储存资料于第{slot}格。" : $"覆盖第{slot}格的资料。";
        var body = new Label { Text = prompt + "\n您确定吗？", Position = Origin + new Vector2(0, 150), Size = new(456, 56),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("line_spacing", GameTextMetrics.LineSpacing(GetThemeFont("font"), 24));
        AddChild(body); _text.Apply(body, new(24, Colors.White, GameTextEffect.Outline, Colors.Black));
        Button("yes", "是", new(180, 212), new(112, 48), () => Confirmed(_note.Text));
        Button("no", "否", new(300, 212), new(112, 48), Cancelled);
        Button("close", "", new(412, 9), new(40, 32), Cancelled);
    }
    private SaveLoadButton Button(string skin, string title, Vector2 at, Vector2 size, Action action)
    {
        var button = new SaveLoadButton { Textures = Textures, Skin = skin, Title = title, FontSize = 24, Position = Origin + at, Size = size };
        button.Pressed += action; AddChild(button); return button;
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _Draw()
    {
        var painter = new SaveLoadSkinPainter(this, Textures);
        painter.Background(new(Origin, new(456, 272))); painter.Frame(new(Origin, new(456, 272)), note: true);
        painter.Title(Origin + new Vector2(13, 12), 184);
    }
    public override void _ExitTree() => _text.Dispose();
}
