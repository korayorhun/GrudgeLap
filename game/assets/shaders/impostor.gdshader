// A photoscanned tree as an impostor (0.39, FoliageBuilder): a card that turns to face the camera about the vertical,
// showing the tree as it was rendered from the nearest of twelve bearings round it (tools/bake-impostors.py), the two
// nearest frames dithered into each other as the camera goes round; the normals baked with each frame light the
// leaves and the trunk as the real tree's would. Until 0.39 the trees were crossed cards of a leaf-cluster photo.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D normal_atlas : filter_linear_mipmap_anisotropic, repeat_disable;
uniform float views = 12.0;
uniform float columns = 4.0;
uniform float rows = 3.0;
uniform float frame_metres = 10.0;
// The tree's height over the frame's (a broad crown's frame is wider than the tree is tall).
uniform float tree_fraction = 1.0;
uniform vec3 tint : source_color = vec3(1.0);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform bool retro = false;
uniform vec3 retro_colour : source_color = vec3(0.20, 0.42, 0.14);

varying flat float frame_a;
varying flat float frame_b;
varying flat float fade;
varying flat vec3 shade;


void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float sx = length(MODEL_MATRIX[0].xyz);
	float sy = length(MODEL_MATRIX[1].xyz);
	float sz = length(MODEL_MATRIX[2].xyz);
	// Towards the camera along the ground; in the shadow pass the "camera" is the light, so the card turns to it.
	vec3 to_cam = INV_VIEW_MATRIX[3].xyz - origin;
	to_cam.y = 0.0;
	float distance = length(to_cam);
	to_cam = distance > 0.001 ? to_cam / distance : vec3(0.0, 0.0, 1.0);
	vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), to_cam));
	vec3 world = origin + right * (UV.x - 0.5) * frame_metres * sx + vec3(0.0, (1.0 - UV.y) * frame_metres * sy, 0.0);
	// The camera's bearing in the tree's own frame picks the frames (bearing 0 looks from the tree's -z side, as Blender
	// rendered view 0 from its +y).
	mat3 basis = mat3(MODEL_MATRIX[0].xyz / sx, MODEL_MATRIX[1].xyz / sy, MODEL_MATRIX[2].xyz / sz);
	vec3 local = transpose(basis) * to_cam;
	float bearing = atan(local.x, -local.z);
	if (bearing < 0.0) {
		bearing += TAU;
	}
	float f = bearing / (TAU / views);
	frame_a = mod(floor(f), views);
	frame_b = mod(frame_a + 1.0, views);
	fade = fract(f);
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
	// Each tree a little different in its greens.
	float h = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);
	shade = mix(vec3(0.86, 0.90, 0.86), vec3(1.08, 1.06, 1.0), h);
}

void fragment() {
	float dither = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
	float frame = dither < fade ? frame_b : frame_a;
	vec2 cell = vec2(mod(frame, columns), floor(frame / columns));
	vec2 uv = (cell + clamp(UV, vec2(0.002), vec2(0.998))) / vec2(columns, rows);
	// The mip level from the card's own coordinates: the frame picked per pixel jumps between cells of the atlas,
	// and derivatives taken across the jump chose the blurriest mip, a cloud of speckles round every tree.
	vec2 grad_x = dFdx(UV) / vec2(columns, rows);
	vec2 grad_y = dFdy(UV) / vec2(columns, rows);
	vec4 c = textureGrad(albedo_atlas, uv, grad_x, grad_y);
	// The leaves' coverage held at a distance: a mipmap averages a leaf with the gaps round it, and the cut at one
	// half then thins the far trees to a scatter of dots. The alpha is raised with the mip level the sample takes.
	vec2 size = vec2(textureSize(albedo_atlas, 0));
	float lod = 0.5 * log2(max(max(dot(grad_x * size, grad_x * size), dot(grad_y * size, grad_y * size)), 1e-6));
	ALPHA = clamp(c.a * (1.0 + 0.45 * max(lod, 0.0)), 0.0, 1.0);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	// The baked normal: Blender's camera frame, x right, y up, z along the view (so towards the camera is -z).
	vec3 n = textureGrad(normal_atlas, uv, grad_x, grad_y).rgb * 2.0 - 1.0;
	n.z = -n.z;
	// Half the baked leaf's own normal, half a round crown's: lit on the sun's side, shaded on the other, as a
	// canopy reads from afar (the leaves' normals alone scatter the light and some trees read pale).
	float h = (1.0 - UV.y) / max(tree_fraction, 0.05);
	vec2 p = vec2((UV.x - 0.5) / 0.5, (h - 0.62) / 0.45);
	vec3 crown = normalize(vec3(p.x, p.y, sqrt(max(1.0 - dot(p, p), 0.08))));
	n = normalize(mix(n, crown, 0.55));
	float theta = frame * TAU / views;
	vec3 r = vec3(-cos(theta), 0.0, -sin(theta));
	vec3 d = vec3(sin(theta), 0.0, -cos(theta));
	vec3 local_n = normalize(n.x * r + vec3(0.0, n.y, 0.0) + n.z * d);
	vec3 world_n = normalize(mat3(MODEL_MATRIX) * local_n);
	NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	// Darker low in the crown and under it, where the leaves shade each other: the bake is unlit.
	float inner = mix(0.42, 1.0, smoothstep(0.05, 0.85, h));
	vec3 albedo = retro ? retro_colour : c.rgb * tint * shade * inner;
	ALBEDO = albedo * mix(1.0, 0.75, wetness);
	BACKLIGHT = albedo * 0.12;
	ROUGHNESS = mix(0.85, 0.55, wetness);
	SPECULAR = 0.25;
}
