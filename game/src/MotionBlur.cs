using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The camera's motion blur (0.38): a compositor effect that smears each pixel along its own motion over a fixed
/// exposure (assets/shaders/motion_blur.glsl), so the verge and the grass streak past at speed while the cabin and the
/// car the camera follows stay sharp. Until 0.38 every frame was a pin-sharp still, and at 160 km/h the world went
/// past in steps; a camera, and the eye, see it smeared.
/// </summary>
/// <remarks>
/// Runs on the render thread before the transparent pass (after it until 0.46), in linear light before glow and tone
/// mapping: the colour is
/// blurred into a copy and the copy written back. <see cref="FrameSeconds"/> comes from the main thread every frame
/// (a float, read once a frame: a frame late at worst); the smear is the motion over <see cref="Exposure"/>, not over
/// the frame, so it is the same at 30 and at 90 frames a second.
/// </remarks>
[GlobalClass]
public partial class MotionBlur : CompositorEffect
{
    private const string Context = "glp_motion_blur";
    private const string CopyName = "copy";

    /// <summary>The exposure the smear stands for: a hundredth of a second, a camera's shutter on a bright day.</summary>
    public float Exposure { get; set; } = 1f / 100f;

    /// <summary>This frame's duration, set by the camera rig every frame.</summary>
    public float FrameSeconds { get; set; } = 1f / 60f;

    /// <summary>The longest smear, as a share of the frame's width.</summary>
    public float MaxShare { get; set; } = 0.035f;

    private RenderingDevice? _device;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _sampler;

    public MotionBlur()
    {
        // Before the transparent pass (0.46; after it until then): what is drawn on the glass (Windscreen) stays sharp
        // as the road behind it streams by, and the particles (rain, dust, smoke) are soft anyway.
        EffectCallbackType = EffectCallbackTypeEnum.PreTransparent;
        NeedsMotionVectors = true;
        AccessResolvedColor = true;
        RenderingServer.CallOnRenderThread(Callable.From(Initialise));
    }

    private void Initialise()
    {
        _device = RenderingServer.GetRenderingDevice();
        if (_device is null) return;
        var file = GD.Load<RDShaderFile>("res://assets/shaders/motion_blur.glsl");
        if (file?.GetSpirV() is not { } spirv) return;
        _shader = _device.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) return;
        _pipeline = _device.ComputePipelineCreate(_shader);
        _sampler = _device.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _device is null) return;
        // The pipeline goes with its shader.
        if (_shader.IsValid) _device.FreeRid(_shader);
        if (_sampler.IsValid) _device.FreeRid(_sampler);
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (_device is null || !_pipeline.IsValid || effectCallbackType != (int)EffectCallbackTypeEnum.PreTransparent) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers) return;
        var size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0) return;
        var views = buffers.GetViewCount();
        if (!buffers.HasTexture(Context, CopyName))
        {
            const uint usage = (uint)(RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanCopyFromBit);
            buffers.CreateTexture(Context, CopyName, RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage, RenderingDevice.TextureSamples.Samples1, size, views, 1, true, false);
        }
        var scale = Math.Clamp(Exposure / MathF.Max(FrameSeconds, 1e-4f), 0f, 2f);
        var maxPixels = MaxShare * size.X;
        var groupsX = (uint)((size.X + 7) / 8);
        var groupsY = (uint)((size.Y + 7) / 8);
        for (var view = 0u; view < views; view++)
        {
            var colour = buffers.GetColorLayer(view);
            var motion = buffers.GetVelocityLayer(view);
            var copy = buffers.GetTextureSlice(Context, CopyName, view, 0, 1, 1);
            if (!colour.IsValid || !motion.IsValid || !copy.IsValid) return;
            // The colour blurred into the copy, then the copy written back unblurred (a scale of 0) into the colour.
            // The colour can be written only without multisampling: resolved from MSAA it has neither storage nor
            // copy-to usage (Bootstrap.ApplyGraphics turns MSAA off while the blur is on; TAA does the smoothing).
            Pass(colour, motion, copy, size, scale, maxPixels, groupsX, groupsY);
            Pass(copy, motion, colour, size, 0f, maxPixels, groupsX, groupsY);
        }
    }

    private void Pass(Rid source, Rid motion, Rid target, Vector2I size, float scale, float maxPixels, uint groupsX, uint groupsY)
    {
        var device = _device!;
        var sourceUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
        sourceUniform.AddId(_sampler);
        sourceUniform.AddId(source);
        var motionUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        motionUniform.AddId(_sampler);
        motionUniform.AddId(motion);
        var targetUniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 2 };
        targetUniform.AddId(target);
        var set = UniformSetCacheRD.GetCache(_shader, 0, [sourceUniform, motionUniform, targetUniform]);
        var constants = new float[] { size.X, size.Y, scale, maxPixels };
        var bytes = new byte[constants.Length * sizeof(float)];
        Buffer.BlockCopy(constants, 0, bytes, 0, bytes.Length);
        var list = device.ComputeListBegin();
        device.ComputeListBindComputePipeline(list, _pipeline);
        device.ComputeListBindUniformSet(list, set, 0);
        device.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        device.ComputeListDispatch(list, groupsX, groupsY, 1);
        device.ComputeListEnd();
    }
}
