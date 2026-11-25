// See-through map surfaces: glass, and water with the see-through flag set. Lit the same way as
// world.gdshader, which explains it.
shader_type spatial;
render_mode cull_disabled, blend_mix, depth_draw_never, ambient_light_disabled, specular_disabled;

uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D light_map : source_color, filter_linear, repeat_disable;
uniform float opacity = 0.66;

// Same value as world.gdshader, which explains it.
const float OVERBRIGHT = 8.2;

void fragment() {
	vec3 albedo = texture(albedo_map, UV).rgb;
	ALBEDO = albedo;
	EMISSION = albedo * texture(light_map, UV2).rgb * OVERBRIGHT;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	ALPHA = opacity;
}
