using Godot;
using San9.Content.Scenarios;

namespace San9.Client.Godot;

/// <summary>Presentation geometry only; this does not define detection or combat eligibility.</summary>
public partial class FormationVisionView : Control
{
    public FormationPresentation? Data { get; init; }
    private ShaderMaterial? _material;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (Data is not { VisionRadius: > 0, VisionAngleDegrees: > 0 }) return;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/formation_vision.gdshader") };
        _material.SetShaderParameter("vision_radius", (float)Data.VisionRadius);
        _material.SetShaderParameter("vision_angle", Mathf.DegToRad(Data.VisionAngleDegrees));
        _material.SetShaderParameter("view_size", Size);
        var surface = new ColorRect { Material = _material, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += UpdateViewSize;
    }

    private void UpdateViewSize() => _material?.SetShaderParameter("view_size", Size);

    public override void _ExitTree()
    {
        Resized -= UpdateViewSize;
        _material?.Dispose();
    }
}
