using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private Control _topLeft = null!, _topRight = null!, _sidebarMotion = null!, _bottomRight = null!, _center = null!, _bottomBar = null!;
    private readonly List<StrategyButton> _hudButtons = [];

    private void BuildLayout()
    {
        var layout = GD.Load<PackedScene>("res://UI/Strategy/StrategyHudLayout.tscn").Instantiate<Control>();
        AddChild(layout);
        _topLeft = layout.GetNode<Control>("%TopLeft");
        _topRight = layout.GetNode<Control>("%TopRight");
        _sidebarMotion = layout.GetNode<Control>("%SidebarMotion");
        _bottomRight = layout.GetNode<Control>("%BottomRight");
        _center = layout.GetNode<Control>("%Center");
        _bottomBar = layout.GetNode<Control>("%BottomBar");
        layout.Resized += QueueRedraw;
    }

    private Vector2 GroupPosition(Control group) => GetGlobalTransform().AffineInverse() * group.GlobalPosition;
    private Rect2 HudRect(Control control) => new(GroupPosition(control), control.Size);

    private IReadOnlyList<OverlayBounds> MapObstacles()
    {
        var result = new List<OverlayBounds>();
        void Add(Rect2 rect)
        {
            rect = rect.Intersection(new(Vector2.Zero, Size));
            if (rect.HasArea()) result.Add(new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y));
        }
        if (!_built) return result;
        foreach (var (asset, position) in VisibleFrames())
        {
            var rect = Textures.HitBounds(Textures.Roles[asset]);
            rect.Position += position;
            Add(rect);
        }
        foreach (var button in _hudButtons.Where(button => button.IsVisibleInTree())) Add(HudRect(button));
        Add(HudRect(_minimap!));
        if (_rosterList.IsVisibleInTree()) Add(HudRect(_rosterList));
        foreach (var rect in _executionPanels.OccupiedAreas()) Add(rect);
        Add(new(0, Size.Y - 32, Size.X, 32));
        return result;
    }
}
