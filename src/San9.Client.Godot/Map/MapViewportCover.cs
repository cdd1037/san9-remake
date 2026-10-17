using Godot;

namespace San9.Client.Godot;

/// <summary>Covers only the area not represented by the retained complete map frame.</summary>
public partial class MapViewportCover : Control
{
    private MapWorld _world = null!;

    public override void _Ready()
    {
        _world = GetNode<MapWorld>("%MapWorld");
        _world.ViewportPresentationChanged += QueueRedraw;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var visible = _world.PresentedViewportBounds;
        Cover(new(0, 0, Size.X, visible.Position.Y));
        Cover(new(0, visible.End.Y, Size.X, Size.Y - visible.End.Y));
        Cover(new(0, visible.Position.Y, visible.Position.X, visible.Size.Y));
        Cover(new(visible.End.X, visible.Position.Y, Size.X - visible.End.X, visible.Size.Y));
    }

    private void Cover(Rect2 bounds)
    {
        if (bounds.HasArea()) DrawRect(bounds, Colors.Black);
    }

    public override void _ExitTree() => _world.ViewportPresentationChanged -= QueueRedraw;
}
