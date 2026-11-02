using Godot;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The showroom (0.25): the car the player is choosing, drawn with its real model on a turntable in a studio of its
/// own and turning slowly through a full circle, with every car of the catalogue a step to the left or right and the
/// paints (<see cref="CarView.Paints"/>) a step up or down. Until 0.24 a car was chosen by its name alone, on the title
/// screen's line; the player asked to see the cars and turn them round before choosing.
/// </summary>
/// <remarks>
/// The stage renders into its own viewport and world (a <see cref="SubViewport"/> with <c>OwnWorld3D</c>), so the
/// game's world stands paused behind it without being drawn (<see cref="Bootstrap"/> turns the main viewport's 3D off
/// while the showroom is open; a paused frame of the track costs the same as a driven one, and the studio in its place
/// draws one car over a floor): the studio's light is its own and the game's sun, weather and fog do not reach it. The
/// light is a photographer's: a grey dome for the ambient light and the reflections, three light strips over the car
/// that its paint mirrors (what a showroom's long strip lights do for a flank), a soft key light from the front
/// quarter with shadows, a fill and a rim, and a glossy floor that reflects the car through the screen-space
/// reflections. The reflection probe over the stage leaves the car out (visual layer 2, as the game's travelling probe
/// does, <see cref="CarView.MarkPlayer"/>), so the paint mirrors the strips and the floor rather than itself.
/// The HUD draws the viewport's texture under its text (<c>Hud.DrawShowroom</c>); the layout of what the mouse can
/// hit lives here, as the menu's does in <see cref="PauseMenu"/>, so the drawing and the hit test agree. The facts
/// printed about a car are worked out from its <see cref="VehicleSpec"/> alone, so a car added to the catalogue
/// appears here with them and nothing else to write.
/// </remarks>
internal sealed partial class Showroom : Node
{
    /// <summary>The turntable's speed, rad/s: a full turn in about 24 s, slow enough to look at any panel as it passes.</summary>
    public const float TurnRate = 0.26f;
    /// <summary>The first view: the front three-quarter from the driver's side (the car's nose is its -Z; the camera looks along -Z).</summary>
    public const float StartYawDegrees = 145f;
    private const float SlideSeconds = 0.32f;
    private const float SlideDistance = 7f;
    private const float DragRadiansPerPixel = 0.007f;
    private const float DiscRadius = 3.1f;      // room for the longest car (the E-Class, 4.9 m) with a step of floor round it
    private const float DiscHeight = 0.04f;
    /// <summary>The camera's lens, degrees (vertical): long, as car photographers use, so the body is not drawn in perspective.</summary>
    private const float CameraFov = 32f;
    private const float CameraPitchDegrees = 11f;
    private static readonly Color Backdrop = new(0.235f, 0.240f, 0.250f);   // the dome at the horizon, and the fog that the floor fades into

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D _turntable = null!;
    private CarView? _car;
    private CarView? _leaving;
    private float _yaw;
    private float _spin;            // the drag's angular velocity, rad/s, decaying once the button is up
    private float _slide = 1f;      // the car change's progress: 0 as it starts, 1 done
    private int _slideDirection;
    private float _zoom = 1f;
    private float _reach = 2.4f;    // half the car's length, m, smoothed: frames the camera
    private float _distance;        // the camera's distance from its aim, m, smoothed
    private float _since;           // seconds open, for the automation's notes
    private readonly Dictionary<string, PackedScene> _preloaded = new();
    private readonly HashSet<string> _loading = new();

    /// <summary>The car on the turntable, as an index into <see cref="VehicleSpec.Catalogue"/>.</summary>
    public int Index { get; private set; }
    /// <summary>The paint on it, as an index into <see cref="CarView.Paints"/>.</summary>
    public int PaintIndex { get; private set; }
    /// <summary>Opened from the Esc menu (over a paused credit) rather than from the title screen: Esc returns there.</summary>
    public bool FromMenu { get; init; }
    /// <summary>The mouse's left button is down on the stage: the turntable follows the mouse and stands still otherwise.</summary>
    public bool Dragging { get; set; }
    /// <summary>Automation (<c>--turn=</c>): the turntable held at its angle for a picture; a drag still turns it.</summary>
    public bool Held { get; init; }
    public VehicleSpec Spec => VehicleSpec.Catalogue[Index];
    public static int Count => VehicleSpec.Catalogue.Length;
    public Texture2D Texture => _viewport.GetTexture();
    public Rid ViewportRid => _viewport.GetViewportRid();
    /// <summary>The turntable's angle, degrees, for the automation's log.</summary>
    public float YawDegrees => Mathf.RadToDeg(_yaw) % 360f;
    /// <summary>The car's own colour (its model's, the lofted body's red), for the ORIGINAL swatch.</summary>
    public Color CarColour => _car?.OwnColour ?? CarView.DefaultPaint;
    /// <summary>How solid the car's name is drawn: it fades out and in again as one car slides out and the next in.</summary>
    public float TextAlpha => _slide >= 1f ? 1f : _slide < 0.5f ? 1f - _slide * 2f : (_slide - 0.5f) * 2f;

