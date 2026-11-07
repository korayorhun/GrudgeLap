// The ground of the plain (0.36, WorldBuilder.Grass): two grass photos (a short lawn and a meadow with clover) and a
// soil photo, each sampled with its tile broken up (no pattern repeats however far it reaches), the near tile giving
// way to a coarser one with distance; the colour varied at three scales (dry stretches over a hundred metres, lusher
// hollows over a few, the grass's own clumps); bare soil where the ground is worn (the trodden strip by the road,
// scattered bald patches, the faces of the deep cuttings) with a ragged edge; a little relief in the light. Until
// 0.36 one 4.3 m photo tile multiplied by a noise covered the whole plain: a printed carpet with dark stains.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
#include "res://assets/shaders/wind.gdshaderinc"
#include "res://assets/shaders/cloud_shadow.gdshaderinc"

uniform sampler2D lawn : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D lawn_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D meadow : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D meadow_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// The grass's tint (WorldBuilder.DryGrass, WetGrass with the weather) and the meadow photo brought to the lawn's mean.
uniform vec3 tint : source_color = vec3(0.56, 0.66, 0.39);
uniform vec3 meadow_balance = vec3(1.5, 1.2, 1.25);
uniform vec3 soil_tint : source_color = vec3(0.78, 0.70, 0.62);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
// 1989 mode: flat colours, the earthworks in the cabinet's orange.
uniform bool retro = false;
uniform vec3 retro_earth : source_color = vec3(0.86, 0.50, 0.27);

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

// A photo sampled with its tile broken up (Quilez, "texture repetition", the third technique): two lookups at
// offsets chosen by a smooth noise index k, blended where the index steps, weighted by the colours so the seam
// follows the picture's own detail. The normal map takes the same offsets and weight.
void sample_broken(sampler2D col, sampler2D nrm, vec2 uv, float k, out vec3 c, out vec3 n) {
	float l = k * 8.0;
	float f = fract(l);
	float ia = floor(l);
	vec2 oa = sin(vec2(3.0, 7.0) * ia);
	vec2 ob = sin(vec2(3.0, 7.0) * (ia + 1.0));
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	vec3 ca = textureGrad(col, uv + oa, dx, dy).rgb;
	vec3 cb = textureGrad(col, uv + ob, dx, dy).rgb;
	float w = smoothstep(0.2, 0.8, f - 0.1 * dot(ca - cb, vec3(1.0)));
	c = mix(ca, cb, w);
	n = mix(textureGrad(nrm, uv + oa, dx, dy).rgb, textureGrad(nrm, uv + ob, dx, dy).rgb, w);
}

void fragment() {
	vec2 p = world_pos.xz;
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	// UV2: x the deep cuttings' bare earth, y the road's weight (1 on the verge by the road, 0 out on the plain);
	// COLOR: the shading the terrain builder bakes in (slopes, the trodden edge, the wooded hills far out).
	float earth = UV2.x;
	float road = UV2.y;
	vec3 shade = COLOR.rgb;
	if (retro) {
		ALBEDO = mix(tint, retro_earth, step(0.5, earth)) * shade;
		ROUGHNESS = 1.0;
		SPECULAR = 0.2;
	} else {
		float far = smoothstep(20.0, 90.0, dist);
		vec3 a1; vec3 an1; sample_broken(lawn, lawn_normal, p / 4.3, vnoise(p * 0.09), a1, an1);
		vec3 a2; vec3 an2; sample_broken(lawn, lawn_normal, p / 17.0 + 0.31, vnoise(p * 0.023 + 4.0), a2, an2);
		vec3 b1; vec3 bn1; sample_broken(meadow, meadow_normal, p / 3.7 + 0.53, vnoise(p * 0.08 + 9.0), b1, bn1);
		vec3 b2; vec3 bn2; sample_broken(meadow, meadow_normal, p / 15.0 + 0.71, vnoise(p * 0.02 + 2.0), b2, bn2);
		// The meadow and the lawn in patches of thirty to sixty metres.
		float meadow_share = smoothstep(0.35, 0.70, fbm(p * 0.035 + 3.1));
		vec3 g = mix(mix(a1, a2, far), mix(b1, b2, far) * meadow_balance, meadow_share);
		vec3 gn = mix(mix(an1, an2, far), mix(bn1, bn2, far), meadow_share);
		// Colour at three scales: dry, straw-coloured stretches over a hundred metres and more, lusher and bluer hollows
		// over a few, the grass's own clumps over half a metre.
		float dry = smoothstep(0.50, 0.85, fbm(p * 0.008 + 7.7));
		float lush = smoothstep(0.55, 0.85, fbm(p * 0.11 + 1.3));
		float clumps = fbm(p * 1.7 + 5.5);
		vec3 grass = g * tint;
		grass *= mix(vec3(1.0), vec3(1.22, 1.06, 0.62), dry * 0.75);
		grass *= mix(vec3(1.0), vec3(0.78, 0.92, 0.86), lush * 0.6);
		grass *= 0.84 + 0.32 * clumps * (1.0 - far * 0.6);
		// Bare soil: the trodden strip by the road, bald patches scattered over the plain, the cuttings' faces; ragged,
		// the soil showing first between the darkest blades.
		vec3 s1; vec3 sn1; sample_broken(soil, soil_normal, p / 3.0 + 0.2, vnoise(p * 0.1 + 3.0), s1, sn1);
		float wornNoise = fbm(p * 0.33 + 11.0);
		float worn = smoothstep(0.62, 0.97, road) * smoothstep(0.40, 0.70, wornNoise + 0.25 * (vnoise(p * 2.3) - 0.5));
		float bald = smoothstep(0.74, 0.80, wornNoise) * (1.0 - 0.6 * far);
		float bare = clamp(max(max(worn * 0.6, bald * 0.6), earth), 0.0, 1.0);
		float darkness = dot(g, vec3(0.333));
		bare = clamp(bare * 1.4 - (darkness - 0.12) * 2.5 * (1.0 - bare), 0.0, 1.0);
		vec3 albedo = mix(grass, s1 * soil_tint, bare) * shade;
		// The clouds' shadows out on the plain (0.44); round the car the sun itself carries them.
		albedo *= cloud_albedo_factor(world_pos, dist);
		// The wind running over the meadow (0.45): a gust bends the blades and shows their paler sides, in patches that
		// run downwind; within half a kilometre, where the grass is seen as grass.
		float gust = wind_gust(p, TIME) * (1.0 - smoothstep(200.0, 500.0, dist)) * (1.0 - bare);
		albedo *= mix(vec3(1.0), vec3(1.13, 1.12, 1.03), gust);
		albedo *= mix(1.0, 0.70, wetness);
		ALBEDO = albedo;
		// A little relief: the plain is never billiard-smooth (molehills, ruts, tussocks), a metre or two across.
		float e = 0.35;
		float h0 = fbm(p * 0.6);
		float hx = fbm((p + vec2(e, 0.0)) * 0.6);
		float hz = fbm((p + vec2(0.0, e)) * 0.6);
		vec2 tilt = vec2(hx - h0, hz - h0) / e * 0.18 * (1.0 - far * 0.5);
		vec3 n = mix(gn, sn1, bare);
		n.xy = clamp(n.xy + tilt, 0.0, 1.0);
		NORMAL_MAP = n;
		NORMAL_MAP_DEPTH = mix(0.65, 0.9, bare) * (1.0 - 0.55 * far);
		ROUGHNESS = mix(mix(0.96, 0.9, bare), 0.72, wetness);
		SPECULAR = 0.25;
	}
}
