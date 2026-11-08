// The ghost car's first pass (0.73, CarView.MakeGhost): its depth and nothing else, drawn just before the visible pass
// (ghost.gdshader; render priorities 9 and 10), so that each pixel shows only the ghost's front-most surface. Until
// 0.73 one translucent material lay over every part of the car, the cabin, the seats and the wheels' insides included,
// and they all showed through each other (the owner, 8 November 2026: "random things seem to show").
// Both faces (0.76): the models are not wound one way throughout, which is why the game draws their paint, glass and
// lenses from both faces. Culled here, the flat-twelve's tail-lamp glass and brake lens, which face into the car, and
// the 86's right-hand arch liners left holes in the ghost.
// The shell is drawn 3 mm behind the real surface along the line of sight, in metres, so that the visible pass on the
// real surface is strictly in front of it whatever the depth test's comparison. Until 0.76 it was moved 3 mm in along
// the normal, in the mesh's own units: the Renault 12's (estate's) mirrored windows, doors, bumpers, lamps and wheels
// (27 primitives) have their normals pointing away from the face that is seen, so that brought the shell towards the
// eye, the visible pass failed the depth test and those parts were holes that hid the ghost behind them too; and the
// models' node scales (0.001 to 12) made the 3 mm anything from 3 um to 3.6 cm.
// Near the eye, where the visible pass fades out, the depth is written all the same: there it hides the ghost's own far
// side, which would show through the faded near side as the inside of a hollow car, and nothing else, since the visible
// pass is the only thing drawn after it that tests depth.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always, blend_mix, shadows_disabled, skip_vertex_transform;

void vertex() {
	vec3 eye = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX = eye + normalize(eye) * 0.003;
}

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = 0.0;
}