    private CarView.CarPaint? Paint => CarView.Paints[PaintIndex].Paint;

    public static Showroom Create(int carIndex, int paintIndex, float startYawDegrees, bool fromMenu, bool held = false) => new()
    {
        Name = "Showroom",
        Index = Math.Clamp(carIndex, 0, Count - 1),
        PaintIndex = Math.Clamp(paintIndex, 0, CarView.Paints.Length - 1),
        FromMenu = fromMenu,
        Held = held,
        _yaw = Mathf.DegToRad(startYawDegrees),
    };

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Name = "Stage",
            OwnWorld3D = true,
            Size = new Vector2I(1600, 900),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            // The spot and omni lights' shadows: the default atlas is sized for a game world, this stage has three lights.
            PositionalShadowAtlasSize = 2048,
        };
        AddChild(_viewport);
        _viewport.AddChild(new WorldEnvironment { Environment = BuildEnvironment() });
        BuildStudio(_viewport);
        _turntable = new Node3D { Name = "Turntable" };
        _viewport.AddChild(_turntable);
        _turntable.AddChild(new MeshInstance3D
        {
            Name = "Disc",
            Mesh = new CylinderMesh
            {
                TopRadius = DiscRadius,
                BottomRadius = DiscRadius,
                Height = DiscHeight,
                RadialSegments = 96,
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.50f, 0.50f, 0.51f), Roughness = 0.55f, Metallic = 0.1f },
            },
            Position = new Vector3(0f, DiscHeight * 0.5f, 0f),
        });
        // A seam across the disc: a turntable that is seen to turn, where a plain disc only shows the car moving.
        _turntable.AddChild(new MeshInstance3D
        {
            Name = "Seam",
            Mesh = new BoxMesh { Size = new Vector3(DiscRadius * 2f - 0.1f, 0.004f, 0.02f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.30f, 0.31f), Roughness = 0.6f } },
            Position = new Vector3(0f, DiscHeight + 0.002f, 0f),
        });
        _camera = new Camera3D { Name = "Camera", Fov = CameraFov, Near = 0.1f, Far = 4000f, Current = true };
        _viewport.AddChild(_camera);
        _car = Place(Spec);
        _reach = ReachOf(_car);
        _distance = CameraDistance;
        PlaceCamera();
        Preload();
    }

    /// <summary>
    /// The studio's light: a neutral grey dome (ambient light and the reflections' background), the key light and its
    /// shadows, a cooler fill from the other side, a rim from behind, three light strips over the stage for the paint to
    /// mirror, and a dark glossy floor that fades into the dome. Tone-mapped as the game is (AgX), so a paint reads the
    /// same colour here and on the road.
    /// </summary>
    private static Godot.Environment BuildEnvironment()
    {
        var dome = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.46f, 0.47f, 0.49f),
            SkyHorizonColor = Backdrop,
            GroundHorizonColor = Backdrop,
            // The same grey all the way down: the glossy floor mirrors the dome below the horizon, and a darker dome there
            // showed as a dark band on the floor behind the car.
            GroundBottomColor = new Color(0.215f, 0.220f, 0.230f),
            SunAngleMax = 0.1f,
            SunCurve = 0.01f,
            EnergyMultiplier = 1.0f,
        };
        return new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = dome, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 0.9f,
            TonemapWhite = 6f,
            SsaoEnabled = true,
            SsaoRadius = 1.0f,
            SsaoIntensity = 1.5f,
            SsrEnabled = true,
            SsrMaxSteps = 96,
            SsrFadeIn = 0.1f,
            SsrFadeOut = 2f,
            SsrDepthTolerance = 0.25f,
            GlowEnabled = true,
            GlowIntensity = 0.25f,
            GlowBloom = 0f,
            // The floor runs to the horizon and fades into the dome's colour, so the stage has no edge; the car, 7 m off, keeps
            // 97 % of its colour.
            FogEnabled = true,
            FogLightColor = Backdrop,
            FogDensity = 0.0045f,
            FogAerialPerspective = 0f,
            FogSkyAffect = 0f,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.03f,
            AdjustmentSaturation = 1.06f,
        };
    }

    private static void BuildStudio(Node parent)
    {
        var floor = new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(3000f, 3000f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.17f, 0.17f, 0.18f), Roughness = 0.22f, Metallic = 0.0f } },
        };
        parent.AddChild(floor);
        // The key light: from the front quarter, high, soft-edged (a 4 degree source), with the shadows that set the car on the floor.
        var key = new DirectionalLight3D
        {
            Name = "Key",
            LightEnergy = 1.35f,
            LightColor = new Color(1f, 0.975f, 0.94f),
            LightAngularDistance = 1.5f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 40f,
            ShadowBias = 0.02f,
            ShadowNormalBias = 1.2f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        key.LookAtFromPosition(new Vector3(5f, 7f, 6f), Vector3.Zero, Vector3.Up);
        parent.AddChild(key);
        // The fill: a cooler, broad light from the other side, no shadow, so the shaded flank still shows its lines.
        parent.AddChild(new OmniLight3D
        {
            Name = "Fill",
            Position = new Vector3(-7f, 4f, 3f),
            OmniRange = 30f,
            LightEnergy = 0.6f,
            LightColor = new Color(0.90f, 0.93f, 1f),
            LightSize = 1.5f,
            // No highlight from the fill or the strips' spots (LightSpecular 0): a wide light's highlight on a clearcoat is
            // the mirror image of its panel, which the probe gives from the strips; the spots' own, as wide as LightSize
            // makes them, burnt the glass and the bonnet to white.
            LightSpecular = 0f,
            ShadowEnabled = false,
        });
        // The rim: from behind and above, so the roofline and the shoulders catch a bright edge against the dome.
        var rim = new SpotLight3D
        {
            Name = "Rim",
            SpotRange = 20f,
            SpotAngle = 35f,
            SpotAngleAttenuation = 1.5f,
            LightEnergy = 2.2f,
            LightColor = new Color(1f, 0.98f, 0.95f),
            LightSize = 0.3f,
            LightSpecular = 0.3f,
            ShadowEnabled = true,
        };
        rim.LookAtFromPosition(new Vector3(-2f, 5.5f, -8f), new Vector3(0f, 0.6f, 0f), Vector3.Up);
        parent.AddChild(rim);
        // The strips: three long panels of light over the stage, faced down. They light nothing (no bounce in this world) but
        // the paint and the glass mirror them, long and bright, as a showroom's strip lights are mirrored in a flank; the
        // spot under each gives the light the strip would.
        var strip = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.99f, 0.96f),
            EmissionEnergyMultiplier = 1.8f,
            Roughness = 1f,
        };
        foreach (var z in new[] { -2.2f, 0f, 2.2f })
        {
            parent.AddChild(new MeshInstance3D
            {
                Name = "Strip",
                Mesh = new QuadMesh { Size = new Vector2(7f, 0.55f), Material = strip },
                Position = new Vector3(0f, 3.8f, z),
                RotationDegrees = new Vector3(90f, 0f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            parent.AddChild(new SpotLight3D
            {
                Name = "StripLight",
                Position = new Vector3(0f, 3.75f, z),
                RotationDegrees = new Vector3(-90f, 0f, 0f),
                SpotRange = 9f,
                SpotAngle = 60f,
                SpotAngleAttenuation = 0.8f,
                LightEnergy = 0.9f,
                LightColor = new Color(1f, 0.99f, 0.96f),
                LightSize = 1.2f,
                LightSpecular = 0f,
                ShadowEnabled = false,
            });
        }
        // The reflections: a probe the size of the stage, box-projected so the floor's and the strips' mirror images sit
        // where they should, rendered once (the stage never changes; the car is masked out by its layer).
        parent.AddChild(new ReflectionProbe
        {
            Name = "StageReflections",
            UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
            Position = new Vector3(0f, 6f, 0f),
            Size = new Vector3(40f, 12f, 40f),
            BoxProjection = true,
            CullMask = 0xFFFFFu & ~2u,
            EnableShadows = false,
            Intensity = 1f,
        });
    }

    /// <summary>The car on the turntable, in the chosen paint, standing on the disc.</summary>
    private CarView Place(VehicleSpec spec)
    {
        var car = CarView.Create(spec, Paint);
        car.Name = $"Car-{spec.Style}";
        car.SetViewpoint(false);
        _turntable.AddChild(car);
        // Layer 2 keeps it out of the probe, and the contact shadow sets it on the disc.
        car.MarkPlayer();
        car.Position = new Vector3(0f, DiscHeight - car.Extent.Road, 0f);
        return car;
    }

    private static float ReachOf(CarView car)
    {
        var bounds = car.Bounds;
        return MathF.Max(bounds.Size.Z * 0.5f, bounds.Size.Y * 1.25f);
    }

    /// <summary>Steps to the next (1) or the previous (-1) car: the car on the stage slides out and the next slides in from the other side.</summary>
    public void Step(int direction)
    {
        if (direction == 0 || Count < 2) return;
        if (_leaving is not null)
        {
            // Pressed again before the last change was over: the car on its way out goes now.
            _leaving.QueueFree();
            _leaving = null;
        }
        _leaving = _car;
        _slideDirection = Math.Sign(direction);
        _slide = 0f;
        Index = (Index + _slideDirection + Count) % Count;
        _car = Place(Spec);
        PlaceCars();
        Preload();
    }

    /// <summary>Steps the paint (up or down): a model is repainted where it stands, a lofted body built again.</summary>
    public void CyclePaint(int step)
    {
        PaintIndex = (PaintIndex + step + CarView.Paints.Length) % CarView.Paints.Length;
        if (_car is null || _car.Repaint(Paint)) return;
        var old = _car;
        _car = Place(Spec);
        old.QueueFree();
        PlaceCars();
    }

    /// <summary>The mouse dragged across the stage by <paramref name="pixels"/> (right positive): the turntable follows, and keeps a little of the motion when it is let go.</summary>
    public void Drag(float pixels, float dt)
    {
        var turn = pixels * DragRadiansPerPixel;
        _yaw += turn;
        _spin = turn / MathF.Max(dt, 1f / 240f);
    }

    /// <summary>The wheel: closer (positive steps) or further, within the stage.</summary>
    public void Zoom(int steps) => _zoom = Mathf.Clamp(_zoom * MathF.Pow(0.88f, steps), 0.55f, 1.8f);

    /// <summary>The stage's picture at the window's size (sharp at any window), with the graphics preset's anti-aliasing and scale.</summary>
    public void Resize(Vector2I size)
    {
        if (size.X < 8 || size.Y < 8 || size == _viewport.Size) return;
        _viewport.Size = size;
    }

    /// <summary>The viewport's part of the graphics preset, as <c>Bootstrap.ApplyGraphics</c> sets the main one (1989 mode: a third of the pixels, no anti-aliasing).</summary>
    public void ApplyGraphics(GraphicsQuality quality, bool retro)
    {
        var low = quality == GraphicsQuality.Low;
        _viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        _viewport.Scaling3DScale = retro ? 0.32f : low ? 0.75f : 1f;
        _viewport.Msaa3D = retro || low ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X;
        _viewport.ScreenSpaceAA = !retro && low ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        _viewport.UseTaa = !retro && !low;
        if (_viewport.GetChild(0) is WorldEnvironment { Environment: { } environment })
        {
            environment.SsaoEnabled = !retro && !low;
            environment.SsrEnabled = !retro && !low;
            environment.GlowEnabled = !retro;
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _since += dt;
        if (!Dragging)
        {
            // The turntable turns on its own, the drag's spin on top, dying away in about a second.
            _spin *= MathF.Exp(-dt * 3f);
            _yaw += ((Held ? 0f : TurnRate) + _spin) * dt;
        }
        _yaw %= MathF.Tau;
        _turntable.Rotation = new Vector3(0f, _yaw, 0f);
        if (_slide < 1f)
        {
            _slide = MathF.Min(1f, _slide + dt / SlideSeconds);
            PlaceCars();
            if (_slide >= 1f && _leaving is not null)
            {
                _leaving.QueueFree();
                _leaving = null;
            }
        }
        if (_car is not null) _reach += (ReachOf(_car) - _reach) * Mathf.Clamp(dt * 6f, 0f, 1f);
        _distance += (CameraDistance - _distance) * Mathf.Clamp(dt * 6f, 0f, 1f);
        PlaceCamera();
        CollectPreloaded();
    }

    private float CameraDistance => _reach * 2.9f * _zoom;

    /// <summary>
    /// The camera: a long lens from the front, a little above the car, aimed under the car's middle so the car stands in
    /// the upper two thirds of the picture, over the HUD's panel.
    /// </summary>
    private void PlaceCamera()
    {
        var pitch = Mathf.DegToRad(CameraPitchDegrees);
        var middle = _car is null ? 0.7f : _car.Position.Y + _car.Bounds.GetCenter().Y;
        var aim = new Vector3(0f, middle - 0.072f * _distance, 0f);
        var eye = aim + new Vector3(0f, MathF.Sin(pitch), MathF.Cos(pitch)) * _distance;
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(aim - eye, Vector3.Up), eye);
    }

    /// <summary>The cars during a change: the one leaving slides off along the screen, the new one in from the other side; both on the turning table.</summary>
    private void PlaceCars()
    {
        var eased = _slide < 0.5f ? 2f * _slide * _slide : 1f - MathF.Pow(-2f * _slide + 2f, 2f) / 2f;
        var toTable = _turntable.Basis.Inverse();
        if (_car is not null) _car.Position = toTable * new Vector3(_slideDirection * SlideDistance * (1f - eased), 0f, 0f) + new Vector3(0f, DiscHeight - _car.Extent.Road, 0f);
        if (_leaving is not null) _leaving.Position = toTable * new Vector3(-_slideDirection * SlideDistance * eased, 0f, 0f) + new Vector3(0f, DiscHeight - _leaving.Extent.Road, 0f);
    }

    /// <summary>
    /// The neighbours' models loaded on a thread while the player looks at this car, so a step does not wait on the disk
    /// (a prepared model is 20-70 MB; read on the main thread it held the picture for most of a second). Only the two
    /// neighbours are kept: eight cars of textures at once would be hundreds of megabytes of video memory.
    /// </summary>
    private void Preload()
    {
        var wanted = new HashSet<string>();
        foreach (var offset in new[] { -1, 0, 1 })
        {
            if (CarView.ModelPath(VehicleSpec.Catalogue[(Index + offset + Count) % Count]) is { } path) wanted.Add(path);
        }
        foreach (var path in _preloaded.Keys.Where(p => !wanted.Contains(p)).ToList()) _preloaded.Remove(path);
        foreach (var path in wanted)
        {
            if (_preloaded.ContainsKey(path) || _loading.Contains(path)) continue;
            if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok) _loading.Add(path);
        }
    }

    /// <summary>Takes the models whose threaded load is done, so they are in the resource cache for <see cref="CarView.Create"/>.</summary>
    private void CollectPreloaded()
    {
        if (_loading.Count == 0) return;
        foreach (var path in _loading.ToList())
        {
            var status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress) continue;
            _loading.Remove(path);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded && ResourceLoader.LoadThreadedGet(path) is PackedScene scene) _preloaded[path] = scene;
        }
    }

    // ---- facts about the car, from its spec ------------------------------------------------------------

    /// <summary>The car's year, from the '84 that ends its name; empty when the name carries none.</summary>
    public static string Year(VehicleSpec spec)
    {
        var at = spec.Name.LastIndexOf('\'');
        if (at < 0 || at + 3 > spec.Name.Length || !int.TryParse(spec.Name.AsSpan(at + 1, 2), out var two)) return "";
        // Two digits: the catalogue runs from the 1980s to the 2010s, so anything under 40 is this century.
        return (two < 40 ? 2000 + two : 1900 + two).ToString();
    }

    /// <summary>
    /// The layout line: the year, where the engine sits, its cylinders and its induction, the driven wheels and the gearbox.
    /// The engine's place is read from the weight the spec puts on each axle (the centre of mass against the axles): a
    /// car carrying more than half its weight on the rear axle has its engine behind the seats (the flat-twelve 40/60,
    /// the V8 42/58), a front-engined car more than half on the front; the spec has no other word for it.
    /// </summary>
    public static string LayoutLine(VehicleSpec spec)
    {
        var front = FrontWeightShare(spec);
        var engine = front >= 0.5f ? "FRONT ENGINE" : "ENGINE BEHIND THE SEATS";
        var induction = spec.Engine.Governor ? "TURBO-DIESEL" : spec.Engine.BoostShare > 0f ? "TURBO" : "";
        var cylinders = $"{spec.Engine.Cylinders} CYLINDERS{(induction == "" ? "" : "  " + induction)}";
        var drive = spec.FourWheelDrive is not null ? "FOUR-WHEEL DRIVE" : spec.FrontWheelDrive ? "FRONT-WHEEL DRIVE" : "REAR-WHEEL DRIVE";
        // A gear change under an eighth of a second is a dual-clutch box handing over; a hand on a gate takes a quarter.
        var gearbox = $"{spec.ForwardGearCount}-SPEED {(spec.ShiftSeconds <= 0.125f ? "DUAL-CLUTCH" : "MANUAL")}";
        var year = Year(spec);
        return string.Join("   ·   ", new[] { year, engine, cylinders, drive, gearbox, $"{front * 100f:0} / {100f - front * 100f:0} FRONT / REAR" }.Where(p => p != ""));
    }

    /// <summary>
    /// The figures line: peak power in metric horsepower (the period brochures' unit, as the title screen prints it) and
    /// peak torque, each with its revs, from the full-throttle torque curve; the mass; the power to weight; the tyres.
    /// </summary>
    public static string FiguresLine(VehicleSpec spec)
    {
        var curve = spec.Engine.FullThrottleCurve;
        var peakPower = curve.MaxBy(p => p.Rpm * p.Torque);
        var peakTorque = curve.MaxBy(p => p.Torque);
        var hp = peakPower.Rpm * peakPower.Torque / 7023f;
        var tyres = spec.FrontTire == spec.RearTire ? Tyre(spec.FrontTire) : $"{Tyre(spec.FrontTire)} FRONT, {Tyre(spec.RearTire)} REAR";
        return $"{hp:0} HP AT {peakPower.Rpm:0} RPM   ·   {peakTorque.Torque:0} NM AT {peakTorque.Rpm:0} RPM   ·   {spec.MassKg:0} KG   ·   {hp / spec.MassKg * 1000f:0} HP PER TONNE   ·   TYRES {tyres}";
    }

    private static string Tyre(TireSize tyre) => $"{tyre.WidthMm:0}/{tyre.AspectPercent:0} R{tyre.RimInches:0}";

    /// <summary>The share of the car's weight on its front axle at rest, from where the spec puts the centre of mass between the axles.</summary>
    public static float FrontWeightShare(VehicleSpec spec) => (spec.RearAxle + spec.CentreOfMassOffset.Z) / (spec.FrontAxle + spec.RearAxle);

    // ---- layout, shared with the HUD's drawing and the mouse's hit test --------------------------------

    public static Rect2 Panel(Vector2 size) => new(0f, size.Y - 236f, size.X, 236f);
    public static Rect2 LeftArrow(Vector2 size) => new(24f, size.Y * 0.5f - 130f, 96f, 160f);
    public static Rect2 RightArrow(Vector2 size) => new(size.X - 120f, size.Y * 0.5f - 130f, 96f, 160f);
    public const float SwatchSize = 34f;
    private const float SwatchGap = 12f;

    public static Rect2 Swatch(Vector2 size, int index)
    {
        var count = CarView.Paints.Length;
        var x0 = size.X * 0.5f - (count * SwatchSize + (count - 1) * SwatchGap) * 0.5f;
        return new Rect2(x0 + index * (SwatchSize + SwatchGap), Panel(size).Position.Y + 96f, SwatchSize, SwatchSize);
    }

    public static Rect2 TakeButton(Vector2 size) => new(size.X - 290f, Panel(size).Position.Y + 92f, 250f, 42f);
    public static Rect2 BackButton(Vector2 size) => new(40f, Panel(size).Position.Y + 92f, 250f, 42f);

    /// <summary>What a click lands on.</summary>
    public enum Hit { Nothing, Left, Right, Take, Back, Swatch }

    public static (Hit Hit, int Index) HitTest(Vector2 size, Vector2 point)
    {
        if (LeftArrow(size).HasPoint(point)) return (Hit.Left, 0);
        if (RightArrow(size).HasPoint(point)) return (Hit.Right, 0);
        if (TakeButton(size).HasPoint(point)) return (Hit.Take, 0);
        if (BackButton(size).HasPoint(point)) return (Hit.Back, 0);
        for (var i = 0; i < CarView.Paints.Length; i++)
        {
            if (Swatch(size, i).Grow(4f).HasPoint(point)) return (Hit.Swatch, i);
        }
        return (Hit.Nothing, 0);
    }
}
