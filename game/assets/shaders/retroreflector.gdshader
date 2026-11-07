// The marker posts' reflectors (0.57, WorldBuilder.BuildMarkerPosts): retroreflective, as a road reflector's prisms are,
// sending light back the way it came. Lit as a plain surface by the sun and the moon; where a lamp shines from near the
// eye, the headlamps at night, its light comes back to the driver many times over: a lobe round the lamp's own
// direction a few degrees wide, so a post a hundred metres ahead flares in the faint light over the low beam's cut-off
// and one beside the car barely does. The sun is left
// out of the lobe: with it behind the driver every post lit up like a lamp by day. The instance's colour (amber on the
// right, white on the left) is the multimesh's.
shader_type spatial;
render_mode cull_disabled;

uniform float retro_gain = 400.0;

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.3;
	SPECULAR = 0.5;
}

void light() {
	DIFFUSE_LIGHT += max(dot(NORMAL, LIGHT), 0.0) * ATTENUATION * LIGHT_COLOR / PI;
	if (!LIGHT_IS_DIRECTIONAL) {
		float back = pow(max(dot(LIGHT, VIEW), 0.0), 100.0);
		DIFFUSE_LIGHT += back * ATTENUATION * LIGHT_COLOR * retro_gain;
	}
}
