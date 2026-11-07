// The volumetric clouds' shadows on the ground (0.44, CloudShadows): the sun's light at a point through the cloud
// layer the sky marches (assets/shaders/volumetric_sky.gdshader), the same clouds by the same noises without their
// billowed edges (a cloud's shadow is soft anyway). The sun itself is dimmed by the clouds over the camera
// (CloudShadows.Update: the car drives into the shade with everything round it), so a material lights a point by its
// own share of the sun over the camera's, out of a few hundred metres. Included by the land, the plain's ground and the
// trees' impostors; CloudShadows sets these uniforms on each of them.
uniform sampler3D cloud_shape : filter_linear, repeat_enable;
uniform sampler2D cloud_weather : filter_linear, repeat_enable;
// x the layer's base and y its top over the plain, z the coverage, w the density: the sky's own values.
uniform vec4 cloud_layer = vec4(1700.0, 3200.0, 0.41, 0.045);
uniform vec2 cloud_wind = vec2(0.0);
uniform vec3 cloud_sun = vec3(-0.431, 0.682, -0.591);
// The camera's share of the sun, which the light's energy already carries.
uniform float cloud_here = 1.0;
uniform bool cloud_shadows = false;

float cloud_shadow_density(vec3 p) {
	float h = (p.y - cloud_layer.x) / (cloud_layer.y - cloud_layer.x);
	vec3 q = p + vec3(cloud_wind.x, 0.0, cloud_wind.y);
	float cover = clamp(cloud_layer.z + (textureLod(cloud_weather, q.xz / 26000.0, 0.0).r - 0.5) * 0.9, 0.0, 1.0);
	float profile = smoothstep(0.0, 0.08, h) * (1.0 - smoothstep(0.35 + 0.45 * cover, 1.0, h));
	return max(textureLod(cloud_shape, q / vec3(5200.0, 2600.0, 5200.0), 0.0).r * profile - (1.0 - cover), 0.0) * cloud_layer.w;
}

// The share of the sun's light that reaches p through the clouds: 1 in the open, 0.12 under a thick cloud (the light
// it scatters on). Four steps towards the sun through the layer.
float cloud_sunlight(vec3 p) {
	float t0 = max((cloud_layer.x - p.y) / cloud_sun.y, 0.0);
	float t1 = (cloud_layer.y - p.y) / cloud_sun.y;
	if (t1 <= 0.0) {
		return 1.0;
	}
	float step_len = (t1 - t0) * 0.25;
	float od = 0.0;
	for (int i = 0; i < 4; i++) {
		od += cloud_shadow_density(p + cloud_sun * (t0 + (float(i) + 0.5) * step_len));
	}
	return mix(0.12, 1.0, exp(-od * step_len));
}

// A point's sun against the camera's: its own share out of 1.5 km, the camera's within 500 m, so the ground round the
// car takes the shade with the car and a cloud's shadow is seen moving over the plain and the mountains beyond.
float cloud_sun_at(vec3 p, float dist) {
	float w = smoothstep(500.0, 1500.0, dist);
	return w <= 0.0 ? cloud_here : mix(cloud_here, cloud_sunlight(p), w);
}

// For a custom light(): the factor on the sun's light at p.
float cloud_light_ratio(vec3 p, float dist) {
	return cloud_shadows ? cloud_sun_at(p, dist) / cloud_here : 1.0;
}

// For a material on the engine's own lighting: the factor on its albedo that gives p its own sun against the camera's,
// the sky's light (taken as three tenths of a sunlit surface's) unchanged.
float cloud_albedo_factor(vec3 p, float dist) {
	return cloud_shadows ? (0.3 + 0.7 * cloud_sun_at(p, dist)) / (0.3 + 0.7 * cloud_here) : 1.0;
}
