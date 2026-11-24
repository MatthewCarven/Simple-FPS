// Sky surfaces: windows onto a box of six pictures around the world, infinitely far away, so each
// pixel shows whatever lies in its direction from the eye, wherever the eye is. They still hide
// what's behind them, as in the original game. A map can set the box slowly spinning.
shader_type spatial;
render_mode unshaded, cull_back;

uniform samplerCube sky_box : source_color, filter_linear, repeat_disable;
uniform bool has_box = false;

// Shown when the map's sky pictures are missing. The same colour the screen is cleared to.
uniform vec3 flat_colour : source_color = vec3(0.33, 0.42, 0.55);

// In Godot's frame, and of length 1. The box turns this many degrees a second about it.
uniform vec3 spin_axis = vec3(0.0, 1.0, 0.0);
uniform float spin_degrees_per_second = 0.0;

void fragment() {
	if (has_box) {
		vec3 dir = mat3(INV_VIEW_MATRIX) * VERTEX;

		// A box turned forward by the angle shows, in each direction, what was there before
		// turning the direction back by it.
		float angle = radians(spin_degrees_per_second) * TIME;
		float c = cos(angle);
		float s = -sin(angle);
		dir = dir * c + cross(spin_axis, dir) * s + spin_axis * dot(spin_axis, dir) * (1.0 - c);

		ALBEDO = texture(sky_box, dir).rgb;
	} else {
		ALBEDO = flat_colour;
	}
}
