using Godot;

namespace San9.Client.Godot;

/// <summary>Paints the full viewport behind its owner without changing the owner's local layout.</summary>
public partial class ViewportFill : Node2D
{
    private Color _color;
    public Color Color
    {
        get => _color;
        set { _color = value; QueueRedraw(); }
    }

    public ViewportFill() => ShowBehindParent = true;

    public override void _Ready()
    {
        SetNotifyTransform(true);
        GetViewport().SizeChanged += QueueRedraw;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged) QueueRedraw();
    }

    public override void _Draw()
    {
        // Drawing uses viewport coordinates; centered pages retain their native coordinates.
        DrawSetTransformMatrix(GetGlobalTransformWithCanvas().AffineInverse());
        DrawRect(GetViewportRect(), Color);
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= QueueRedraw;
}
