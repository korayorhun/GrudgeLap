// The rain's streaks (0.65, CameraRig.SetRain): thin quads turned to the camera about their own vertical axis, faint, gone
// within a metre and a bit of the eye, as the standard material's fixed-Y billboard and pixel-alpha fade drew them until
// 0.64. Lit since 0.65: the grey sky's light by day, as before; at night only what reaches them. A drop is a small round
// lens and sends a lamp's light back the way it came, as the paint's glass beads do (road_paint_retro.gdshader), so the
// rain in the car's own beams shines as it does from a driver's seat, and a lamp behind a drop lights it through.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled;

uniform vec4 colour : source_color = vec4(0.78, 0.82, 0.90, 0.30);
uniform float fade_min = 1.2;
uniform float fade_max = 4.5;
// How much a lamp's light the drops send back towards it, and how narrowly.
uniform float retro_gain = 5.0;
uniform float retro_power = 12.0;
uniform float through = 0.85;

void vertex() {
	// A billboard fixed on Y, the particle's scale kept (BaseMaterial3D's code for BILLBOARD_FIXED_Y and keep-scale).
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
			vec4(normalize(cross(vec3(0.0, 1.0, 0.0), MAIN_CAM_INV_VIEW_MATRIX[2].xyz)), 0.0),
			vec4(0.0, 1.0, 0.0, 0.0),
			vec4(normalize(cross(MAIN_CAM_INV_VIEW_MATRIX[0].xyz, vec3(0.0, 1.0, 0.0))), 0.0),
			MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(
			vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
			vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
			vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
			vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	ALBEDO = colour.rgb;
	ALPHA = colour.a * clamp(smoothstep(fade_min, fade_max, length(VERTEX)), 0.0, 1.0);
}

void light() {
	float facing = max(dot(NORMAL, LIGHT), 0.0);
	float behind = max(-dot(NORMAL, LIGHT), 0.0) * through;
	// Back the way it came: only a lamp's (the sun's comes from everywhere under the deck, and by day the sky lights them).
	float back = LIGHT_IS_DIRECTIONAL ? 0.0 : retro_gain * pow(max(dot(LIGHT, VIEW), 0.0), retro_power);
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * (facing + behind + back) / PI;
}
