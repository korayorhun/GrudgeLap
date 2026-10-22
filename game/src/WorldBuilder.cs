using Godot;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Builds the visible world straight from the track geometry: textured road ribbons with the alternating
/// slabs of the original, orange shoulders, a terrain mesh sampled from the simulation's own ground field
/// (so embankments, cuttings and dips are the same surface the car drives on), bridge decks with pillars and
/// railings, the skid pad, sky and sun, and roadside props. Textures are procedural noise so the repository
/// stays free of binary assets. <see cref="SetRetro"/> switches the whole world to the flat-shaded look of 1989.
/// </summary>
internal static class WorldBuilder
{
    private const float TerrainCell = 8f;
    private const float TerrainMargin = 480f;

    private static StandardMaterial3D? _grass;
    private static StandardMaterial3D? _asphalt;
    private static StandardMaterial3D? _concrete;
    private static Godot.Environment? _environment;
    private static readonly List<(StandardMaterial3D Material, Texture2D? Albedo, Texture2D? Normal, Texture2D? Roughness)> _textured = new();
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
        _environment = null;
        _textured.Clear();
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
        foreach (var prop in track.Props) root.AddChild(PropBuilder.Build(prop));
    }

    /// <summary>
    /// The 1989 look: no textures, nearest filtering, no global illumination, ambient occlusion, glow or fog,
    /// a flat sky. The caller lowers the render resolution and caps the frame rate.
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
        if (_environment is null) return;
        _environment.SdfgiEnabled = !retro;
        _environment.SsaoEnabled = !retro;
        _environment.GlowEnabled = !retro;
        _environment.FogEnabled = !retro;
        _environment.BackgroundMode = retro ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
        _environment.BackgroundColor = new Color(0.30f, 0.52f, 0.86f);
        _environment.AmbientLightSource = retro ? Godot.Environment.AmbientSource.Color : Godot.Environment.AmbientSource.Sky;
        _environment.AmbientLightColor = new Color(0.55f, 0.6f, 0.7f);
        _environment.AmbientLightEnergy = retro ? 0.9f : 1f;
        _environment.TonemapMode = retro ? Godot.Environment.ToneMapper.Linear : Godot.Environment.ToneMapper.Aces;
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
        // Slightly darkened photo asphalt; the slab shade comes from the vertex colour multiplied in.
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
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.85f,
            TonemapWhite = 6f,
            SdfgiEnabled = true,
            SdfgiCascades = 6,
            SdfgiMinCellSize = 0.4f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 1.5f,
            GlowEnabled = true,
            GlowIntensity = 0.2f,
            GlowBloom = 0.02f,
            FogEnabled = true,
            FogLightColor = new Color(0.78f, 0.84f, 0.92f),
            FogDensity = 0.0009f,
            FogAerialPerspective = 0.35f,
            FogSkyAffect = 0.15f,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.05f,
            AdjustmentSaturation = 1.08f,
        };
        return new WorldEnvironment { Environment = _environment };
    }

    private static DirectionalLight3D BuildSun()
    {
        var sun = new DirectionalLight3D
        {
            LightEnergy = 1.2f,
            LightColor = new Color(1f, 0.96f, 0.90f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 450f,
            DirectionalShadowSplit1 = 0.05f,
            DirectionalShadowSplit2 = 0.15f,
            DirectionalShadowSplit3 = 0.4f,
            ShadowBias = 0.025f,
            ShadowNormalBias = 1.5f,
        };
        // Shine from where the HDRI's sun is, so shadows and the sky agree.
        sun.LookAtFromPosition(Vector3.Zero, -SunDirection, Vector3.Up);
        return sun;
    }

    /// <summary>Flat ground to the horizon, just under the terrain mesh, where the ground field has flattened out.</summary>
    private static MeshInstance3D BuildFarGround()
    {
        var mesh = new PlaneMesh { Size = new Vector2(6000f, 6000f), SubdivideWidth = 12, SubdivideDepth = 12, Material = Grass };
        return new MeshInstance3D { Mesh = mesh, Position = new Vector3(0f, -0.06f, 0f), Name = "FarGround" };
    }

    /// <summary>Grid mesh of the ground field over the circuit; vertex colour darkens cuttings and slopes.</summary>
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
        for (var j = 0; j < nz; j++)
        {
            for (var i = 0; i < nx; i++) heights[j * nx + i] = terrain.Height(minX + i * TerrainCell, minZ + j * TerrainCell);
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var j = 0; j < nz; j++)
        {
            for (var i = 0; i < nx; i++)
            {
                var x = minX + i * TerrainCell;
                var z = minZ + j * TerrainCell;
                var h = heights[j * nx + i];
                var hx0 = heights[j * nx + Math.Max(i - 1, 0)];
                var hx1 = heights[j * nx + Math.Min(i + 1, nx - 1)];
                var hz0 = heights[Math.Max(j - 1, 0) * nx + i];
                var hz1 = heights[Math.Min(j + 1, nz - 1) * nx + i];
                var normal = new SN.Vector3(-(hx1 - hx0) / (2f * TerrainCell), 1f, -(hz1 - hz0) / (2f * TerrainCell));
                normal = SN.Vector3.Normalize(normal);
                // Flat ground is full brightness; slopes and low ground shade toward a darker green.
                var shade = 0.78f + 0.22f * normal.Y - 0.015f * MathF.Max(-h, 0f);
                st.SetColor(new Color(shade, shade, shade));
                st.SetNormal(Convert.ToGodot(normal));
                st.SetUV(new Vector2(x / 5000f + 0.5f, z / 5000f + 0.5f));
                st.AddVertex(Convert.ToGodot(new SN.Vector3(x, h, z)));
            }
        }
        for (var j = 0; j < nz - 1; j++)
        {
            for (var i = 0; i < nx - 1; i++)
            {
                var a = j * nx + i;
                var b = a + 1;
                var c = a + nx;
                var d = c + 1;
                // Godot front faces wind clockwise; the Z flip of the view makes the simulation's counter-clockwise order clockwise.
                st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                st.AddIndex(b); st.AddIndex(d); st.AddIndex(c);
            }
        }
        st.GenerateTangents();
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
            Mesh = new CylinderMesh { TopRadius = 2.2f, BottomRadius = 2.2f, Height = 0.02f, RadialSegments = 32, Material = paint },
            Position = new Vector3(0f, 0.07f, 0f),
        });
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = pad.Radius * 0.6f - 0.15f, OuterRadius = pad.Radius * 0.6f + 0.15f, RingSegments = 96, Rings = 4, Material = paint },
            Position = new Vector3(0f, 0.07f, 0f),
        });
        return node;
    }

    // ---- road ribbons ---------------------------------------------------------------------------------

    private static MeshInstance3D BuildRoad(TrackPath path)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var frames = path.Frames;
        var light = new Color(1f, 1f, 1f);
        var dark = new Color(0.72f, 0.72f, 0.73f);
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            // Alternate the shade every original slab length: the stripey look of the 1989 road.
            var slab = (int)MathF.Floor(a.Distance / TrackBuilder.Slab);
            var colour = slab % 2 == 0 ? light : dark;
            Quad(st,
                a.Origin - a.Lateral * a.HalfWidth, a.Origin + a.Lateral * a.HalfWidth,
                b.Origin - b.Lateral * b.HalfWidth, b.Origin + b.Lateral * b.HalfWidth,
                a.Normal, b.Normal, colour, a.Distance, b.Distance);
        }
        st.GenerateTangents();
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Asphalt, Name = $"Road-{path.Kind}" };
    }

    private static MeshInstance3D BuildShoulders(TrackPath path)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var frames = path.Frames;
        var orange = new Color(0.66f, 0.34f, 0.10f);
        const float width = 1.3f;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            foreach (var side in new[] { -1f, 1f })
            {
                var ao = a.Origin + a.Lateral * (side * a.HalfWidth);
                var bo = b.Origin + b.Lateral * (side * b.HalfWidth);
                var ai = ao + a.Lateral * (side * width) - a.Normal * 0.02f;
                var bi = bo + b.Lateral * (side * width) - b.Normal * 0.02f;
                // Keep the winding left-to-right on both sides: back faces get their normal flipped and go dark.
                if (side > 0) Quad(st, ao, ai, bo, bi, a.Normal, b.Normal, orange, a.Distance, b.Distance);
                else Quad(st, ai, ao, bi, bo, a.Normal, b.Normal, orange, a.Distance, b.Distance);
            }
        }
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.92f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, Name = $"Shoulder-{path.Kind}" };
    }

    private static MeshInstance3D BuildCentreLine(TrackPath path)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
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
        var railings = new SurfaceTool();
        railings.Begin(Mesh.PrimitiveType.Triangles);
        var grey = new Color(0.75f, 0.75f, 0.75f);
        var anyRail = false;
        var lastPillar = -100f;
        var pillars = new Node3D { Name = $"Pillars-{path.Kind}" };

        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            var bridge = a.Kind is SegmentKind.Overpass or SegmentKind.RampUp or SegmentKind.RampDown && a.Origin.Y > 0.4f;
            if (!bridge) continue;

            foreach (var side in new[] { -1f, 1f })
            {
                var ao = a.Origin + a.Lateral * (side * (a.HalfWidth + 1.2f));
                var bo = b.Origin + b.Lateral * (side * (b.HalfWidth + 1.2f));
                if (side > 0) Quad(railings, ao, ao + a.Normal * 1.0f, bo, bo + b.Normal * 1.0f, a.Lateral * side, b.Lateral * side, grey, a.Distance, b.Distance);
                else Quad(railings, ao + a.Normal * 1.0f, ao, bo + b.Normal * 1.0f, bo, a.Lateral * side, b.Lateral * side, grey, a.Distance, b.Distance);
                anyRail = true;
            }

            if (a.Distance - lastPillar >= 14f && a.Origin.Y > 1.0f)
            {
                lastPillar = a.Distance;
                foreach (var side in new[] { -1f, 1f })
                {
                    var foot = a.Origin + a.Lateral * (side * (a.HalfWidth - 1.2f));
                    var ground = terrain.Height(foot.X, foot.Z) - 0.3f;
                    var height = foot.Y - ground;
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

    /// <summary>Two triangles between consecutive cross-sections (al, ar) and (bl, br), with per-vertex normals.</summary>
    internal static void Quad(SurfaceTool st, SN.Vector3 al, SN.Vector3 ar, SN.Vector3 bl, SN.Vector3 br, SN.Vector3 na, SN.Vector3 nb, Color colour, float ua, float ub)
    {
        var gal = Convert.ToGodot(al);
        var gar = Convert.ToGodot(ar);
        var gbl = Convert.ToGodot(bl);
        var gbr = Convert.ToGodot(br);
        var gna = Convert.ToGodot(na);
        var gnb = Convert.ToGodot(nb);
        st.SetColor(colour);
        st.SetNormal(gna); st.SetUV(new Vector2(0f, ua)); st.AddVertex(gal);
        st.SetNormal(gnb); st.SetUV(new Vector2(0f, ub)); st.AddVertex(gbl);
        st.SetNormal(gna); st.SetUV(new Vector2(1f, ua)); st.AddVertex(gar);
        st.SetNormal(gna); st.SetUV(new Vector2(1f, ua)); st.AddVertex(gar);
        st.SetNormal(gnb); st.SetUV(new Vector2(0f, ub)); st.AddVertex(gbl);
        st.SetNormal(gnb); st.SetUV(new Vector2(1f, ub)); st.AddVertex(gbr);
    }
}
