using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// What the wheels throw up (0.42): dust off the verge and the dry ground of the valley when a wheel runs there at speed,
/// thicker the faster; and the pale smoke of rubber when a tyre slides hard on the asphalt (a lock-up, a burnout, a
/// spin). Until 0.42 a car could cross the plain at 150 km/h without raising a grain.
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
            AddChild(_dust[i]);
            AddChild(_smoke[i]);
        }
    }

    private static GpuParticles3D Make(Texture2D puff, Color colour, float lifetime, float startSize, float endSize)
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
        };
        var scale = new Curve();
        scale.AddPoint(new Vector2(0f, startSize / endSize));
        scale.AddPoint(new Vector2(1f, 1f));
        var fade = new Gradient { Colors = [new Color(1f, 1f, 1f, 0.9f), new Color(1f, 1f, 1f, 0f)], Offsets = [0f, 1f] };
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.6f,
            Gravity = new Vector3(0f, 0.25f, 0f),
            DampingMin = 1.5f,
            DampingMax = 3f,
            ScaleCurve = new CurveTexture { Curve = scale },
            ColorRamp = new GradientTexture1D { Gradient = fade },
        };
        return new GpuParticles3D
        {
            Amount = PerWheel,
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
        for (var i = 0; i < car.Wheels.Length && i < 4; i++)
        {
            var w = car.Wheels[i];
            var at = Convert.ToGodot(w.ContactPoint);
            var dust = live && w.InContact && w.Surface == SurfaceType.Grass ? MathUtil.Clamp01((speed - 5f) / 25f) : 0f;
            var smoke = live && w.InContact && w.Surface == SurfaceType.Road ? MathUtil.Clamp01((w.SlipIntensity - 0.55f) / 0.4f) * MathUtil.Clamp01(speed / 6f + 0.3f) : 0f;
            Drive(_dust[i], at, dust);
            Drive(_smoke[i], at + Vector3.Up * 0.15f, smoke);
        }
    }

    private static void Drive(GpuParticles3D particles, Vector3 at, float amount)
    {
        particles.GlobalPosition = at;
        particles.AmountRatio = Mathf.Max(amount, 0.01f);
        particles.Emitting = amount > 0.02f;
    }
}
