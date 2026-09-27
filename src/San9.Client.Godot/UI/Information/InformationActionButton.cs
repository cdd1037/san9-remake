using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationActionButton : BaseButton
{
    public string? SkinPrefix { get; init; }
    public int? FontSize { get; init; }
    public bool MapToggle { get; init; }
    private readonly GameTextPresenter _text = new();
    public override void _ExitTree() => _text.Dispose();
    public string Title { get; init; } = "";
    public TextureScope Textures { get; init; } = null!;
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        var label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label);
        _text.Apply(label, GameTextStyles.For(GameTextRole.DialogTitle) with { FontSize = FontSize ?? (MapToggle ? 16 : 24) });
    }
    private (bool Disabled, bool Pressed, bool Hovered, bool Focused, DrawMode Mode)? _visual;
    public override void _Process(double delta)
    {
        var state = (Disabled, ButtonPressed, IsHovered(), HasFocus(), GetDrawMode());
        if (_visual == state) return;
        _visual = state; Modulate = Disabled ? new Color("888888") : Colors.White; QueueRedraw();
    }
    public override void _Draw()
    {
        int frame = Disabled ? 3 : ButtonPressed || GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed ? 2 : IsHovered() || HasFocus() ? 1 : 0;
        string suffix = frame == 0 ? "" : $"-{frame}";
        string prefix = SkinPrefix ?? (MapToggle ? "information-action-map-" : "information-action-");
        var left = Textures[prefix + "left" + suffix]; var mid = Textures[prefix + "middle" + suffix]; var right = Textures[prefix + "right" + suffix];
        float l = left.GetWidth(), r = right.GetWidth();
        DrawTextureRect(left, new(0, 0, l, Size.Y), false); DrawTextureRect(mid, new(l, 0, Size.X - l - r, Size.Y), false); DrawTextureRect(right, new(Size.X - r, 0, r, Size.Y), false);
    }
}
