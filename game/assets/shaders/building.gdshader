// The buildings' walls and roofs weathered (0.41, WorldBuilder.Weathered): the photo projected on the three axes (as
// the standard material's triplanar did) with its normal map turned into the world, then the marks of years outdoors:
// grime and damp splashed up from the ground along the foot of the walls, streaks the rain has run down them, the paint
// faded unevenly. Until 0.41 the barn was one clean, even red and every wall met the grass like a model on a table.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D roughness_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool textured = false;
uniform bool has_normal = false;
uniform bool has_roughness = false;
uniform vec3 tint : source_color = vec3(1.0);
uniform float uv_scale = 0.5;
uniform float normal_scale = 0.6;
uniform float roughness_value = 0.8;
// How much of the weather: walls fully, roofs and trim less.
uniform float weathering = 1.0;
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform bool retro = false;

// The building's foot, the ground's height where it stands (set on each of its parts: BuildingBuilder.SetFoot).
instance uniform float foot = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
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

vec3 weights(vec3 n) {
	vec3 w = pow(abs(n), vec3(4.0));
	return w / max(dot(w, vec3(1.0)), 1e-4);
}

void fragment() {
	vec3 n = normalize(world_normal);
	vec3 w = weights(n);
	vec3 p = world_pos * uv_scale;
	vec3 c = tint;
	float rough = roughness_value;
	vec3 bent = n;
	if (textured && !retro) {
		c *= texture(albedo_tex, p.zy).rgb * w.x + texture(albedo_tex, p.xz).rgb * w.y + texture(albedo_tex, p.xy).rgb * w.z;
		if (has_roughness) {
			rough = texture(roughness_tex, p.zy).r * w.x + texture(roughness_tex, p.xz).r * w.y + texture(roughness_tex, p.xy).r * w.z;
		}
		if (has_normal) {
			// Each projection's tangent normal laid onto its axis (the "whiteout" blend), summed by the weights.
			vec3 tx = (texture(normal_tex, p.zy).xyz * 2.0 - 1.0) * vec3(normal_scale, normal_scale, 1.0);
			vec3 ty = (texture(normal_tex, p.xz).xyz * 2.0 - 1.0) * vec3(normal_scale, normal_scale, 1.0);
			vec3 tz = (texture(normal_tex, p.xy).xyz * 2.0 - 1.0) * vec3(normal_scale, normal_scale, 1.0);
			tx = vec3(tx.xy + n.zy, abs(tx.z) * n.x);
			ty = vec3(ty.xy + n.xz, abs(ty.z) * n.y);
			tz = vec3(tz.xy + n.xy, abs(tz.z) * n.z);
			bent = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
		}
	}
	if (!retro && weathering > 0.0) {
		float h = world_pos.y - foot;
		// Grime and damp splashed up from the ground, ragged, darker and browner below the knee.
		float edge = 0.75 + 0.3 * (vnoise(world_pos.xz * 2.5 + world_pos.y * 0.7) - 0.5);
		float grime = 1.0 - smoothstep(0.05, edge, h);
		c = mix(c, c * vec3(0.52, 0.47, 0.40), grime * 0.6 * weathering);
		// The rain's streaks down the walls (vertical faces only), from the eaves, a few centimetres wide.
		float vertical = 1.0 - abs(n.y);
		float across = dot(world_pos.xz, normalize(vec2(-n.z, n.x) + 1e-4));
		float streak = vnoise(vec2(across * 6.0, world_pos.y * 0.35)) * vnoise(vec2(across * 1.3 + 7.0, world_pos.y * 0.15));
		c *= 1.0 - vertical * 0.30 * smoothstep(0.30, 0.60, streak) * weathering;
		// The paint faded unevenly, sun-bleached in broad patches.
		float fade = vnoise(world_pos.xz * 0.45 + world_pos.y * 0.3) * 0.6 + vnoise(world_pos.xz * 1.7 - world_pos.y) * 0.4;
		c *= mix(0.84, 1.08, fade);
		c = mix(c, vec3(dot(c, vec3(0.333))), 0.10 * weathering);
		rough = mix(rough, 1.0, grime * 0.5);
	}
	ALBEDO = c * mix(1.0, 0.72, wetness);
	ROUGHNESS = mix(rough, 0.35, wetness);
	NORMAL = normalize((VIEW_MATRIX * vec4(bent, 0.0)).xyz);
	SPECULAR = 0.35;
}
