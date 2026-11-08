// Road paint (0.67, WorldBuilder.BuildCentreLine and BuildEdgeLines). Until 0.66 the lines were flat colour quads with
// ruler-straight edges laid over the tar, the classic decal look. Paint on a real road is thin: the asphalt's grain shows
// through it (brighter on the stones' tops, darker between them), the traffic wears it through to the tar in specks and
// patches, a sprayed line's edges are ragged by a few millimetres, and in the rain it shines like the road round it. The
// vertex colour keeps each dash's and each patch's tone from the builders; UV.x runs across the line (0 on the road's
// side, 1 on the verge's), UV.y is the distance along the road in metres.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D asphalt_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D asphalt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// The line's width in metres, for its edges' raggedness.
uniform float line_width = 0.15;
uniform float wetness = 0.0;
// The road's own albedo colour (WorldBuilder.ApplyWeather: dry 0.52 to wet 0.15), an sRGB colour as the road's is, so the
// worn specks match the tar round them (0.69: constants mixed as light left them two to eight times too light).
uniform vec3 tar_tint : source_color = vec3(0.52, 0.52, 0.53);
// The centre line: 3 m dashes every 9 m, cut to their length here (the mesh's ends fall on its frames).
uniform bool dashed = false;
// The edge lines: a little greyer towards the verge, where the tyres seldom clean them.
uniform bool outer_grey = false;
// The 1989 look: the flat paint of the original.
uniform bool retro = false;

varying vec3 world_pos;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	if (retro) {
		ALBEDO = COLOR.rgb;
		ROUGHNESS = 0.7;
		ALPHA = 1.0;
	} else {
		// The road's own photo, on its 5.5 m tile; h is a texel's brightness against the tile's mean (its last mip).
		vec2 tuv = world_pos.xz / 5.5;
		vec3 tar = texture(asphalt_color, tuv).rgb;
		float mean = dot(textureLod(asphalt_color, tuv, 12.0).rgb, vec3(1.0 / 3.0));
		float h = dot(tar, vec3(1.0 / 3.0)) / max(mean, 0.001);
		vec3 paint = COLOR.rgb * mix(0.85, 1.04, clamp(h, 0.0, 1.5));
		// Worn through to the tar in the hollows, more in some stretches than others; about a tenth to a fifth of it.
		float wear = 0.10 + 0.20 * vnoise(vec2(UV.y / 9.0, 3.0)) + 0.10 * vnoise(world_pos.xz * 0.7);
		float exposed = smoothstep(wear - 0.04, wear + 0.04, 1.0 - h);
		// The tar as the road shows it: its albedo colour times the photo.
		vec3 colour = mix(paint, tar * tar_tint, exposed);
		if (outer_grey) {
			colour *= mix(1.0, 0.88, clamp(UV.x, 0.0, 1.0));
		}
		ALBEDO = colour;
		// The edges, a sprayed line's: a few millimetres ragged, smoothed away where a pixel spans more than that across the
		// line (6 mm); where a pixel spans more than half of the raggedness's 4 cm cells along the line the noise is steadied
		// to its mean, a constant 3 mm inset, rather than sampled a cell or more apart from row to row (0.68 faded it out
		// there, which took the fray away from a few metres out; 0.67 let it fizz on Low, without TAA). The noise's input
		// wraps every 164 m: a sine hash fed thousands loses its precision and turns periodic.
		float near = 1.0 - smoothstep(0.003, 0.006, fwidth(UV.x) * line_width);
		float across = min(UV.x, 1.0 - UV.x) * line_width;
		float side = UV.x > 0.5 ? 7.0 : 0.0;
		float fray = mix(vnoise(vec2(mod(UV.y * 25.0, 4096.0), side)), 0.5, smoothstep(0.5, 1.0, fwidth(UV.y) * 25.0));
		float keep = step(0.006 * fray * near, across);
		if (dashed) {
			// The dash's ends ragged by up to 2 cm (the mesh cuts it at 9k and 9k + 3 m exactly).
			float along = mod(UV.y, 9.0);
			float to_end = min(along, 3.0 - along);
			// Steady: one noise value per dash across its width, the same from frame to frame.
			keep *= step(0.02 * vnoise(vec2(UV.x * 6.0, mod(floor(UV.y / 9.0), 512.0))), to_end);
		}
		ALPHA = keep;
		NORMAL_MAP = texture(asphalt_normal, tuv).rgb;
		NORMAL_MAP_DEPTH = 0.7;
		ROUGHNESS = mix(0.62, 0.25, wetness);
		SPECULAR = 0.4;
	}
}
