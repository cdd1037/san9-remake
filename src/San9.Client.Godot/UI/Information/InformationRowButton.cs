using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationRowButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public bool Selected { get; set; }
    public override void _Ready() { FocusMode = FocusModeEnum.All; MouseFilter = MouseFilterEnum.Pass; }
    private (bool Selected, bool Focused, bool Hovered)? _visual;
    public override void _Process(double delta)
    {
        var state = (Selected, HasFocus(), IsHovered());
        if (_visual == state) return;
        _visual = state; QueueRedraw();
    }
    public override void _Draw()
    {
        if (!Selected && !HasFocus() && !IsHovered()) return;
        DrawTextureRect(Textures["roster-row-left-1"], new(0, 4, 8, 16), false);
        DrawTextureRect(Textures["roster-row-middle-1"], new(8, 4, Size.X - 16, 16), false);
        DrawTextureRect(Textures["roster-row-right-1"], new(Size.X - 8, 4, 8, 16), false);
    }
}
