// The ghost car's first pass (0.73, CarView.MakeGhost): its depth and nothing else, drawn before the visible pass
// (ghost.gdshader; render priorities -10 and +10), so that each pixel shows only the ghost's front-most surface. Until
// 0.73 one translucent material lay over every part of the car, the cabin, the seats and the wheels' insides included,
// and they all showed through each other (the owner, 8 November 2026: "random things seem to show"). The shell is drawn
// 3 mm inside the real surface so that the visible pass, on the real surface, is strictly in front of it whatever the
// depth test's comparison.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_always, blend_mix, shadows_disabled;

void vertex() {
	VERTEX -= NORMAL * 0.003;
}

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = 0.0;
}
