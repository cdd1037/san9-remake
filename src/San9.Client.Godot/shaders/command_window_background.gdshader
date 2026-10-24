shader_type canvas_item;

uniform sampler2D background_texture : filter_nearest, repeat_disable;
uniform vec4 background_region;
uniform sampler2D top_left_texture : filter_nearest, repeat_disable;
uniform vec4 top_left_region;
uniform sampler2D top_right_texture : filter_nearest, repeat_disable;
uniform vec4 top_right_region;
uniform sampler2D bottom_left_texture : filter_nearest, repeat_disable;
uniform vec4 bottom_left_region;
uniform vec2 surface_size;
varying vec2 surface_pixel;

void vertex() {
    surface_pixel = VERTEX + vec2(32.0, 64.0);
}

float mask_alpha(sampler2D source, vec4 region, vec2 point) {
    if (any(lessThan(point, vec2(0.0))) || any(greaterThanEqual(point, region.zw))) return 0.0;
    return texture(source, (region.xy + point + vec2(0.5)) / vec2(textureSize(source, 0))).a;
}

void fragment() {
    vec2 pixel = floor(surface_pixel);
    vec2 source_pixel = background_region.xy + mod(pixel, background_region.zw) + vec2(0.5);
    vec4 background = texture(background_texture, source_pixel / vec2(textureSize(background_texture, 0)));
    float cutout = max(mask_alpha(top_left_texture, top_left_region, pixel),
        max(mask_alpha(top_right_texture, top_right_region, pixel - vec2(surface_size.x - 144.0, 0.0)),
            mask_alpha(bottom_left_texture, bottom_left_region, pixel - vec2(0.0, surface_size.y - 160.0))));
    if (cutout > 0.0) background.a = 0.0;
    COLOR *= background;
}
