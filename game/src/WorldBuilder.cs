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
    /// <summary>
    /// Metres the terrain mesh keeps below the road plane under a road, its shoulders and verges: deeper than the
    /// worn asphalt goes (a pothole of <see cref="RoadWear.MaxDepth"/> and a centimetre of roughness), or the grass
    /// shows through the wheel tracks. The shoulders slope down to meet the ground at their outer edge
    /// (<see cref="ShoulderDrop"/>).
    /// </summary>
    private const float RoadClearance = RoadWear.MaxDepth + 0.03f;
    /// <summary>How far the shoulders' outer edge sits below the road plane: a centimetre above the ground there.</summary>
    private const float ShoulderDrop = RoadClearance - 0.01f;
    private const float TerrainMargin = 2400f;
    /// <summary>
    /// White edge lines and marker posts along the roads: on, as on a rural road of today (0.13 took them off for the
    /// arcade's bare road; the player found the road less real without them, and 0.15 put them back).
    /// </summary>
    private static readonly bool RoadFurniture = true;
    /// <summary>Metres one tile of the grass's macro-variation noise spans (the terrain's second UV set; see <see cref="Grass"/>).</summary>
    private const float MacroTile = 90f;

    private static StandardMaterial3D? _grass;
    // The ground, the verge and the tufts as drawn since 0.36 (assets/shaders): the standard materials above stay for the
    // glTF export (ExportMode), which carries only those.
    private static ShaderMaterial? _groundShader;
    private static ShaderMaterial? _shoulderShader;
    /// <summary>--export-world: the world built with the standard materials the glTF export carries (0.36).</summary>
    public static bool ExportMode { get; set; }
    /// <summary>The ground and the verge drawn by the shaders of 0.36; not for the export.</summary>
    private static bool ShaderGround => !ExportMode;
    private static StandardMaterial3D? _asphalt;
    private static StandardMaterial3D? _concrete;
    private static StandardMaterial3D? _gravel;
    private static StandardMaterial3D? _earth;
    // The road furniture of today (edge lines, marker posts): hidden in 1989 mode, the cabinet's road had neither.
    private static readonly List<Node3D> _modernRoad = [];
    /// <summary>The old far ground and mountains, shown only in the 1989 look once the real land is there (0.37).</summary>
    private static readonly List<Node3D> _retroLand = [];
    private static Node3D? _land;
    private static ShaderMaterial? _landShader;
    private static StandardMaterial3D? _edgeLines;
    // 1989 mode's orange for the shoulders and the earthworks; otherwise the shoulders are grey-brown gravel and the
    // cuttings' earth a natural brown (0.9.1; before, the orange of the original was kept in both).
    private static readonly Color OrangeShoulder = new(0.95f, 0.46f, 0.12f);
    private static readonly Color OrangeEarth = new(0.86f, 0.50f, 0.27f);
    // The shoulders are compacted grey-brown gravel (0.15 again; 0.13's red earth, the arcade's, read as paint on the ground).
    private static readonly Color GravelTint = new(0.80f, 0.77f, 0.72f);
    /// <summary>
    /// The paint of the verge's stones (0.36): the arcade's burnt orange (its verges measure about 155, 85, 20 on the
    /// screen in the attract video; the owner found 0.93, 0.42, 0.08 pink in the sun), faded and chipped unevenly.
    /// </summary>
    private static readonly Color StonePaint = new(0.60f, 0.30f, 0.02f);
    private static readonly Color StoneFaded = new(0.60f, 0.40f, 0.16f);
    private static readonly Color SoilTint = new(0.72f, 0.58f, 0.46f);
    private static Godot.Environment? _environment;
    private static Godot.Environment? _mirrorEnvironment;
    private static DirectionalLight3D? _sun;
    // Ambient light: mostly the sky, the rest a warm tone for the light the sunlit ground throws back. It stands in
    // for SDFGI, which lit the scene the same way but cost a third of the GPU's frame (see SetQuality).
    private const float SkyAmbientShare = 0.8f;
    private static readonly Color GroundBounce = new(0.62f, 0.58f, 0.46f);
    // 0.15: back to the fill of 0.10. 0.11 took it down to 0.95 with the sun up at 1.75 for a harder contrast, and
    // the track read less like a real place than before; the player said so, and the light of 0.10 is restored.
    private const float AmbientEnergy = 1.15f;
    private static StandardMaterial3D? _steel;
    private static MeshInstance3D? _clouds;
    private static Node3D? _forest;
    /// <summary>The reflection probe that travels with the player's car (<see cref="BuildCarProbe"/>), and where and when it was last moved.</summary>
    private static ReflectionProbe? _carProbe;
    private static Vector3 _carProbeAt;
    private static float _carProbeMovedAt = -100f;
    private static GraphicsQuality _quality = GraphicsQuality.High;
    private static bool _retro;
    // The credit's weather (0.12): the sky, the light and the wet ground (ApplyWeather); 1989 mode keeps its flat day.
    private static Weather _weather = Weather.Dry;
    private static Material? _clearSky;              // the photo sky, kept while the overcast one shows
    private static ProceduralSkyMaterial? _overcastSky;
    private static NoiseTexture2D? _puddles;
    private static readonly Color DryAsphalt = new(0.52f, 0.52f, 0.53f);
    private static readonly Color WetAsphalt = new(0.15f, 0.15f, 0.16f);
    private static readonly Color DryGrass = new(0.56f, 0.66f, 0.39f);
    private static readonly Color WetGrass = new(0.40f, 0.50f, 0.28f);
    private static readonly Color DrySun = new(1f, 0.96f, 0.90f);
    private static readonly Color CloudSun = new(0.84f, 0.87f, 0.93f);
    private const float SunEnergy = 1.45f;
    /// <summary>The wind that drifts the clouds, metres a second along x and z (0.37: in tiles a second until then).</summary>
    private static readonly Vector2 CloudDrift = new(5f, 1.75f);
    private static Vector2 _cloudOffset;
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
        _carProbe = null;
        _clearSky = null;
        _overcastSky = null;
        _puddles = null;
        _grass = null;
        _groundShader = null;
        _shoulderShader = null;
        _asphalt = null;
        _concrete = null;
        _gravel = null;
        _earth = null;
        _edgeLines = null;
        _modernRoad.Clear();
        _retroLand.Clear();
        _land = null;
        _landShader = null;
        _environment = null;
        _mirrorEnvironment = null;
        _sun = null;
        _steel = null;
        _clouds = null;
        _cloudOffset = Vector2.Zero;
        _forest = null;
        _textured.Clear();
        _propMaterials.Clear();
        FoliageBuilder.ReleaseShared();
        PropBuilder.ReleaseShared();
    }

    /// <summary>--perf-log: each stage of <see cref="Build"/> timed to the log (U3).</summary>
    public static bool PerfLog { get; set; }

    private static System.Diagnostics.Stopwatch? _stageWatch;

    private static void Stage(string name)
    {
        if (!PerfLog) return;
        if (_stageWatch is not null) GD.Print($"start:   world {name}: {_stageWatch.Elapsed.TotalMilliseconds:0} ms");
        _stageWatch = System.Diagnostics.Stopwatch.StartNew();
    }

    public static void Build(Node3D root, TrackGeometry track)
    {
        _stageWatch = null;
        Stage("");
        root.AddChild(BuildEnvironment());
        root.AddChild(BuildSun());
        var farGround = BuildFarGround(track);
        var mountains = BuildMountains(track);
        root.AddChild(farGround);
        root.AddChild(mountains);
        if (BuildClouds(track) is { } clouds) root.AddChild(clouds);
        Stage("sky, sun, far ground, mountains, clouds");
        // The real land (RealLand) in place of the far ground and the old mountains, which stay for the 1989 look.
        _land = BuildLand(track);
        if (_land is not null)
        {
            root.AddChild(_land);
            _retroLand.Add(farGround);
            _retroLand.Add(mountains);
            ShowLand();
        }
        Stage("land");
        root.AddChild(BuildTerrain(track));
        Stage("terrain");
        foreach (var path in track.Paths)
        {
            root.AddChild(BuildRoad(track, path));
            root.AddChild(BuildShoulders(track, path));
            root.AddChild(BuildCentreLine(track, path));
            if (RoadFurniture)
            {
                root.AddChild(BuildEdgeLines(track, path));
                if (BuildMarkerPosts(track, path) is { } posts) root.AddChild(posts);
            }
            foreach (var node in BuildBridges(path)) root.AddChild(node);
            if (BuildLoopFrame(path, track.Terrain) is { } frame) root.AddChild(frame);
        }
        Stage("roads, shoulders, lines, posts, bridges, the loop");
        if (BuildTowers(track) is { } towers) root.AddChild(towers);
        foreach (var pad in track.Pads) root.AddChild(BuildPad(pad));
        root.AddChild(FoliageBuilder.BuildGrass(track));
        Stage("towers, pads, grass");
        var (minX, maxX, minZ, maxZ) = RoadBounds(track);
        // The wood fills the terrain out to its margin; beyond that lies the flat far ground.
        _forest = FoliageBuilder.BuildForest(track, minX - TerrainMargin + 40f, maxX + TerrainMargin - 40f, minZ - TerrainMargin + 40f, maxZ + TerrainMargin - 40f);
        root.AddChild(_forest);
        Stage("forest");
        ApplyQuality();
        Stage("quality");
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
        if (_clouds?.Mesh is PlaneMesh { Material: ShaderMaterial material })
        {
            _cloudOffset += CloudDrift * dt;
            material.SetShaderParameter("drift", _cloudOffset);
        }
    }

    /// <summary>
    /// A reflection probe for the player's car (0.11): the road, the verges and the horizon mirrored in the paint, the
    /// glass and the chrome, where the sky alone made the car look like a toy set down on a photograph. It renders
    /// once each time it is moved (<see cref="FollowCar"/>), six small faces every half second rather than six a
    /// frame. The player's car is on visual layer 2 as well as 1 (CarView.MarkPlayer) and is masked out of the
    /// capture: the probe stands at the car's roof, and the car itself would fill the cubemap. Off on Low and in
    /// 1989 mode (ApplyQuality); the car then reflects the sky alone, as before.
    /// </summary>
    public static ReflectionProbe BuildCarProbe()
    {
        _carProbe = new ReflectionProbe
        {
            Name = "CarReflections",
            UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
            Size = new Vector3(90f, 50f, 90f),
            CullMask = 0xFFFFFu & ~2u,
            // The probe lights the player's car alone (its meshes are on layer 2, CarView.MarkPlayer), 0.36. Until then it
            // lit everything in its 90 m box: the road within 45 m of the car reflected the probe's dark capture of the
            // ground and the trees, the road beyond it the bright sky, so the far road was pale and darkened as the box
            // reached it, in a step every 12 m as the probe moved (the owner's "the far road looks light and darkens as
            // you approach").
            ReflectionMask = ProbeAffectsAll ? 0xFFFFFu : 2u,
            EnableShadows = false,
            Intensity = 1f,
        };
        ApplyQuality();
        return _carProbe;
    }

    /// <summary>Moves the car's reflection probe along with the car every half second or twelve metres; each move renders it again.</summary>
    public static void FollowCar(Vector3 carPosition, float time)
    {
        if (_carProbe is null || !_carProbe.Visible) return;
        if (time - _carProbeMovedAt < 0.5f && carPosition.DistanceTo(_carProbeAt) < 12f) return;
        _carProbeAt = carPosition;
        _carProbeMovedAt = time;
        _carProbe.GlobalPosition = carPosition + new Vector3(0f, 1.3f, 0f);
    }

    /// <summary>
    /// The cost of the picture: what the environment does on top of the geometry. High is everything (indirect
    /// light, reflections, haze); Medium drops the screen-space indirect light and reflections; Low keeps only the
    /// sun and the fog and thins the wood. The caller sets the viewport's part (resolution, anti-aliasing). 1989 mode
    /// overrides all of it while it is on.
    /// </summary>
    /// <remarks>
    /// SDFGI (signed-distance-field global illumination) was part of High until 0.8. Measured on the development
    /// laptop it was a third of the main view's GPU time (12.9 ms a frame with it, 9.2 ms without) and doubled the
    /// worst frames (38 ms against 18): its cascades re-voxelise the world as the camera moves, which a car at
    /// 35 m/s does all the time. Every other effect measured under half a millisecond. The game now draws its
    /// ambient light from the sky plus a warm ground tone instead (SkyAmbientShare, GroundBounce).
    /// </remarks>
    public static void SetQuality(GraphicsQuality quality)
    {
        _quality = quality;
        ApplyQuality();
    }

    /// <summary>Automation (--ssao=, --sun-shadow=): one effect forced on or off whatever the quality says, to see what it does to a picture (0.34).</summary>
    public static bool? SsaoOverride { get; set; }
    public static bool? SunShadowOverride { get; set; }
    /// <summary>Automation (0.36): --ssr=, --volfog=, --fog=, --probe= force one more effect on or off; --probe-all=1 lets the car's probe light everything in its box again, as it did until 0.36.</summary>
    public static bool? SsrOverride { get; set; }
    public static bool? VolumetricFogOverride { get; set; }
    public static bool? FogOverride { get; set; }
    public static bool? ProbeOverride { get; set; }
    /// <summary>--sdfgi=1: signed-distance-field global illumination on (0.36 experiment; off since 0.8 for its cost).</summary>
    public static bool? SdfgiOverride { get; set; }
    public static bool ProbeAffectsAll { get; set; }

    private static void ApplyQuality()
    {
        var retro = _retro;
        var high = !retro && _quality == GraphicsQuality.High;
        var medium = !retro && _quality >= GraphicsQuality.Medium;
        if (_sun is not null) _sun.ShadowEnabled = SunShadowOverride ?? medium;
        if (_clouds is not null) _clouds.Visible = !retro;
        if (_carProbe is not null) _carProbe.Visible = ProbeOverride ?? medium;
        if (_forest is not null)
        {
            foreach (var child in _forest.GetChildren())
            {
                // The near band stays at every quality and in the 1989 look (the card trees' "Forest-near", the scanned "Wood-near").
                if (child is MultiMeshInstance3D band) band.Visible = medium || band.Name.ToString().StartsWith("Forest-near", StringComparison.Ordinal) || band.Name.ToString().StartsWith("Wood-near", StringComparison.Ordinal);
            }
        }
        if (_environment is null) return;
        _environment.SsaoEnabled = SsaoOverride ?? medium;
        _environment.SsilEnabled = high;
        _environment.SsrEnabled = SsrOverride ?? high;
        _environment.GlowEnabled = medium;
        _environment.VolumetricFogEnabled = VolumetricFogOverride ?? medium;
        if (FogOverride is { } fog) _environment.FogEnabled = fog;
        if (SdfgiOverride is { } sdfgi)
        {
            _environment.SdfgiEnabled = sdfgi;
            _environment.SdfgiUseOcclusion = true;
            _environment.SdfgiReadSkyLight = true;
            _environment.SdfgiCascades = 4;
            _environment.SdfgiMinCellSize = 0.25f;
            _environment.SdfgiBounceFeedback = 0.5f;
            _environment.SdfgiEnergy = 1f;
        }
        ApplyWeather();
    }

    /// <summary>The weather of the credit: sets the sky, the light and the wet ground (<see cref="ApplyWeather"/>).</summary>
    public static void SetWeather(Weather weather)
    {
        _weather = weather;
        ApplyWeather();
    }

    /// <summary>
    /// The look of the weather (0.12). Under any weather but a clear day the photo sky gives way to a grey procedural
    /// one, the sun drops to a wide, soft source behind the cloud, the ambient light comes from the sky alone, the
    /// air thickens and a camera opens up a little for the dim day. A wet road is dark and mirror-like with puddles
    /// (a steep noise as the roughness map), the gravel and the grass darken and take a sheen, the paint of the lines
    /// shines. 1989 mode keeps its flat day whatever the weather; the grip is the session's either way.
    /// </summary>
    private static void ApplyWeather()
    {
        var wet = _retro ? 0f : WeatherModel.Wetness(_weather);
        if (_asphalt is not null)
        {
            _asphalt.AlbedoColor = DryAsphalt.Lerp(WetAsphalt, wet);
            _asphalt.Roughness = Mathf.Lerp(0.9f, 0.30f, wet);
            _asphalt.RoughnessTexture = wet > 0f ? Puddles : null;
        }
        if (_gravel is not null && !_retro)
        {
            _gravel.AlbedoColor = GravelTint * Mathf.Lerp(1f, 0.62f, wet);
            _gravel.Roughness = Mathf.Lerp(1f, 0.6f, wet);
        }
        if (_grass is not null)
        {
            _grass.AlbedoColor = DryGrass.Lerp(WetGrass, wet);
            _grass.Roughness = Mathf.Lerp(1f, 0.8f, wet);
        }
        _groundShader?.SetShaderParameter("tint", DryGrass.Lerp(WetGrass, wet));
        _groundShader?.SetShaderParameter("wetness", wet);
        _landShader?.SetShaderParameter("wetness", wet);
        _shoulderShader?.SetShaderParameter("wetness", wet);
        FoliageBuilder.SetWeather(DryGrass.Lerp(WetGrass, wet), wet);
        if (_edgeLines is not null) _edgeLines.Roughness = Mathf.Lerp(0.65f, 0.25f, wet);
        if (_environment is null || _sun is null || _retro) return;
        var overcast = WeatherModel.Overcast(_weather);
        if (_environment.Sky is { } sky)
        {
            _clearSky ??= sky.SkyMaterial;
            sky.SkyMaterial = overcast ? OvercastSky : _clearSky;
        }
        if (_clouds is not null) _clouds.Visible = !overcast;   // the grey sky is its own cloud
        _sun.LightEnergy = overcast ? 0.6f : SunEnergy;
        _sun.LightColor = overcast ? CloudSun : DrySun;
        // Behind cloud the sun is a wide source: shadows all but gone. In clear weather a point (0.36: at 0.5 degrees the
        // contact-hardening filter's noise crawled over the dashboard as the car moved, the owner's trembling cockpit shadows).
        _sun.LightAngularDistance = overcast ? 10f : 0f;
        foreach (var environment in new[] { _environment, _mirrorEnvironment })
        {
            if (environment is null) continue;
            environment.AmbientLightSkyContribution = overcast ? 0.95f : SkyAmbientShare;
            environment.AmbientLightEnergy = overcast ? 1.1f : AmbientEnergy;
            environment.FogLightColor = overcast ? new Color(0.70f, 0.72f, 0.76f) : ClearAir;
            environment.FogDensity = !overcast ? ClearAirDensity : WeatherModel.Raining(_weather) ? 0.00032f : 0.00016f;
            environment.FogAerialPerspective = overcast ? 0.8f : 0.55f;
            environment.VolumetricFogDensity = overcast ? 0.00035f : 0.00012f;
            environment.TonemapExposure = overcast ? 1.15f : 0.9f;
        }
    }

    /// <summary>A grey, sunless sky for the overcast days: flat light from everywhere, as under a real overcast.</summary>
    private static ProceduralSkyMaterial OvercastSky => _overcastSky ??= new ProceduralSkyMaterial
    {
        SkyTopColor = new Color(0.34f, 0.37f, 0.42f),
        SkyHorizonColor = new Color(0.60f, 0.62f, 0.66f),
        SkyCurve = 0.12f,
        GroundBottomColor = new Color(0.18f, 0.19f, 0.18f),
        GroundHorizonColor = new Color(0.52f, 0.54f, 0.55f),
        SunAngleMax = 0f,
        UseDebanding = true,
    };

    /// <summary>
    /// The wet road's roughness map: a steep noise that leaves most of the asphalt at the material's roughness and
    /// drops the puddles (a fifth of the surface, with soft rims) to a mirror, mapped on the road's 5.5 m tile.
    /// </summary>
    private static NoiseTexture2D Puddles => _puddles ??= Noise(0.9f, 2, new[] { (0f, new Color(0.12f, 0.12f, 0.12f)), (0.30f, new Color(0.12f, 0.12f, 0.12f)), (0.48f, new Color(0.75f, 0.75f, 0.75f)), (1f, new Color(1f, 1f, 1f)) }, seed: 101);

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
        if (_grass is not null) _grass.DetailEnabled = !retro && _grass.DetailAlbedo is not null;
        // The shoulders and the earthworks take the cabinet's orange back with the textures off; the edge lines and the
        // marker posts go.
        if (_gravel is not null) _gravel.AlbedoColor = retro ? OrangeShoulder : GravelTint;
        _groundShader?.SetShaderParameter("retro", retro);
        FoliageBuilder.SetRetro(retro);
        _shoulderShader?.SetShaderParameter("retro", retro);
        if (_earth is not null) _earth.AlbedoColor = retro ? OrangeEarth : SoilTint;
        foreach (var node in _modernRoad)
        {
            if (GodotObject.IsInstanceValid(node)) node.Visible = !retro;
        }
        ShowLand();
        ApplyQuality();
        if (_environment is null) return;
        RetroLook(_environment, retro);
        if (_mirrorEnvironment is not null) RetroLook(_mirrorEnvironment, retro);
        ApplyWeather();
    }

    /// <summary>The part of the 1989 look the rear-view mirror shares with the main view: flat sky, flat ambient, no fog, linear tones.</summary>
    private static void RetroLook(Godot.Environment environment, bool retro)
    {
        environment.FogEnabled = !retro;
        environment.BackgroundMode = retro ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
        environment.BackgroundColor = new Color(0.30f, 0.52f, 0.86f);
        environment.AmbientLightSource = retro ? Godot.Environment.AmbientSource.Color : Godot.Environment.AmbientSource.Sky;
        environment.AmbientLightColor = retro ? new Color(0.55f, 0.6f, 0.7f) : GroundBounce;
        environment.AmbientLightEnergy = retro ? 0.9f : AmbientEnergy;
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
        // A deeper green than the photo's own: the pale tint read as hay, and the plain of the 1989 game is a saturated
        // green; a touch less saturated and a little brighter since 0.11, when the macro variation below began darkening it.
        AlbedoColor = new Color(0.56f, 0.66f, 0.39f),
        Roughness = 1f,
        // Grass barely reflects and is matte all over (no roughness map): with the photo's roughness map at the
        // default specular, its smoother specks glinted on every slope facing the sun, a patch of white frost on
        // the rims of the cuttings.
        MetallicSpecular = 0.2f,
        Uv1Scale = new Vector3(1150f, 1150f, 1f),    // the terrain's UV spans 5000 m: a 4.3 m tile
        VertexColorUseAsAlbedo = true,
        // Macro variation (0.11): a low-frequency noise multiplied in on the second UV set, which spans MacroTile metres,
        // so the 4.3 m tile no longer reads as a repeat across the plain. SetRetro switches it off with the textures.
        DetailEnabled = true,
        DetailAlbedo = Noise(0.6f, 3, new[] { (0f, new Color(0.78f, 0.72f, 0.56f)), (0.45f, new Color(0.93f, 0.94f, 0.90f)), (1f, new Color(1f, 1f, 0.97f)) }, seed: 91),   // the low end is dry straw: a field is never one green
        DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul,
        DetailUVLayer = BaseMaterial3D.DetailUV.UV2,
    }, Photo("grass_color") ?? Noise(0.9f, 5, new[] { (0f, new Color(0.62f, 0.72f, 0.55f)), (0.5f, new Color(0.95f, 1.0f, 0.9f)), (1f, new Color(1.25f, 1.2f, 1.1f)) }, seed: 3),
       Photo("grass_normal") ?? Noise(2.5f, 4, null, seed: 11, asNormal: true), normalScale: 0.5f);   // 0.11: at 0.7 the blades glinted frost-white at a distance

    private static StandardMaterial3D Asphalt
    {
        get
        {
            if (_asphalt is not null) return _asphalt;
            _asphalt = Textured(new StandardMaterial3D
            {
                // The grey photo asphalt of 0.10 (0.46; 0.11-0.14 darkened it to 0.33, and the road read as fresh
                // black tarmac, less real than before). The slab shade, the wheel tracks, the patches and the potholes
                // come from the vertex colour multiplied in. Rough all over, without the photo's roughness map: that
                // map averages 0.52, and under the screen-space reflections a dry road mirrored the car and the sky
                // as if it had just rained (0.11). A dry road only shines at a grazing angle towards a low sun.
                AlbedoColor = new Color(0.52f, 0.52f, 0.53f),
                VertexColorUseAsAlbedo = true,
                // Matter and less specular since 0.36 (0.8 and the default 0.5): a dry road shines only towards a low sun.
                Roughness = 0.9f,
                MetallicSpecular = 0.3f,
                Uv1Scale = new Vector3(2f, 0.18f, 1f),      // u spans the 11 m width, v is metres along: a 5.5 m tile
                // Both faces: on the bridges, the ramps and the loop this ribbon is the whole deck, seen from below too.
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }, Photo("asphalt_color") ?? Noise(9f, 4, new[] { (0f, new Color(0.82f, 0.82f, 0.82f)), (0.5f, new Color(0.98f, 0.98f, 0.98f)), (1f, new Color(1.15f, 1.15f, 1.15f)) }, seed: 5),
               Photo("asphalt_normal") ?? Noise(16f, 4, null, seed: 17, asNormal: true), normalScale: 0.5f);
            // The wear sheet (tools/make-road-wear.py): tar snakes, sealed cracks and stains multiplied in on the
            // second UV set, which runs 160 m along the road (40 until 0.13) and drifts across it so the sheet's repeat
            // does not read.
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

    /// <summary>
    /// Bare earth of the earthworks: the fork hill and its flanks, the walls of the cuttings. It uses the terrain's own
    /// UVs (a 4.3 m tile) and its vertex colour for the shading, like the grass it replaces there.
    /// </summary>
    private static StandardMaterial3D Earth => _earth ??= Textured(new StandardMaterial3D
    {
        // Brown soil from the dirt photo; 1989 mode turns it to the red-brown and orange of the original (SetRetro).
        AlbedoColor = _retro ? OrangeEarth : SoilTint,
        Roughness = 1f,
        Uv1Scale = new Vector3(1150f, 1150f, 1f),
        VertexColorUseAsAlbedo = true,
    }, Photo("dirt_color") ?? Noise(4f, 3, new[] { (0f, new Color(0.7f, 0.65f, 0.55f)), (1f, new Color(1f, 0.95f, 0.85f)) }, seed: 43),
       Photo("dirt_normal"), 0.6f, Photo("dirt_roughness"));

    /// <summary>
    /// Shoulders: compacted grey-brown gravel along the road (u across, v in metres along); the vertex colour only
    /// varies the shade every couple of metres. 1989 mode shows them in the cabinet's flat orange (SetRetro).
    /// </summary>
    private static StandardMaterial3D Gravel => _gravel ??= Textured(new StandardMaterial3D
    {
        AlbedoColor = _retro ? OrangeShoulder : GravelTint,
        VertexColorUseAsAlbedo = true,
        Roughness = 1f,   // compacted gravel is matte; its photo roughness map (mean 0.54) gave it a damp sheen (0.11)
        Uv1Scale = new Vector3(0.9f, 0.45f, 1f),
        // Both faces: the shoulders run along the bridge decks too, seen from below.
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    }, Photo("shoulder_color") ?? Photo("dirt_color") ?? Noise(4f, 3, new[] { (0f, new Color(0.7f, 0.65f, 0.55f)), (1f, new Color(1f, 0.95f, 0.85f)) }, seed: 41),
       Photo("shoulder_normal") ?? Photo("dirt_normal"), 0.8f);

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

    /// <summary>The keys of the prop materials made so far, for <see cref="ReleaseUnusedPropMaterials"/>.</summary>
    public static HashSet<string> PropMaterialKeys() => new(_propMaterials.Keys);

    /// <summary>
    /// Lets go of the prop materials made since <paramref name="before"/> was taken that nothing draws any more, so their
    /// photo textures go with them at the next collection. Returns how many went.
    /// </summary>
    /// <remarks>
    /// The showroom (cars13 review): the first lofted car it shows makes the cabin's leather, fabric and carpet
    /// materials, and the cache kept them, and their textures, for the rest of the session (33 MB of video memory and 7
    /// resources over the level before it, after the showroom closed). Called once its cars have been freed and collected:
    /// a material only this cache still holds has one reference, its .NET wrapper's; one a car in the scene still draws
    /// (the player's lofted car built while the showroom was open) has more and is kept, and so is every material made
    /// before the showroom opened, which the world's props and the player's car draw. The retro switch's list of
    /// textured materials lets go of them too.
    /// </remarks>
    public static int ReleaseUnusedPropMaterials(IReadOnlySet<string> before)
    {
        var released = 0;
        foreach (var (key, material) in _propMaterials.ToList())
        {
            if (before.Contains(key) || material.GetReferenceCount() > 1) continue;
            _propMaterials.Remove(key);
            _textured.RemoveAll(t => t.Material == material);
            released++;
        }
        return released;
    }

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
            AmbientLightSkyContribution = SkyAmbientShare,
            AmbientLightColor = GroundBounce,
            AmbientLightEnergy = AmbientEnergy,
            // AgX keeps the saturated paint and the sky from clipping the way ACES does.
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 0.9f,
            TonemapWhite = 6f,
            SsaoEnabled = true,
            // A metre (1.5 until 0.36): inside the cabin a radius of a metre and a half darkened broad smudges that
            // slid about as the view moved.
            SsaoRadius = 1.0f,
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
            FogLightColor = ClearAir,
            FogDensity = ClearAirDensity,
            FogAerialPerspective = 0.55f,
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
            LightEnergy = 1.75f,
            LightColor = new Color(1f, 0.96f, 0.90f),
            // Steady shadows in the cabin (0.36, the owner's "the cockpit's shadows tremble"): a point sun (0.5 degrees used
            // the contact-hardening filter, whose noise crawled over the dashboard as the car moved), a bias the dashboard's
            // grazing light needs (0.02 and 1.0 put a speckle of self-shadow on it), and the nearest cascade 10 m deep
            // instead of 25 for finer texels in the cabin; shadows end at 350 m (500).
            LightAngularDistance = 0f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 350f,
            DirectionalShadowSplit1 = 0.03f,
            DirectionalShadowSplit2 = 0.12f,
            DirectionalShadowSplit3 = 0.4f,
            ShadowBias = 0.05f,
            ShadowNormalBias = 1.5f,
            ShadowBlur = 1.3f,
            DirectionalShadowBlendSplits = true,
        };
        // Shine from where the HDRI's sun is, so shadows and the sky agree.
        _sun.LookAtFromPosition(Vector3.Zero, -SunDirection, Vector3.Up);
        return _sun;
    }

    /// <summary>
    /// Flat ground to the horizon round the terrain mesh, just under the level the ground field flattens out to at its
    /// edge; it reaches past the mountains.
    /// </summary>
    /// <remarks>
    /// A frame with the terrain mesh's ground (<see cref="TerrainBounds"/>) cut out of it, overlapping the mesh's edge a
    /// little so the two meet without a crack. Until 0.8 it was a whole 16 km plane at this height under the terrain,
    /// harmless while no ground went more than a few centimetres below it. The 1989 course's two cuttings go 4.5 m
    /// down: the plane lay across them and their roads, and a car drove under a sheet of grass at the DIP sign.
    /// </remarks>
    private static MeshInstance3D BuildFarGround(TrackGeometry track)
    {
        const float half = 8000f;
        const float overlap = 20f;
        const int split = 6;
        var (minX, maxX, minZ, maxZ) = TerrainBounds(track);
        var st = new MeshBuilder();
        var vertices = 0;
        void Rectangle(float x0, float x1, float z0, float z1)
        {
            var first = vertices;
            for (var j = 0; j <= split; j++)
            {
                for (var i = 0; i <= split; i++)
                {
                    var x = MathUtil.Lerp(x0, x1, i / (float)split);
                    var z = MathUtil.Lerp(z0, z1, j / (float)split);
                    st.SetColor(Colors.White);
                    st.SetNormal(Vector3.Up);
                    st.SetTangent(Vector3.Right, 1f);
                    // The terrain mesh's UVs, so the grass keeps its scale across the seam.
                    st.SetUV(new Vector2(x / 5000f + 0.5f, z / 5000f + 0.5f));
                    st.AddVertex(Convert.ToGodot(new SN.Vector3(x, 0f, z)));
                    vertices++;
                }
            }
            // Wound as the terrain's cells (see BuildTerrain), front faces up.
            for (var j = 0; j < split; j++)
            {
                for (var i = 0; i < split; i++)
                {
                    var a = first + j * (split + 1) + i;
                    var c = a + split + 1;
                    st.AddIndex(a); st.AddIndex(c); st.AddIndex(a + 1);
                    st.AddIndex(a + 1); st.AddIndex(c); st.AddIndex(c + 1);
                }
            }
        }
        var (holeX0, holeX1, holeZ0, holeZ1) = (minX + overlap, maxX - overlap, minZ + overlap, maxZ - overlap);
        Rectangle(-half, half, -half, holeZ0);
        Rectangle(-half, half, holeZ1, half);
        Rectangle(-half, holeX0, holeZ0, holeZ1);
        Rectangle(holeX1, half, holeZ0, holeZ1);
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = GroundMaterial, Position = new Vector3(0f, -0.06f, 0f), Name = "FarGround" };
    }

    /// <summary>The ground the terrain mesh covers (simulation x and z): every road's extent and <see cref="TerrainMargin"/> round it.</summary>
    private static (float MinX, float MaxX, float MinZ, float MaxZ) TerrainBounds(TrackGeometry track)
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
        return (minX - TerrainMargin, maxX + TerrainMargin, minZ - TerrainMargin, maxZ + TerrainMargin);
    }

    /// <summary>
    /// Fair-weather clouds: one wide sheet of the cloud texture (tools/make-clouds.py) high over the circuit, seen
    /// from below, unlit so that it stays as white as the sky's own clouds, drifting slowly (<see cref="Tick"/>).
    /// </summary>
    /// <summary>The cloud layer's side and height (0.37: 14 km at 1.5 km until the real land's peaks rose through it).</summary>
    private const float CloudSize = 60000f;
    private const float CloudHeight = 2800f;

    private static MeshInstance3D? BuildClouds(TrackGeometry track)
    {
        if (!ResourceLoader.Exists("res://assets/textures/clouds.png")) return null;
        // The sheet at two scales, thinned into clear and cloudy stretches, faded out from 9 to 28 km so the layer's
        // edge is never seen (assets/shaders/clouds.gdshader).
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/clouds.gdshader") };
        material.SetShaderParameter("clouds_tex", GD.Load<Texture2D>("res://assets/textures/clouds.png"));
        material.SetShaderParameter("tile", 5200f);
        material.SetShaderParameter("drift", _cloudOffset);
        var (minX, maxX, minZ, maxZ) = RoadBounds(track);
        _clouds = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(CloudSize, CloudSize), Material = material },
            Position = Convert.ToGodot(new SN.Vector3((minX + maxX) * 0.5f, CloudHeight, (minZ + maxZ) * 0.5f)),
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
        // and the saddles rounded, continuous round the horizon. Its crest also wanders in and out by 500 m. A low distant
        // chain: 0.13 raised it to the arcade's 360-800 m at 3.9 km, and it stood over the plain like a fence of teeth (0.15).
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
        // Darker and greyer than a field's green (0.12, 0.21, 0.10 until 0.36: under the haze it turned mint).
        var forest = new Color(0.09f, 0.14f, 0.11f);
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
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Back };
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
        var (minX, maxX, minZ, maxZ) = TerrainBounds(track);
        var nx = (int)MathF.Ceiling((maxX - minX) / TerrainCell) + 1;
        var nz = (int)MathF.Ceiling((maxZ - minZ) / TerrainCell) + 1;
        var heights = new float[nx * nz];
        var roadWeights = new float[nx * nz];
        // The ground field only reads the finished track, so its rows can be sampled side by side.
        Parallel.For(0, nz, j =>
        {
            for (var i = 0; i < nx; i++) heights[j * nx + i] = terrain.Height(minX + i * TerrainCell, minZ + j * TerrainCell, out roadWeights[j * nx + i]);
        });
        Stage("  terrain: the coarse grid sampled");

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
        // Sized for the circuit (a quarter of a million points, half a million triangles), and the coarse corners, a
        // regular grid and 178,000 of the points, indexed by an array: the dictionary holds only the fine points of
        // the refined cells (U3, 0.31.0: one dictionary for every point, grown by doubling, took 150 ms).
        var lookup = new Dictionary<long, int>(1 << 17);
        var coarseIndex = new int[nx * nz];
        Array.Fill(coarseIndex, -1);
        var points = new List<(int I, int J)>(1 << 18);
        var indices = new List<int>(1 << 21);
        bool Coarse(int pi, int pj) => pi % split == 0 && pj % split == 0;
        int Point(int pi, int pj)
        {
            if (Coarse(pi, pj))
            {
                ref var slot = ref coarseIndex[(pj / split) * nx + pi / split];
                if (slot < 0)
                {
                    slot = points.Count;
                    points.Add((pi, pj));
                }
                return slot;
            }
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
            // Godot's front faces wind clockwise seen from the front. Seen from above with north up, a, b (east of a)
            // and c (north of a) run counter-clockwise in the view as in the simulation: the Z flip relabels the
            // axis, it does not mirror the ground. Wound a, b, c (as until 0.8) every triangle faced down and was
            // culled from above; the flat ground plane under it hid that, and the far slopes of the hills showed
            // their undersides.
            indices.Add(a); indices.Add(c); indices.Add(b);
            indices.Add(b); indices.Add(c); indices.Add(d);
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

        Stage("  terrain: the cells and their points");
        // Heights and normals: the coarse corners keep their grid values; the finer points sample the field, except
        // on an edge shared with a coarse cell, where they lie on that edge's straight line so the two cells meet
        // without a crack.
        var count = points.Count;
        var vertexHeight = new float[count];
        var vertexWeight = new float[count];
        var vertexNormal = new SN.Vector3[count];
        // The field's raw height at every fine point, sampled once (U3, 0.31.0): a fine point's normal is the central
        // difference over its four lattice neighbours, which are mostly fine points themselves (or coarse corners,
        // already in the grid); until 0.30 each point sampled the field five times, 380,000 calls for 76,000 points.
        // The neighbours outside every refined cell (the ring round a refined patch) are sampled into a side table.
        var raw = new float[count];
        Parallel.For(0, count, v =>
        {
            var (pi, pj) = points[v];
            if (Coarse(pi, pj)) return;
            raw[v] = terrain.Height(minX + pi * step, minZ + pj * step, out vertexWeight[v]);
        });
        var ring = new Dictionary<long, float>();
        var ringKeys = new List<long>();
        for (var v = 0; v < count; v++)
        {
            var (pi, pj) = points[v];
            if (Coarse(pi, pj)) continue;
            Ring(pi + 1, pj); Ring(pi - 1, pj); Ring(pi, pj + 1); Ring(pi, pj - 1);
        }
        void Ring(int ni, int nj)
        {
            if (Coarse(ni, nj)) return;
            var key = ((long)ni << 32) | (uint)nj;
            if (lookup.ContainsKey(key) || ring.ContainsKey(key)) return;
            ring[key] = 0f;
            ringKeys.Add(key);
        }
        var ringHeights = new float[ringKeys.Count];
        Parallel.For(0, ringKeys.Count, r =>
        {
            var ni = (int)(ringKeys[r] >> 32);
            var nj = (int)(uint)ringKeys[r];
            ringHeights[r] = terrain.Height(minX + ni * step, minZ + nj * step);
        });
        for (var r = 0; r < ringKeys.Count; r++) ring[ringKeys[r]] = ringHeights[r];
        float RawAt(int pi, int pj)
        {
            if (Coarse(pi, pj)) return heights[(pj / split) * nx + pi / split];
            var key = ((long)pi << 32) | (uint)pj;
            return lookup.TryGetValue(key, out var v) ? raw[v] : ring[key];
        }
        Parallel.For(0, count, v =>
        {
            var (pi, pj) = points[v];
            if (Coarse(pi, pj))
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
            vertexHeight[v] = raw[v];
            var dx = RawAt(pi + 1, pj) - RawAt(pi - 1, pj);
            var dz = RawAt(pi, pj + 1) - RawAt(pi, pj - 1);
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

        Stage("  terrain: the vertices' heights and normals");
        var variation = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 140f, FractalOctaves = 2, Seed = 77 };
        var st = new MeshBuilder();
        var vertexEarth = new float[count];
        var vertexShade = new float[count];
        var vertexColour = new Color[count];
        // The ring of hills beyond the roads (TerrainField.Base) is wooded: from the circuit it reads as the dark band
        // of trees under the mountains, as in the 1989 game, without a tree being placed there.
        var roadMinX = minX + TerrainMargin; var roadMaxX = maxX - TerrainMargin;
        var roadMinZ = minZ + TerrainMargin; var roadMaxZ = maxZ - TerrainMargin;
        // The relief the roads cut into the plain: the height over the undisturbed rolling ground (0.13; until then the
        // plain was flat enough for the height itself to serve, now its own swells would read as earthworks). Read
        // side by side (U3, 0.31.0); the noise object and the builder are used from this thread only.
        var vertexRelief = new float[count];
        Parallel.For(0, count, v => vertexRelief[v] = vertexHeight[v] - terrain.Base(minX + points[v].I * step, minZ + points[v].J * step));
        for (var v = 0; v < count; v++)
        {
            var x = minX + points[v].I * step;
            var z = minZ + points[v].J * step;
            var h = vertexHeight[v];
            var normal = vertexNormal[v];
            var relief = vertexRelief[v];
            var shade = 0.80f + 0.20f * normal.Y - 0.012f * MathF.Max(-relief, 0f);
            vertexShade[v] = shade;
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
            // Earthworks: where the ground has been pulled off the plain to meet a road, inside the circuit's box. The
            // cuttings and embankments of a farm road are grassed over, so only a deep, steep cut shows bare earth
            // (BuildEarth) and a graded slope's grass is a little drier; from 0.13 every slope over 1.8 m was bare
            // brown earth, and on the rolling plain the roads ran between wedges of it that read as unreal (0.15).
            var steep = MathUtil.Clamp01((0.90f - normal.Y) / 0.12f);
            var earth = outside < 0f ? MathUtil.Clamp01((MathF.Abs(relief) - 4.5f) / 1.0f) * steep : 0f;
            vertexEarth[v] = earth;
            var brown = MathUtil.Clamp01((MathF.Abs(relief) - 1.5f) / 2.5f) * (outside < 0f ? 1f : 0f);
            r = MathUtil.Lerp(r, shade * 0.95f, brown * 0.30f);
            g = MathUtil.Lerp(g, shade * 0.86f, brown * 0.30f);
            b = MathUtil.Lerp(b, shade * 0.58f, brown * 0.30f);
            vertexColour[v] = new Color(r, g, b);
            st.SetColor(vertexColour[v]);
            var gNormal = Convert.ToGodot(normal);
            // u follows x on this grid: the tangent is +x laid onto the slope, the bitangent then runs along v.
            st.SetTangent((Vector3.Right - gNormal * gNormal.X).Normalized(), 1f);
            st.SetNormal(gNormal);
            st.SetUV(new Vector2(x / 5000f + 0.5f, z / 5000f + 0.5f));
            // The shader's ground (0.36) reads the bare earth and the road's weight here; the export's material its macro tile.
            st.SetUV2(ShaderGround ? new Vector2(earth, vertexWeight[v]) : new Vector2(x / MacroTile, z / MacroTile));
            st.AddVertex(Convert.ToGodot(new SN.Vector3(x, h, z)));
        }
        Stage("  terrain: the vertices' colours, into the builder");
        // Grass or bare earth, split along the earthworks' contour (vertexEarth = 0.5, ground about 2.1 m off the
        // plain): a triangle the contour crosses is cut where it crosses, one piece to each mesh. Sorted whole by its
        // corners, the triangles left a saw-tooth of 2 m spikes along every cutting and round the hill.
        var es = new MeshBuilder();
        var grassVertices = count;
        var earthOf = new Dictionary<int, int>();
        var crossings = new Dictionary<long, (int Grass, int Earth)>();
        var earthTriangles = 0;
        float EarthTone(int v) => vertexShade[v] * (1f + 0.08f * variation.GetNoise2D((minX + points[v].I * step) * 3f, (minZ + points[v].J * step) * 3f));
        void EarthAttributes(MeshBuilder builder, Vector3 gNormal, float tone, float x, float z)
        {
            builder.SetColor(new Color(tone, tone, tone));
            builder.SetTangent((Vector3.Right - gNormal * gNormal.X).Normalized(), 1f);
            builder.SetNormal(gNormal);
            builder.SetUV(new Vector2(x / 5000f + 0.5f, z / 5000f + 0.5f));
        }
        int EarthVertex(int v)
        {
            if (earthOf.TryGetValue(v, out var e)) return e;
            e = earthOf.Count + crossings.Count;
            var x = minX + points[v].I * step;
            var z = minZ + points[v].J * step;
            EarthAttributes(es, Convert.ToGodot(vertexNormal[v]), EarthTone(v), x, z);
            es.AddVertex(Convert.ToGodot(new SN.Vector3(x, vertexHeight[v], z)));
            earthOf[v] = e;
            return e;
        }
        // The point on edge u-w where the earth weight is one half, added to both meshes once per edge.
        (int Grass, int Earth) Crossing(int u, int w)
        {
            var key = u < w ? ((long)u << 32) | (uint)w : ((long)w << 32) | (uint)u;
            if (crossings.TryGetValue(key, out var known)) return known;
            var t = MathUtil.Clamp01((0.5f - vertexEarth[u]) / (vertexEarth[w] - vertexEarth[u]));
            var x = MathUtil.Lerp(minX + points[u].I * step, minX + points[w].I * step, t);
            var z = MathUtil.Lerp(minZ + points[u].J * step, minZ + points[w].J * step, t);
            var position = Convert.ToGodot(new SN.Vector3(x, MathUtil.Lerp(vertexHeight[u], vertexHeight[w], t), z));
            var gNormal = Convert.ToGodot(SN.Vector3.Normalize(SN.Vector3.Lerp(vertexNormal[u], vertexNormal[w], t)));
            st.SetColor(vertexColour[u].Lerp(vertexColour[w], t));
            st.SetTangent((Vector3.Right - gNormal * gNormal.X).Normalized(), 1f);
            st.SetNormal(gNormal);
            st.SetUV(new Vector2(x / 5000f + 0.5f, z / 5000f + 0.5f));
            st.SetUV2(new Vector2(x / MacroTile, z / MacroTile));
            st.AddVertex(position);
            EarthAttributes(es, gNormal, MathUtil.Lerp(EarthTone(u), EarthTone(w), t), x, z);
            es.AddVertex(position);
            var made = (grassVertices++, earthOf.Count + crossings.Count);
            crossings[key] = made;
            return made;
        }
        for (var t = 0; t < indices.Count; t += 3)
        {
            var (a, b, c) = (indices[t], indices[t + 1], indices[t + 2]);
            // The shader's ground draws the earth itself, blended (0.36): no second mesh, no hard contour.
            var earthCorners = ShaderGround ? 0 : (vertexEarth[a] >= 0.5f ? 1 : 0) + (vertexEarth[b] >= 0.5f ? 1 : 0) + (vertexEarth[c] >= 0.5f ? 1 : 0);
            if (earthCorners == 0)
            {
                st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                continue;
            }
            if (earthCorners == 3)
            {
                es.AddIndex(EarthVertex(a)); es.AddIndex(EarthVertex(b)); es.AddIndex(EarthVertex(c));
                earthTriangles++;
                continue;
            }
            // Turn the corners (keeping the winding) until the one alone on its side of the contour comes first.
            var loneIsEarth = earthCorners == 1;
            while ((vertexEarth[a] >= 0.5f) != loneIsEarth) (a, b, c) = (b, c, a);
            var ab = Crossing(a, b);
            var ac = Crossing(a, c);
            if (loneIsEarth)
            {
                es.AddIndex(EarthVertex(a)); es.AddIndex(ab.Earth); es.AddIndex(ac.Earth);
                st.AddIndex(ab.Grass); st.AddIndex(b); st.AddIndex(c);
                st.AddIndex(ab.Grass); st.AddIndex(c); st.AddIndex(ac.Grass);
                earthTriangles++;
            }
            else
            {
                st.AddIndex(a); st.AddIndex(ab.Grass); st.AddIndex(ac.Grass);
                es.AddIndex(ab.Earth); es.AddIndex(EarthVertex(b)); es.AddIndex(EarthVertex(c));
                es.AddIndex(ab.Earth); es.AddIndex(EarthVertex(c)); es.AddIndex(ac.Earth);
                earthTriangles += 2;
            }
        }
        GD.Print($"terrain: {count} vertices, {indices.Count / 3} triangles, {refined} of {cellsX * cellsZ} cells refined along the roads, {earthTriangles} of earthworks, {crossings.Count} contour points");
        Stage("  terrain: the triangles sorted by the contour");
        var ground = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = GroundMaterial, Name = "Terrain" };
        Stage("  terrain: the mesh committed");
        if (earthTriangles > 0) ground.AddChild(new MeshInstance3D { Mesh = es.Commit(), MaterialOverride = Earth, Name = "TerrainEarth" });
        return ground;
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

        // The rows first: a frame every half metre along each run, joined to the row before unless a gap or a run's
        // start parts them. Listed before they are built so the wear under every station (five field reads a station,
        // 136,000 stations round the circuit) is read side by side (U3, 0.31.0); the noise objects and the builder
        // are used from this thread only.
        var rows = new List<(RoadFrame Frame, bool Joins)>();
        var open = false;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm)
            {
                open = false;
                continue;
            }
            var steps = Math.Max(1, (int)MathF.Round((b.Distance - a.Distance) / RoadStep));
            for (var s = 0; s < steps; s++)
            {
                rows.Add((s == 0 ? a : RoadFrame.Lerp(a, b, (float)s / steps), open));
                open = true;
            }
            // The run's last frame closes it: at the end of the path or before a gap.
            if (i == frames.Count - 2 || frames[i + 2].Surface == SurfaceType.Chasm)
            {
                rows.Add((b, true));
                open = false;
            }
        }
        const int perRow = RoadStations + 1;
        var wear = new (float Depth, float Along, float Across, float Patch)[rows.Count * perRow];
        Parallel.For(0, rows.Count, r =>
        {
            var f = rows[r].Frame;
            for (var k = 0; k <= RoadStations; k++)
            {
                var lateral = (-1f + 2f * k / RoadStations) * f.HalfWidth;
                wear[r * perRow + k] = (
                    RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth),
                    (RoadWear.Depth(path.Kind, f.Kind, f.Distance + 0.25f, lateral, f.HalfWidth) - RoadWear.Depth(path.Kind, f.Kind, f.Distance - 0.25f, lateral, f.HalfWidth)) / 0.5f,
                    (RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral + 0.1f, f.HalfWidth) - RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral - 0.1f, f.HalfWidth)) / 0.2f,
                    RoadWear.PatchTone(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth));
            }
        });

        int Row(in RoadFrame f, int r)
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
                var (depth, alongSlope, acrossSlope, patch) = wear[r * perRow + k];
                var normal = SN.Vector3.Normalize(f.Normal + f.Tangent * alongSlope + f.Lateral * acrossSlope);
                var position = f.Origin + f.Lateral * lateral - f.Normal * (depth + (shared ? sink : 0f));
                var tone = slabShade * TrackShade(fraction)
                    * patch
                    * MathF.Max(1f - 9f * MathF.Max(depth - 0.004f, 0f), 0.6f)
                    * (1f + 0.05f * grain.GetNoise2D(f.Distance * 3.7f, lateral * 2.1f));
                var colour = tint * tone;
                colour.A = 1f;
                st.SetColor(colour);
                st.SetNormal(Convert.ToGodot(normal));
                st.SetTangent(gLateral, sign);
                st.SetUV(new Vector2((fraction + 1f) * 0.5f, f.Distance));
                st.SetUV2(new Vector2((fraction + 1f) * 0.5f + acrossDrift, f.Distance / 160f));
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
        for (var r = 0; r < rows.Count; r++)
        {
            var row = Row(rows[r].Frame, r);
            if (rows[r].Joins) Join(previousRow, row);
            previousRow = row;
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Asphalt, Name = $"Road-{path.Kind}" };
    }

    /// <summary>A frame of a deck on a structure (a bridge, a drawbridge ramp, the loop): a slab with kerbs, no earth shoulders.</summary>
    private static bool IsDeck(in RoadFrame f) => f.Kind is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Overpass;

    private const float KerbWidth = 0.45f;
    private const float KerbHeight = 0.28f;
    /// <summary>The bridge slabs' depth from the road to the soffit: the edge beams of a short concrete span (0.9 m in 0.34).</summary>
    private const float DeckDepth = 1.2f;

    /// <summary>
    /// The body of a deck between two frames (0.34, the owner's "the bridges have no thickness"): a kerb along each edge
    /// (its top and its face to the road), the deck's side from the kerb's outer top edge down to the soffit, and the
    /// soffit closing the box underneath. Each face is wound left to right across it as the shoulders are (the lateral
    /// for a face looking up, and so round), so it lights from outside.
    /// </summary>
    private static void DeckBox(MeshBuilder st, in RoadFrame a, in RoadFrame b, float kerbWidth, float kerbHeight, float depth, Color colour)
    {
        var aUp = a.Normal * kerbHeight;
        var bUp = b.Normal * kerbHeight;
        var aDown = -a.Normal * depth;
        var bDown = -b.Normal * depth;
        foreach (var side in new[] { -1f, 1f })
        {
            var ao = a.Origin + a.Lateral * (side * a.HalfWidth);
            var bo = b.Origin + b.Lateral * (side * b.HalfWidth);
            var aOut = ao + a.Lateral * (side * kerbWidth);
            var bOut = bo + b.Lateral * (side * kerbWidth);
            if (side > 0)
            {
                Quad(st, ao + aUp, aOut + aUp, bo + bUp, bOut + bUp, a.Normal, b.Normal, colour, a.Distance, b.Distance);
                Quad(st, ao, ao + aUp, bo, bo + bUp, -a.Lateral, -b.Lateral, colour, a.Distance, b.Distance);
                Quad(st, aOut + aUp, aOut + aDown, bOut + bUp, bOut + bDown, a.Lateral, b.Lateral, colour, a.Distance, b.Distance);
            }
            else
            {
                Quad(st, aOut + aUp, ao + aUp, bOut + bUp, bo + bUp, a.Normal, b.Normal, colour, a.Distance, b.Distance);
                Quad(st, ao + aUp, ao, bo + bUp, bo, a.Lateral, b.Lateral, colour, a.Distance, b.Distance);
                Quad(st, aOut + aDown, aOut + aUp, bOut + bDown, bOut + bUp, -a.Lateral, -b.Lateral, colour, a.Distance, b.Distance);
            }
        }
        var aRight = a.Origin + a.Lateral * (a.HalfWidth + kerbWidth) + aDown;
        var aLeft = a.Origin - a.Lateral * (a.HalfWidth + kerbWidth) + aDown;
        var bRight = b.Origin + b.Lateral * (b.HalfWidth + kerbWidth) + bDown;
        var bLeft = b.Origin - b.Lateral * (b.HalfWidth + kerbWidth) + bDown;
        Quad(st, aRight, aLeft, bRight, bLeft, -a.Normal, -b.Normal, colour, a.Distance, b.Distance);
    }

    /// <summary>
    /// The end of a deck where it stops at the drawbridge's gap (0.36, the owner's "the bridge is a millimetre thick when
    /// you jump"): the slab's face from the road down to the soffit, and the kerbs' ends, facing along the road
    /// (<paramref name="facing"/> +1 ahead, -1 back). Until 0.36 the deck had sides and an underside but no end, so the
    /// lip of the jump and the landing ramp's edge, seen across the gap, were the road ribbon's edge: a sheet.
    /// </summary>
    private static void DeckEnd(MeshBuilder st, RoadFrame f, float facing, float kerbWidth, float kerbHeight, float depth, Color colour)
    {
        var up = f.Normal;
        var down = -f.Normal * depth;
        var n = f.Tangent * facing;
        // Wound as DeckBox's faces are (normal = (b row - a row) x (right - left), simulation axes): the a row at the
        // foot, the b row at the top, left and right swapped with the facing.
        void Face(float from, float to, float top)
        {
            var left = f.Origin + f.Lateral * from;
            var right = f.Origin + f.Lateral * to;
            var (l0, r0) = facing > 0f ? (right, left) : (left, right);
            Quad(st, l0 + down, r0 + down, l0 + up * top, r0 + up * top, n, n, colour, 0f, 1f);
        }
        Face(-f.HalfWidth, f.HalfWidth, 0f);
        Face(-f.HalfWidth - kerbWidth, -f.HalfWidth, kerbHeight);
        Face(f.HalfWidth, f.HalfWidth + kerbWidth, kerbHeight);
    }

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
        // A broad gravel shoulder (2.6 m: 0.13's 3.2 m of earth read as a second road, 0.12's 2.2 m as a kerb); the ground
        // field keeps its plane out past this (TerrainField.Verge).
        const float width = 2.6f;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            // The decks (the bridges, the ramps, the loop) have kerbs on a slab (BuildBridges, BuildLoopFrame), not gravel
            // (0.34: until then the gravel ran up the loop and along the drawbridge).
            if (IsDeck(a) || IsDeck(b)) continue;
            // Gravel is never one shade: every couple of metres the shoulder is a little paler or darker.
            var colour = Colors.White * (0.93f + 0.14f * (RoadWear.Hash((int)(a.Distance / 2f), 0, 23) - 0.5f));
            colour.A = 1f;
            var shared = SiblingSide(track, path, a);
            foreach (var side in new[] { -1f, 1f })
            {
                if ((int)side == shared) continue;
                var ao = a.Origin + a.Lateral * (side * a.HalfWidth);
                var bo = b.Origin + b.Lateral * (side * b.HalfWidth);
                var ai = ao + a.Lateral * (side * width) - a.Normal * ShoulderDrop;
                var bi = bo + b.Lateral * (side * width) - b.Normal * ShoulderDrop;
                // Keep the winding left-to-right on both sides: back faces get their normal flipped and go dark. u runs from
                // the asphalt's edge (0) out (1) on both sides, for the verge's shader (0.36).
                if (side > 0) Quad(st, ao, ai, bo, bi, a.Normal, b.Normal, colour, a.Distance, b.Distance);
                else Quad(st, ai, ao, bi, bo, a.Normal, b.Normal, colour, a.Distance, b.Distance, uLeft: 1f, uRight: 0f);
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = ShaderGround ? ShoulderMaterial : Gravel, Name = $"Shoulder-{path.Kind}" };
    }

    private static MeshInstance3D BuildCentreLine(TrackGeometry track, TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        var yellow = new Color(0.90f, 0.72f, 0.22f);   // road paint, not a traffic cone: duller since 0.11
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            var shared = SiblingSide(track, path, a);
            // One dashed yellow centre line, as on the 1989 circuit: a 3 m dash every 9 m, 16 cm wide, the rural road's
            // own measure (0.13's 4.5 m every 12 m, the arcade's, read as oversized; 0.15). Paint wears: every dash is a
            // little brighter or duller than the next, a few are gone. Where the branches share the road, only the one
            // on the right paints the centre line.
            var dash = (int)MathF.Floor(a.Distance / 9f);
            var dashTone = 0.55f + 0.45f * RoadWear.Hash(dash, (int)path.Kind, 41);
            if (a.Distance % 9f < 3f && shared != 1 && RoadWear.Hash(dash, (int)path.Kind, 42) > 0.08f)
            {
                Quad(st, a.Origin - a.Lateral * 0.08f + a.Normal * 0.012f, a.Origin + a.Lateral * 0.08f + a.Normal * 0.012f,
                    b.Origin - b.Lateral * 0.08f + b.Normal * 0.012f, b.Origin + b.Lateral * 0.08f + b.Normal * 0.012f,
                    a.Normal, b.Normal, (yellow * dashTone) with { A = 1f }, a.Distance, b.Distance);
            }
        }
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Back };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, Name = $"Lines-{path.Kind}" };
    }

    /// <summary>
    /// White edge lines, 15 cm wide and 25 cm in from the asphalt's edge, as on a rural road of today (the 1989 circuit
    /// had only the yellow centre line, and 1989 mode hides them). Where the branches share the road at the fork and
    /// the merge, the inner side's line is left out, like the shoulder there.
    /// </summary>
    private static MeshInstance3D BuildEdgeLines(TrackGeometry track, TrackPath path)
    {
        var st = new MeshBuilder();
        var frames = path.Frames;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            if (a.Surface == SurfaceType.Chasm || b.Surface == SurfaceType.Chasm) continue;
            var shared = SiblingSide(track, path, a);
            // The paint wears in patches.
            var tone = 0.78f + 0.22f * RoadWear.Hash((int)(a.Distance / 7f), (int)path.Kind, 47);
            var white = new Color(0.84f * tone, 0.84f * tone, 0.82f * tone);
            foreach (var side in new[] { -1f, 1f })
            {
                if ((int)side == shared) continue;
                var aOut = a.Origin + a.Lateral * (side * (a.HalfWidth - 0.25f)) + a.Normal * 0.012f;
                var bOut = b.Origin + b.Lateral * (side * (b.HalfWidth - 0.25f)) + b.Normal * 0.012f;
                var aIn = aOut - a.Lateral * (side * 0.15f);
                var bIn = bOut - b.Lateral * (side * 0.15f);
                if (side > 0) Quad(st, aIn, aOut, bIn, bOut, a.Normal, b.Normal, white, a.Distance, b.Distance);
                else Quad(st, aOut, aIn, bOut, bIn, a.Normal, b.Normal, white, a.Distance, b.Distance);
            }
        }
        _edgeLines ??= new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.65f, CullMode = BaseMaterial3D.CullModeEnum.Back };
        var lines = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _edgeLines, Name = $"EdgeLines-{path.Kind}", Visible = !_retro };
        _modernRoad.Add(lines);
        return lines;
    }

    /// <summary>
    /// Marker posts (delineators) along both sides every 50 m on open road: white posts a metre high at the shoulder's
    /// outer edge, a black band near the top and a reflector facing the traffic, amber on the right and white on the
    /// left, as on rural roads. None on the bridges, the ramps and the loop (railings there), on the skid pad or in a
    /// shared stretch at the fork. Hidden in 1989 mode.
    /// </summary>
    private static Node3D? BuildMarkerPosts(TrackGeometry track, TrackPath path)
    {
        var placements = new List<(Transform3D At, bool Right)>();
        var frames = path.Frames;
        var last = -100f;
        foreach (var f in frames)
        {
            if (f.Distance - last < 50f) continue;
            if (f.Kind is not (SegmentKind.Straight or SegmentKind.Curve) || f.Surface != SurfaceType.Road || f.Origin.Y > 0.4f) continue;
            if (SiblingSide(track, path, f) != 0) continue;
            last = f.Distance;
            foreach (var side in new[] { -1f, 1f })
            {
                var foot = f.Origin + f.Lateral * (side * (f.HalfWidth + 2.0f));
                var ground = track.Terrain.Height(foot.X, foot.Z);
                if (MathF.Abs(ground - f.Origin.Y) > 0.6f) continue;
                // The reflector faces the traffic of the nearer lane: back along the road on the right, ahead on the left.
                var facing = Convert.ToGodot(f.Tangent * (side > 0 ? -1f : 1f));
                facing.Y = 0f;
                var basis = Basis.LookingAt(facing.Normalized(), Vector3.Up);
                placements.Add((new Transform3D(basis, Convert.ToGodot(foot with { Y = ground })), side > 0));
            }
        }
        if (placements.Count == 0) return null;
        var group = new Node3D { Name = $"MarkerPosts-{path.Kind}", Visible = !_retro };
        var posts = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = MarkerPostMesh(false), InstanceCount = placements.Count };
        var reflectors = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = MarkerPostMesh(true), InstanceCount = placements.Count };
        for (var i = 0; i < placements.Count; i++)
        {
            posts.SetInstanceTransform(i, placements[i].At);
            reflectors.SetInstanceTransform(i, placements[i].At);
            reflectors.SetInstanceColor(i, placements[i].Right ? new Color(1f, 0.55f, 0.08f) : new Color(0.95f, 0.95f, 0.95f));
        }
        group.AddChild(new MultiMeshInstance3D { Multimesh = posts, Name = "Posts" });
        group.AddChild(new MultiMeshInstance3D { Multimesh = reflectors, Name = "Reflectors" });
        _modernRoad.Add(group);
        return group;
    }

    /// <summary>
    /// The air's colour and density on a clear day: a visibility of about 130 km, as in the dry air of the Rockies (the
    /// Tetons a quarter hazed at 10 km). 0.36's 0.00009 suited mountains 4 km out; with the real land 8 to 24 km out it
    /// turned them into the bank of cloud the old ones were (and 0.00004 until 0.36 let nothing fade).
    /// </summary>
    private static readonly Color ClearAir = new(0.66f, 0.74f, 0.86f);
    private const float ClearAirDensity = 0.00003f;

    /// <summary>The plain's colour seen from afar (the terrain shader's lawn under its tint), which the real land takes by the terrain's edge.</summary>
    private static readonly Color PlainFar = new(0.22f, 0.29f, 0.08f);

    /// <summary>Automation (--motion-blur=0): the camera without its motion blur, for comparisons.</summary>
    public static bool? MotionBlurOverride { get; set; }

    /// <summary>The camera's motion blur (MotionBlur, 0.38): on at medium and high quality, off in the 1989 look.</summary>
    public static bool CameraBlur => (MotionBlurOverride ?? true) && !_retro && _quality >= GraphicsQuality.Medium;

    /// <summary>Automation (--land=0): the old far ground and mountains instead of the real land, for comparisons.</summary>
    public static bool? LandOverride { get; set; }

    /// <summary>The real land round the game's terrain (RealLand), or null without its data.</summary>
    private static Node3D? BuildLand(TrackGeometry track)
    {
        if (LandOverride == false || !ResourceLoader.Exists(RealLand.AlbedoPath)) return null;
        var (minX, maxX, minZ, maxZ) = TerrainBounds(track);
        var albedo = GD.Load<Texture2D>(RealLand.AlbedoPath);
        Stage("  land: the colour loaded");
        Material material;
        if (ExportMode)
        {
            // glTF carries no Godot shader: the colour alone, for the Unreal version to light.
            material = new StandardMaterial3D { AlbedoTexture = albedo, Roughness = 1f, MetallicSpecular = 0.2f };
        }
        else
        {
            _landShader = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/land.gdshader") };
            _landShader.SetShaderParameter("albedo_tex", albedo);
            _landShader.SetShaderParameter("plain_colour", PlainFar);
            material = _landShader;
        }
        // The terrain's bounds are the simulation's x and z; Godot's z is the simulation's turned round.
        return RealLand.Build((minX, maxX, -maxZ, -minZ), material, Stage);
    }

    /// <summary>The real land in the modern look, the old far ground and mountains in the 1989 one.</summary>
    private static void ShowLand()
    {
        if (_land is null || !GodotObject.IsInstanceValid(_land)) return;
        _land.Visible = !_retro;
        foreach (var node in _retroLand)
        {
            if (GodotObject.IsInstanceValid(node)) node.Visible = _retro;
        }
    }

    /// <summary>The ground's shader (assets/shaders/terrain.gdshader), or the standard grass for the export.</summary>
    private static Material GroundMaterial => ExportMode ? Grass : _groundShader ??= MakeGroundShader();

    private static ShaderMaterial MakeGroundShader()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/terrain.gdshader") };
        material.SetShaderParameter("lawn", Photo("grass_color"));
        material.SetShaderParameter("lawn_normal", Photo("grass_normal"));
        material.SetShaderParameter("meadow", Photo("meadow_color"));
        material.SetShaderParameter("meadow_normal", Photo("meadow_normal"));
        material.SetShaderParameter("soil", Photo("soil_color"));
        material.SetShaderParameter("soil_normal", Photo("soil_normal"));
        material.SetShaderParameter("tint", DryGrass);
        // The meadow photo (ambientCG Grass001) brought most of the way to the lawn's (Grass004) mean, a little greener.
        material.SetShaderParameter("meadow_balance", new Vector3(1.7f, 1.3f, 1.35f));
        material.SetShaderParameter("soil_tint", new Color(0.62f, 0.60f, 0.60f));
        material.SetShaderParameter("retro", _retro);
        return material;
    }

    /// <summary>The verge's painted stones (assets/shaders/shoulder.gdshader).</summary>
    private static ShaderMaterial ShoulderMaterial => _shoulderShader ??= MakeShoulderShader();

    private static ShaderMaterial MakeShoulderShader()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/shoulder.gdshader") };
        material.SetShaderParameter("stones", Photo("stones_color"));
        material.SetShaderParameter("stones_normal", Photo("stones_normal"));
        material.SetShaderParameter("stones_height", Photo("stones_height"));
        material.SetShaderParameter("paint", StonePaint);
        material.SetShaderParameter("faded", StoneFaded);
        material.SetShaderParameter("retro_colour", OrangeShoulder);
        material.SetShaderParameter("retro", _retro);
        return material;
    }

    /// <summary>
    /// A marker post: a white post 1.05 m tall, 10 cm by 12 cm, with a black band under its top; or, with
    /// <paramref name="reflector"/>, the reflector plate on the band's front (-z, Godot's forward), white for the
    /// instance colour to tint.
    /// </summary>
    private static ArrayMesh MarkerPostMesh(bool reflector)
    {
        var st = new MeshBuilder();
        var white = new Color(0.93f, 0.93f, 0.91f);
        var black = new Color(0.04f, 0.04f, 0.045f);
        void Block(Vector3 min, Vector3 max, Color colour)
        {
            Vector3[] normals = [Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward];
            var half = (max - min) * 0.5f;
            var middle = (min + max) * 0.5f;
            foreach (var normal in normals)
            {
                // The face's corners, wound clockwise seen from outside (v = normal x u keeps every face the same way round).
                var u = MathF.Abs(normal.Y) > 0.5f ? Vector3.Right : Vector3.Up;
                var v = normal.Cross(u);
                var centre = middle + normal * half.Dot(normal.Abs());
                var hu = half.Dot(u.Abs());
                var hv = half.Dot(v.Abs());
                var c0 = centre - u * hu - v * hv;
                var c1 = centre + u * hu - v * hv;
                var c2 = centre + u * hu + v * hv;
                var c3 = centre - u * hu + v * hv;
                st.SetNormal(normal);
                st.SetColor(colour);
                foreach (var p in new[] { c0, c2, c1, c0, c3, c2 }) st.AddVertex(p);
            }
        }
        if (reflector)
        {
            Block(new Vector3(-0.03f, 0.86f, -0.068f), new Vector3(0.03f, 0.96f, -0.061f), new Color(1f, 1f, 1f));
        }
        else
        {
            Block(new Vector3(-0.05f, 0f, -0.06f), new Vector3(0.05f, 0.82f, 0.06f), white);
            Block(new Vector3(-0.051f, 0.82f, -0.061f), new Vector3(0.051f, 1.0f, 0.061f), black);
            Block(new Vector3(-0.05f, 1.0f, -0.06f), new Vector3(0.05f, 1.05f, 0.06f), white);
        }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = reflector ? 0.25f : 0.55f, Metallic = reflector ? 0.4f : 0f });
        return mesh;
    }

    /// <summary>
    /// Overpass decks and drawbridge ramps: a concrete slab with kerbs (0.34; until then the deck was the road ribbon
    /// alone, a sheet with no side or underside), concrete pillars (down to the ground) and railings on the kerbs.
    /// </summary>
    private static IEnumerable<Node3D> BuildBridges(TrackPath path)
    {
        var frames = path.Frames;
        var railings = new MeshBuilder();
        var slab = new MeshBuilder();
        var grey = new Color(0.75f, 0.75f, 0.75f);
        var concrete = new Color(0.86f, 0.85f, 0.82f);
        var anyRail = false;
        var anySlab = false;
        var lastPost = -100f;
        var pillars = new Node3D { Name = $"Pillars-{path.Kind}" };

        for (var i = 0; i < frames.Count - 1; i++)
        {
            var a = frames[i];
            var b = frames[i + 1];
            var deck = IsDeck(a) && a.Kind != SegmentKind.Loop && IsDeck(b) && b.Kind != SegmentKind.Loop;
            if (!deck) continue;
            // The slab runs the whole deck, the ramps' feet on into the ground: an abutment where the deck meets the road.
            DeckBox(slab, a, b, KerbWidth, KerbHeight, DeckDepth, concrete);
            anySlab = true;
            if (a.Origin.Y <= 0.4f) continue;

            // Steel guardrail on the kerbs: two thin rails on posts every six metres, not a wall.
            foreach (var side in new[] { -1f, 1f })
            {
                var ao = a.Origin + a.Lateral * (side * (a.HalfWidth + KerbWidth * 0.5f)) + a.Normal * KerbHeight;
                var bo = b.Origin + b.Lateral * (side * (b.HalfWidth + KerbWidth * 0.5f)) + b.Normal * KerbHeight;
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

            // The piers under the deck are props of the track (TrackGeometry.AddBridgePiers), solid to the car and
            // drawn by PropBuilder.

        }

        // The slab's ends at the drawbridge's gap.
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            if (!IsDeck(f) || f.Kind == SegmentKind.Loop || f.Surface == SurfaceType.Chasm) continue;
            if (i + 1 < frames.Count && frames[i + 1].Surface == SurfaceType.Chasm)
            {
                DeckEnd(slab, f, 1f, KerbWidth, KerbHeight, DeckDepth, concrete);
                anySlab = true;
            }
            if (i > 0 && frames[i - 1].Surface == SurfaceType.Chasm)
            {
                DeckEnd(slab, f, -1f, KerbWidth, KerbHeight, DeckDepth, concrete);
                anySlab = true;
            }
        }

        yield return pillars;
        if (anySlab) yield return new MeshInstance3D { Mesh = slab.Commit(), MaterialOverride = Concrete, Name = $"Slab-{path.Kind}" };
        if (anyRail)
        {
            var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.5f, Metallic = 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            yield return new MeshInstance3D { Mesh = railings.Commit(), MaterialOverride = material, Name = $"Railings-{path.Kind}" };
        }
    }

    /// <summary>
    /// The drawbridge towers at the lips of the gap (TrackGeometry.DrawbridgeTowers, which also makes them solid): the
    /// white A-frames of the original, a cross-tie between the legs, cables back to the deck and a portal beam over
    /// the road joining the two towers of a lip.
    /// </summary>
    private static MeshInstance3D? BuildTowers(TrackGeometry track)
    {
        var st = new MeshBuilder();
        var white = new Color(0.93f, 0.93f, 0.90f);
        var cable = new Color(0.25f, 0.25f, 0.27f);
        var any = false;
        foreach (var lip in track.DrawbridgeTowers().GroupBy(t => (t.Path, t.Edge.Distance)))
        {
            foreach (var (path, edge, side, foot, apex) in lip)
            {
                var along = SN.Vector3.Normalize(edge.Tangent with { Y = 0f });
                var legA = foot - along * TrackGeometry.TowerLegSpread;
                var legB = foot + along * TrackGeometry.TowerLegSpread;
                Beam(st, legA, apex, 0.55f, white);
                Beam(st, legB, apex, 0.55f, white);
                Beam(st, SN.Vector3.Lerp(legA, apex, 0.55f), SN.Vector3.Lerp(legB, apex, 0.55f), 0.35f, white);
                // Cables back along the deck, away from the gap: forward from the far lip (the gap lies behind it),
                // backward from the near one.
                var back = path.FrameAt(MathUtil.Clamp(edge.Distance - 3f, 0f, path.Length)).Kind == SegmentKind.Gap ? 1f : -1f;
                for (var k = 1; k <= 3; k++)
                {
                    var frame = path.FrameAt(MathUtil.Clamp(edge.Distance + back * 12f * k, 0f, path.Length));
                    var anchor = frame.Origin + frame.Lateral * (side * (frame.HalfWidth + 1.0f)) + frame.Normal * 0.9f;
                    Beam(st, apex, anchor, 0.09f, cable);
                }
                any = true;
            }
            var apexes = lip.Select(t => t.Apex).ToArray();
            if (apexes.Length == 2) Beam(st, apexes[0], apexes[1], 0.5f, white);   // the portal beam over the road
        }
        return any ? new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Steel, Name = "Towers" } : null;
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
        var plate = new Color(0.46f, 0.47f, 0.50f);
        var any = false;
        var last = -100f;
        SN.Vector3[]? previousRing = null;
        // The deck itself: a steel box under the road with a low kerb plate along each edge (0.34), so the loop has a
        // body from outside and the car cannot look through its floor.
        var frames = path.Frames;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            if (frames[i].Kind != SegmentKind.Loop || frames[i + 1].Kind != SegmentKind.Loop) continue;
            DeckBox(st, frames[i], frames[i + 1], 0.3f, 0.2f, 0.5f, plate);
            any = true;
        }
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
