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
    private static readonly List<(StandardMaterial3D Material, Texture2D? Albedo, Texture2D? Normal, Texture2D? Roughness)> _textured = new();
    private static readonly Dictionary<string, StandardMaterial3D> _propMaterials = new();
    /// <summary>Sun direction measured from the brightest patch of the sky HDRI (see assets/textures/SOURCES.md): elevation 47.9, panorama u 0.595.</summary>
    private static readonly Vector3 SunDirection = new(-0.377f, 0.742f, -0.554f);

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
        root.AddChild(BuildTerrain(track));
        foreach (var path in track.Paths)
        {
            root.AddChild(BuildRoad(path));
            root.AddChild(BuildShoulders(path));
            root.AddChild(BuildCentreLine(path));
            foreach (var node in BuildBridges(path, track.Terrain)) root.AddChild(node);
        }
        foreach (var pad in track.Pads) root.AddChild(BuildPad(pad));
        root.AddChild(FoliageBuilder.BuildGrass(track));
    }

    /// <summary>
    /// The 1989 look: no textures, nearest filtering, no global illumination, ambient occlusion, reflections,
    /// glow, fog or shadows, a flat sky. The caller lowers the render resolution and caps the frame rate.
    /// </summary>
    public static void SetRetro(bool retro)
    {
        foreach (var (material, albedo, normal, roughness) in _textured)
        {
            material.AlbedoTexture = retro ? null : albedo;
            material.NormalEnabled = !retro && normal is not null;
            material.NormalTexture = retro ? null : normal;
            material.RoughnessTexture = retro ? null : roughness;
            material.TextureFilter = retro ? BaseMaterial3D.TextureFilterEnum.Nearest : BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        }
        if (_sun is not null) _sun.ShadowEnabled = !retro;
        // 1989 shoulders were orange everywhere; today's are gravel except on the structures.
        if (_gravel is not null) _gravel.AlbedoColor = retro ? new Color(0.9f, 0.45f, 0.1f) : new Color(0.9f, 0.86f, 0.78f);
        if (_environment is null) return;
        _environment.SdfgiEnabled = !retro;
        _environment.SsaoEnabled = !retro;
        _environment.SsilEnabled = !retro;
        _environment.SsrEnabled = !retro;
        _environment.GlowEnabled = !retro;
        _environment.VolumetricFogEnabled = !retro;
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
        AlbedoColor = new Color(0.60f, 0.68f, 0.48f),
        Roughness = 1f,
        Uv1Scale = new Vector3(1150f, 1150f, 1f),    // the terrain's UV spans 5000 m: a 4.3 m tile
        VertexColorUseAsAlbedo = true,
    }, Photo("grass_color") ?? Noise(0.9f, 5, new[] { (0f, new Color(0.62f, 0.72f, 0.55f)), (0.5f, new Color(0.95f, 1.0f, 0.9f)), (1f, new Color(1.25f, 1.2f, 1.1f)) }, seed: 3),
       Photo("grass_normal") ?? Noise(2.5f, 4, null, seed: 11, asNormal: true), normalScale: 0.7f, Photo("grass_roughness"));

    private static StandardMaterial3D Asphalt => _asphalt ??= Textured(new StandardMaterial3D
    {
        // Slightly darkened photo asphalt; the slab shade and the wheel tracks come from the vertex colour multiplied in.
        AlbedoColor = new Color(0.46f, 0.46f, 0.47f),
        VertexColorUseAsAlbedo = true,
        Roughness = 1f,
        Uv1Scale = new Vector3(2f, 0.18f, 1f),      // u spans the 11 m width, v is metres along: a 5.5 m tile
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    }, Photo("asphalt_color") ?? Noise(9f, 4, new[] { (0f, new Color(0.82f, 0.82f, 0.82f)), (0.5f, new Color(0.98f, 0.98f, 0.98f)), (1f, new Color(1.15f, 1.15f, 1.15f)) }, seed: 5),
       Photo("asphalt_normal") ?? Noise(16f, 4, null, seed: 17, asNormal: true), normalScale: 0.5f, Photo("asphalt_roughness"));

    private static StandardMaterial3D Concrete => _concrete ??= Textured(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.80f, 0.79f, 0.76f),
        Roughness = 1f,
        Uv1Scale = new Vector3(0.4f, 0.4f, 0.4f),
        Uv1Triplanar = true,
    }, Photo("concrete_color") ?? Noise(1.5f, 3, new[] { (0f, new Color(0.8f, 0.8f, 0.8f)), (1f, new Color(1f, 1f, 1f)) }, seed: 23),
       Photo("concrete_normal"), 0.5f, Photo("concrete_roughness"));

    /// <summary>Shoulders: gravel along the road (u across, v in metres along); the vertex colour tints the structures orange.</summary>
    private static StandardMaterial3D Gravel => _gravel ??= Textured(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.9f, 0.86f, 0.78f),
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
    private static Texture2D? Photo(string name)
    {
        var path = $"res://assets/textures/{name}.jpg";
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
            sky = new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>("res://assets/textures/sky.hdr"), EnergyMultiplier = 0.85f };
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
            FogEnabled = true,
            FogLightColor = new Color(0.78f, 0.84f, 0.92f),
            FogDensity = 0.00022f,
            FogAerialPerspective = 0.65f,
            FogSkyAffect = 0.1f,
            // A thin haze near the ground: it softens the far hills and gives the sun something to light.
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.0007f,
            VolumetricFogAlbedo = new Color(0.9f, 0.93f, 1f),
            VolumetricFogLength = 180f,
            VolumetricFogSkyAffect = 0.2f,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.04f,
            AdjustmentSaturation = 1.06f,
        };
        return new WorldEnvironment { Environment = _environment };
    }

    private static DirectionalLight3D BuildSun()
    {
        _sun = new DirectionalLight3D
        {
            LightEnergy = 1.2f,
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

    /// <summary>Flat ground to the horizon, just under the terrain mesh, where the ground field has flattened out.</summary>
    private static MeshInstance3D BuildFarGround()
    {
        var mesh = new PlaneMesh { Size = new Vector2(9000f, 9000f), SubdivideWidth = 12, SubdivideDepth = 12, Material = Grass };
        return new MeshInstance3D { Mesh = mesh, Position = new Vector3(0f, -0.06f, 0f), Name = "FarGround" };
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
            // Sandy, trodden ground right at the road edge.
            var edge = MathUtil.Clamp01((vertexWeight[v] - 0.55f) / 0.45f);
            r = MathUtil.Lerp(r, shade * 1.05f, edge * 0.6f);
            g = MathUtil.Lerp(g, shade * 0.95f, edge * 0.6f);
            b = MathUtil.Lerp(b, shade * 0.72f, edge * 0.6f);
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

    /// <summary>Lateral stations across the road (fractions of the half width) and the shade at each: darker in the wheel tracks.</summary>
    private static readonly (float Fraction, float Shade)[] RoadProfile =
    [
        (-1f, 1f), (-0.62f, 0.84f), (-0.38f, 0.84f), (0f, 1f), (0.38f, 0.84f), (0.62f, 0.84f), (1f, 1f),
    ];

    private static MeshInstance3D BuildRoad(TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            // Alternate the shade every original slab length: the stripey look of the 1989 road.
            var slab = (int)MathF.Floor(a.Distance / TrackBuilder.Slab);
            var slabShade = slab % 2 == 0 ? 1f : 0.76f;
            // The loop is a painted steel structure in the original: a dark red deck.
            var tint = a.Kind == SegmentKind.Loop ? new Color(1f, 0.62f, 0.55f) : new Color(1f, 1f, 1f);
            for (var k = 0; k < RoadProfile.Length - 1; k++)
            {
                var (f0, s0) = RoadProfile[k];
                var (f1, s1) = RoadProfile[k + 1];
                var colour = tint * (slabShade * (0.5f * (s0 + s1)));
                colour.A = 1f;
                Quad(st,
                    a.Origin + a.Lateral * (f0 * a.HalfWidth), a.Origin + a.Lateral * (f1 * a.HalfWidth),
                    b.Origin + b.Lateral * (f0 * b.HalfWidth), b.Origin + b.Lateral * (f1 * b.HalfWidth),
                    a.Normal, b.Normal, colour, a.Distance, b.Distance, (f0 + 1f) * 0.5f, (f1 + 1f) * 0.5f);
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Asphalt, Name = $"Road-{path.Kind}" };
    }

    private static bool IsStructure(in RoadFrame f) => f.Kind is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Overpass or SegmentKind.BankedCurve;

    private static MeshInstance3D BuildShoulders(TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        var gravel = new Color(0.95f, 0.92f, 0.85f);
        var orange = new Color(0.95f, 0.46f, 0.12f);
        const float width = 1.3f;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            var colour = IsStructure(a) ? orange : gravel;
            foreach (var side in new[] { -1f, 1f })
            {
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

    private static MeshInstance3D BuildCentreLine(TrackPath path)
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
            if (a.Distance % 9f < 3f)
            {
                Quad(st, a.Origin - a.Lateral * 0.08f + a.Normal * 0.012f, a.Origin + a.Lateral * 0.08f + a.Normal * 0.012f,
                    b.Origin - b.Lateral * 0.08f + b.Normal * 0.012f, b.Origin + b.Lateral * 0.08f + b.Normal * 0.012f,
                    a.Normal, b.Normal, yellow, a.Distance, b.Distance);
            }
            foreach (var side in new[] { -1f, 1f })
            {
                var o = side * (a.HalfWidth - 0.25f);
                Quad(st, a.Origin + a.Lateral * (o - 0.07f) + a.Normal * 0.012f, a.Origin + a.Lateral * (o + 0.07f) + a.Normal * 0.012f,
                    b.Origin + b.Lateral * (o - 0.07f) + b.Normal * 0.012f, b.Origin + b.Lateral * (o + 0.07f) + b.Normal * 0.012f,
                    a.Normal, b.Normal, white, a.Distance, b.Distance);
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

            // Drawbridge towers at the lip of the gap.
            if (a.Kind == SegmentKind.RampUp && b.Kind == SegmentKind.Gap || a.Kind == SegmentKind.Gap && b.Kind == SegmentKind.RampDown)
            {
                var edge = a.Kind == SegmentKind.Gap ? b : a;
                foreach (var side in new[] { -1f, 1f })
                {
                    var foot = edge.Origin + edge.Lateral * (side * (edge.HalfWidth + 2.2f));
                    var ground = terrain.Height(foot.X, foot.Z) - 0.3f;
                    var height = foot.Y + 6f - ground;
                    var tower = new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(2.4f, height, 3.0f), Material = Concrete },
                        Position = Convert.ToGodot(new SN.Vector3(foot.X, ground + height * 0.5f, foot.Z)),
                        Rotation = new Vector3(0f, -MathF.Atan2(edge.Tangent.X, edge.Tangent.Z), 0f),
                    };
                    pillars.AddChild(tower);
                }
            }
        }

        yield return pillars;
        if (anyRail)
        {
            var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.5f, Metallic = 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            yield return new MeshInstance3D { Mesh = railings.Commit(), MaterialOverride = material, Name = $"Railings-{path.Kind}" };
        }
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
