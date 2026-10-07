using Godot;

namespace San9.Client.Godot;

/// <summary>Five-state command pagination arrow, including hover while disabled.</summary>
public partial class CommandPageButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string SkinPrefix { get; init; } = "";
    private int frame = -1;

    public override void _Ready()
    {
        Size = new(56,40);
        FocusMode = FocusModeEnum.None;
        RefreshFrame();
    }
    public override void _Process(double delta) => RefreshFrame();
    private void RefreshFrame()
    {
        int next = Disabled ? IsHovered() ? 4 : 3
            : GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed ? 2 : IsHovered() ? 1 : 0;
        if (frame == next) return;
        frame = next; QueueRedraw();
    }
    public override void _Draw() => DrawTexture(Textures[SkinPrefix + (frame == 0 ? "" : $"-{frame}")], Vector2.Zero);
}
