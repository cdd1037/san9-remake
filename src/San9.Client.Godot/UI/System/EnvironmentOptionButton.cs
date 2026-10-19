using Godot;

namespace San9.Client.Godot;

public partial class EnvironmentOptionButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string Title { get; init; } = "";
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            if (_label is not null) RefreshAppearance();
        }
    }
    private readonly GameTextPresenter _text = new();
    private Label _label = null!;
    private (bool Disabled, bool Selected, bool Highlighted, bool Pressed)? _previous;
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label); RefreshAppearance();
    }
    public override void _Process(double delta) => RefreshAppearance();
    private void RefreshAppearance()
    {
        bool pressed = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        bool highlighted = !Disabled && (IsHovered() || HasFocus() || pressed);
        var state = (Disabled, Selected, highlighted, pressed);
        if (_previous == state) return;
        _previous = state;
        Modulate = Disabled ? new Color(0.7f, 0.7f, 0.7f) : Colors.White;
        _text.Apply(_label, new(14, new Color(Disabled ? "a4a4a4" : highlighted ? "f0f0f0" : "d8d8d8"),
            GameTextEffect.Outline, new Color(highlighted ? "603000" : "000000")));
        _label.Position = Selected || pressed ? new(2, 2) : Vector2.Zero;
        QueueRedraw();
    }
    public override void _Draw()
    {
        // The source's fourth frame is fully transparent, so disabled options retain their visible selection frame.
        int frame = Selected || (!Disabled && GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed) ? 2
            : !Disabled && (IsHovered() || HasFocus()) ? 1 : 0;
        string suffix = frame == 0 ? "" : "-" + frame;
        SkinPainter.Strip(this, Textures.Get("environment-choice-left" + suffix, this), Textures.Get("environment-choice-middle" + suffix, this),
            Textures.Get("environment-choice-right" + suffix, this), new(Vector2.Zero, Size));
    }
    public override void _ExitTree() => _text.Dispose();
}
