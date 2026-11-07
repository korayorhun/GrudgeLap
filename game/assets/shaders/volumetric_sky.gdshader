// The sky with volumetric clouds (0.43, WorldBuilder.BuildEnvironment): the clear-sky photo (the same HDRI the panorama
// material showed, so the sun's place and the light are as before) with fair-weather cumulus marched through at a
// quarter of the resolution: a slab from 1.7 to 3.2 km over the valley, its clouds shaped by a 3D noise and thinned into
// clear and cloudy stretches by a slow 2D one, lit by the sun through their own depth (Beer's law, a few steps towards
// it, with the powder effect at their edges and a forward-scattering phase), and by the sky from above. Until 0.43 the
// clouds were a flat sheet with a painted underside. The radiance map (the scene's ambient light and reflections) is the
// photo alone, so the clouds' drift does not re-render it.
shader_type sky;
render_mode use_quarter_res_pass;

uniform sampler2D panorama : source_color, filter_linear, repeat_enable;
uniform float panorama_energy = 1.0;
uniform sampler3D shape_noise : filter_linear_mipmap, repeat_enable;
uniform sampler3D detail_noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D weather_map : filter_linear, repeat_enable;
uniform float cloud_bottom = 1700.0;
uniform float cloud_top = 3200.0;
uniform float coverage = 0.41;
uniform float density = 0.045;
uniform vec2 wind = vec2(0.0);
uniform vec3 sun_tint : source_color = vec3(1.0, 0.97, 0.92);
uniform float sun_strength = 3.6;
uniform vec3 ambient_top : source_color = vec3(0.58, 0.68, 0.85);
uniform vec3 ambient_bottom : source_color = vec3(0.34, 0.37, 0.42);
uniform float ambient_strength = 1.0;
uniform float max_distance = 40000.0;
// The air between the eye and a cloud: how far it takes to fade a cloud into the sky behind it, and how far it goes.
// The clear sky's 28 km and 0.8; under a rain deck (0.53) the air is as thick as the fog that hides the land, so the
// deck fades into the same grey towards the horizon as the mountains do (with the clear sky's haze it stayed dark
// down to the horizon, and the fogged mountains stood against it as pale cut-outs).
uniform float haze_distance = 28000.0;
uniform float haze_strength = 0.8;
// The overcast's sky (0.53): no photo behind the deck but a grey dome, lighter towards the horizon as a real overcast is,
// and darker below it.
uniform bool use_photo = true;
uniform vec3 dome_top : source_color = vec3(0.34, 0.37, 0.42);
uniform vec3 dome_horizon : source_color = vec3(0.60, 0.62, 0.66);
uniform vec3 dome_ground : source_color = vec3(0.30, 0.31, 0.31);

vec3 dome(vec3 d) {
	return d.y >= 0.0 ? mix(dome_horizon, dome_top, pow(clamp(d.y, 0.0, 1.0), 0.6)) : mix(dome_horizon, dome_ground, clamp(-d.y * 4.0, 0.0, 1.0));
}


float cloud(vec3 p) {
	float h = (p.y - cloud_bottom) / (cloud_top - cloud_bottom);
	if (h <= 0.0 || h >= 1.0) {
		return 0.0;
	}
	vec3 q = p + vec3(wind.x, 0.0, wind.y);
	// Clear and cloudy stretches some kilometres across.
	float weather = texture(weather_map, q.xz / 26000.0).r;
	float cover = clamp(coverage + (weather - 0.5) * 0.9, 0.0, 1.0);
	// A cumulus's profile: a flat, sharp base, rounded and thinning towards its top.
	float profile = smoothstep(0.0, 0.08, h) * (1.0 - smoothstep(0.35 + 0.45 * cover, 1.0, h));
	// The noise as fine up as across (its texture is half as tall as it is wide): until 0.44 it changed over twice the
	// height, and the clouds stood as columns with flat walls.
	float shape = texture(shape_noise, q / vec3(5200.0, 2600.0, 5200.0)).r;
	float d = shape * profile - (1.0 - cover);
	if (d <= 0.0) {
		return 0.0;
	}
	// The billows eaten into its edges.
	float detail = texture(detail_noise, q / 1100.0 + vec3(0.0, h * 0.3, 0.0)).r;
	d -= (1.0 - detail) * 0.32 * (1.0 - clamp(d * 3.0, 0.0, 1.0));
	return max(d, 0.0) * density;
}

