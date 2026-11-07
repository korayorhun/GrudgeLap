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

    public WheelDust()
    {
        Name = "WheelDust";
        var puff = new GradientTexture2D
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

    private static void Drive(GpuParticles3D particles, Vector3 at, float amount)
    {
        particles.GlobalPosition = at;
        particles.AmountRatio = Mathf.Max(amount, 0.01f);
        particles.Emitting = amount > 0.02f;
    }
}
