using Godot;

namespace San9.Client.Godot;

/// <summary>Viewport background and input surface behind fixed content, with one shade per visible page stack.</summary>
public partial class PageSurface : Control
{
    private Control _content = null!;
    private Color _color;
    private ColorRect _background = null!;
    public static PageSurface Create(Control content, Color color)
    {
        var surface = GD.Load<PackedScene>("res://UI/Common/PageSurface.tscn").Instantiate<PageSurface>();
        surface._content = content; surface._color = color;
        return surface;
    }
    public override void _Ready()
    {
        _background = GetNode<ColorRect>("%Background");
        GetViewport().SizeChanged += Layout;
        _content.Resized += Layout;
        Layout();
        _Process(0);
    }
    private void Layout()
    {
        var size = GetViewportRect().Size;
        // Controls snap their transforms independently. Keep the centered page and
        // its inverse surface offset integral so they cancel at the viewport edge.
        _content.GlobalPosition = ((size - _content.Size) / 2).Floor();
        GlobalPosition = Vector2.Zero;
        Size = size.Ceil();
    }
    public override void _Process(double delta)
    {
        bool inherited = false;
        for (Node? parent = _content.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is OverlayPage page && page.BackgroundColor.A > 0) inherited = true;
            foreach (var surface in parent.GetChildren(includeInternal: true).OfType<PageSurface>())
                if (surface.IsVisibleInTree() && surface._color.A > 0) inherited = true;
        }
        _background.Color = _color.A < 1 && inherited ? Colors.Transparent : _color;
    }
    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        _content.Resized -= Layout;
    }
}
