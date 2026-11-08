using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// What the wheels throw up (0.42): dust off the verge and the dry ground of the valley when a wheel runs there at speed,
/// thicker the faster; and the pale smoke of rubber when a tyre slides hard on the asphalt (a lock-up, a burnout, a
/// spin). Until 0.42 a car could cross the plain at 150 km/h without raising a grain. On a wet road (0.53) the tyres
/// throw up a mist of spray behind them, thicker the faster and the wetter.
/// </summary>
/// <remarks>
/// One GPU particle system a wheel, in the world's space so the dust hangs where it was thrown, its emitter moved to
/// the wheel's contact every frame and its amount turned with the speed or the slide (<c>AmountRatio</c>); soft round
/// puffs, lit, that grow and thin out. None in the 1989 look nor in the replay.
/// </remarks>
internal sealed partial class WheelDust : Node3D
{
    private const int PerWheel = 48;
    private readonly GpuParticles3D[] _dust = new GpuParticles3D[4];
    private readonly GpuParticles3D[] _smoke = new GpuParticles3D[4];
    private readonly GpuParticles3D[] _spray = new GpuParticles3D[4];
    // The traffic's spray (0.62): two emitters behind each drone's rear wheels, made as the drones first need them.
    private readonly List<(GpuParticles3D Left, GpuParticles3D Right)> _trafficSpray = [];
    private readonly Texture2D _puff;

