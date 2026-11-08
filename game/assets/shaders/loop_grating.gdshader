// The loop's running surface (0.72, the owner's P16: "a perforated steel deck we can see through, as in the original"):
// a hot-dip galvanised bar grating, computed per pixel rather than sampled. Bearing bars run along the road, 10 mm thick
// and 76 mm deep every 60 mm across it; cross bars 6 mm thick and 10 mm deep every 102 mm along it; 78 % open from
// straight above. Each set of bars is a pulse train whose coverage of the pixel's footprint is an exact box filter (the
// difference of its running integral), so a bar narrower than a pixel greys to its share instead of crawling. The bars'
// depth is taken into account: a ray through the deck walks sideways by depth * tan(angle) inside the bars, so the
// deck closes up at a grazing angle, the far top of the loop seen from the road reads as a darker band and a view along
// the slots stays open, as a real grating does; the part of the blocked band below the bar tops is the bars' side
// wall, shaded darker. The result is the deck's opacity, blended (the transparent pass): a far grating is a smooth veil
// of its physical opacity. Alpha hash, tried first, left a far deck as a field of white specks the temporal
// anti-aliasing did not settle, which would have glittered as the car moved; MSAA is off by default with the motion
// blur on, so alpha to coverage would only dither; and a texture with alpha scissor lost the whole deck in its
// mipmaps a few metres out. Blended, the deck writes no depth: it casts no shadow (WorldBuilder.BuildLoopDeck turns
// that off; the steel under it casts the ladder) and the cars' glass, also blended, is sorted by object. The mesh is WorldBuilder.BuildLoopDeck: UV.x is
// metres across from the left edge, UV.y metres along from the grating's first frame, the tangent the road's lateral,
// the binormal its direction of travel, the normal the deck's up (towards the loop's centre). The physics never sees
// this surface. Method after loop-grating research (C:\temp\log\grudgelap\v70\research\loop-grating.md, 8 Nov 2026).
shader_type spatial;
render_mode cull_disabled, blend_mix;

uniform vec2 pitch = vec2(0.060, 0.102);   // the bearing bars' spacing across the deck, the cross bars' along it (m)
uniform vec2 bar = vec2(0.010, 0.006);     // their thickness seen from above (m)
uniform vec2 depth = vec2(0.076, 0.010);   // how far each set reaches below the running surface (m)
uniform vec3 steel_colour : source_color = vec3(0.62, 0.63, 0.64);
uniform float wetness = 0.0;
// The 1989 look: the arcade's deck as its footage shows it, flat navy with a one-pixel column dither, opaque.
uniform bool retro = false;

varying vec3 up_v;      // the deck's up in view space; passed on so that a back face's flipped NORMAL cannot confuse it
varying vec3 across_v;  // the direction of UV.x in view space
varying vec3 along_v;   // the direction of UV.y in view space

void vertex() {
	up_v = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	across_v = normalize((MODELVIEW_MATRIX * vec4(TANGENT, 0.0)).xyz);
	along_v = normalize((MODELVIEW_MATRIX * vec4(BINORMAL, 0.0)).xyz);
}

// The running integral of bars w wide every p, centred on the multiples of p.
float bars_integral(float x, float p, float w) {
	float k = floor(x / p + 0.5);
	return k * w + clamp(x - k * p + 0.5 * w, 0.0, w);
}

// The share of a footprint fw wide, centred on x, that falls on a bar: an exact box filter.
float bars(float x, float p, float w, float fw) {
	fw = max(fw, 1e-5);
	return (bars_integral(x + 0.5 * fw, p, w) - bars_integral(x - 0.5 * fw, p, w)) / fw;
}

void fragment() {
	if (retro) {
		vec3 navy = mod(floor(FRAGCOORD.x), 2.0) < 1.0 ? vec3(38.0, 47.0, 93.0) / 255.0 : vec3(28.0, 37.0, 82.0) / 255.0;
		ALBEDO = navy;
		METALLIC = 0.0;
		ROUGHNESS = 1.0;
		ALPHA = 1.0;
	} else {
		vec3 ray = -VIEW;                       // from the eye (in the shadow pass, the light) to this point
		float rn = dot(ray, up_v);              // below zero seen from above the deck, above zero from under it
		float inv = 1.0 / max(abs(rn), 1e-3);
		// Half the sideways walk through the bars' depth, signed for the side the deck is seen from.
		vec2 half_walk = vec2(dot(ray, across_v), dot(ray, along_v)) * inv * depth * (rn < 0.0 ? 0.5 : -0.5);
		// The footprint widened by 1.6 against the moire an exact box filter leaves where the pitch nears a pixel (a
		// tent's worth of smoothing; seen from the seat as wavy bands up the deck ahead).
		vec2 fw = fwidth(UV) * 1.6;
		vec2 top = vec2(bars(UV.x, pitch.x, bar.x, fw.x), bars(UV.y, pitch.y, bar.y, fw.y));
		vec2 band = min(pitch, bar + 2.0 * abs(half_walk));
		vec2 blocked = vec2(bars(UV.x + half_walk.x, pitch.x, band.x, fw.x), bars(UV.y + half_walk.y, pitch.y, band.y, fw.y));
		float top_cover = 1.0 - (1.0 - top.x) * (1.0 - top.y);
		float solid = 1.0 - (1.0 - blocked.x) * (1.0 - blocked.y);
		// The share of what blocks the view that is the bars' side walls rather than their tops.
		float wall = clamp((solid - top_cover) / max(solid, 1e-4), 0.0, 1.0);
		vec3 face = rn < 0.0 ? up_v : -up_v;
		// The walls face across or along the deck, towards the eye; leaning the normal that way shades them as walls.
		vec3 wall_n = normalize(-sign(dot(ray, across_v) + 1e-6) * across_v * max(blocked.x - top.x, 0.0)
			- sign(dot(ray, along_v) + 1e-6) * along_v * max(blocked.y - top.y, 0.0) + face * 1e-3);
		NORMAL = normalize(mix(face, wall_n, wall * 0.7));
		ALBEDO = steel_colour * mix(1.0, 0.5, wall) * mix(1.0, 0.72, wetness);
		METALLIC = 0.85;
		// Rougher where a pixel spans the pitch, against specular sparkle on the thin bars.
		float far = smoothstep(0.3, 1.0, max(fw.x / pitch.x, fw.y / pitch.y));
		ROUGHNESS = max(mix(0.45, 0.22, wetness), mix(0.0, 0.6, far));
		ALPHA = solid;
	}
}
