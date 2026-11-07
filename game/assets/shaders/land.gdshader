// The real land round the plain (0.37, RealLand): Jackson Hole's ground in its satellite colour, lit by the game's
// sun with the mountains' own shadows from the data (the shadow maps end at 350 m) and darker in the gullies, where
// less of the sky reaches. By the game's terrain the colour turns to the plain's own green, so the seam is not seen;
// where the image's 9 m texels are magnified (the near valley floor) a little grain keeps the ground from going soft.
// The clouds' shadows drift over it (0.44, cloud_shadow.gdshaderinc), worked out at each vertex: soft, a kilometre
// across, and the vertices tens of metres apart out there.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert, specular_disabled;
#include "res://assets/shaders/cloud_shadow.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 plain_colour : source_color = vec3(0.36, 0.45, 0.20);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
// The hour (0.55, WorldBuilder.SetSunrise): 1 at sunrise, whose sun's shadows the vertices carry in their alpha.
uniform float sunrise : hint_range(0.0, 1.0) = 0.0;

// COLOR: r the game's sun's visibility, g the sky's, b the weight of the image over the plain's colour, a the sunrise's
// sun's visibility.
varying float sun_vis;
varying float cloud_ratio;
varying vec3 world_pos;

void vertex() {
	sun_vis = mix(COLOR.r, COLOR.a, sunrise);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	cloud_ratio = cloud_light_ratio(world_pos, length(world_pos - CAMERA_POSITION_WORLD));
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
	vec3 image = texture(albedo_tex, UV).rgb;
	vec3 c = mix(plain_colour, image, COLOR.b);
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	float grain = (vnoise(world_pos.xz / 3.0) - 0.5) * 0.7 + (vnoise(world_pos.xz / 11.0) - 0.5) * 0.3;
	c *= 1.0 + 0.16 * grain * (1.0 - smoothstep(2500.0, 7000.0, dist));
	ALBEDO = c * mix(1.0, 0.7, wetness);
	ROUGHNESS = 0.95;
	// The sky's share darkens only the light from the sky.
	AO = COLOR.g;
	AO_LIGHT_AFFECT = 0.0;
}

void light() {
	float shadow = LIGHT_IS_DIRECTIONAL ? sun_vis * cloud_ratio : 1.0;
	DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI * shadow;
}
