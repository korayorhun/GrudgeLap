using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Builds the visible world straight from the track geometry: a terrain mesh sampled from the simulation's
/// own ground field (embankments, cuttings, dips, rolling fields, a ring of hills), photo-textured road
/// ribbons with the alternating slabs of the original and worn wheel tracks, gravel shoulders (orange rims
/// on the stunt structures), bridge decks with pillars and railings, the skid pad and the grass, under a
/// photo sky with the sun where the HDRI has it. Props, fields, trees and the train are built by
/// <see cref="PropBuilder"/>, <see cref="FoliageBuilder"/> and <see cref="TrainView"/> with the materials
/// from here. <see cref="SetRetro"/> switches the whole world to the flat-shaded look of 1989.
/// </summary>
internal static class WorldBuilder
{
    private const float TerrainCell = 14f;
    /// <summary>Terrain cells along the roads are split this many times each way (2 m; see <see cref="BuildTerrain"/>).</summary>
    private const int TerrainSplit = 7;
    /// <summary>Metres the terrain mesh keeps below the asphalt under a road.</summary>
    private const float RoadClearance = 0.04f;
    private const float TerrainMargin = 2400f;

    private static StandardMaterial3D? _grass;
    private static StandardMaterial3D? _asphalt;
    private static StandardMaterial3D? _concrete;
    private static StandardMaterial3D? _gravel;
    private static Godot.Environment? _environment;
    private static Godot.Environment? _mirrorEnvironment;
    private static DirectionalLight3D? _sun;
    private static StandardMaterial3D? _steel;
    private static MeshInstance3D? _clouds;
    private static Node3D? _forest;
    private static GraphicsQuality _quality = GraphicsQuality.High;
    private static bool _retro;
    /// <summary>How fast the cloud sheet drifts, in tiles per second: about five metres a second on a 2.8 km tile.</summary>
    private const float CloudDrift = 0.0018f;
    private static readonly List<(StandardMaterial3D Material, Texture2D? Albedo, Texture2D? Normal, Texture2D? Roughness)> _textured = new();
    private static readonly Dictionary<string, StandardMaterial3D> _propMaterials = new();
    /// <summary>Sun direction measured from the brightest patch of the sky HDRI (see assets/textures/SOURCES.md): elevation 43.0, panorama u 0.600.</summary>
    private static readonly Vector3 SunDirection = new(-0.431f, 0.682f, -0.591f);
    /// <summary>The mountain range on the horizon (<see cref="BuildMountains"/>): where its foot, its ridge and its far side lie, metres from the circuit's centre.</summary>
    private const float MountainFoot = 3200f;
    private const float MountainRidge = 4400f;
    private const float MountainBack = 6400f;

    /// <summary>
    /// Drops the shared materials. Called when the game quits: a Godot resource still held by a static field
    /// is finalised after the engine has gone, which Godot reports as a leaked reference and which has crashed
    /// the process on exit.
    /// </summary>
    public static void ReleaseShared()
    {
        _grass = null;
        _asphalt = null;
        _concrete = null;
        _gravel = null;
        _environment = null;
        _mirrorEnvironment = null;
        _sun = null;
        _steel = null;
        _clouds = null;
        _forest = null;
        _textured.Clear();
        _propMaterials.Clear();
        FoliageBuilder.ReleaseShared();
        PropBuilder.ReleaseShared();
    }

    public static void Build(Node3D root, TrackGeometry track)
    {
        root.AddChild(BuildEnvironment());
        root.AddChild(BuildSun());
        root.AddChild(BuildFarGround());
        root.AddChild(BuildMountains(track));
        if (BuildClouds(track) is { } clouds) root.AddChild(clouds);
        root.AddChild(BuildTerrain(track));
        foreach (var path in track.Paths)
        {
            root.AddChild(BuildRoad(track, path));
            root.AddChild(BuildShoulders(track, path));
            root.AddChild(BuildCentreLine(track, path));
            foreach (var node in BuildBridges(path, track.Terrain)) root.AddChild(node);
            if (BuildLoopFrame(path, track.Terrain) is { } frame) root.AddChild(frame);
        }
        foreach (var pad in track.Pads) root.AddChild(BuildPad(pad));
        root.AddChild(FoliageBuilder.BuildGrass(track));
        var (minX, maxX, minZ, maxZ) = RoadBounds(track);
        // The wood fills the terrain out to its margin; beyond that lies the flat far ground.
        _forest = FoliageBuilder.BuildForest(track, minX - TerrainMargin + 40f, maxX + TerrainMargin - 40f, minZ - TerrainMargin + 40f, maxZ + TerrainMargin - 40f);
        root.AddChild(_forest);
        ApplyQuality();
    }

