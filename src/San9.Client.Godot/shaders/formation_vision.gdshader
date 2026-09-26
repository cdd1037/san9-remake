shader_type canvas_item;
render_mode unshaded;

uniform vec2 view_size = vec2(120.0);
uniform float vision_radius = 3.0;
uniform float vision_angle = 3.141592654;
// Logical pixels per radius unit; resizing the panel does not change the scale.
uniform float ring_spacing = 12.0;
uniform vec4 inner_color : source_color = vec4(0.188, 0.941, 0.251, 0.89);
uniform vec4 middle_color : source_color = vec4(0.122, 0.910, 0.557, 0.67);
uniform vec4 outer_color : source_color = vec4(0.157, 0.812, 0.745, 0.38);
uniform vec4 ring_color : source_color = vec4(0.345, 0.282, 0.780, 0.65);

void fragment() {
    vec2 p = (UV - vec2(0.5)) * view_size;
    float distance = length(p);
    float radius = vision_radius * ring_spacing;
    float aa = max(fwidth(distance) * 0.5, 0.0001);
    float coverage = 1.0 - smoothstep(-aa, aa, distance - radius);

    // Angular distance converted to pixels keeps cut edges smooth at any scale.
    // A full circle bypasses the angular cut so it has no seam at the back.
    if (vision_angle < TAU) {
        float angle = distance > 0.0001 ? abs(atan(p.x, -p.y)) : 0.0;
        float edge = sin(clamp(angle - vision_angle * 0.5, -PI * 0.5, PI * 0.5)) * distance;
        float edge_aa = max(fwidth(edge) * 0.5, 0.0001);
        coverage *= 1.0 - smoothstep(-edge_aa, edge_aa, edge);
    }

    float ring = floor(distance / ring_spacing);
    float tone = clamp(ring / max(vision_radius - 1.0, 1.0), 0.0, 1.0);
    vec4 fill = tone < 0.5 ? mix(inner_color, middle_color, tone * 2.0)
                           : mix(middle_color, outer_color, (tone - 0.5) * 2.0);
    // A slight radial fade retains the translucent appearance of the source art.
    fill.a *= 1.0 - 0.12 * fract(distance / ring_spacing);
    float boundary = max(1.0, floor(distance / ring_spacing + 0.5));
    float stroke = 1.0 - smoothstep(0.65 - aa, 0.65 + aa,
                                   abs(distance - boundary * ring_spacing));
    if (boundary >= vision_radius) { stroke = 0.0; }
    vec4 color = mix(fill, ring_color, stroke);
    color.a *= coverage * (1.0 - smoothstep(radius - 1.5, radius, distance));
    if (vision_radius <= 0.0 || vision_angle <= 0.0) { color.a = 0.0; }
    COLOR = color;
}
