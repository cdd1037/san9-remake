using Godot;

namespace San9.Client.Godot;

public partial class NumberInputButton : TextureButton
{
    public string Title { get; init; } = "";
    public int FontSize { get; init; } = 16;
    public Color ActiveOutline { get; init; } = Colors.Black;
    private readonly GameTextPresenter _text = new();
    private Label _label = null!;
    private (bool, bool, bool)? _previous;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        bool hovered = IsHovered(), pressed = !Disabled && GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var state = (hovered, pressed, Disabled);
        if (_previous == state) return;
        _previous = state;
        _text.Apply(_label, new(FontSize, new Color(Disabled ? "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8"),
            GameTextEffect.Outline, !Disabled && (hovered || pressed) ? ActiveOutline : Colors.Black));
        _label.Position = pressed ? new(2, 2) : Vector2.Zero;
    }

    public override void _ExitTree() => _text.Dispose();
}
