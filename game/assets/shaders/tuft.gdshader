// The grass tufts (0.36, FoliageBuilder.Tuft): the blade atlas on crossed cards, tinted by the instance and by the
// same dry and lush patches the ground has (terrain.gdshader), so a tuft in a dry stretch is straw and one in a lush
// hollow dark green, not the same bright speck everywhere; the light through the blades kept small; faded out
// between fade_start and fade_end metres, dithered, so the far plain is the ground's texture and not a pepper of
// cards. Until 0.36 the tufts were brighter than the ground and lit from behind like lamps.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D blades : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 tint : source_color = vec3(0.56, 0.66, 0.39);
uniform float fade_start = 70.0;
uniform float fade_end = 120.0;
uniform float wetness : hint_range(0.0, 1.0) = 0.0;

varying vec3 world_pos;
varying vec3 instance_origin;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	instance_origin = MODEL_MATRIX[3].xyz;
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x), mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		s += a * vnoise(p);
		p = p * 2.07 + vec2(13.7, 7.1);
		a *= 0.5;
	}
	return s / 0.9375;
}

void fragment() {
	vec4 t = texture(blades, UV);
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	float fade = 1.0 - smoothstep(fade_start, fade_end, dist);
	float dither = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
	ALPHA = t.a * step(dither, fade);
	ALPHA_SCISSOR_THRESHOLD = 0.45;
	vec2 p = instance_origin.xz;
	float dry = smoothstep(0.50, 0.85, fbm(p * 0.008 + 7.7));
	float lush = smoothstep(0.55, 0.85, fbm(p * 0.11 + 1.3));
	// About the ground's own green (the blades' atlas is brighter and more saturated than the ground's photo).
	vec3 c = t.rgb * COLOR.rgb * tint * vec3(1.25, 0.72, 1.4);
	c *= mix(vec3(1.0), vec3(1.25, 1.06, 0.58), dry * 0.8);
	c *= mix(vec3(1.0), vec3(0.78, 0.92, 0.86), lush * 0.6);
	// Darker at the root, where the blades shade each other (the atlas has two rows of cells, the root at a cell's foot).
	float cellv = UV.y - floor(UV.y * 2.0) * 0.5;
	c *= mix(1.0, 0.55, smoothstep(0.25, 0.5, cellv));
	ALBEDO = c * mix(1.0, 0.72, wetness);
	BACKLIGHT = c * 0.15;
	ROUGHNESS = mix(0.9, 0.6, wetness);
	SPECULAR = 0.2;
}