    public WheelDust()
    {
        Name = "WheelDust";
        var puff = _puff = new GradientTexture2D
        {
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Colors = [new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0f)], Offsets = [0f, 1f] },
        };
        for (var i = 0; i < 4; i++)
        {
            _dust[i] = Make(puff, new Color(0.58f, 0.53f, 0.46f), 1.6f, 0.6f, 2.2f);
            _smoke[i] = Make(puff, new Color(0.86f, 0.86f, 0.87f), 2.2f, 0.8f, 3.0f);
            // The spray: a fine pale mist, quick to grow and quick to thin.
            // Twice the puffs, smaller and fainter (0.55): a mist, where 0.53's fewer and larger showed as soft blocks over a dark road.
            _spray[i] = Make(puff, new Color(0.72f, 0.75f, 0.78f), 0.8f, 0.35f, 1.3f, 0.2f, PerWheel * 2);
            AddChild(_dust[i]);
            AddChild(_smoke[i]);
            AddChild(_spray[i]);
        }
    }

    private static GpuParticles3D Make(Texture2D puff, Color colour, float lifetime, float startSize, float endSize, float opacity = 0.9f, int amount = PerWheel)
    {
        var material = new StandardMaterial3D
        {
            AlbedoTexture = puff,
            AlbedoColor = colour,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Roughness = 1f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            // Soft where a puff meets the road or the ground (0.53): the quads cut into it in hard bands.
            ProximityFadeEnabled = true,
            ProximityFadeDistance = 0.6f,
            // Clear within 1.5 m of the eye and whole from 3.5 m (0.74): a sliding rear tyre's puffs, up to 3 m across and
            // facing the eye, reached into the cabin, and from the seat the car seemed to fill with smoke (the owner,
            // 8 November 2026). Behind the car, through the rear window and from the chase camera, the smoke still shows.
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
            DistanceFadeMinDistance = 1.5f,
            DistanceFadeMaxDistance = 3.5f,
        };
        var scale = new Curve();
        scale.AddPoint(new Vector2(0f, startSize / endSize));
        scale.AddPoint(new Vector2(1f, 1f));
        var fade = new Gradient { Colors = [new Color(1f, 1f, 1f, opacity), new Color(1f, 1f, 1f, 0f)], Offsets = [0f, 1f] };
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.6f,
            // Rising a little, and carried downwind (0.45: the wind of assets/shaders/wind.gdshaderinc, to Godot's -x
            // and -z; the damping holds it to a drift of a few metres a second).
            Gravity = new Vector3(-0.944f * 4f, 0.25f, -0.330f * 4f),
            DampingMin = 1.5f,
            DampingMax = 3f,
            ScaleCurve = new CurveTexture { Curve = scale },
            ColorRamp = new GradientTexture1D { Gradient = fade },
        };
        return new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(endSize, endSize), Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The emitter moves with the wheel: the box it may draw in has to hold the cloud behind it.
            VisibilityAabb = new Aabb(new Vector3(-20f, -2f, -20f), new Vector3(40f, 8f, 40f)),
        };
    }

    /// <summary>Once a frame: each wheel's dust by its speed on the dry ground, its smoke by its slide on the road.</summary>
    public void Update(RaceSession session, bool retro)
    {
        var car = session.Car;
        var live = !retro;
        var speed = car.Velocity.Length();
        var wet = WeatherModel.Wetness(session.Weather);
        for (var i = 0; i < car.Wheels.Length && i < 4; i++)
        {
            var w = car.Wheels[i];
            var at = Convert.ToGodot(w.ContactPoint);
            var dust = live && w.InContact && w.Surface == SurfaceType.Grass ? MathUtil.Clamp01((speed - 5f) / 25f) : 0f;
            var smoke = live && w.InContact && w.Surface == SurfaceType.Road ? MathUtil.Clamp01((w.SlipIntensity - 0.55f) / 0.4f) * MathUtil.Clamp01(speed / 6f + 0.3f) : 0f;
            // On a wet road from 30 km/h, full at 120.
            var spray = live && w.InContact && w.Surface == SurfaceType.Road ? wet * MathUtil.Clamp01((speed - 8f) / 25f) : 0f;
            // Through a puddle (0.56) the tyre throws up twice as much and more: the water it pushes aside (VehicleSim.Puddles).
            spray *= 1f + 1.2f * MathUtil.Clamp01((w.WaterDepth - 1.5f) / 2.5f);
            Drive(_dust[i], at, dust);
            Drive(_smoke[i], at + Vector3.Up * 0.15f, smoke);
            Drive(_spray[i], at + Vector3.Up * 0.1f, spray * 0.45f);
        }
    }

    /// <summary>
    /// The other cars' spray on a wet road (0.62): behind each drone's rear wheels (0.75 m out from its centre, 1.3 m
    /// back, at the road), as the player's from 30 km/h, full at 120, by the road's wetness. The drones have no wheels
    /// in the simulation; their views' frames place the spray.
    /// </summary>
    public void UpdateTraffic(IReadOnlyList<CarView> views, IReadOnlyList<GrudgeLap.Physics.Race.TrafficCar> cars, Weather weather, bool retro)
    {
        var wet = retro ? 0f : WeatherModel.Wetness(weather);
        for (var i = 0; i < views.Count && i < cars.Count; i++)
        {
            if (i >= _trafficSpray.Count)
            {
                if (wet <= 0f) break;
                var left = Make(_puff, new Color(0.72f, 0.75f, 0.78f), 0.8f, 0.35f, 1.3f, 0.2f, PerWheel * 2);
                var right = Make(_puff, new Color(0.72f, 0.75f, 0.78f), 0.8f, 0.35f, 1.3f, 0.2f, PerWheel * 2);
                AddChild(left);
                AddChild(right);
                _trafficSpray.Add((left, right));
            }
            var (l, r) = _trafficSpray[i];
            var frame = views[i].GlobalTransform;
            var spray = wet * MathUtil.Clamp01((MathF.Abs(cars[i].Speed) - 8f) / 25f) * 0.45f;
            Drive(l, frame * new Vector3(-0.75f, -0.5f, 1.3f), spray);
            Drive(r, frame * new Vector3(0.75f, -0.5f, 1.3f), spray);
        }
    }

    private static void Drive(GpuParticles3D particles, Vector3 at, float amount)
    {
        particles.GlobalPosition = at;
        particles.AmountRatio = Mathf.Max(amount, 0.01f);
        particles.Emitting = amount > 0.02f;
    }
}
