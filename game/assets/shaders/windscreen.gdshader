// The glass between the driver and the road (0.46, Windscreen): what a windscreen adds to the view through it. In the
// rain the drops bead on the glass and bend the road behind them like little lenses, more of them the longer since the
// wipers passed, and the blades sweep them off; between the sweeps a film of water softens the view. On a dry day the
// glass's dust and the fine arcs the wipers have scratched into it light up into a haze when the sun stands ahead. A
// quad over the whole view (drawn after everything, its depth test off): the cabin, nearer than the glass, is left
// alone; the glass is wherever the view reaches past it. Premultiplied alpha: the drops replace what is behind them,
// the haze adds to it.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, blend_premul_alpha, shadows_disabled, fog_disabled;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
// How hard it rains, 0 to 1, and the water left on the glass after the rain, 0 to 1.
uniform float rain = 0.0;
uniform float wet = 0.0;
// The share of the glass's places that can hold a drop: most in the rain, a few on a wet day.
uniform float drop_cover = 0.85;
// The wipers: their clock (seconds since they started), a sweep's length and the time from one sweep's start to the next.
uniform bool wipers = false;
uniform float wiper_clock = 0.0;
uniform float wiper_sweep = 1.1;
uniform float wiper_period = 1.1;
// The car's speed, m/s: the air pushes the drops up the glass and stretches them.
uniform float speed = 0.0;
// The sun in the camera's frame (towards it), its colour and how much of it the glass's dust takes (0 under cloud).
uniform vec3 sun_view = vec3(0.0, 0.0, -1.0);
uniform vec3 sun_colour : source_color = vec3(1.0, 0.95, 0.88);
uniform float glare = 0.0;
// Metres from the eye beyond which a pixel is seen through glass: the windscreen is 0.6 to 0.9 m off, the far end of the
// dashboard 1.5 m, the bonnet from 1.7 m.
uniform float glass_from = 1.7;

// The wipers' pivots and blades, on the view with y up and x in heights (the driver's on the left of a left-hand-drive
// car): parked pointing right along the foot of the glass, sweeping up to near upright.
const vec2 PIVOT_DRIVER = vec2(0.26, 0.30);
const vec2 PIVOT_PASSENGER = vec2(0.98, 0.30);
const float BLADE_FROM = 0.10;
const float BLADE_TO_DRIVER = 0.78;
const float BLADE_TO_PASSENGER = 0.66;
const float PARKED = 0.10;
const float RAISED = 1.62;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec3 hash32(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yxz + 33.33);
	return fract((p3.xxy + p3.yzz) * p3.zyx);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x), mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

// The blade's angle about its pivot now: parked, or along a sweep up and back with the eased motion of a crank.
float blade_angle() {
	float t = mod(wiper_clock, wiper_period);
	if (!wipers || t >= wiper_sweep) {
		return PARKED;
	}
	float x = t / wiper_sweep;
	float e = x < 0.5 ? 0.5 - 0.5 * cos(PI * 2.0 * x) : 0.5 - 0.5 * cos(PI * (2.0 - 2.0 * x));
	return mix(PARKED, RAISED, e);
}

// Seconds since a blade last passed the point g, or a long time for a point no blade reaches.
float since_wipe(vec2 g) {
	if (!wipers) {
		return 1e4;
	}
	float best = 1e4;
	for (int k = 0; k < 2; k++) {
		vec2 pivot = k == 0 ? PIVOT_DRIVER : PIVOT_PASSENGER;
		float reach = k == 0 ? BLADE_TO_DRIVER : BLADE_TO_PASSENGER;
		vec2 d = g - pivot;
		float r = length(d);
		float a = atan(d.y, d.x);
		if (r < BLADE_FROM || r > reach || a < PARKED || a > RAISED) {
			continue;
		}
		// Where on the sweep the blade passes this angle, going up and coming back.
		float f = (a - PARKED) / (RAISED - PARKED);
		float up = acos(1.0 - 2.0 * f) / PI * 0.5 * wiper_sweep;
		float down = wiper_sweep - up;
		float t = mod(wiper_clock, wiper_period);
		float s = t >= down ? t - down : t >= up ? t - up : t + wiper_period - down;
		best = min(best, s);
	}
	return best;
}

// One layer of drops on cells `cell` across: xy the lens's offset to sample the scene at (glass units), z the drop's
// cover (0 to 1, soft at its rim), w its rim's shade.
vec4 drops(vec2 g, float cell, float rate, float cap, float seed, float since) {
	vec2 c = floor(g / cell);
	vec4 found = vec4(0.0);
	// At speed the air drives the drops up the glass and draws them out into short streaks.
	float push = clamp((speed - 14.0) / 20.0, 0.0, 1.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id = c + vec2(float(i), float(j));
			vec3 h = hash32(id + seed);
			if (hash12(id * 3.1 + seed) > cap) {
				continue;
			}
			vec2 centre = (id + 0.15 + 0.7 * h.xy) * cell;
			// It lands at a time drawn from the rain's rate after the last sweep over it, and stays until the next.
			float landed = -log(1.0 - 0.999 * h.z) / max(rate, 1e-3);
			float age = since - landed;
			if (age < 0.0) {
				continue;
			}
			float radius = cell * mix(0.16, 0.40, hash12(id * 1.7 + seed));
			// The air's push: a landed drop creeps up, stretched along its way, faster as the car goes faster.
			centre.y += push * min(age, 3.0) * cell * 0.9 * fract(h.x * 7.3);
			vec2 d = g - centre;
			d.y /= 1.0 + push * 0.8;
			float r = length(d) / radius;
			if (r < 1.0) {
				float cover = 1.0 - smoothstep(0.82, 1.0, r);
				if (cover > found.z) {
					// A drop is a lens: it shows the scene behind it upside down and drawn in from round it.
					found = vec4(-d * 2.2, cover, smoothstep(0.7, 1.0, r));
				}
			}
		}
	}
	return found;
}

