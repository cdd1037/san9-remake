using Godot;

namespace San9.Client.Godot;

public partial class SaveLoadButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string Skin { get; init; } = "page";
    public string Title { get; init; } = "";
    public int FontSize { get; init; } = 16;
    public int DisabledFrame { get; init; } = 3;
    public bool Selected { get; set; }
    public Color TextColor { get; init; } = new("f0f0f0");
    public Control? Content { get; init; }
    private readonly List<(Label Label, Color Outline, Color? FixedColor)> _contentLabels = [];
    public void StyleContentLabel(Label label, Color outline, Color? fixedColor = null)
    {
        _contentLabels.Add((label, outline, fixedColor));
        _previous = null;
    }
    private readonly GameTextPresenter _text = new();
    private Label? _label;
    private (bool, bool, bool, bool)? _previous;
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label); Refresh();
        if (Content is not null) AddChild(Content);
    }
    public override void _Process(double delta) => Refresh();
    private void Refresh()
    {
        bool pressed = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var state = (Disabled, IsHovered() || HasFocus(), pressed, Selected);
        if (_previous == state) return;
        _previous = state;
        bool highlighted = IsHovered() || HasFocus() || pressed;
        var style = Skin switch
        {
            "yes" => GameTextStyles.Button(GameButtonTextRole.ConfirmYes, highlighted, Selected || pressed, Disabled),
            "no" => GameTextStyles.Button(GameButtonTextRole.ConfirmNo, highlighted, Selected || pressed, Disabled),
            _ => new GameTextStyle(FontSize, Disabled ? new Color("a4a4a4") : TextColor,
                GameTextEffect.Outline, new Color(highlighted ? "603000" : "000000"))
        };
        _text.Apply(_label!, style with { FontSize = FontSize });
        _label!.Position = pressed || Selected ? new(2, 2) : Vector2.Zero;
        if (Content is not null) Content.Position = _label.Position;
        foreach (var (label, outline, fixedColor) in _contentLabels)
            _text.Apply(label, new(label.LabelSettings.FontSize,
                fixedColor ?? (highlighted || Selected ? new Color("f0f0f0") : new Color("d8d8d8")),
                GameTextEffect.Outline, outline));
        QueueRedraw();
    }
    public override void _Draw()
    {
        int frame = Disabled ? DisabledFrame : Selected || GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed ? 2 : IsHovered() || HasFocus() ? 1 : 0;
        DrawTexture(Textures.Get("save-load-" + Skin + (frame == 0 ? "" : $"-{frame}"), this), Vector2.Zero);
    }
    public override void _ExitTree() => _text.Dispose();
}
