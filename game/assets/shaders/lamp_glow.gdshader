// A lamp's glow at night (0.58): a small card turned to the camera, added over the scene, bright enough past the glow's
// threshold to bloom. It shines the way its lamp faces (the node's -Z): a headlamp seen from behind, or a tail lamp from
// ahead, fades out. Tested against the depth so the car's body hides the far lamp.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 colour : source_color = vec3(1.0, 0.95, 0.85);
uniform float energy = 6.0;
// 1: it shines only the way it faces (a car's lamp); 0: all round (a yard lamp's refractor).
uniform float directional = 1.0;

varying float facing;

void vertex() {
	vec3 forward = normalize(-MODEL_MATRIX[2].xyz);
	vec3 to_camera = normalize(CAMERA_POSITION_WORLD - MODEL_MATRIX[3].xyz);
	facing = dot(forward, to_camera);
	// Turned to the camera, the node's scale kept.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz), INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz), INV_VIEW_MATRIX[2] * length(MODEL_MATRIX[2].xyz), MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float r = length(UV - 0.5) * 2.0;
	float glow = pow(max(1.0 - r, 0.0), 3.0);
	ALBEDO = colour * energy * glow * mix(1.0, smoothstep(-0.15, 0.6, facing), directional);
}
