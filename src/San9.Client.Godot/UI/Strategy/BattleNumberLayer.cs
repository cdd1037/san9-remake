using Godot;

namespace San9.Client.Godot;

/// <summary>Draws numeric overlays above effect sprites and below the scene transition.</summary>
public partial class BattleNumberLayer : Node2D
{
    public Action<BattleNumberLayer>? DrawNumbers { get; init; }
    public override void _Draw() => DrawNumbers?.Invoke(this);
}
