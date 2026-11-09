using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The windscreen's drops, film, blades and haze drawn over the cockpit view after the transparent pass (0.77, Windscreen,
/// assets/shaders/windscreen.glsl): the scene is copied then with its mip levels, transparent surfaces and all, and the
/// glass is blended over it in premultiplied alpha. Until 0.77 a spatial shader drew the glass in the transparent pass
/// from Godot's screen texture, which is copied before that pass, so the loop's blended grating was missing from it: in
/// the rain every drop showed the ground or the sky through the deck, and the water film faded the deck by up to 55 %.
/// </summary>
/// <remarks>
/// Two instances on the camera's compositor: <see cref="DistancePass"/> before the transparent pass keeps how far the eye
/// is from what the opaque pass drew, so that the glass leaves the cabin alone (the ghost's depth-only shell, drawn in the
/// transparent pass, would otherwise be taken for it); the glass itself after the pass. Both run on the render thread; the
/// glass's state is set from the main thread every frame (a float each, read once a frame: a frame late at worst). They
/// draw into the colour Godot renders to, which the motion blur's MSAA rule leaves single-sampled; under MSAA (the blur
/// switched off) they draw into the resolved colour where it takes a draw, and otherwise leave the view without glass.
/// </remarks>
internal sealed partial class WindscreenGlass : CompositorEffect
{
    private const string Context = "glp_windscreen";
    private const string SceneName = "scene";
    private const string DistanceName = "distance";
    /// <summary>The scene's levels the glass reads: the drops' lenses between 0 and 1, the film between 2 and 3.</summary>
    private const uint SceneLevels = 4;
    /// <summary>Bytes of the glass's uniform block: the inverse projection and five vectors (windscreen.glsl's Glass).</summary>
    private const uint GlassBlockSize = 16 * 4 + 5 * 16;

    private readonly bool _distanceOnly;
    private RenderingDevice? _device;
    private bool _failed;
    private Rid _copyShader;
    private Rid _copyPipeline;
    private Rid _glassShader;
    private Rid _glassPipeline;
    private long _glassPipelineFormat = RenderingDevice.InvalidFormatId;
    private Rid _glassBlock;
    private Rid _framebuffer;
    private Rid _framebufferColour;
    private Rid _nearest;
    private Rid _linear;

    public WindscreenGlass() : this(false) { }

    private WindscreenGlass(bool distanceOnly)
    {
        _distanceOnly = distanceOnly;
        EffectCallbackType = distanceOnly ? EffectCallbackTypeEnum.PreTransparent : EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = !distanceOnly;
        AccessResolvedDepth = distanceOnly;
    }

    /// <summary>The pass before the transparent one that keeps the opaque scene's distance from the eye for the glass.</summary>
    public static WindscreenGlass DistancePass() => new(true);

    /// <summary>How hard it rains, 0 to 1.</summary>
    public float Rain { get; set; }
    /// <summary>The water left on the glass after the rain, 0 to 1.</summary>
    public float Wet { get; set; }
    public float DropCover { get; set; } = 0.85f;
    public bool Wipers { get; set; }
    public float WiperClock { get; set; }
    public float WiperSweep { get; set; } = Windscreen.WiperSweep;
    public float WiperPeriod { get; set; } = Windscreen.WiperSweep;
    /// <summary>The car's speed, m/s.</summary>
    public float Speed { get; set; }
    /// <summary>The sun in the camera's frame (towards it).</summary>
    public Vector3 SunView { get; set; } = new(0f, 0f, -1f);
    public float Glare { get; set; }
    public float Glint { get; set; } = 1f;
    public float CabinLight { get; set; } = 1f;

