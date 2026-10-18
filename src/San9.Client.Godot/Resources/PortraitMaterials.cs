using Godot;

namespace San9.Client.Godot;

internal static class PortraitMaterials
{
    internal static ShaderMaterial Deceased() => new()
    {
        Shader = new()
        {
            Code =
        "shader_type canvas_item; void fragment() { vec4 c = COLOR; float gray = dot(c.rgb, vec3(0.299, 0.587, 0.114)); COLOR = vec4(vec3(gray), c.a); }"
        }
    };
}