void sky() {
	vec3 photo = use_photo ? texture(panorama, SKY_COORDS).rgb * panorama_energy : dome(EYEDIR);
	if (AT_CUBEMAP_PASS) {
		COLOR = photo;
	} else if (AT_QUARTER_RES_PASS) {
		vec3 dir = EYEDIR;
		COLOR = vec3(0.0);
		ALPHA = 0.0;
		if (dir.y > 0.012) {
			float t0 = (cloud_bottom - POSITION.y) / dir.y;
			float t1 = min((cloud_top - POSITION.y) / dir.y, max_distance);
			if (t0 < t1) {
				const int STEPS = 64;
				float step_len = (t1 - t0) / float(STEPS);
				// The steps at the middles of equal parts, the same for every pixel (0.45): any offset varied from pixel to pixel
				// showed as a pattern on the clouds (blotches from a random hash until 0.44, diagonal streaks from interleaved
				// gradient noise; the view's temporal anti-aliasing does not reach the quarter-resolution pass to average a
				// changing one), and 64 steps keep the bands this leaves faint.
				float t = t0 + step_len * 0.5;
				float transmittance = 1.0;
				vec3 light = vec3(0.0);
				vec3 sun = normalize(LIGHT0_DIRECTION);
				float mu = dot(dir, sun);
				// Henyey-Greenstein, a forward lobe and a little back: the silver lining towards the sun.
				float g1 = 0.55;
				float g2 = -0.2;
				float hg = mix((1.0 - g2 * g2) / pow(1.0 + g2 * g2 - 2.0 * g2 * mu, 1.5), (1.0 - g1 * g1) / pow(1.0 + g1 * g1 - 2.0 * g1 * mu, 1.5), 0.7) / (4.0 * PI);
				for (int i = 0; i < STEPS; i++) {
					vec3 p = POSITION + dir * t;
					float d = cloud(p);
					if (d > 0.0) {
						// The sun's light through the cloud above this point: four steps towards it.
						float od = 0.0;
						for (int k = 1; k <= 4; k++) {
							od += cloud(p + sun * (float(k) * 160.0)) * 160.0;
						}
						float beer = exp(-od);
						float powder = 1.0 - exp(-od * 2.0);
						float h = (p.y - cloud_bottom) / (cloud_top - cloud_bottom);
						vec3 sunlit = sun_tint * sun_strength * LIGHT0_ENERGY * beer * mix(1.0, powder, 0.3) * hg * 4.0;
						vec3 ambient = mix(ambient_bottom, ambient_top, h) * ambient_strength;
						float absorbed = 1.0 - exp(-d * step_len);
						light += transmittance * absorbed * (sunlit + ambient);
						transmittance *= 1.0 - absorbed;
						if (transmittance < 0.03) {
							break;
						}
					}
					t += step_len;
				}
				// A cloud the march gave up in is opaque: at the cut-off's 3 % the photo's sun, thousands of times the sky's
				// brightness, still shone through a thick cumulus as a white point (0.46).
				if (transmittance < 0.03) {
					transmittance = 0.0;
				}
				// The air between: far clouds fade into the sky's own colour, near the horizon.
				float haze = 1.0 - exp(-t0 / haze_distance);
				light = mix(light, photo * (1.0 - transmittance), haze * haze_strength);
				COLOR = light;
				ALPHA = 1.0 - transmittance;
			}
		}
	} else {
		vec4 clouds = QUARTER_RES_COLOR;
		COLOR = photo * (1.0 - clouds.a) + clouds.rgb;
	}
}
