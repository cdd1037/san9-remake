using Godot;

namespace San9.Client.Godot;

/// <summary>Owns one centered page, its full-viewport background and independent pointer blocker.</summary>
public partial class OverlayPage : Control
{
    private Control _content = null!;
    private Color _backgroundColor;
    private bool _blocksInput;
    public Color BackgroundColor => _backgroundColor;

    public static OverlayPage Create(Control content, Color background, bool blocksInput = true)
    {
        var page = GD.Load<PackedScene>("res://UI/Common/OverlayPage.tscn").Instantiate<OverlayPage>();
        page._content = content;
        page._backgroundColor = background;
        page._blocksInput = blocksInput;
        return page;
    }

    public override void _Ready()
    {
        var background = GetNode<ColorRect>("%Background");
        var blocker = GetNode<Control>("%InputBlocker");
        var contentRoot = GetNode<Control>("%ContentRoot");
        background.Color = _backgroundColor;
        blocker.MouseFilter = _blocksInput ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        // Services and page data are supplied by the caller before the page enters the tree.
        contentRoot.AddChild(_content);
    }
}
