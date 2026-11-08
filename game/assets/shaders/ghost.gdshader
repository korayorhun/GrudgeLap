// The ghost car's visible pass (0.73, CarView.MakeGhost; after ghost_depth.gdshader): a cool translucent shell,
// brighter at its outline (a Fresnel rim) so that the car's shape reads against the road and the sky, fading out
// within a few metres of the eye so that at the start, where it sits on the player's car, it never fills the view.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, blend_mix, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.62, 0.86, 1.0);
// The shell's own opacity, and what the outline adds to it.
uniform float body = 0.12;
uniform float rim = 0.6;

void fragment() {
	float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float edge = pow(1.0 - facing, 2.2);
	float near = smoothstep(2.5, 8.0, length(VERTEX));
	ALBEDO = tint * (0.55 + 0.9 * edge);
	ALPHA = clamp(body + rim * edge, 0.0, 0.9) * near;
}