    /// <summary>The extent of the road centre lines.</summary>
    private static (float MinX, float MaxX, float MinZ, float MaxZ) RoadBounds(TrackGeometry track)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var path in track.Paths)
        {
            foreach (var f in path.Frames)
            {
                minX = MathF.Min(minX, f.Origin.X); maxX = MathF.Max(maxX, f.Origin.X);
                minZ = MathF.Min(minZ, f.Origin.Z); maxZ = MathF.Max(maxZ, f.Origin.Z);
            }
        }
        return (minX, maxX, minZ, maxZ);
    }

    /// <summary>Once a frame: the clouds drift.</summary>
    public static void Tick(float dt)
    {
        if (_clouds?.Mesh is PlaneMesh { Material: StandardMaterial3D material })
        {
            material.Uv1Offset += new Vector3(dt * CloudDrift, dt * CloudDrift * 0.35f, 0f);
        }
    }

    /// <summary>
    /// The cost of the picture: what the environment does on top of the geometry. High is everything (global
    /// illumination, indirect light, reflections, haze); Medium drops the three screen-space passes that cost the
    /// most; Low keeps only the sun and the fog and thins the wood. The caller sets the viewport's part (resolution,
    /// anti-aliasing). 1989 mode overrides all of it while it is on.
    /// </summary>
    public static void SetQuality(GraphicsQuality quality)
    {
        _quality = quality;
        ApplyQuality();
    }

    private static void ApplyQuality()
    {
        var retro = _retro;
        var high = !retro && _quality == GraphicsQuality.High;
        var medium = !retro && _quality >= GraphicsQuality.Medium;
        if (_sun is not null) _sun.ShadowEnabled = medium;
        if (_clouds is not null) _clouds.Visible = !retro;
        if (_forest is not null)
        {
            foreach (var child in _forest.GetChildren())
            {
                if (child is MultiMeshInstance3D band) band.Visible = medium || band.Name.ToString().StartsWith("Forest-near", StringComparison.Ordinal);
            }
        }
        if (_environment is null) return;
        _environment.SdfgiEnabled = high;
        _environment.SsaoEnabled = medium;
        _environment.SsilEnabled = high;
        _environment.SsrEnabled = high;
        _environment.GlowEnabled = medium;
        _environment.VolumetricFogEnabled = medium;
    }

    /// <summary>
    /// The 1989 look: no textures, nearest filtering, no global illumination, ambient occlusion, reflections,
    /// glow, fog or shadows, a flat sky. The caller lowers the render resolution and caps the frame rate.
    /// </summary>
    public static void SetRetro(bool retro)
    {
        _retro = retro;
        foreach (var (material, albedo, normal, roughness) in _textured)
        {
            material.AlbedoTexture = retro ? null : albedo;
            material.NormalEnabled = !retro && normal is not null;
            material.NormalTexture = retro ? null : normal;
            material.RoughnessTexture = retro ? null : roughness;
            material.TextureFilter = retro ? BaseMaterial3D.TextureFilterEnum.Nearest : BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        }
        if (_asphalt is not null) _asphalt.DetailEnabled = !retro && _asphalt.DetailAlbedo is not null;
        // 1989 shoulders were orange everywhere; today's are dry earth except on the structures.
        if (_gravel is not null) _gravel.AlbedoColor = retro ? new Color(0.9f, 0.45f, 0.1f) : new Color(0.88f, 0.68f, 0.46f);
        ApplyQuality();
        if (_environment is null) return;
        RetroLook(_environment, retro);
        if (_mirrorEnvironment is not null) RetroLook(_mirrorEnvironment, retro);
    }

    /// <summary>The part of the 1989 look the rear-view mirror shares with the main view: flat sky, flat ambient, no fog, linear tones.</summary>
    private static void RetroLook(Godot.Environment environment, bool retro)
    {
        environment.FogEnabled = !retro;
        environment.BackgroundMode = retro ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
        environment.BackgroundColor = new Color(0.30f, 0.52f, 0.86f);
        environment.AmbientLightSource = retro ? Godot.Environment.AmbientSource.Color : Godot.Environment.AmbientSource.Sky;
        environment.AmbientLightColor = new Color(0.55f, 0.6f, 0.7f);
        environment.AmbientLightEnergy = retro ? 0.9f : 1f;
        environment.TonemapMode = retro ? Godot.Environment.ToneMapper.Linear : Godot.Environment.ToneMapper.Agx;
    }

    /// <summary>
    /// The environment of the rear-view mirror's camera: the main sky, tone mapping and fog without global
    /// illumination, screen-space effects, glow or volumetric fog, which would cost far more than the small
    /// picture shows. Null before the world is built.
    /// </summary>
    public static Godot.Environment? MirrorEnvironment()
    {
        if (_environment is null) return null;
        if (_mirrorEnvironment is null)
        {
            _mirrorEnvironment = (Godot.Environment)_environment.Duplicate();
            _mirrorEnvironment.SdfgiEnabled = false;
            _mirrorEnvironment.SsaoEnabled = false;
            _mirrorEnvironment.SsilEnabled = false;
            _mirrorEnvironment.SsrEnabled = false;
            _mirrorEnvironment.GlowEnabled = false;
            _mirrorEnvironment.VolumetricFogEnabled = false;
        }
        return _mirrorEnvironment;
    }

    // ---- materials ---------------------------------------------------------------------------------

    // Photo textures (CC0, see assets/textures/SOURCES.md); the procedural noise stays as the fallback if a file is missing.
    private static StandardMaterial3D Grass => _grass ??= Textured(new StandardMaterial3D
    {
        // A deeper green than the photo's own: the pale tint read as hay, and the plain of the 1989 game is a saturated green.
        AlbedoColor = new Color(0.50f, 0.66f, 0.36f),
        Roughness = 1f,
        Uv1Scale = new Vector3(1150f, 1150f, 1f),    // the terrain's UV spans 5000 m: a 4.3 m tile
        VertexColorUseAsAlbedo = true,
    }, Photo("grass_color") ?? Noise(0.9f, 5, new[] { (0f, new Color(0.62f, 0.72f, 0.55f)), (0.5f, new Color(0.95f, 1.0f, 0.9f)), (1f, new Color(1.25f, 1.2f, 1.1f)) }, seed: 3),
       Photo("grass_normal") ?? Noise(2.5f, 4, null, seed: 11, asNormal: true), normalScale: 0.7f, Photo("grass_roughness"));

    private static StandardMaterial3D Asphalt
    {
        get
        {
            if (_asphalt is not null) return _asphalt;
            _asphalt = Textured(new StandardMaterial3D
            {
                // Slightly darkened photo asphalt; the slab shade, the wheel tracks, the patches and the potholes
                // come from the vertex colour multiplied in.
                AlbedoColor = new Color(0.46f, 0.46f, 0.47f),
                VertexColorUseAsAlbedo = true,
                Roughness = 1f,
                Uv1Scale = new Vector3(2f, 0.18f, 1f),      // u spans the 11 m width, v is metres along: a 5.5 m tile
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }, Photo("asphalt_color") ?? Noise(9f, 4, new[] { (0f, new Color(0.82f, 0.82f, 0.82f)), (0.5f, new Color(0.98f, 0.98f, 0.98f)), (1f, new Color(1.15f, 1.15f, 1.15f)) }, seed: 5),
               Photo("asphalt_normal") ?? Noise(16f, 4, null, seed: 17, asNormal: true), normalScale: 0.5f, Photo("asphalt_roughness"));
            // The wear sheet (tools/make-road-wear.py): tar snakes, sealed cracks and stains multiplied in on the
            // second UV set, which runs 40 m along the road and drifts across it so the sheet's repeat does not read.
            var wear = Photo("road_wear", "png");
            if (wear is not null)
            {
                _asphalt.DetailEnabled = true;
                _asphalt.DetailAlbedo = wear;
                _asphalt.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul;
                _asphalt.DetailUVLayer = BaseMaterial3D.DetailUV.UV2;
            }
            return _asphalt;
        }
    }

    private static StandardMaterial3D Concrete => _concrete ??= Textured(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.80f, 0.79f, 0.76f),
        Roughness = 1f,
        Uv1Scale = new Vector3(0.4f, 0.4f, 0.4f),
        Uv1Triplanar = true,
    }, Photo("concrete_color") ?? Noise(1.5f, 3, new[] { (0f, new Color(0.8f, 0.8f, 0.8f)), (1f, new Color(1f, 1f, 1f)) }, seed: 23),
       Photo("concrete_normal"), 0.5f, Photo("concrete_roughness"));

    /// <summary>Shoulders: dry earth along the road (u across, v in metres along); the vertex colour tints the structures orange.</summary>
    private static StandardMaterial3D Gravel => _gravel ??= Textured(new StandardMaterial3D
    {
        // Warm tan: the dirt photo tinted toward the orange shoulders of the original without going to paint.
        AlbedoColor = new Color(0.88f, 0.68f, 0.46f),
        VertexColorUseAsAlbedo = true,
        Roughness = 1f,
        Uv1Scale = new Vector3(1f, 0.45f, 1f),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    }, Photo("dirt_color") ?? Noise(4f, 3, new[] { (0f, new Color(0.7f, 0.65f, 0.55f)), (1f, new Color(1f, 0.95f, 0.85f)) }, seed: 41),
       Photo("dirt_normal"), 0.6f, Photo("dirt_roughness"));

    /// <summary>
    /// A photo material for props and foliage, cached by texture, tint and scale and tracked for the retro
    /// switch. The texture name is the base of the files in assets/textures (bark, siding, bricks...); a
    /// missing texture leaves a plain tinted material.
    /// </summary>
    /// <summary>Painted steel for beams (<see cref="Beam"/>): the colour comes with the vertices, both faces drawn.</summary>
    private static StandardMaterial3D Steel => _steel ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.5f,
        Metallic = 0.35f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static StandardMaterial3D PropMaterial(string texture, Color tint, float uvScale, bool triplanar = true, bool matte = false)
    {
        var key = $"{texture}|{tint}|{uvScale}|{triplanar}|{matte}";
        if (_propMaterials.TryGetValue(key, out var cached)) return cached;
        var material = new StandardMaterial3D
        {
            AlbedoColor = tint,
            // Matte drops the photo's roughness map: a dashboard is grained to keep the sky out of the windscreen.
            Roughness = matte ? 0.88f : 1f,
            Uv1Triplanar = triplanar,
            Uv1Scale = new Vector3(uvScale, uvScale, uvScale),
        };
        var albedo = Photo($"{texture}_color");
        if (albedo is not null) Textured(material, albedo, Photo($"{texture}_normal"), 0.6f, matte ? null : Photo($"{texture}_roughness"));
        _propMaterials[key] = material;
        return material;
    }

    /// <summary>Attaches the textures and remembers them so the retro switch can take them away and bring them back.</summary>
    private static StandardMaterial3D Textured(StandardMaterial3D material, Texture2D albedo, Texture2D? normal, float normalScale, Texture2D? roughness = null)
    {
        material.AlbedoTexture = albedo;
        material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        if (normal is not null)
        {
            material.NormalEnabled = true;
            material.NormalTexture = normal;
            material.NormalScale = normalScale;
        }
        if (roughness is not null) material.RoughnessTexture = roughness;
        _textured.Add((material, albedo, normal, roughness));
        return material;
    }

    /// <summary>A photo texture from assets/textures, or null when it is not there (then the noise stands in).</summary>
    private static Texture2D? Photo(string name, string extension = "jpg")
    {
        var path = $"res://assets/textures/{name}.{extension}";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    private static NoiseTexture2D Noise(float frequency, int octaves, (float, Color)[]? ramp, int seed, bool asNormal = false)
    {
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = frequency / 100f, FractalOctaves = octaves, Seed = seed };
        var texture = new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true, AsNormalMap = asNormal, BumpStrength = 6f };
        if (ramp is not null)
        {
            var gradient = new Gradient();
            gradient.SetColor(0, ramp[0].Item2);
            gradient.SetColor(1, ramp[^1].Item2);
            for (var i = 1; i < ramp.Length - 1; i++) gradient.AddPoint(ramp[i].Item1, ramp[i].Item2);
            texture.ColorRamp = gradient;
        }
        return texture;
    }

    // ---- sky, sun, ground ----------------------------------------------------------------------------

    private static WorldEnvironment BuildEnvironment()
    {
        // Photo sky (CC0 HDRI) when present, the procedural one otherwise.
        Material sky;
        if (ResourceLoader.Exists("res://assets/textures/sky.hdr"))
        {
            sky = new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>("res://assets/textures/sky.hdr"), EnergyMultiplier = 1.0f };
        }
        else
        {
            sky = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.20f, 0.42f, 0.84f),
                SkyHorizonColor = new Color(0.70f, 0.79f, 0.92f),
                GroundBottomColor = new Color(0.16f, 0.22f, 0.10f),
                GroundHorizonColor = new Color(0.52f, 0.60f, 0.52f),
                SunAngleMax = 18f,
                SunCurve = 0.08f,
            };
        }
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.4f,
            // AgX keeps the saturated paint and the sky from clipping the way ACES does.
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 0.9f,
            TonemapWhite = 6f,
            SdfgiEnabled = true,
            SdfgiCascades = 6,
            SdfgiMinCellSize = 0.4f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 1.5f,
            SsilEnabled = true,
            SsilRadius = 3f,
            SsilIntensity = 1f,
            SsrEnabled = true,
            SsrMaxSteps = 64,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2f,
            SsrDepthTolerance = 0.2f,
            GlowEnabled = true,
            GlowIntensity = 0.2f,
            GlowBloom = 0.02f,
            // Clear, dry air: the mountains three kilometres out stay crisp and only pick up a little blue, as on the
            // plain of the 1989 game, where nothing faded. Denser fog washed everything past the next bend to grey.
            FogEnabled = true,
            FogLightColor = new Color(0.72f, 0.80f, 0.94f),
            FogDensity = 0.00004f,
            FogAerialPerspective = 0.35f,
            FogSkyAffect = 0.05f,
            // A trace of haze near the ground, so the sun still has something to light in the dips.
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.00012f,
            VolumetricFogAlbedo = new Color(0.9f, 0.93f, 1f),
            VolumetricFogLength = 300f,
            VolumetricFogSkyAffect = 0.1f,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.05f,
            AdjustmentSaturation = 1.12f,
        };
        return new WorldEnvironment { Environment = _environment };
    }

    private static DirectionalLight3D BuildSun()
    {
        _sun = new DirectionalLight3D
        {
            LightEnergy = 1.45f,
            LightColor = new Color(1f, 0.96f, 0.90f),
            LightAngularDistance = 0.5f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 500f,
            DirectionalShadowSplit1 = 0.05f,
            DirectionalShadowSplit2 = 0.15f,
            DirectionalShadowSplit3 = 0.4f,
            ShadowBias = 0.02f,
            ShadowNormalBias = 1.5f,
        };
        // Shine from where the HDRI's sun is, so shadows and the sky agree.
        _sun.LookAtFromPosition(Vector3.Zero, -SunDirection, Vector3.Up);
        return _sun;
    }

    /// <summary>Flat ground to the horizon, just under the terrain mesh, where the ground field has flattened out; it reaches past the mountains.</summary>
    private static MeshInstance3D BuildFarGround()
    {
        var mesh = new PlaneMesh { Size = new Vector2(16000f, 16000f), SubdivideWidth = 12, SubdivideDepth = 12, Material = Grass };
        return new MeshInstance3D { Mesh = mesh, Position = new Vector3(0f, -0.06f, 0f), Name = "FarGround" };
    }

    /// <summary>
    /// Fair-weather clouds: one wide sheet of the cloud texture (tools/make-clouds.py) high over the circuit, seen
    /// from below, unlit so that it stays as white as the sky's own clouds, drifting slowly (<see cref="Tick"/>).
    /// </summary>
    private static MeshInstance3D? BuildClouds(TrackGeometry track)
    {
        if (!ResourceLoader.Exists("res://assets/textures/clouds.png")) return null;
        var material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/clouds.png"),
            AlbedoColor = new Color(0.94f, 0.95f, 0.97f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Uv1Scale = new Vector3(5f, 5f, 1f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        var (minX, maxX, minZ, maxZ) = RoadBounds(track);
        _clouds = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(14000f, 14000f), Material = material },
            Position = Convert.ToGodot(new SN.Vector3((minX + maxX) * 0.5f, 1500f, (minZ + maxZ) * 0.5f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Clouds",
        };
        return _clouds;
    }

    /// <summary>
    /// The mountain range round the horizon, as the snow-capped ridge that rings the plain of the 1989 game: a
    /// ring of ground between <see cref="MountainFoot"/> and <see cref="MountainBack"/> from the circuit's centre
    /// rising to a ridge whose height wanders with the bearing, forest at its foot, bare rock above and snow on the
    /// peaks. Too far for shadows, lit by the sun and the sky and tinted by the air's blue through the fog.
    /// </summary>
    private static MeshInstance3D BuildMountains(TrackGeometry track)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var path in track.Paths)
        {
            foreach (var f in path.Frames)
            {
                minX = MathF.Min(minX, f.Origin.X); maxX = MathF.Max(maxX, f.Origin.X);
                minZ = MathF.Min(minZ, f.Origin.Z); maxZ = MathF.Max(maxZ, f.Origin.Z);
            }
        }
        var centreX = (minX + maxX) * 0.5f;
        var centreZ = (minZ + maxZ) * 0.5f;
        const int around = 720;
        const int across = 36;
        var ridge = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f, FractalOctaves = 4, Seed = 1989 };
        var relief = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 420f, FractalOctaves = 5, FractalLacunarity = 2.3f, Seed = 7 };

        // The ridge's height at a bearing: a ring of peaks between 240 and 560 m, ridged noise so the summits are sharp
        // and the saddles rounded, continuous round the horizon. Its crest also wanders in and out by 500 m. Taller
        // and nearer, the range stood over the plain like a fence of teeth; the original's is a low distant chain.
        float RidgeHeight(float bearing) => 240f + 320f * (1f - MathF.Abs(ridge.GetNoise2D(MathF.Cos(bearing) * 3.5f, MathF.Sin(bearing) * 3.5f)));
        float RidgeRadius(float bearing) => MountainRidge + 500f * ridge.GetNoise2D(MathF.Cos(bearing) * 3f + 9f, MathF.Sin(bearing) * 3f);

        float Height(float bearing, float r)
        {
            var peak = RidgeHeight(bearing);
            var crest = RidgeRadius(bearing);
            float profile;
            if (r < MountainFoot) profile = 0f;
            else if (r < crest) profile = MathF.Pow(MathUtil.SmoothStep((r - MountainFoot) / (crest - MountainFoot)), 1.3f);
            else profile = 1f - MathUtil.SmoothStep((r - crest) / (MountainBack - crest));
            var x = centreX + MathF.Cos(bearing) * r;
            var z = centreZ + MathF.Sin(bearing) * r;
            // Spurs and gullies down the flanks, growing with the height so the summits break up.
            var bumps = relief.GetNoise2D(x, z) * (30f + 70f * profile) * MathF.Min(profile * 3f, 1f);
            return peak * profile + bumps;
        }

        var st = new MeshBuilder();
        // Seen from the circuit only the upper slopes clear the plain's own hills, so those carry the colour: dark rock
        // with snow on the tips alone, or the whole range read as a bank of cloud.
        var forest = new Color(0.12f, 0.21f, 0.10f);
        var rock = new Color(0.22f, 0.19f, 0.17f);
        var scree = new Color(0.33f, 0.29f, 0.25f);
        var snow = new Color(0.93f, 0.94f, 0.97f);
        var plain = new Color(0.50f, 0.66f, 0.36f);
        for (var j = 0; j <= across; j++)
        {
            var r = MountainFoot + (MountainBack - MountainFoot) * j / across;
            for (var i = 0; i < around; i++)
            {
                var bearing = MathF.Tau * i / around;
                var h = Height(bearing, r);
                var x = centreX + MathF.Cos(bearing) * r;
                var z = centreZ + MathF.Sin(bearing) * r;
                // Normal from central differences along and across the ring.
                var db = MathF.Tau / around;
                var hb0 = Height(bearing - db, r);
                var hb1 = Height(bearing + db, r);
                var hr0 = Height(bearing, r - 30f);
                var hr1 = Height(bearing, r + 30f);
                var tangentB = new SN.Vector3(-MathF.Sin(bearing) * r * db * 2f, hb1 - hb0, MathF.Cos(bearing) * r * db * 2f);
                var tangentR = new SN.Vector3(MathF.Cos(bearing) * 60f, hr1 - hr0, MathF.Sin(bearing) * 60f);
                var normal = SN.Vector3.Normalize(SN.Vector3.Cross(tangentR, tangentB));
                if (normal.Y < 0f) normal = -normal;
                // Forest up to half the ridge, dark rock above with lighter scree on the gentler ground, and snow only
                // on the summits from a wandering snow line, lying on the shelves rather than the steep faces.
                var fraction = MathUtil.Clamp01(h / MathF.Max(RidgeHeight(bearing), 1f));
                var snowLine = 0.90f + 0.04f * ridge.GetNoise2D(MathF.Cos(bearing) * 5f + 3f, MathF.Sin(bearing) * 5f);
                var ground = normal.Y > 0.84f ? rock.Lerp(scree, (normal.Y - 0.84f) / 0.16f) : rock;
                var colour = fraction < 0.05f ? plain.Lerp(forest, fraction / 0.05f)
                    : fraction < 0.58f ? forest
                    : fraction < 0.68f ? forest.Lerp(ground, (fraction - 0.58f) / 0.10f)
                    : ground;
                var snowCover = MathUtil.Clamp01((fraction - snowLine) / 0.05f) * MathUtil.Clamp01((normal.Y - 0.5f) / 0.3f);
                colour = colour.Lerp(snow, snowCover);
                var shade = 0.88f + 0.12f * normal.Y;
                st.SetColor(new Color(colour.R * shade, colour.G * shade, colour.B * shade));
                st.SetNormal(Convert.ToGodot(normal));
                st.SetUV(new Vector2(i / (float)around, j / (float)across));
                st.AddVertex(Convert.ToGodot(new SN.Vector3(x, h, z)));
            }
        }
        for (var j = 0; j < across; j++)
        {
            for (var i = 0; i < around; i++)
            {
                var a = j * around + i;
                var b = j * around + (i + 1) % around;
                var c = a + around;
                var d = b + around;
                // Bearings run counter-clockwise seen from above in the simulation's frame; the Z flip of the view makes that clockwise, Godot's front face.
                st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                st.AddIndex(b); st.AddIndex(d); st.AddIndex(c);
            }
        }
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, Name = "Mountains", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>
    /// Grid mesh of the ground field over the circuit and the hills around it: the field sampled every 14 m, and
    /// every 2 m in the cells a road's earthwork reaches. The coarse grid alone cut straight across the embankments
    /// and cuttings the field shapes along the roads, so road slabs floated over dips and, where a road levels out
    /// after a slope, the ground rose a few centimetres over the asphalt and hid it (plainly in the Unreal version,
    /// which imports this mesh). The vertex colour carries the shading: darker on slopes and in cuttings, a sandy
    /// trodden tint along the road edges, and patches of paler and deeper green from two octaves of noise so the
    /// texture repeat does not read.
    /// </summary>
    private static MeshInstance3D BuildTerrain(TrackGeometry track)
    {
        var terrain = track.Terrain;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var path in track.Paths)
        {
            foreach (var f in path.Frames)
            {
                minX = MathF.Min(minX, f.Origin.X); maxX = MathF.Max(maxX, f.Origin.X);
                minZ = MathF.Min(minZ, f.Origin.Z); maxZ = MathF.Max(maxZ, f.Origin.Z);
            }
        }
        minX -= TerrainMargin; maxX += TerrainMargin; minZ -= TerrainMargin; maxZ += TerrainMargin;
        var nx = (int)MathF.Ceiling((maxX - minX) / TerrainCell) + 1;
        var nz = (int)MathF.Ceiling((maxZ - minZ) / TerrainCell) + 1;
        var heights = new float[nx * nz];
        var roadWeights = new float[nx * nz];
        // The ground field only reads the finished track, so its rows can be sampled side by side.
        Parallel.For(0, nz, j =>
        {
            for (var i = 0; i < nx; i++) heights[j * nx + i] = terrain.Height(minX + i * TerrainCell, minZ + j * TerrainCell, out roadWeights[j * nx + i]);
        });

        // A cell is refined when the field pulls any of its corners toward a road (or a skid pad): the earthwork
        // reaches at least 9 m past the shoulder, so every cell a road crosses has such a corner.
        const int split = TerrainSplit;
        const float step = TerrainCell / split;
        var cellsX = nx - 1;
        var cellsZ = nz - 1;
        var fine = new bool[cellsX * cellsZ];
        for (var j = 0; j < cellsZ; j++)
        {
            for (var i = 0; i < cellsX; i++)
            {
                var c = j * nx + i;
                fine[j * cellsX + i] = roadWeights[c] > 0f || roadWeights[c + 1] > 0f || roadWeights[c + nx] > 0f || roadWeights[c + nx + 1] > 0f;
            }
        }
        bool IsFine(int i, int j) => i >= 0 && j >= 0 && i < cellsX && j < cellsZ && fine[j * cellsX + i];

        // Vertices on a lattice of 2 m steps; the coarse corners are every split-th point of it.
        var lookup = new Dictionary<long, int>();
        var points = new List<(int I, int J)>();
        var indices = new List<int>();
        int Point(int pi, int pj)
        {
            var key = ((long)pi << 32) | (uint)pj;
            if (!lookup.TryGetValue(key, out var v))
            {
                v = points.Count;
                lookup[key] = v;
                points.Add((pi, pj));
            }
            return v;
        }
        void Cell(int pi, int pj, int size)
        {
            var a = Point(pi, pj);
            var b = Point(pi + size, pj);
            var c = Point(pi, pj + size);
            var d = Point(pi + size, pj + size);
            // Godot front faces wind clockwise; the Z flip of the view makes the simulation's counter-clockwise order clockwise.
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(b); indices.Add(d); indices.Add(c);
        }
        var refined = 0;
        for (var j = 0; j < cellsZ; j++)
        {
            for (var i = 0; i < cellsX; i++)
            {
                if (!IsFine(i, j))
                {
                    Cell(i * split, j * split, split);
                    continue;
                }
                refined++;
                for (var t = 0; t < split; t++)
                {
                    for (var s = 0; s < split; s++) Cell(i * split + s, j * split + t, 1);
                }
            }
        }

        // Heights and normals: the coarse corners keep their grid values; the finer points sample the field, except
        // on an edge shared with a coarse cell, where they lie on that edge's straight line so the two cells meet
        // without a crack.
        var count = points.Count;
        var vertexHeight = new float[count];
        var vertexWeight = new float[count];
        var vertexNormal = new SN.Vector3[count];
        Parallel.For(0, count, v =>
        {
            var (pi, pj) = points[v];
            var x = minX + pi * step;
            var z = minZ + pj * step;
            if (pi % split == 0 && pj % split == 0)
            {
                var i = pi / split;
                var j = pj / split;
                var hx0 = heights[j * nx + Math.Max(i - 1, 0)];
                var hx1 = heights[j * nx + Math.Min(i + 1, nx - 1)];
                var hz0 = heights[Math.Max(j - 1, 0) * nx + i];
                var hz1 = heights[Math.Min(j + 1, nz - 1) * nx + i];
                vertexHeight[v] = heights[j * nx + i] - (roadWeights[j * nx + i] >= 0.999f ? RoadClearance : 0f);
                vertexWeight[v] = roadWeights[j * nx + i];
                vertexNormal[v] = SN.Vector3.Normalize(new SN.Vector3(-(hx1 - hx0) / (2f * TerrainCell), 1f, -(hz1 - hz0) / (2f * TerrainCell)));
                return;
            }
            vertexHeight[v] = terrain.Height(x, z, out vertexWeight[v]);
            var dx = terrain.Height(x + step, z) - terrain.Height(x - step, z);
            var dz = terrain.Height(x, z + step) - terrain.Height(x, z - step);
            vertexNormal[v] = SN.Vector3.Normalize(new SN.Vector3(-dx / (2f * step), 1f, -dz / (2f * step)));
            // Under a road the ground sits a few centimetres below the asphalt rather than on it: flat triangles 2 m
            // across still bulge through a road that curves between them.
            if (vertexWeight[v] >= 0.999f) vertexHeight[v] -= RoadClearance;
            if (pi % split == 0 && !(IsFine(pi / split - 1, pj / split) && IsFine(pi / split, pj / split)))
            {
                var i = pi / split;
                var j = pj / split;
                vertexHeight[v] = MathUtil.Lerp(heights[j * nx + i], heights[(j + 1) * nx + i], (float)(pj % split) / split);
            }
            else if (pj % split == 0 && !(IsFine(pi / split, pj / split - 1) && IsFine(pi / split, pj / split)))
            {
                var i = pi / split;
                var j = pj / split;
                vertexHeight[v] = MathUtil.Lerp(heights[j * nx + i], heights[j * nx + i + 1], (float)(pi % split) / split);
            }
        });

        var variation = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 140f, FractalOctaves = 2, Seed = 77 };
        var st = new MeshBuilder();
        // The ring of hills beyond the roads (TerrainField.Base) is wooded: from the circuit it reads as the dark band
        // of trees under the mountains, as in the 1989 game, without a tree being placed there.
        var roadMinX = minX + TerrainMargin; var roadMaxX = maxX - TerrainMargin;
        var roadMinZ = minZ + TerrainMargin; var roadMaxZ = maxZ - TerrainMargin;
        for (var v = 0; v < count; v++)
        {
            var x = minX + points[v].I * step;
            var z = minZ + points[v].J * step;
            var h = vertexHeight[v];
            var normal = vertexNormal[v];
            var shade = 0.80f + 0.20f * normal.Y - 0.012f * MathF.Max(-h, 0f);
            var n = variation.GetNoise2D(x, z);                      // -1..1
            var r = shade * (1f + 0.10f * n);
            var g = shade * (1f + 0.05f * n);
            var b = shade * (1f - 0.08f * n);
            // Trodden, dusty ground right at the road edge: a touch browner than the grass, not a pale band.
            var edge = MathUtil.Clamp01((vertexWeight[v] - 0.55f) / 0.45f);
            r = MathUtil.Lerp(r, shade * 0.98f, edge * 0.5f);
            g = MathUtil.Lerp(g, shade * 0.88f, edge * 0.5f);
            b = MathUtil.Lerp(b, shade * 0.66f, edge * 0.5f);
            var outside = MathF.Max(MathF.Max(roadMinX - 140f - x, x - roadMaxX - 140f), MathF.Max(roadMinZ - 140f - z, z - roadMaxZ - 140f));
            var wooded = MathUtil.SmoothStep((outside - 250f) / 300f) * MathUtil.Clamp01((h - 6f) / 10f);
            r = MathUtil.Lerp(r, shade * 0.36f, wooded);
            g = MathUtil.Lerp(g, shade * 0.50f, wooded);
            b = MathUtil.Lerp(b, shade * 0.34f, wooded);
            st.SetColor(new Color(r, g, b));
            var gNormal = Convert.ToGodot(normal);
            // u follows x on this grid: the tangent is +x laid onto the slope, the bitangent then runs along v.
            st.SetTangent((Vector3.Right - gNormal * gNormal.X).Normalized(), 1f);
            st.SetNormal(gNormal);
            st.SetUV(new Vector2(x / 5000f + 0.5f, z / 5000f + 0.5f));
            st.AddVertex(Convert.ToGodot(new SN.Vector3(x, h, z)));
        }
        foreach (var index in indices) st.AddIndex(index);
        GD.Print($"terrain: {count} vertices, {indices.Count / 3} triangles, {refined} of {cellsX * cellsZ} cells refined along the roads");
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Grass, Name = "Terrain" };
    }

    /// <summary>The road material mapped by world position, for the disc of the pad.</summary>
    private static StandardMaterial3D PadAsphalt()
    {
        var material = (StandardMaterial3D)Asphalt.Duplicate();
        material.Uv1Triplanar = true;
        material.Uv1Scale = new Vector3(0.18f, 0.18f, 0.18f);
        material.AlbedoColor = new Color(0.55f, 0.55f, 0.55f);
        _textured.Add((material, material.AlbedoTexture, material.NormalTexture, material.RoughnessTexture));
        return material;
    }

    private static Node3D BuildPad(SkidPad pad)
    {
        var node = new Node3D { Name = "SkidPad", Position = Convert.ToGodot(pad.Centre) };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = pad.Radius, BottomRadius = pad.Radius + 1.5f, Height = 0.08f, RadialSegments = 96, Material = PadAsphalt() },
            Position = new Vector3(0f, 0.02f, 0f),
        });
        var paint = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.9f), Roughness = 0.7f };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = pad.Radius * 0.6f - 0.15f, OuterRadius = pad.Radius * 0.6f + 0.15f, RingSegments = 96, Rings = 4, Material = paint },
            Position = new Vector3(0f, 0.07f, 0f),
        });
        return node;
    }

    // ---- road ribbons ---------------------------------------------------------------------------------

    /// <summary>Vertices across the road: enough to carry a pothole's bowl and the wheel tracks' shade.</summary>
    private const int RoadStations = 16;
    /// <summary>Metres between rows of road vertices along the path (the frames are a metre apart).</summary>
    private const float RoadStep = 0.5f;

    /// <summary>The shade across the road: darker in the two wheel tracks of each lane.</summary>
    private static float TrackShade(float fraction)
    {
        var f = MathF.Abs(fraction);
        var wear = MathF.Max(Bump(f, 0.38f, 0.14f), Bump(f, 0.62f, 0.14f));
        return 1f - 0.16f * wear;

        static float Bump(float x, float centre, float width)
        {
            var d = MathF.Abs(x - centre) / width;
            return d >= 1f ? 0f : (1f - d * d) * (1f - d * d);
        }
    }

    /// <summary>
    /// The road ribbon as a grid: a row of vertices every half metre, sixteen stations across, each sunk by the
    /// road's wear (<see cref="RoadWear.Depth"/>, the same the wheels feel) with the normal tilted into the dent,
    /// and shaded by the slab, the wheel tracks, the patches, the pothole's bottom and a fine grain, so no two
    /// metres of asphalt look alike. UV1 carries the photo asphalt, UV2 the wear sheet.
    /// </summary>
    private static MeshInstance3D BuildRoad(TrackGeometry track, TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        // Where the stunt branch shares the road with the speed branch its asphalt sits a centimetre lower, under the
        // other's: two ribbons on one plane flickered against each other.
        var sink = path.Kind == PathKind.Stunt ? 0.012f : 0f;
        var grain = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.9f, Seed = 31 + (int)path.Kind };
        var drift = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 70f, Seed = 5 };
        var loopTint = new Color(1f, 0.62f, 0.55f);   // the loop is a painted steel structure in the original: a dark red deck
        var vertices = 0;

        int Row(in RoadFrame f)
        {
            var first = vertices;
            var shared = sink > 0f && SiblingSide(track, path, f) != 0;
            // Alternate the shade every original slab length: the stripey look of the 1989 road, softened.
            var slab = (int)MathF.Floor(f.Distance / TrackBuilder.Slab);
            var slabShade = slab % 2 == 0 ? 1f : 0.88f;
            var tint = f.Kind == SegmentKind.Loop ? loopTint : Colors.White;
            var gLateral = Convert.ToGodot(f.Lateral);
            var sign = Convert.ToGodot(f.Normal).Cross(gLateral).Dot(Convert.ToGodot(f.Tangent)) < 0f ? -1f : 1f;
            var acrossDrift = 0.25f * drift.GetNoise1D(f.Distance);
            for (var k = 0; k <= RoadStations; k++)
            {
                var fraction = -1f + 2f * k / RoadStations;
                var lateral = fraction * f.HalfWidth;
                var depth = RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth);
                var alongSlope = (RoadWear.Depth(path.Kind, f.Kind, f.Distance + 0.25f, lateral, f.HalfWidth) - RoadWear.Depth(path.Kind, f.Kind, f.Distance - 0.25f, lateral, f.HalfWidth)) / 0.5f;
                var acrossSlope = (RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral + 0.1f, f.HalfWidth) - RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral - 0.1f, f.HalfWidth)) / 0.2f;
                var normal = SN.Vector3.Normalize(f.Normal + f.Tangent * alongSlope + f.Lateral * acrossSlope);
                var position = f.Origin + f.Lateral * lateral - f.Normal * (depth + (shared ? sink : 0f));
                var tone = slabShade * TrackShade(fraction)
                    * RoadWear.PatchTone(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth)
                    * MathF.Max(1f - 9f * MathF.Max(depth - 0.004f, 0f), 0.6f)
                    * (1f + 0.05f * grain.GetNoise2D(f.Distance * 3.7f, lateral * 2.1f));
                var colour = tint * tone;
                colour.A = 1f;
                st.SetColor(colour);
                st.SetNormal(Convert.ToGodot(normal));
                st.SetTangent(gLateral, sign);
                st.SetUV(new Vector2((fraction + 1f) * 0.5f, f.Distance));
                st.SetUV2(new Vector2((fraction + 1f) * 0.5f + acrossDrift, f.Distance / 40f));
                st.AddVertex(Convert.ToGodot(position));
                vertices++;
            }
            return first;
        }

        void Join(int previous, int row)
        {
            for (var k = 0; k < RoadStations; k++)
            {
                st.AddIndex(previous + k); st.AddIndex(row + k); st.AddIndex(previous + k + 1);
                st.AddIndex(previous + k + 1); st.AddIndex(row + k); st.AddIndex(row + k + 1);
            }
        }

        var previousRow = -1;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm)
            {
                previousRow = -1;
                continue;
            }
            var steps = Math.Max(1, (int)MathF.Round((b.Distance - a.Distance) / RoadStep));
            for (var s = 0; s < steps; s++)
            {
                var row = Row(s == 0 ? a : RoadFrame.Lerp(a, b, (float)s / steps));
                if (previousRow >= 0) Join(previousRow, row);
                previousRow = row;
            }
            // The run's last frame closes it: at the end of the path or before a gap.
            if (i == frames.Count - 2 || frames[i + 2].Surface == SurfaceType.Chasm)
            {
                var row = Row(b);
                Join(previousRow, row);
                previousRow = -1;
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Asphalt, Name = $"Road-{path.Kind}" };
    }

    private static bool IsStructure(in RoadFrame f) => f.Kind is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Overpass or SegmentKind.BankedCurve;

    /// <summary>Metres from a fork or a merge over which the two branches still overlap and share one road.</summary>
    private const float BranchOverlap = 55f;

    /// <summary>
    /// The side (+1 right, -1 left) of a frame on which its sibling branch runs, where the branches part at the
    /// fork or meet at the merge, or 0 away from both. The branches leave one frame and arrive at one frame, so
    /// over their first and last metres two ribbons lie on the same ground: each keeps its own outer edge, and the
    /// shoulder and the white line on the inner side are left to the other, or the lines crossed and the shoulders
    /// cut through the asphalt. Compared against the sibling's own overlap so the test is cheap.
    /// </summary>
    private static int SiblingSide(TrackGeometry track, TrackPath path, in RoadFrame f)
    {
        TrackPath? sibling = path.Kind switch
        {
            PathKind.Speed => track.Stunt,
            PathKind.Stunt => track.Speed,
            _ => null,
        };
        if (sibling is null) return 0;
        var atFork = f.Distance < BranchOverlap;
        var atMerge = path.Length - f.Distance < BranchOverlap;
        if (!atFork && !atMerge) return 0;
        var frames = sibling.Frames;
        var nearest = Vector3.Zero;
        var best = float.MaxValue;
        var from = atFork ? 0 : Math.Max(frames.Count - 1 - (int)(BranchOverlap * 2f), 0);
        var to = atFork ? Math.Min((int)(BranchOverlap * 2f), frames.Count - 1) : frames.Count - 1;
        for (var i = from; i <= to; i++)
        {
            var d = SN.Vector3.DistanceSquared(frames[i].Origin, f.Origin);
            if (d < best)
            {
                best = d;
                nearest = Convert.ToGodot(frames[i].Origin);
            }
        }
        // Past two road widths apart the branches have parted and both edges are their own again.
        if (best > 4f * f.HalfWidth * f.HalfWidth) return 0;
        var toSibling = nearest - Convert.ToGodot(f.Origin);
        return toSibling.Dot(Convert.ToGodot(f.Lateral)) >= 0f ? 1 : -1;
    }

    private static MeshInstance3D BuildShoulders(TrackGeometry track, TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        var gravel = new Color(0.96f, 0.92f, 0.84f);
        var orange = new Color(0.95f, 0.46f, 0.12f);
        // As wide as the original's dirt shoulders; the ground field keeps its plane out past this (TerrainField.Verge).
        const float width = 2.2f;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            // Earth is never one colour: every couple of metres the shoulder is a little paler or darker.
            var colour = (IsStructure(a) ? orange : gravel) * (1f + 0.14f * (RoadWear.Hash((int)(a.Distance / 2f), 0, 23) - 0.5f));
            colour.A = 1f;
            var shared = SiblingSide(track, path, a);
            foreach (var side in new[] { -1f, 1f })
            {
                if ((int)side == shared) continue;
                var ao = a.Origin + a.Lateral * (side * a.HalfWidth);
                var bo = b.Origin + b.Lateral * (side * b.HalfWidth);
                var ai = ao + a.Lateral * (side * width) - a.Normal * 0.02f;
                var bi = bo + b.Lateral * (side * width) - b.Normal * 0.02f;
                // Keep the winding left-to-right on both sides: back faces get their normal flipped and go dark.
                if (side > 0) Quad(st, ao, ai, bo, bi, a.Normal, b.Normal, colour, a.Distance, b.Distance);
                else Quad(st, ai, ao, bi, bo, a.Normal, b.Normal, colour, a.Distance, b.Distance);
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Gravel, Name = $"Shoulder-{path.Kind}" };
    }

    private static MeshInstance3D BuildCentreLine(TrackGeometry track, TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        var yellow = new Color(0.95f, 0.78f, 0.20f);
        var white = new Color(0.9f, 0.9f, 0.9f);
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            var shared = SiblingSide(track, path, a);
            // Paint wears: every dash is a little brighter or duller than the next, a few are gone; the edge lines
            // fade in and out along the road. Where the branches share the road, only the one on the right paints
            // the centre line.
            var dash = (int)MathF.Floor(a.Distance / 9f);
            var dashTone = 0.55f + 0.45f * RoadWear.Hash(dash, (int)path.Kind, 41);
            if (a.Distance % 9f < 3f && shared != 1 && RoadWear.Hash(dash, (int)path.Kind, 42) > 0.08f)
            {
                Quad(st, a.Origin - a.Lateral * 0.08f + a.Normal * 0.012f, a.Origin + a.Lateral * 0.08f + a.Normal * 0.012f,
                    b.Origin - b.Lateral * 0.08f + b.Normal * 0.012f, b.Origin + b.Lateral * 0.08f + b.Normal * 0.012f,
                    a.Normal, b.Normal, (yellow * dashTone) with { A = 1f }, a.Distance, b.Distance);
            }
            foreach (var side in new[] { -1f, 1f })
            {
                if ((int)side == shared) continue;
                var o = side * (a.HalfWidth - 0.25f);
                var edgeTone = 0.6f + 0.4f * RoadWear.Hash((int)(a.Distance / 3f), (int)side, 43);
                Quad(st, a.Origin + a.Lateral * (o - 0.07f) + a.Normal * 0.012f, a.Origin + a.Lateral * (o + 0.07f) + a.Normal * 0.012f,
                    b.Origin + b.Lateral * (o - 0.07f) + b.Normal * 0.012f, b.Origin + b.Lateral * (o + 0.07f) + b.Normal * 0.012f,
                    a.Normal, b.Normal, (white * edgeTone) with { A = 1f }, a.Distance, b.Distance);
            }
        }
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, Name = $"Lines-{path.Kind}" };
    }

    /// <summary>Overpass decks and drawbridge ramps get concrete pillars (down to the ground) and railings.</summary>
    private static IEnumerable<Node3D> BuildBridges(TrackPath path, TerrainField terrain)
    {
        var frames = path.Frames;
        var railings = new MeshBuilder();
        var grey = new Color(0.75f, 0.75f, 0.75f);
        var anyRail = false;
        var lastPillar = -100f;
        var lastPost = -100f;
        var pillars = new Node3D { Name = $"Pillars-{path.Kind}" };
        var towers = new MeshBuilder();
        var anyTower = false;

        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            var bridge = a.Kind is SegmentKind.Overpass or SegmentKind.RampUp or SegmentKind.RampDown && a.Origin.Y > 0.4f;
            if (!bridge) continue;

            // Steel guardrail: two thin rails on posts every six metres, not a wall.
            foreach (var side in new[] { -1f, 1f })
            {
                var ao = a.Origin + a.Lateral * (side * (a.HalfWidth + 1.2f));
                var bo = b.Origin + b.Lateral * (side * (b.HalfWidth + 1.2f));
                foreach (var (lo, hi) in new[] { (0.72f, 0.88f), (0.36f, 0.46f) })
                {
                    if (side > 0) Quad(railings, ao + a.Normal * lo, ao + a.Normal * hi, bo + b.Normal * lo, bo + b.Normal * hi, a.Lateral * side, b.Lateral * side, grey, a.Distance, b.Distance);
                    else Quad(railings, ao + a.Normal * hi, ao + a.Normal * lo, bo + b.Normal * hi, bo + b.Normal * lo, a.Lateral * side, b.Lateral * side, grey, a.Distance, b.Distance);
                }
                anyRail = true;
                if (a.Distance - lastPost >= 6f)
                {
                    var post = new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.95f, 0.1f), Material = Concrete },
                        Position = Convert.ToGodot(ao + a.Normal * 0.45f),
                        Rotation = new Vector3(0f, -MathF.Atan2(a.Tangent.X, a.Tangent.Z), 0f),
                    };
                    pillars.AddChild(post);
                }
            }
            if (a.Distance - lastPost >= 6f) lastPost = a.Distance;

            if (a.Distance - lastPillar >= 14f && a.Origin.Y > 1.0f)
            {
                lastPillar = a.Distance;
                foreach (var side in new[] { -1f, 1f })
                {
                    var foot = a.Origin + a.Lateral * (side * (a.HalfWidth - 1.2f));
                    var ground = terrain.Height(foot.X, foot.Z) - 0.3f;
                    // Stop 0.25 m under the deck so the pillar top never shows through a low stretch of ramp.
                    var height = foot.Y - 0.25f - ground;
                    if (height < 0.5f) continue;
                    var pillar = new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.2f, height, 1.6f), Material = Concrete },
                        Position = Convert.ToGodot(new SN.Vector3(foot.X, ground + height * 0.5f, foot.Z)),
                        Rotation = new Vector3(0f, -MathF.Atan2(a.Tangent.X, a.Tangent.Z), 0f),
                    };
                    pillars.AddChild(pillar);
                }
            }

            // Drawbridge towers at the lip of the gap: the white A-frames of the original, joined over the road, with
            // cables back to the deck.
            if (a.Kind == SegmentKind.RampUp && b.Kind == SegmentKind.Gap || a.Kind == SegmentKind.Gap && b.Kind == SegmentKind.RampDown)
            {
                var edge = a.Kind == SegmentKind.Gap ? b : a;
                var back = a.Kind == SegmentKind.Gap ? 1f : -1f;   // along the path, toward the ramp's deck
                var white = new Color(0.93f, 0.93f, 0.90f);
                var cable = new Color(0.25f, 0.25f, 0.27f);
                var apexes = new SN.Vector3[2];
                for (var sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    var side = sideIndex == 0 ? -1f : 1f;
                    var foot = edge.Origin + edge.Lateral * (side * (edge.HalfWidth + 2.6f));
                    var ground = new SN.Vector3(foot.X, terrain.Height(foot.X, foot.Z) - 0.3f, foot.Z);
                    var apex = new SN.Vector3(foot.X, foot.Y + 11f, foot.Z);
                    apexes[sideIndex] = apex;
                    var legA = ground - edge.Tangent * 2.4f;
                    var legB = ground + edge.Tangent * 2.4f;
                    Beam(towers, legA, apex, 0.55f, white);
                    Beam(towers, legB, apex, 0.55f, white);
                    Beam(towers, SN.Vector3.Lerp(legA, apex, 0.55f), SN.Vector3.Lerp(legB, apex, 0.55f), 0.35f, white);
                    for (var k = 1; k <= 3; k++)
                    {
                        var frame = path.FrameAt(MathUtil.Clamp(edge.Distance + back * 12f * k, 0f, path.Length));
                        var anchor = frame.Origin + frame.Lateral * (side * (frame.HalfWidth + 1.0f)) + frame.Normal * 0.9f;
                        Beam(towers, apex, anchor, 0.09f, cable);
                    }
                }
                Beam(towers, apexes[0], apexes[1], 0.5f, white);   // the portal beam over the road
                anyTower = true;
            }
        }

        yield return pillars;
        if (anyTower) yield return new MeshInstance3D { Mesh = towers.Commit(), MaterialOverride = Steel, Name = $"Towers-{path.Kind}" };
        if (anyRail)
        {
            var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.5f, Metallic = 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            yield return new MeshInstance3D { Mesh = railings.Commit(), MaterialOverride = material, Name = $"Railings-{path.Kind}" };
        }
    }

    /// <summary>
    /// The loop's steelwork: a ring of girders outside the deck on both sides, struts from the deck's edges to the
    /// ring, cross-beams and braces under the deck, and legs to the ground where the loop is low. From the plain it
    /// reads as the lattice the real thing could not stand without.
    /// </summary>
    private static MeshInstance3D? BuildLoopFrame(TrackPath path, TerrainField terrain)
    {
        var st = new MeshBuilder();
        var steel = new Color(0.58f, 0.60f, 0.63f);
        var any = false;
        var last = -100f;
        SN.Vector3[]? previousRing = null;
        foreach (var f in path.Frames)
        {
            if (f.Kind != SegmentKind.Loop)
            {
                previousRing = null;
                continue;
            }
            if (f.Distance - last < 2.5f) continue;
            last = f.Distance;
            var ring = new SN.Vector3[2];
            for (var sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                var side = sideIndex == 0 ? -1f : 1f;
                var edge = f.Origin + f.Lateral * (side * (f.HalfWidth + 0.25f));
                // The ring sits outside the deck: below it on the way in, above it over the top.
                ring[sideIndex] = edge - f.Normal * 1.6f + f.Lateral * (side * 0.3f);
                Beam(st, edge, ring[sideIndex], 0.16f, steel);
                if (previousRing is not null) Beam(st, previousRing[sideIndex], ring[sideIndex], 0.36f, steel);
                // Legs where the deck is low and faces up: the way in, the way out and the bottom of the loop.
                var ground = terrain.Height(ring[sideIndex].X, ring[sideIndex].Z) - 0.3f;
                var clearance = ring[sideIndex].Y - ground;
                if (f.Normal.Y > 0.3f && clearance is > 0.6f and < 9f && (int)MathF.Round(f.Distance / 2.5f) % 2 == 0)
                {
                    Beam(st, ring[sideIndex], new SN.Vector3(ring[sideIndex].X, ground, ring[sideIndex].Z), 0.3f, steel);
                }
            }
            Beam(st, ring[0], ring[1], 0.3f, steel);
            if (previousRing is not null) Beam(st, previousRing[0], ring[1], 0.12f, steel);
            previousRing = ring;
            any = true;
        }
        return any ? new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Steel, Name = $"LoopFrame-{path.Kind}" } : null;
    }

    /// <summary>A box of square section <paramref name="thickness"/> from <paramref name="a"/> to <paramref name="b"/> (simulation space), flat-shaded; for <see cref="Steel"/>, which draws both faces.</summary>
    internal static void Beam(MeshBuilder st, SN.Vector3 a, SN.Vector3 b, float thickness, Color colour)
    {
        var ga = Convert.ToGodot(a);
        var gb = Convert.ToGodot(b);
        var axis = gb - ga;
        var length = axis.Length();
        if (length < 1e-3f) return;
        axis /= length;
        var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.Up : Vector3.Right;
        var u = axis.Cross(reference).Normalized() * (thickness * 0.5f);
        var v = axis.Cross(u).Normalized() * (thickness * 0.5f);
        Span<Vector3> corners = [ga + u + v, ga - u + v, ga - u - v, ga + u - v];
        var along = axis * length;
        for (var i = 0; i < 4; i++)
        {
            var c0 = corners[i];
            var c1 = corners[(i + 1) % 4];
            Face(st, c0, c1, c1 + along, c0 + along, ((c0 + c1) * 0.5f - ga).Normalized(), colour);
        }
        Face(st, corners[3], corners[2], corners[1], corners[0], -axis, colour);
        Face(st, corners[0] + along, corners[1] + along, corners[2] + along, corners[3] + along, axis, colour);
    }

    private static void Face(MeshBuilder st, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Color colour)
    {
        st.SetColor(colour);
        st.SetNormal(normal);
        st.SetUV(new Vector2(0f, 0f)); st.AddVertex(p0);
        st.SetUV(new Vector2(1f, 0f)); st.AddVertex(p1);
        st.SetUV(new Vector2(1f, 1f)); st.AddVertex(p2);
        st.SetUV(new Vector2(0f, 0f)); st.AddVertex(p0);
        st.SetUV(new Vector2(1f, 1f)); st.AddVertex(p2);
        st.SetUV(new Vector2(0f, 1f)); st.AddVertex(p3);
    }

    /// <summary>Two triangles between consecutive cross-sections (al, ar) and (bl, br), with per-vertex normals; u runs left to right across the strip.</summary>
    internal static void Quad(MeshBuilder st, SN.Vector3 al, SN.Vector3 ar, SN.Vector3 bl, SN.Vector3 br, SN.Vector3 na, SN.Vector3 nb, Color colour, float ua, float ub, float uLeft = 0f, float uRight = 1f)
    {
        var gal = Convert.ToGodot(al);
        var gar = Convert.ToGodot(ar);
        var gbl = Convert.ToGodot(bl);
        var gbr = Convert.ToGodot(br);
        var gna = Convert.ToGodot(na);
        var gnb = Convert.ToGodot(nb);
        st.SetColor(colour);
        // u runs across the strip and v along it, so the tangent is the strip's left-to-right edge; the sign
        // turns the bitangent along v, as the general tangent solver would.
        var tangent = (gar - gal).Normalized();
        st.SetTangent(tangent, gna.Cross(tangent).Dot(gbl - gal) < 0f ? -1f : 1f);
        st.SetNormal(gna); st.SetUV(new Vector2(uLeft, ua)); st.AddVertex(gal);
        st.SetNormal(gnb); st.SetUV(new Vector2(uLeft, ub)); st.AddVertex(gbl);
        st.SetNormal(gna); st.SetUV(new Vector2(uRight, ua)); st.AddVertex(gar);
        st.SetNormal(gna); st.SetUV(new Vector2(uRight, ua)); st.AddVertex(gar);
        st.SetNormal(gnb); st.SetUV(new Vector2(uLeft, ub)); st.AddVertex(gbl);
        st.SetNormal(gnb); st.SetUV(new Vector2(uRight, ub)); st.AddVertex(gbr);
    }
}
