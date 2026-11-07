// The power lines' wires (0.50, PowerLineBuilder): ribbons that turn about the wire to face the camera, their width the
// wire's own 2 cm or a pixel and a half, whichever is wider, and their opacity the wire's share of that width: a wire
// a few hundred metres off is a faint hairline, not a dotted line that crawls as the car moves (a 2 cm cylinder there
// is a twentieth of a pixel). The mesh carries each ribbon's two edges at the same points on the wire; UV.x says which
// side (-1 or 1), the normal holds the wire's direction there.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, unshaded, shadows_disabled;

uniform vec3 colour : source_color = vec3(0.05, 0.05, 0.055);
uniform float wire_width = 0.02;

varying float coverage;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 along = normalize(mat3(MODEL_MATRIX) * NORMAL);
	vec3 to_eye = CAMERA_POSITION_WORLD - world;
	float distance_to = length(to_eye);
	vec3 across = normalize(cross(along, to_eye / max(distance_to, 0.001)));
	// The size of a pixel at that distance: the view's vertical extent there over the viewport's height.
	float pixel = 2.0 * distance_to / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float width = max(wire_width, 1.5 * pixel);
	coverage = wire_width / width;
	world += across * (UV.x * 0.5 * width);
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
}

void fragment() {
	ALBEDO = colour;
	ALPHA = coverage;
}
