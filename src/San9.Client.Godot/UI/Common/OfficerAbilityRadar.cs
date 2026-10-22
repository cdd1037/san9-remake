using Godot;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Native grid sizes and label bounds relative to the grid origin.</summary>
public sealed record OfficerAbilityRadarLayout(string GridRole, int Diameter,
    Rect2 Leadership, Rect2 War, Rect2 Intelligence, Rect2 Politics)
{
    public static readonly OfficerAbilityRadarLayout Editor = new("editor-radar", 84,
        new(22,-26,40,24), new(-40,30,40,24), new(22,82,40,24), new(84,30,40,24));
    public static readonly OfficerAbilityRadarLayout Detail = new("detail-radar-grid", 164,
        new(57,-28,48,24), new(-39,68,40,24), new(57,164,48,24), new(169,68,40,24));
    public static readonly OfficerAbilityRadarLayout Card = new("detail-leader-radar-grid", 52,
        new(5,-20,40,24), new(-35,13,40,24), new(5,47,40,24), new(51,13,40,24));
    public static readonly OfficerAbilityRadarLayout Leader = new("detail-leader-radar-grid", 52,
        new(6,-20,40,24), new(-34,14,40,24), new(6,46,40,24), new(46,14,40,24));
}

/// <summary>Ability fill, native grid and axis captions share one lifecycle and origin.</summary>
public partial class OfficerAbilityRadar : Control
{
    public OfficerAbilities Abilities { get; init; }
    public TextureScope Textures { get; init; } = null!;
    public OfficerAbilityRadarLayout Layout { get; init; } = OfficerAbilityRadarLayout.Detail;
    private readonly GameTextPresenter _text = new();
    private ShaderMaterial? _material;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new(Layout.Diameter, Layout.Diameter);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/officer_ability_radar.gdshader") };
        _material.SetShaderParameter("abilities", new Vector4(Abilities.Leadership, Abilities.Politics, Abilities.Intelligence, Abilities.War));
        AddChild(new ColorRect { Size = Size, Material = _material, MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new TextureRect { Texture = Textures.Get(Layout.GridRole, this), MouseFilter = MouseFilterEnum.Ignore });
        Axis("统率", Layout.Leadership, "f8f850");
        Axis("武力", Layout.War, "e85440");
        Axis("智力", Layout.Intelligence, "60d868");
        Axis("政治", Layout.Politics, "5098d8");
    }

    private void Axis(string value, Rect2 bounds, string color)
    {
        var label = new Label { Text = value, Position = bounds.Position, Size = bounds.Size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label);
        _text.Apply(label, new GameTextStyle(20, new Color(color), GameTextEffect.Outline, Colors.Black));
    }

    public override void _ExitTree()
    {
        _text.Dispose();
        _material?.Dispose();
    }
}
