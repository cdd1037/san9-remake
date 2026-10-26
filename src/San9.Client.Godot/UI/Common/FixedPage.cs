using Godot;

namespace San9.Client.Godot;

/// <summary>Fixed page coordinates with a viewport-sized scene surface. Page instances retain their state.</summary>
public abstract partial class FixedPage : ModalView
{
    protected virtual Color PageBackground => Colors.Transparent;
    public override void _EnterTree()
    {
        base._EnterTree();
        Size = new(ClientLayout.ContentWidth, ClientLayout.ContentHeight);
        var surface = PageSurface.Create(this, PageBackground);
        AddChild(surface, false, InternalMode.Front);
    }
}
