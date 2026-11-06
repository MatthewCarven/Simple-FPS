// Solid map surfaces: the texture multiplied by the map's baked light. No lighting is worked out
// at run time, which is what keeps this cheap.
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D light_map : source_color, filter_linear, repeat_disable;

// 2.6 times brighter in display space. Both textures are decoded to linear here, so that's
// 2.6 ^ 2.2. Maps of that era were lit for about x2 plus a player's raised gamma, and plain x2
// looks murky. Keep MapMesher.UnlitValue at 255 / 2.6 so unlit surfaces show their true colours.
const float OVERBRIGHT = 8.2;

void fragment() {
	ALBEDO = texture(albedo_map, UV).rgb * texture(light_map, UV2).rgb * OVERBRIGHT;
}