void vertex() {
	// A quad over the whole view, at the near plane.
	POSITION = vec4(VERTEX.xy * 2.0, 0.5, 1.0);
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	float distance_to = length(view.xyz);
	// The cabin (the dashboard, the pillars, the mirror) is nearer than the glass: nothing on it.
	if (distance_to < glass_from) {
		discard;
	}
	vec2 aspect = vec2(VIEWPORT_SIZE.x / VIEWPORT_SIZE.y, 1.0);
	vec2 g = vec2(SCREEN_UV.x, 1.0 - SCREEN_UV.y) * aspect;
	// Layers over the view in premultiplied alpha: the film, the drops over it, the blades over them.
	vec3 colour = vec3(0.0);
	float alpha = 0.0;
	float water = max(rain, wet * 0.35);
	if (water > 0.0) {
		float s = since_wipe(g);
		// The film between the sweeps: the view goes soft as water gathers (and stays soft where no blade reaches).
		float film = rain * 0.55 * smoothstep(0.0, 4.0, s);
		if (film > 0.0) {
			colour = textureLod(screen_texture, SCREEN_UV, 2.5).rgb * film;
			alpha = film;
		}
		// Two sizes of drops; the rate is drops a cell a second.
		// Drops of 3 to 9 mm and of 2 to 5 mm on glass 0.8 m off, as a cell of 0.03 and of 0.016 view heights frames them.
		// The time since the blade passed is this pixel's for every drop round it: a drop the blade is crossing shows cut
		// for that frame, as the blade cuts it (a time a drop, at its centre, cost 36 arc functions a pixel).
		vec4 big = drops(g, 0.03, water * 0.15, drop_cover, 3.1, s);
		vec4 small = drops(g, 0.016, water * 0.4, drop_cover, 7.7, s);
		vec4 drop = big.z >= small.z ? big : small;
		if (drop.z > 0.0) {
			vec2 at = SCREEN_UV + vec2(drop.x, -drop.y) / aspect;
			vec3 lens = textureLod(screen_texture, clamp(at, vec2(0.001), vec2(0.999)), 0.5).rgb;
			// Darker at the rim, where the light bends off; a glint of the sky near the top.
			lens *= mix(1.0, 0.65, drop.w);
			lens += vec3(0.25) * (1.0 - drop.w) * smoothstep(0.6, 0.0, length(vec2(drop.x, drop.y) - vec2(-0.004, 0.006)) * 120.0);
			colour = lens * drop.z + colour * (1.0 - drop.z);
			alpha = drop.z + alpha * (1.0 - drop.z);
		}
		// The blades themselves: dark rubber on a thin arm, some 2 cm across at 0.8 m.
		float angle = blade_angle();
		for (int k = 0; k < 2; k++) {
			vec2 pivot = k == 0 ? PIVOT_DRIVER : PIVOT_PASSENGER;
			float reach = k == 0 ? BLADE_TO_DRIVER : BLADE_TO_PASSENGER;
			vec2 along = vec2(cos(angle), sin(angle));
			vec2 d = g - pivot;
			float t = dot(d, along);
			float off = abs(d.x * along.y - d.y * along.x);
			if (t > BLADE_FROM && t < reach && wipers) {
				float blade = 1.0 - smoothstep(0.0085, 0.0105, off);
				colour = vec3(0.02) * blade + colour * (1.0 - blade);
				alpha = blade + alpha * (1.0 - blade);
			}
		}
	}
	if (glare > 0.0) {
		// The dust and the wipers' arcs catch the sun standing ahead: a haze round it, brighter close to it.
		vec3 eye_ray = normalize(view.xyz);
		float facing = max(dot(eye_ray, normalize(sun_view)), 0.0);
		// Nothing to see more than 50 degrees off the sun: the noises are not worked out there.
		if (facing > 0.64) {
		float halo = pow(facing, 24.0) * 0.5 + pow(facing, 5.0) * 0.12;
		float dust = 0.55 + 0.45 * vnoise(g * 23.0) * vnoise(g * 61.0 + 4.0);
		float arcs = 0.0;
		for (int k = 0; k < 2; k++) {
			vec2 pivot = k == 0 ? PIVOT_DRIVER : PIVOT_PASSENGER;
			float r = length(g - pivot);
			float ring = fract(r * 340.0 + vnoise(g * 9.0) * 3.0);
			arcs += smoothstep(0.92, 1.0, ring) * step(BLADE_FROM, r) * step(r, k == 0 ? BLADE_TO_DRIVER : BLADE_TO_PASSENGER) * vnoise(g * 30.0 + float(k) * 11.0);
		}
		colour += sun_colour * glare * halo * (dust + 1.6 * arcs);
		}
	}
	ALBEDO = colour;
	ALPHA = alpha;
}
