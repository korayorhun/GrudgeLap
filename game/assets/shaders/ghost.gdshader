// The ghost car's visible pass (0.73, CarView.MakeGhost; after ghost_depth.gdshader): a cool translucent shell,
// brighter at its outline (a Fresnel rim) so that the car's shape reads against the road and the sky, fading out
// within a few metres of the eye so that at the start, where it sits on the player's car, it never fills the view.
// From both faces, as the depth pass (0.76), the rim taken from the surface's angle to the line of sight whichever way
// its normal points: the models' normals do not all point out of the face that is seen (the Renault 12's mirrored
// panels), and one turned away from the eye would draw its whole panel as outline.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.62, 0.86, 1.0);
// The shell's own opacity, and what the outline adds to it.
uniform float body = 0.12;
uniform float rim = 0.6;

void fragment() {
	float facing = min(abs(dot(normalize(NORMAL), normalize(VIEW))), 1.0);
	float edge = pow(1.0 - facing, 2.2);
	float near = smoothstep(2.5, 8.0, length(VERTEX));
	ALBEDO = tint * (0.55 + 0.9 * edge);
	ALPHA = clamp(body + rim * edge, 0.0, 0.9) * near;
}
