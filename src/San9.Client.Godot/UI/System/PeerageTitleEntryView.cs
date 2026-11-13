using Godot;

namespace San9.Client.Godot;

/// <summary>Optional full title entry using the existing save-note window skin.</summary>
public partial class PeerageTitleEntryView : FixedPage, IModalInputRouter
{
    public TextureScope Textures { get; init; } = null!;
    public string InitialTitle { get; init; } = "";
    public string Heading { get; init; } = "封号";
    public string Description { get; init; } = "输入完整封号，留空则只显示爵级。";
    public Action<string?> Confirmed { get; init; } = null!;
    public Action Cancelled { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private LineEdit _title = null!;
    private static readonly Vector2 Origin = new(284, 248);
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        var heading = new Label { Text = Heading, Position = Origin + new Vector2(45, 16), Size = new(120, 40),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(heading); _text.Apply(heading, new(24, Colors.White, GameTextEffect.Outline, Colors.Black));
        _title = new LineEdit { Text = InitialTitle, MaxLength = 32, Position = Origin + new Vector2(60, 90), Size = new(304, 32) };
        _title.AddThemeFontSizeOverride("font_size", 24);
        _title.AddThemeColorOverride("font_color", Colors.Black);
        _title.AddThemeColorOverride("caret_color", Colors.Black);
        foreach (string state in new[] { "normal", "focus" })
            _title.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = new("d4c8b4"), ContentMarginLeft = 4 });
        AddChild(_title); _title.GrabFocus();
        _title.TextSubmitted += _ => Submit();
        var body = new Label { Text = Description, Position = Origin + new Vector2(0, 150), Size = new(456, 56),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(body); _text.Apply(body, new(20, Colors.White, GameTextEffect.Outline, Colors.Black));
        Button("yes", "确定", new(180, 212), new(112, 48), Submit);
        Button("no", "返回", new(300, 212), new(112, 48), Cancelled);
        Button("close", "", new(412, 9), new(40, 32), Cancelled);
    }
    private void Submit() { string value = _title.Text.Trim(); Confirmed(value.Length == 0 ? null : value); }
    private void Button(string skin, string title, Vector2 at, Vector2 size, Action action)
    {
        var button = new SaveLoadButton { Textures = Textures, Skin = skin, Title = title, FontSize = 24, Position = Origin + at, Size = size };
        button.Pressed += action; AddChild(button);
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { Cancelled(); GetViewport().SetInputAsHandled(); }
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
