shader_type canvas_item;
render_mode unshaded;

// Clockwise: leadership, politics, intelligence, war. Values use a 115-point scale.
uniform vec4 abilities;
uniform vec4 center_color : source_color = vec4(0.514, 0.482, 0.510, 1.0);
uniform vec4 leadership_color : source_color = vec4(0.922, 0.890, 0.075, 1.0);
uniform vec4 politics_color : source_color = vec4(0.110, 0.522, 0.925, 1.0);
uniform vec4 intelligence_color : source_color = vec4(0.176, 0.784, 0.071, 1.0);
uniform vec4 war_color : source_color = vec4(0.800, 0.145, 0.224, 1.0);

float outside_edge(vec2 p, vec2 a, vec2 b) {
    vec2 edge = b - a;
    float edge_length = length(edge);
    if (edge_length < 0.00001) { return -1000.0; }
    vec2 offset = p - a;
    return (edge.y * offset.x - edge.x * offset.y) / edge_length;
}

void fragment() {
    vec2 p = UV * 164.0 - vec2(80.0);
    vec4 radii = clamp(abilities / 115.0, vec4(0.0), vec4(1.0)) * 80.0;
    vec2 top = vec2(0.0, -radii.x);
    vec2 right = vec2(radii.y, 0.0);
    vec2 bottom = vec2(0.0, radii.z);
    vec2 left = vec2(-radii.w, 0.0);
    float distance_outside = max(max(outside_edge(p, top, right), outside_edge(p, right, bottom)),
                                max(outside_edge(p, bottom, left), outside_edge(p, left, top)));
    // Derivatives keep the transition about one screen pixel wide at any window scale.
    float aa = max(fwidth(distance_outside) * 0.5, 0.0001);
    float coverage = 1.0 - smoothstep(-aa, aa, distance_outside);
    if ((radii.x + radii.z) * (radii.y + radii.w) < 0.00001) { coverage = 0.0; }

    // These weights depend on panel position, never on the current polygon vertices.
    vec2 weight = abs(p) / 80.0;
    vec3 horizontal = p.x < 0.0 ? war_color.rgb : politics_color.rgb;
    vec3 vertical = p.y < 0.0 ? leadership_color.rgb : intelligence_color.rgb;
    vec3 color = center_color.rgb + weight.x * (horizontal - center_color.rgb)
                                 + weight.y * (vertical - center_color.rgb);
    COLOR = vec4(clamp(color, vec3(0.0), vec3(1.0)), coverage);
}