    private bool Initialise()
    {
        if (_device is not null || _failed) return !_failed;
        _device = RenderingServer.GetRenderingDevice();
        _failed = true;
        if (_device is null) return false;
        if (GD.Load<RDShaderFile>("res://assets/shaders/windscreen_copy.glsl")?.GetSpirV() is not { } copy) return false;
        _copyShader = _device.ShaderCreateFromSpirV(copy);
        if (!_copyShader.IsValid) return false;
        _copyPipeline = _device.ComputePipelineCreate(_copyShader);
        _nearest = _device.SamplerCreate(new RDSamplerState
        {
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
        if (!_distanceOnly)
        {
            if (GD.Load<RDShaderFile>("res://assets/shaders/windscreen.glsl")?.GetSpirV() is not { } glass) return false;
            _glassShader = _device.ShaderCreateFromSpirV(glass);
            if (!_glassShader.IsValid) return false;
            _glassBlock = _device.UniformBufferCreate(GlassBlockSize);
            // As the spatial shader's screen texture was read (filter_linear_mipmap, repeat_disable).
            _linear = _device.SamplerCreate(new RDSamplerState
            {
                MinFilter = RenderingDevice.SamplerFilter.Linear,
                MagFilter = RenderingDevice.SamplerFilter.Linear,
                MipFilter = RenderingDevice.SamplerFilter.Linear,
                RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
                RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            });
        }
        _failed = false;
        return true;
    }

    /// <summary>
    /// Frees the pass's GPU objects (Windscreen, as it leaves the tree): left to the effect's own deletion they outlived the
    /// rendering server at quit, which reported them leaked. The pipelines go with their shaders, the framebuffer with the
    /// colour it was made of.
    /// </summary>
    internal void Release()
    {
        if (_device is null) return;
        foreach (var rid in new[] { _copyShader, _glassShader, _glassBlock, _nearest, _linear })
        {
            if (rid.IsValid) _device.FreeRid(rid);
        }
        _copyShader = _glassShader = _glassBlock = _nearest = _linear = _copyPipeline = _glassPipeline = default;
        _device = null;
        _failed = true;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers || renderData.GetRenderSceneData() is not { } scene) return;
        var size = buffers.GetInternalSize();
        // One view: the game renders no multiview (VR), and the whole textures below are 2D only with one.
        if (size.X <= 0 || size.Y <= 0 || buffers.GetViewCount() != 1 || !Initialise()) return;
        const uint usage = (uint)(RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit);
        // Whole textures, not layer slices, where Godot's own passes write them: read through a slice, the depth at times
        // lacked part of what the opaque pass drew (2 hidden runs in 8, none in 16 since), and the car's rear quarter, seen
        // from the seat through the back window, took the glass's haze as if it were the road behind.
        if (_distanceOnly)
        {
            if (!buffers.HasTexture(Context, DistanceName))
                buffers.CreateTexture(Context, DistanceName, RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage, RenderingDevice.TextureSamples.Samples1, size, 1, 1, true, false);
            var depth = buffers.GetDepthTexture(false);
            if (depth.IsValid) Copy(depth, buffers.GetTexture(Context, DistanceName), size, 2f, InverseProjection(scene));
            return;
        }
        if (!buffers.HasTexture(Context, DistanceName)) return;
        if (!buffers.HasTexture(Context, SceneName))
            buffers.CreateTexture(Context, SceneName, RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage, RenderingDevice.TextureSamples.Samples1, size, 1, SceneLevels, true, false);
        var colour = buffers.GetColorTexture(false);
        if (!colour.IsValid) return;
        // The scene with everything the transparent pass drew, and its levels made two by two from it.
        Copy(colour, buffers.GetTextureSlice(Context, SceneName, 0, 0, 1, 1), size, 0f, Projection.Identity);
        for (var level = 1u; level < SceneLevels; level++)
        {
            var levelSize = new Vector2I(Math.Max(size.X >> (int)level, 1), Math.Max(size.Y >> (int)level, 1));
            Copy(buffers.GetTextureSlice(Context, SceneName, 0, level - 1, 1, 1), buffers.GetTextureSlice(Context, SceneName, 0, level, 1, 1), levelSize, 1f, Projection.Identity);
        }
        Draw(colour, buffers.GetTexture(Context, SceneName), buffers.GetTexture(Context, DistanceName), size, InverseProjection(scene));
    }

    /// <summary>
    /// The inverse of the projection the view was drawn with, as the spatial shaders' INV_PROJECTION_MATRIX is. Godot hands
    /// it already corrected for the device (y flipped, depth reversed from 1 at the near plane), with the TAA's jitter.
    /// </summary>
    private static Projection InverseProjection(RenderSceneData scene) => scene.GetViewProjection(0).Inverse();

    private void Copy(Rid source, Rid target, Vector2I size, float mode, Projection inverse)
    {
        var device = _device!;
        var sourceUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
        sourceUniform.AddId(_nearest);
        sourceUniform.AddId(source);
        var targetUniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
        targetUniform.AddId(target);
        var set = UniformSetCacheRD.GetCache(_copyShader, 0, [sourceUniform, targetUniform]);
        Span<float> constants = stackalloc float[20];
        Write(constants, inverse);
        constants[16] = size.X;
        constants[17] = size.Y;
        constants[18] = mode;
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(constants).ToArray();
        var list = device.ComputeListBegin();
        device.ComputeListBindComputePipeline(list, _copyPipeline);
        device.ComputeListBindUniformSet(list, set, 0);
        device.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        device.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        device.ComputeListEnd();
    }

    private void Draw(Rid colour, Rid scene, Rid distance, Vector2I size, Projection inverse)
    {
        var device = _device!;
        if (colour != _framebufferColour || !device.FramebufferIsValid(_framebuffer))
        {
            if (device.FramebufferIsValid(_framebuffer)) device.FreeRid(_framebuffer);
            _framebuffer = default;
            _framebufferColour = colour;
            // A colour that takes no draw (MSAA's resolved colour may not) leaves the view without glass rather than
            // failing every frame.
            if ((device.TextureGetFormat(colour).UsageBits & RenderingDevice.TextureUsageBits.ColorAttachmentBit) == 0) return;
            _framebuffer = device.FramebufferCreate([colour]);
        }
        if (!device.FramebufferIsValid(_framebuffer)) return;
        var format = device.FramebufferGetFormat(_framebuffer);
        if (format != _glassPipelineFormat || !_glassPipeline.IsValid)
        {
            // Premultiplied alpha, as the spatial shader's blend_premul_alpha: the drops replace, the haze adds.
            var attachment = new RDPipelineColorBlendStateAttachment
            {
                EnableBlend = true,
                SrcColorBlendFactor = RenderingDevice.BlendFactor.One,
                DstColorBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = RenderingDevice.BlendOperation.Add,
                SrcAlphaBlendFactor = RenderingDevice.BlendFactor.One,
                DstAlphaBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = RenderingDevice.BlendOperation.Add,
            };
            var blend = new RDPipelineColorBlendState { Attachments = [attachment] };
            _glassPipeline = device.RenderPipelineCreate(_glassShader, format, RenderingDevice.InvalidFormatId, RenderingDevice.RenderPrimitive.Triangles,
                new RDPipelineRasterizationState(), new RDPipelineMultisampleState(), new RDPipelineDepthStencilState(), blend);
            _glassPipelineFormat = format;
            if (!_glassPipeline.IsValid) return;
        }
        Span<float> block = stackalloc float[(int)GlassBlockSize / 4];
        Write(block, inverse);
        var sun = SunView;
        block[16] = sun.X; block[17] = sun.Y; block[18] = sun.Z;
        block[20] = Rain; block[21] = Wet; block[22] = DropCover; block[23] = Wipers ? 1f : 0f;
        block[24] = WiperClock; block[25] = WiperSweep; block[26] = WiperPeriod; block[27] = Speed;
        block[28] = Glare; block[29] = Glint; block[30] = CabinLight;
        block[32] = size.X; block[33] = size.Y;
        device.BufferUpdate(_glassBlock, 0, GlassBlockSize, System.Runtime.InteropServices.MemoryMarshal.AsBytes(block).ToArray());
        var sceneUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
        sceneUniform.AddId(_linear);
        sceneUniform.AddId(scene);
        var distanceUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        distanceUniform.AddId(_nearest);
        distanceUniform.AddId(distance);
        var blockUniform = new RDUniform { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 2 };
        blockUniform.AddId(_glassBlock);
        var set = UniformSetCacheRD.GetCache(_glassShader, 0, [sceneUniform, distanceUniform, blockUniform]);
        var list = device.DrawListBegin(_framebuffer, RenderingDevice.DrawFlags.DefaultAll);
        device.DrawListBindRenderPipeline(list, _glassPipeline);
        device.DrawListBindUniformSet(list, set, 0);
        device.DrawListDraw(list, false, 1, 3);
        device.DrawListEnd();
    }

    /// <summary>A projection into 16 floats, column by column, as GLSL's mat4 lays them.</summary>
    private static void Write(Span<float> into, Projection p)
    {
        for (var column = 0; column < 4; column++)
        {
            var c = p[column];
            into[column * 4] = c.X;
            into[column * 4 + 1] = c.Y;
            into[column * 4 + 2] = c.Z;
            into[column * 4 + 3] = c.W;
        }
    }
}
