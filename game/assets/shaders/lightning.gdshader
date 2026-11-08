// A lightning channel (0.64, Thunderstorm): ribbons turned to the eye, added over the scene past the glow's threshold so
// the stroke blooms. Vertex colour red 1: the core, flat-topped across its ribbon so that a line a few pixels wide keeps
// its whole brightness under the temporal anti-aliasing; 0: the halo round it, faint and falling off. Not fogged: a
// stroke burns through the rain's haze, which the energy takes down a little with the distance instead.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 colour : source_color = vec3(0.86, 0.88, 1.0);
uniform float energy = 12.0;

void fragment() {
	float across = 1.0 - abs(UV.x * 2.0 - 1.0);
	float core = smoothstep(0.0, 0.5, across);
	float halo = across * across * across * 0.05;
	ALBEDO = colour * energy * mix(halo, core, COLOR.r);
}
