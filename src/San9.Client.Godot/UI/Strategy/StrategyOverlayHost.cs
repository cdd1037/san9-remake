using Godot;

namespace San9.Client.Godot;

public partial class StrategyOverlayHost : Control
{
    public static StrategyOverlayHost Create() =>
        GD.Load<PackedScene>("res://UI/Strategy/StrategyOverlayHost.tscn").Instantiate<StrategyOverlayHost>();

    public bool HasModal => GetChildren().OfType<Control>().Any(view => view.IsVisibleInTree());
}
