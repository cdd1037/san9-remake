using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class InformationMenuButton : BaseButton
{
    public string SkinPrefix { get; init; } = "information-menu-button";
    private string _title = "";
    public string Title
    {
        get => _title;
        set { _title = value; if (_label is not null) _label.Text = value; }
    }
    public string HintText { get; init; } = "";
    public Func<bool, bool, bool, GameTextStyle> TextStyle { get; init; } = GameTextStyles.InformationMenu;
    public TextureScope Textures { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private Label _label = null!;
    private (bool Hovered, bool Pressed, bool Disabled)? _state;
    public int VisualFrame { get; private set; }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        RefreshVisual();
    }

    public override void _Process(double delta) => RefreshVisual();
    public void RefreshVisual()
    {
        bool pressed = !Disabled && GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var state = (IsHovered(), pressed, Disabled);
        if (_state == state) return;
        _state = state;
        _text.Apply(_label, TextStyle(state.Item1, pressed, Disabled));
        _label.Position = pressed ? new(2, 2) : Vector2.Zero;
        VisualFrame = ButtonVisualState.Frame(false, state.Item1, pressed, Disabled);
        QueueRedraw();
    }

    public override void _Draw()
    {
        string suffix = VisualFrame == 0 ? "" : $"-{VisualFrame}";
        var left = Textures[SkinPrefix + "-left" + suffix];
        var middle = Textures[SkinPrefix + "-middle" + suffix];
        var right = Textures[SkinPrefix + "-right" + suffix];
        DrawTexture(left, Vector2.Zero);
        float end = Size.X - right.GetWidth();
        for (float x = left.GetWidth(); x < end; x += middle.GetWidth())
        {
            var size = new Vector2(Math.Min(middle.GetWidth(), end - x), middle.GetHeight());
            DrawTextureRectRegion(middle, new(new(x, 0), size), new(Vector2.Zero, size));
        }
        DrawTexture(right, new(end, 0));
    }

    public override void _ExitTree() { _text.Dispose(); }
}
