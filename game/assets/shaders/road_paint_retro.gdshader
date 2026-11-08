// The road paint's glass beads at night (0.59, WorldBuilder.ApplyHour): a pass added over the centre and edge lines'
// own, only at night, that sends a lamp's light back towards it as the beads in road paint do, so the lines ahead shine
// in the headlamps far past the beams' pool on the asphalt. Only lamps (not the moon), only this lobe: the paint's own
// lighting is its material's, which this pass leaves as it is (no ambient, no fog, nothing from directional lights).
shader_type spatial;
render_mode blend_add, depth_draw_never, ambient_light_disabled, fog_disabled, cull_back;

uniform float retro_gain = 6.0;

void fragment() {
	ALBEDO = COLOR.rgb;
}

void light() {
	if (!LIGHT_IS_DIRECTIONAL) {
		// Narrow (a degree or two): near the car the beam's own light is enough, and a wider lobe lit the near lines like neon.
		float back = pow(max(dot(LIGHT, VIEW), 0.0), 400.0);
		DIFFUSE_LIGHT += back * ATTENUATION * LIGHT_COLOR * retro_gain;
	}
}
