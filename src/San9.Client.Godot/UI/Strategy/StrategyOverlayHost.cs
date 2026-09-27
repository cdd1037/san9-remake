using Godot;

namespace San9.Client.Godot;

public partial class StrategyOverlayHost : Node
{
    public bool HasModal => GetChildren().OfType<Control>().Any(view => view.IsVisibleInTree());
}
