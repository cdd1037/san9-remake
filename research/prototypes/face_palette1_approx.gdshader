shader_type canvas_item;
render_mode unshaded;

// P_Face[1] 的研究预览：降饱和、偏棕灰、压低高光。
// 该映射由 P_Face[0] -> P_Face[1] 的 RGB 调色板条目拟合得到，
// 不是产品实现，也不是原版 shader 的反编译结果。

uniform float palette_strength : hint_range(0.0, 1.0) = 1.0;

vec3 approximate_palette_1(vec3 color) {
	vec3 mapped;
	mapped.r = dot(color, vec3(0.374448, 0.151235, 0.327778)) + 0.065637;
	mapped.g = dot(color, vec3(0.328614, 0.119421, 0.324054)) + 0.022642;
	mapped.b = dot(color, vec3(0.285358, 0.088055, 0.316107)) - 0.021956;
	return clamp(mapped, 0.0, 1.0);
}

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	vec3 palette_1_color = approximate_palette_1(source.rgb);
	vec3 output_color = mix(source.rgb, palette_1_color, palette_strength);
	COLOR = vec4(output_color, source.a);
}
