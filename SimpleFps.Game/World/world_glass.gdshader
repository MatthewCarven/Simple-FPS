// See-through map surfaces: glass, and water with the see-through flag set.
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D light_map : source_color, filter_linear, repeat_disable;
uniform float opacity = 0.66;

// Same value as world.gdshader, which explains it.
const float OVERBRIGHT = 8.2;

void fragment() {
	ALBEDO = texture(albedo_map, UV).rgb * texture(light_map, UV2).rgb * OVERBRIGHT;
	ALPHA = opacity;
}
