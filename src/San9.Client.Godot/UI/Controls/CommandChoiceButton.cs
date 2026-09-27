using Godot;

namespace San9.Client.Godot;

/// <summary>A persistent two-way choice, independent of transient hover and pressed state.</summary>
public partial class CommandChoiceButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string Title { get; init; } = "";
    private readonly GameTextPresenter _text = new();
    private Label _label = null!;
    private (bool, bool)? _state;
    public override void _Ready()
    {
        ToggleMode = true; FocusMode = FocusModeEnum.None;
        _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label); Refresh();
    }
    public override void _Process(double delta) => Refresh();
    private void Refresh()
    {
        var state = (ButtonPressed, IsHovered());
        if (_state == state) return;
        _state = state;
        bool hovered = IsHovered();
        string color = ButtonPressed ? hovered ? "f0f0f0" : "d8d8d8" : hovered ? "d8d8d8" : "a4a4a4";
        _text.Apply(_label, new(16, new Color(color), GameTextEffect.Outline,
            ButtonPressed || hovered ? new Color("603000") : Colors.Black));
        QueueRedraw();
    }
    public override void _Draw()
    {
        string role = "deployment-choice-" + (ButtonPressed ? "selected" : IsHovered() ? "hover" : "normal");
        var left = Textures[role + "-left"]; var right = Textures[role + "-right"];
        // The option takes the top 32 pixels of the 40-pixel source strip.
        DrawTextureRectRegion(left, new(0, 0, 20, Size.Y), new(0, 0, 20, Size.Y));
        SkinPainter.Tile(this, Textures[role + "-middle"], new(20, 0, Size.X - 40, Size.Y));
        DrawTextureRectRegion(right, new(Size.X - 20, 0, 20, Size.Y), new(0, 0, 20, Size.Y));
    }
    public override void _ExitTree() => _text.Dispose();
}
