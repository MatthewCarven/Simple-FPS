// Solid map surfaces: the texture multiplied by the map's baked light, given out as emission so it
// shows exactly as baked. Small lights placed at run time (teleporter pads now, energy rounds and
// blasts later) add to that, lighting the texture underneath. Ambient light and shine are off, so
// a surface with no such light near looks just as it did when this shader was unshaded. No shadows
// or other lighting are worked out at run time, which is what keeps this cheap.
shader_type spatial;
render_mode cull_back, ambient_light_disabled, specular_disabled;

uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D light_map : source_color, filter_linear, repeat_disable;

// 2.6 times brighter in display space. Both textures are decoded to linear here, so that's
// 2.6 ^ 2.2. Maps of that era were lit for about x2 plus a player's raised gamma, and plain x2
// looks murky. Keep MapMesher.UnlitValue at 255 / 2.6 so unlit surfaces show their true colours.
const float OVERBRIGHT = 8.2;

void fragment() {
	vec3 albedo = texture(albedo_map, UV).rgb;
	ALBEDO = albedo;
	EMISSION = albedo * texture(light_map, UV2).rgb * OVERBRIGHT;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
}
