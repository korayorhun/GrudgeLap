// The wind at the ground (0.45): the breeze the clouds drift on (WorldBuilder.CloudDrift; the sky's clouds move to
// Godot's -x and -z, west-south-west on the map), slower down here, in gusts that run downwind across the plain. The
// grass tufts bend to it, the trees sway, the meadow shows the paler sides of its blades as a gust runs over it, and
// the wheels' dust drifts with it (WheelDust). Shared by tuft, terrain and impostor shaders.
const vec2 WIND_DIR = vec2(-0.944, -0.330);
// Metres a second a metre over the grass: a fresh breeze (the clouds' drift at 2.8 km is twice CloudDrift's 5.3 m/s).
const float WIND_SPEED = 4.5;

float wind_hash(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float wind_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(wind_hash(i), wind_hash(i + vec2(1.0, 0.0)), u.x), mix(wind_hash(i + vec2(0.0, 1.0)), wind_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// How hard the wind blows at p (Godot's x and z) at time t: 0 in a lull, 1 in a gust. A gust's front lies across the
// wind, tens of metres wide and a few deep, and runs downwind a little faster than the breeze.
float wind_gust(vec2 p, float t) {
	vec2 across = vec2(-WIND_DIR.y, WIND_DIR.x);
	float along = dot(p, WIND_DIR) - WIND_SPEED * 1.3 * t;
	float side = dot(p, across);
	float n = wind_noise(vec2(along / 24.0, side / 70.0)) * 0.65 + wind_noise(vec2(along / 8.0, side / 18.0) + 17.0) * 0.35;
	return smoothstep(0.38, 0.82, n);
}

// The sway of a plant's tip downwind, metres per metre of height: a steady lean, more in a gust, a quick flutter over it.
vec3 wind_lean(vec2 p, float t, float stiffness) {
	float gust = wind_gust(p, t);
	float flutter = sin(t * 6.3 + dot(p, vec2(1.7, 2.3))) * (0.3 + 0.7 * gust);
	return vec3(WIND_DIR.x, 0.0, WIND_DIR.y) * (0.06 + 0.22 * gust + 0.05 * flutter) / stiffness;
}
