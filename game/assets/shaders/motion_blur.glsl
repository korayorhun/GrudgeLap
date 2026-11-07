#[compute]
#version 450

// The camera's motion blur (0.38, MotionBlur.cs): each pixel smeared along its own motion over a fixed exposure, as a
// camera's shutter or the eye at speed smears the world going past, so the verge and the grass streak at speed while
// the cabin and the car the camera rides with stay sharp. Godot's motion vectors (the frame's movement in UV) are
// scaled by the exposure over the frame's time, so the smear is the same at 30 and at 90 frames a second, and held to
// a maximum; a sample is counted only as far as it moves at least as fast as the pixel itself, so a still edge (the
// car's body, the windscreen's frame) does not bleed into the moving world behind it.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D source_colour;
layout(set = 0, binding = 1) uniform sampler2D motion;
layout(rgba16f, set = 0, binding = 2) uniform restrict writeonly image2D target;

layout(push_constant, std430) uniform Params {
	vec2 size;
	float scale;      // the exposure over this frame's duration
	float max_pixels; // the longest smear
} params;

const int SAMPLES = 12;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void main() {
	ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
	if (pixel.x >= int(params.size.x) || pixel.y >= int(params.size.y)) {
		return;
	}
	vec2 uv = (vec2(pixel) + 0.5) / params.size;
	vec2 velocity = textureLod(motion, uv, 0.0).xy * params.scale;
	vec2 in_pixels = velocity * params.size;
	float length_pixels = length(in_pixels);
	vec4 centre = textureLod(source_colour, uv, 0.0);
	if (length_pixels < 0.75) {
		imageStore(target, pixel, centre);
		return;
	}
	if (length_pixels > params.max_pixels) {
		velocity *= params.max_pixels / length_pixels;
		length_pixels = params.max_pixels;
	}
	// The samples spread over the smear symmetrically, jittered per pixel so the steps do not show as copies.
	float jitter = hash(vec2(pixel)) - 0.5;
	vec4 sum = centre;
	float weight = 1.0;
	for (int i = 0; i < SAMPLES; i++) {
		float t = (float(i) + 0.5 + jitter) / float(SAMPLES) - 0.5;
		vec2 at = uv + velocity * t;
		vec2 its = textureLod(motion, at, 0.0).xy * params.scale * params.size;
		float w = clamp(length(its) / length_pixels * 1.5, 0.0, 1.0);
		sum += textureLod(source_colour, at, 0.0) * w;
		weight += w;
	}
	imageStore(target, pixel, sum / weight);
}
