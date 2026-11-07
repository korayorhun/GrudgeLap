// The verge along the roads (0.36, WorldBuilder.Gravel): a band of real stones (a photographed stone bed, ambientCG
// Gravel020) painted the 1989 cabinet's burnt orange, as the owner asked ("the stones at the roadside orange, like
// painted orange stones", "a darker orange, like the game's"). The photo is sampled with its tile broken up, so no
// pattern repeats; its height picks the stones' tops (painted) from the joints (dark soil); the paint keeps the
// stone's own shading under it, is chipped and faded unevenly, covers the stones by the asphalt and thins out towards
// the outer edge, which frays into the grass; the photo's normal map gives every stone its relief in the sun.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D stones : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D stones_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D stones_height : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 paint : source_color = vec3(0.60, 0.30, 0.02);
uniform vec3 faded : source_color = vec3(0.60, 0.40, 0.16);
uniform vec3 soil : source_color = vec3(0.20, 0.15, 0.11);
uniform float tile = 2.6;
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform bool retro = false;
uniform vec3 retro_colour : source_color = vec3(0.95, 0.46, 0.12);

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
	// UV.x: 0 at the asphalt's edge, 1 at the band's outer edge (WorldBuilder.BuildShoulders).
	float across = UV.x;
	vec2 wp = world_pos.xz;
	// The outer edge frays into the grass: the ground shows through where the band runs out.
	float fray = across + 0.20 * (vnoise(wp * 2.2) - 0.5) + 0.10 * (vnoise(wp * 8.0) - 0.5);
	ALPHA = 1.0 - smoothstep(0.84, 0.90, fray);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	if (retro) {
		ALBEDO = retro_colour * COLOR.rgb;
		ROUGHNESS = 1.0;
	} else {
		// The photo with its tile broken up (Quilez's third technique, as the ground's): two offset lookups blended
		// where a smooth noise index steps; the colour, the height and the normal take the same offsets and weight.
		vec2 uv = wp / tile;
		float l = vnoise(wp * 0.45) * 8.0;
		float fl = fract(l);
		float ia = floor(l);
		vec2 oa = sin(vec2(3.0, 7.0) * ia);
		vec2 ob = sin(vec2(3.0, 7.0) * (ia + 1.0));
		vec2 dx = dFdx(uv);
		vec2 dy = dFdy(uv);
		vec3 ca = textureGrad(stones, uv + oa, dx, dy).rgb;
		vec3 cb = textureGrad(stones, uv + ob, dx, dy).rgb;
		float w = smoothstep(0.2, 0.8, fl - 0.1 * dot(ca - cb, vec3(1.0)));
		vec3 c = mix(ca, cb, w);
		float h = mix(textureGrad(stones_height, uv + oa, dx, dy).r, textureGrad(stones_height, uv + ob, dx, dy).r, w);
		vec3 nm = mix(textureGrad(stones_normal, uv + oa, dx, dy).rgb, textureGrad(stones_normal, uv + ob, dx, dy).rgb, w);
		float lum = dot(c, vec3(0.299, 0.587, 0.114)) / 0.20;   // the photo's shading about 1 (its linear luma is about 0.2)
		// A stone's top against the joint around it.
		float top = smoothstep(0.22, 0.42, h);
		// Painted by the asphalt, less so outwards; the paint faded unevenly along the road, and chipped in flecks.
		float zone = 1.0 - smoothstep(0.42, 0.95, across + 0.30 * (vnoise(wp * 1.1 + 4.0) - 0.5));
		float age = vnoise(wp * 0.7 + 9.0);
		float chips = vnoise(wp * 32.0) * 0.6 + vnoise(wp * 90.0 + 3.0) * 0.4;
		float keep = zone * step(chips, 0.93 - 0.40 * age - 0.25 * (1.0 - top));
		vec3 coat = mix(paint, faded, age * 0.65) * mix(1.0, lum, 0.55);
		// Unpainted stones are the earth's colour, not the photo's grey (a pale fringe along the band's outer edge).
		vec3 face = mix(c * vec3(0.80, 0.66, 0.50), coat, keep);
		// Dust from the road on the side nearest it.
		face *= 1.0 - 0.20 * (1.0 - smoothstep(0.0, 0.25, across));
		vec3 joint = soil * mix(0.7, 1.3, lum * 0.5);
		vec3 albedo = mix(joint, face, top) * COLOR.rgb;
		albedo *= mix(1.0, 0.65, wetness);
		ALBEDO = albedo;
		// The photo's relief, turned into the world (its u runs along +x, its v along +z; an OpenGL normal map's green
		// points up the image, -z) and back to the view.
		vec3 nw = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
		vec2 t = (nm.xy * 2.0 - 1.0) * 1.2;
		vec3 bent = normalize(nw + vec3(t.x, 0.0, -t.y));
		NORMAL = normalize((VIEW_MATRIX * vec4(bent, 0.0)).xyz);
		ROUGHNESS = mix(mix(0.95, mix(0.86, 0.62, keep), top), 0.45, wetness);
		// Little specular: the sky's blue in a stone's sheen turned the orange pink.
		SPECULAR = 0.2;
		AO = mix(0.45, 1.0, smoothstep(0.12, 0.40, h));
		AO_LIGHT_AFFECT = 0.35;
	}
}
