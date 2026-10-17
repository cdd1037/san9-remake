using Godot;

namespace San9.Client.Godot;

public abstract partial class HudDropdown : Control
{
    public Control AnchorControl { get; init; } = null!;
    protected abstract Vector2 MenuSize { get; }
    protected virtual bool AlignRight => false;
    protected Rect2 MenuBounds { get; private set; }
    public override void _Process(double delta)
    {
        Size = GetViewportRect().Size;
        var anchor = GetGlobalTransform().AffineInverse() * AnchorControl.GlobalPosition;
        float x = AlignRight ? anchor.X + AnchorControl.Size.X - MenuSize.X + 15 : anchor.X;
        var next = new Rect2(new(Math.Clamp(x, 0, Math.Max(0, Size.X - MenuSize.X)),
            Math.Clamp(anchor.Y + AnchorControl.Size.Y, 0, Math.Max(0, Size.Y - MenuSize.Y))), MenuSize);
        if (next == MenuBounds) return;
        var shift = next.Position - MenuBounds.Position;
        foreach (var button in GetChildren().OfType<InformationMenuButton>()) button.Position += shift;
        MenuBounds = next;
        QueueRedraw();
    }

    protected void DrawFrame(TextureScope textures) => SkinPainter.InformationMenu(this, textures, MenuBounds);
}
