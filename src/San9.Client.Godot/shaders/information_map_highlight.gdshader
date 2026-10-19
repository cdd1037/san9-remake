shader_type canvas_item;

uniform sampler2D target_mask : filter_nearest, repeat_disable;
uniform vec3 hovered_code = vec3(-1.0);

void fragment() {
    vec4 territory = texture(TEXTURE, UV);
    vec4 target = texture(target_mask, UV);
    if (target.a > 0.0 && all(equal(round(target.rgb * 255.0), hovered_code))) {
        territory.rgb = round(mix(territory.rgb, vec3(1.0), 0.45) * 255.0) / 255.0;
    }
    COLOR = territory;
}
