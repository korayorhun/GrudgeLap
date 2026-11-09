#[compute]
#version 450

// The windscreen's views of the scene (0.77, WindscreenGlass): before the transparent pass, how far the eye is from what
// the opaque pass drew (mode 2: the depth unprojected); after it, the colour copied (mode 0) and each of its mip levels
// averaged from the level above, two by two (mode 1), as Godot makes its screen texture's.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D source;
layout(rgba16f, set = 0, binding = 1) uniform restrict writeonly image2D target;

layout(push_constant, std430) uniform Params {
	mat4 inv_projection;
	vec2 size;   // the target's size in pixels
	float mode;
	float pad;
} params;

void main() {
	ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
	if (pixel.x >= int(params.size.x) || pixel.y >= int(params.size.y)) {
		return;
	}
	if (params.mode < 0.5) {
		imageStore(target, pixel, texelFetch(source, pixel, 0));
	} else if (params.mode < 1.5) {
		ivec2 from = pixel * 2;
		ivec2 top = textureSize(source, 0) - 1;
		vec4 sum = texelFetch(source, min(from, top), 0) + texelFetch(source, min(from + ivec2(1, 0), top), 0)
			+ texelFetch(source, min(from + ivec2(0, 1), top), 0) + texelFetch(source, min(from + ivec2(1, 1), top), 0);
		imageStore(target, pixel, sum * 0.25);
	} else {
		vec2 uv = (vec2(pixel) + 0.5) / params.size;
		vec4 view = params.inv_projection * vec4(uv * 2.0 - 1.0, texelFetch(source, pixel, 0).r, 1.0);
		imageStore(target, pixel, vec4(length(view.xyz / view.w)));
	}
}
