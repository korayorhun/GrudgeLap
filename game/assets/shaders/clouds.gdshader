// The fair-weather cumulus over the valley (0.37, WorldBuilder.BuildClouds): the cloud sheet (tools/make-clouds.py)
// at two scales and angles so its tile does not repeat, thinned by a slow noise into clear and cloudy stretches of sky,
// drifting with the wind (WorldBuilder.Tick), unlit, faded out before the layer's edge. Until 0.37 one sheet tiled
// 5 x 5 over a 14 km plane at 1.5 km; raised above the Tetons and spread out to 60 km it showed as a wallpaper.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

uniform sampler2D clouds_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(0.94, 0.95, 0.97);
uniform vec2 drift = vec2(0.0);
uniform float tile = 5200.0;
uniform float fade_near = 9000.0;
uniform float fade_far = 28000.0;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
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

void fragment() {
	vec2 p = world_pos.xz + drift;
	vec4 a = texture(clouds_tex, p / tile);
	vec2 q = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * p;
	vec4 b = texture(clouds_tex, q / (tile * 1.63) + vec2(0.37, 0.71));
	// Clear and cloudy stretches some kilometres across.
	float cover = vnoise(p / 7000.0) * 0.65 + vnoise(p / 2300.0 + 5.3) * 0.35;
	cover = smoothstep(0.38, 0.72, cover);
	float alpha = max(a.a, b.a * 0.85) * cover;
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	alpha *= 1.0 - smoothstep(fade_near, fade_far, dist);
	ALBEDO = mix(a.rgb, b.rgb, b.a / max(a.a + b.a, 0.001)) * tint;
	ALPHA = alpha;
}
