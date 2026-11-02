using Godot;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The showroom (0.25): the car the player is choosing, drawn with its real model on a turntable in a studio of its
/// own and turning slowly through a full circle, with every car of the catalogue a step to the left or right and the
/// paints a step up or down: the car's own colour, the presets (<see cref="CarView.Paints"/>) and a colour of the
/// player's own, mixed in the picker (<see cref="ColourPicker"/>) in a finish and seen on the car as it is mixed. Until
/// 0.24 a car was chosen by its name alone, on the title screen's line, and one paint served every car; the player
/// asked to see the cars and turn them round before choosing, and to choose each one's colour there.
/// </summary>
/// <remarks>
/// The stage renders into its own viewport and world (a <see cref="SubViewport"/> with <c>OwnWorld3D</c>), so the
/// game's world stands paused behind it without being drawn (<see cref="Bootstrap"/> turns the main viewport's 3D off
/// while the showroom is open; a paused frame of the track costs the same as a driven one, and the studio in its place
/// draws one car over a floor): the studio's light is its own and the game's sun, weather and fog do not reach it. The
/// light is a photographer's: a grey dome with the studio's lamps in it (<see cref="StudioSky"/>: long strips overhead
/// and softboxes round the car, which the paint, the clearcoat and the glass mirror as it turns), a soft key light from
/// the front quarter with shadows, a fill and a rim, and a glossy floor that reflects the car through the screen-space
/// reflections.
/// The HUD draws the viewport's texture under its text (<c>Hud.DrawShowroom</c>); the layout of what the mouse can
/// hit lives here, as the menu's does in <see cref="PauseMenu"/>, so the drawing and the hit test agree. The facts
/// printed about a car are worked out from its <see cref="VehicleSpec"/> alone, so a car added to the catalogue
/// appears here with them and nothing else to write. The paints chosen here are edits until the player takes a car
/// (<see cref="Edits"/>): Bootstrap writes them to the cars' settings then (<see cref="PaintBook"/>), and leaving with
/// Esc forgets them.
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
    private PaintBook _book = null!;
    private readonly Dictionary<int, PaintChoice> _edits = new();     // the paints chosen here, by car, until taken
    private readonly Dictionary<int, PaintChoice> _mixed = new();     // the colour each car was last mixed in here
    private PaintChoice? _mixedBefore;                                // the car's mixed colour when the picker opened

    /// <summary>The car on the turntable, as an index into <see cref="VehicleSpec.Catalogue"/>.</summary>
    public int Index { get; private set; }
    /// <summary>The paint on it: chosen here, or the one its settings keep.</summary>
    public PaintChoice Choice => ChoiceOf(Index);
    /// <summary>The paint as a place in the row of swatches: the presets (0 its own colour), then the mixed colour.</summary>
    public int PaintSlot => Choice.IsCustom ? CustomSlot : Choice.Preset;
    /// <summary>The paints chosen here, by car (an index into the catalogue): written to the settings when a car is taken.</summary>
    public IReadOnlyDictionary<int, PaintChoice> Edits => _edits;
    /// <summary>The colour picker while it is open (C, or a click on the mixed colour's swatch).</summary>
    public ColourPicker? Picker { get; private set; }
    /// <summary>The HUD's size (the canvas's, 1600 x 900 or wider): the camera moves the car clear of the picker by it.</summary>
    public Vector2 ScreenSize { get; set; } = new(1600f, 900f);
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
    /// <summary>The colour of the last swatch: the car's mixed colour, or the one the picker would start from.</summary>
    public PaintChoice MixedChoice => MixedFor(Index);
    /// <summary>The swatches: the presets and the mixed colour.</summary>
    public static int SlotCount => CarView.Paints.Length + 1;
    public static int CustomSlot => CarView.Paints.Length;
    /// <summary>How solid the car's name is drawn: it fades out and in again as one car slides out and the next in.</summary>
    public float TextAlpha => _slide >= 1f ? 1f : _slide < 0.5f ? 1f - _slide * 2f : (_slide - 0.5f) * 2f;

    public static Showroom Create(int carIndex, PaintBook book, float startYawDegrees, bool fromMenu, bool held = false) => new()
    {
        Name = "Showroom",
        Index = Math.Clamp(carIndex, 0, Count - 1),
        _book = book,
        FromMenu = fromMenu,
        Held = held,
        _yaw = Mathf.DegToRad(startYawDegrees),
    };

    private PaintChoice ChoiceOf(int car) => _edits.TryGetValue(car, out var choice) ? choice : _book[car];

    /// <summary>
    /// The colour a car's picker starts from: the one it was last mixed in (here, or kept in the settings), else its
    /// own colour as a solid, so a player who wants the factory colour a shade darker starts from it.
    /// </summary>
    private PaintChoice MixedFor(int car) =>
        _mixed.TryGetValue(car, out var mixed) ? mixed
        : _book.LastMixed(car) is { } kept ? kept
        : PaintChoice.Mixed(car == Index ? CarColour : CarView.DefaultPaint, PaintFinish.Solid);

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
        _car = Place(Index);
        _reach = ReachOf(_car);
        _distance = CameraDistance;
        PlaceCamera();
        Preload();
    }

    /// <summary>
    /// The studio's sky, an HDRI studio of its own: a grey dome (lighter overhead, the floor's grey below the horizon)
    /// with the lamps a car photographer's studio hangs as its light, three long strips overhead across the car and three
    /// tall softboxes round it at the height of its flanks, behind the camera and to either side, all out of the lens's
    /// view. The paint, the clearcoat and the glass mirror them as the car turns (the long bright bands of a showroom
    /// on a bonnet and a flank) and they light the car with the dome, softly.
    /// </summary>
    private const string StudioSky = """
        shader_type sky;
        render_mode use_debanding;
        uniform vec3 top_colour : source_color = vec3(0.46, 0.47, 0.49);
        uniform vec3 horizon_colour : source_color = vec3(0.235, 0.24, 0.25);
        uniform vec3 bottom_colour : source_color = vec3(0.215, 0.22, 0.23);
        uniform vec3 lamp_colour : source_color = vec3(1.0, 0.99, 0.96);
        uniform float lamp_energy = 1.0;
        float lamp(vec2 p, vec2 centre, vec2 half_size, float soft) {
            vec2 d = abs(p - centre) - half_size;
            return 1.0 - smoothstep(-soft, soft, max(d.x, d.y));
        }
        void sky() {
            vec3 d = normalize(EYEDIR);
            float up = d.y;
            vec3 colour = up >= 0.0
                ? mix(horizon_colour, top_colour, sqrt(clamp(up, 0.0, 1.0)))
                : mix(horizon_colour, bottom_colour, sqrt(clamp(-up, 0.0, 1.0)));
            float light = 0.0;
            if (up > 0.3) {
                // Where the ray meets a ceiling a unit over the car: the strips 3.2 m up, 2.2 m apart, 7 m by 0.55 m.
                vec2 p = d.xz / up;
                for (int i = -1; i <= 1; i++) light += lamp(p, vec2(0.0, float(i) * 0.69), vec2(1.1, 0.086), 0.02);
            }
            // The softboxes by bearing (0 towards +Z, where the camera stands) and elevation, radians.
            vec2 at = vec2(atan(d.x, d.z), asin(clamp(up, -1.0, 1.0)));
            light += lamp(at, vec2(0.0, 0.2), vec2(0.45, 0.2), 0.03);
            light += lamp(at, vec2(1.5708, 0.2), vec2(0.3, 0.2), 0.03);
            light += lamp(at, vec2(-1.5708, 0.2), vec2(0.3, 0.2), 0.03);
            COLOR = colour + lamp_colour * lamp_energy * min(light, 1.0);
        }
        """;

    /// <summary>
    /// The studio's light: the dome and its lamps (<see cref="StudioSky"/>) for the ambient light and the reflections,
    /// and a dark glossy floor that fades into the dome; the key, the fill and the rim are BuildStudio's. Tone-mapped as
    /// the game is (AgX), so a paint reads the same colour here and on the road.
    /// </summary>
    private static Godot.Environment BuildEnvironment()
    {
        // The studio's lamps are in its sky (StudioSky): a car's clearcoat mirrors the sky alone, so a lamp that is a
        // mesh in the room (the long strips and the reflection probe that caught them until the showroom's review)
        // reached the paint only through the base coat's rough reflection, and every flank read as satin.
        var dome = new ShaderMaterial { Shader = new Shader { Code = StudioSky } };
        return new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = dome, RadianceSize = Sky.RadianceSizeEnum.Size256, ProcessMode = Sky.ProcessModeEnum.Quality },
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
            // Glow kept faint: a softbox mirrored in a flank or a side window is white, and a stronger glow haloed it into a blob.
            GlowIntensity = 0.12f,
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
        // The key light: from the front quarter, high, soft-edged, with the shadows that set the car on the floor. Its own
        // highlight is held down (LightSpecular): a sun-sized source drew a hard white spot with a halo of glow on the
        // bonnet and the roof, where a studio's key is a softbox whose reflection is a broad soft patch.
        var key = new DirectionalLight3D
        {
            Name = "Key",
            LightEnergy = 1.35f,
            LightColor = new Color(1f, 0.975f, 0.94f),
            LightAngularDistance = 4f,
            LightSpecular = 0.35f,
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
            // No highlight from the fill (LightSpecular 0): a wide light's highlight on a clearcoat is the mirror image of
            // its panel, which the sky's lamps give; its own, as wide as LightSize makes it, burnt the glass to white.
            LightSpecular = 0f,
            ShadowEnabled = false,
        });
        // The rim: from behind and above, so the roofline and the shoulders catch a bright edge against the dome. No
        // highlight of its own: a camera in front of the car sees the bonnet and the roof mirror the space above and
        // behind it, and the rim's small source there was a hard white spot on them.
        var rim = new SpotLight3D
        {
            Name = "Rim",
            SpotRange = 20f,
            SpotAngle = 35f,
            SpotAngleAttenuation = 1.5f,
            LightEnergy = 2.2f,
            LightColor = new Color(1f, 0.98f, 0.95f),
            LightSize = 0.3f,
            LightSpecular = 0f,
            ShadowEnabled = true,
        };
        rim.LookAtFromPosition(new Vector3(-2f, 5.5f, -8f), new Vector3(0f, 0.6f, 0f), Vector3.Up);
        parent.AddChild(rim);
    }

    /// <summary>The car on the turntable, in its paint, standing on the disc.</summary>
    private CarView Place(int index)
    {
        var spec = VehicleSpec.Catalogue[index];
        var car = CarView.Create(spec, ChoiceOf(index).Paint);
        car.Name = $"Car-{spec.Style}";
        car.SetViewpoint(false);
        _turntable.AddChild(car);
        // The contact shadow sets it on the disc.
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
        // A colour being mixed stays on the car it was mixed for.
        if (Picker is not null) ClosePicker(keep: true);
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
        _car = Place(Index);
        PlaceCars();
        Preload();
    }

    /// <summary>Steps the paint (up or down) through the presets and the mixed colour.</summary>
    public void CyclePaint(int step) => ChooseSlot((PaintSlot + step + SlotCount) % SlotCount);

    /// <summary>A swatch taken: a preset, or the mixed colour (the last one, or the car's own as a solid to start from).</summary>
    public void ChooseSlot(int slot)
    {
        if (Picker is not null && slot != CustomSlot) ClosePicker(keep: true);
        SetChoice(slot == CustomSlot ? MixedFor(Index) : PaintChoice.OfPreset(slot));
    }

    /// <summary>The car's paint chosen here: a model is repainted where it stands, a lofted body built again.</summary>
    private void SetChoice(PaintChoice choice)
    {
        _edits[Index] = choice;
        if (choice.IsCustom) _mixed[Index] = choice;
        if (_car is null || _car.Repaint(choice.Paint)) return;
        var old = _car;
        _car = Place(Index);
        old.QueueFree();
        PlaceCars();
    }

    /// <summary>Opens the colour picker on the car's mixed colour (its last one, or its own colour to start from).</summary>
    public void OpenPicker()
    {
        if (Picker is not null) return;
        var before = Choice;
        _mixedBefore = _mixed.TryGetValue(Index, out var mixed) ? mixed : null;
        if (!before.IsCustom) SetChoice(MixedFor(Index));
        Picker = new ColourPicker(Choice, before);
    }

    /// <summary>
    /// Closes the picker: the mixed colour kept on the car, or (cancelled) the car and its mixed swatch put back as
    /// they were when it opened.
    /// </summary>
    public void ClosePicker(bool keep)
    {
        if (Picker is not { } picker) return;
        Picker = null;
        if (keep) return;
        SetChoice(picker.Before);
        if (_mixedBefore is { } mixed) _mixed[Index] = mixed;
        else _mixed.Remove(Index);
    }

    /// <summary>The picker's colour or finish changed: the car is repainted in it as it turns.</summary>
    public void ApplyPicker()
    {
        if (Picker is { } picker) SetChoice(picker.Choice);
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
        _camera.HOffset += (PickerShift - _camera.HOffset) * Mathf.Clamp(dt * 6f, 0f, 1f);
        PlaceCamera();
        CollectPreloaded();
    }

    private float CameraDistance => _reach * 2.9f * _zoom;

    /// <summary>
    /// The camera's sideways move while the picker is open, m: the car centred in the stage left of the picker's panel
    /// rather than behind it. The fraction of the half-screen it moves by, times the half-width the lens sees at the
    /// car (its vertical angle and the screen's shape).
    /// </summary>
    private float PickerShift
    {
        get
        {
            if (Picker is null) return 0f;
            var free = (LeftArrow(ScreenSize).End.X + ColourPicker.Panel(ScreenSize).Position.X) * 0.5f;
            var fraction = (ScreenSize.X * 0.5f - free) / (ScreenSize.X * 0.5f);
            return fraction * MathF.Tan(Mathf.DegToRad(CameraFov * 0.5f)) * ScreenSize.X / ScreenSize.Y * _distance;
        }
    }

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

    /// <summary>A swatch of the row: the presets, then (<see cref="CustomSlot"/>) the mixed colour.</summary>
    public static Rect2 Swatch(Vector2 size, int index)
    {
        var count = SlotCount;
        var x0 = size.X * 0.5f - (count * SwatchSize + (count - 1) * SwatchGap) * 0.5f;
        return new Rect2(x0 + index * (SwatchSize + SwatchGap), Panel(size).Position.Y + 96f, SwatchSize, SwatchSize);
    }

    public static Rect2 TakeButton(Vector2 size) => new(size.X - 290f, Panel(size).Position.Y + 92f, 250f, 42f);
    public static Rect2 BackButton(Vector2 size) => new(40f, Panel(size).Position.Y + 92f, 250f, 42f);

    /// <summary>What a click lands on (the picker's own parts are <see cref="ColourPicker.HitTest"/>'s).</summary>
    public enum Hit { Nothing, Left, Right, Take, Back, Swatch }

    public static (Hit Hit, int Index) HitTest(Vector2 size, Vector2 point)
    {
        if (LeftArrow(size).HasPoint(point)) return (Hit.Left, 0);
        if (RightArrow(size).HasPoint(point)) return (Hit.Right, 0);
        if (TakeButton(size).HasPoint(point)) return (Hit.Take, 0);
        if (BackButton(size).HasPoint(point)) return (Hit.Back, 0);
        for (var i = 0; i < SlotCount; i++)
        {
            if (Swatch(size, i).Grow(4f).HasPoint(point)) return (Hit.Swatch, i);
        }
        return (Hit.Nothing, 0);
    }
}
