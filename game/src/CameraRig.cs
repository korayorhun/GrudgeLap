using Godot;
using GrudgeLap.Physics;
using SN = System.Numerics;

namespace GrudgeLap.Game;

internal enum CameraMode
{
    Cockpit,
    Chase,
    /// <summary>Fixed view of the car from a given angle; for screenshots of the car itself (<c>--orbit</c>).</summary>
    Orbit,
}

/// <summary>
/// Cockpit view by default (the cabinet had no other), a chase view on request, and an aerial view for the
/// instant replay. The cockpit camera sits where the driver's head would be and rolls with the car.
/// </summary>
internal sealed partial class CameraRig : Node3D
{
    private Camera3D _camera = null!;
    private Vector3 _chasePosition;
    private bool _chaseInitialised;
    private GpuParticles3D? _rain;
    private Windscreen? _windscreen;

    public CameraMode Mode { get; set; } = CameraMode.Cockpit;
    /// <summary>The ground's height under a point (Godot space), for keeping the eye out of it (0.35).</summary>
    public Func<Vector3, float>? Ground { get; set; }
    /// <summary>The seat's road vibration (automation can switch it off: --shake=0).</summary>
    public bool Shake { get; set; } = true;
    /// <summary>Orbit view: yaw around the car (0 = from behind, 90 = from its right), pitch above the horizon, distance; degrees and metres.</summary>
    public Vector3 Orbit { get; set; } = new(35f, 18f, 7f);
    /// <summary>Height of the car node's origin (its centre of gravity) above the road at rest; offsets below are ground-relative.</summary>
    public float CgHeight { get; set; } = 0.61f;
    /// <summary>0 on asphalt, 1 on grass: how much the cockpit camera jitters.</summary>
    public float Roughness { get; set; }
    /// <summary>The driver's eyes in the car node's space (<see cref="CarView.Eye"/>).</summary>
    public Vector3 Eye { get; set; } = new(-0.38f, 0.39f, -0.05f);
    /// <summary>Automation: the cockpit view turned by yaw (left positive) and pitch (up positive), degrees (<c>--seat-look=</c>).</summary>
    public Vector2 SeatLook { get; set; }
    /// <summary>The car's lateral and longitudinal acceleration, m/s², which the driver's head lags.</summary>
    public Vector2 Acceleration { get; set; }
    private readonly Random _rng = new(3);
    // The seat's vibration (0.36): smooth noise at about 11 Hz, not a fresh random offset every frame, which shook the
    // whole cabin by a few pixels a frame and read as trembling shadows (the owner's 7 November report).
    private readonly FastNoiseLite _vibration = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f, Seed = 3 };
    private float _vibrationTime;
    private MotionBlur? _blur;
    private Vector3 _head;
    // A knock's jolt: an offset of the eye in the car's frame on a stiff, well-damped spring (Jolt).
    private Vector3 _jolt;
    private Vector3 _joltVelocity;

    /// <summary>
    /// A knock felt in the seat: the head is thrown along <paramref name="push"/> (the car's frame, Godot axes) by a
    /// few centimetres for a hard one, and springs back within half a second.
    /// </summary>
    public void Jolt(Vector3 push, float speed)
    {
        if (push.LengthSquared() < 1e-6f) return;
        _joltVelocity += push.Normalized() * Mathf.Clamp(speed * 0.06f, 0.04f, 0.9f);
    }

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 72f, Near = 0.15f, Far = 65000f, Current = true };   // the real land and its skirt reach 60 km (0.37; 9 km for the old mountains)
        // The motion blur (0.38) as the camera's compositor effect; switched with the quality and the 1989 look each frame.
        _blur = new MotionBlur { Enabled = WorldBuilder.CameraBlur };
        _camera.Compositor = new Compositor { CompositorEffects = [_blur] };
        AddChild(_camera);
        // The glass between the driver and the road (0.46): drops and wipers in the rain, the dust's haze in the sun.
        _windscreen = new Windscreen();
        _camera.AddChild(_windscreen);
    }

    public override void _Process(double delta)
    {
        if (_blur is null) return;
        _blur.FrameSeconds = (float)delta;
        _blur.Enabled = WorldBuilder.CameraBlur;
        LeadRain((float)delta);
    }

    /// <summary>Where the rain's box hangs from the camera when it stands still: 10 m over it, 5 m ahead.</summary>
    private static readonly Vector3 RainBox = new(0f, 10f, -5f);

    private Vector3 _rainEye;
    private Vector3 _rainVelocity;
    private bool _rainEyeKnown;

    /// <summary>
    /// The rain's box ahead of the camera by where it will be when the drops reach the eye (0.65). The drops fall in the
    /// world, so at speed those down at the eye's height and the beams' had been let go a second earlier where the car
    /// has since left: ahead of a car at 30 m/s there was only the fresh rain high up, from the seat the air looked dry
    /// by day, and at night the beams lit nothing. The camera's velocity, smoothed over a fifth of a second (a jump of
    /// the view, a cut or a reset, is not taken for one), times the 1.2 s a drop takes from the box to below the eye.
    /// </summary>
    private void LeadRain(float dt)
    {
        if (_rain is null || !_rain.Emitting || dt <= 0f) return;
        var eye = _camera.GlobalPosition;
        if (_rainEyeKnown)
        {
            var velocity = (eye - _rainEye) / dt;
            if (velocity.LengthSquared() < 120f * 120f) _rainVelocity = _rainVelocity.Lerp(velocity, MathF.Min(dt / 0.2f, 1f));
        }
        _rainEye = eye;
        _rainEyeKnown = true;
        _rain.Position = RainBox + _camera.GlobalBasis.Inverse() * (_rainVelocity * 1.2f);
    }

    /// <summary>
    /// Rain in the air (0.12): a box of falling streaks that travels with the camera, 32 m across and 10 m above it,
    /// the drops in world space so they fall straight past a moving car. Thin vertical quads turned to the camera
    /// about their own axis (a billboard fixed on Y), faint, a few thousand at a time, and none right at the eye. Lit
    /// since 0.65: by day the grey sky's light, at night only what reaches them, the car's beams ahead, an oncoming
    /// car's lamps through them from behind, a yard lamp, a flash; unlit, they stood white in the dark.
    /// </summary>
    public void SetRain(bool raining)
    {
        if (!raining)
        {
            if (_rain is not null) _rain.Emitting = false;
            return;
        }
        if (_rain is null)
        {
            var process = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(16f, 0.5f, 16f),
                Direction = new Vector3(0f, -1f, 0f),
                Spread = 1.5f,
                InitialVelocityMin = 9f,
                InitialVelocityMax = 11f,
                Gravity = new Vector3(0.7f, -1.5f, 0f),
            };
            var streak = new QuadMesh
            {
                Size = new Vector2(0.014f, 0.42f),
                // The rain's own shader (0.65, assets/shaders/rain.gdshader): the standard material's fixed-Y billboard and its
                // fade within a metre and a bit of the eye to whole at four and a half (0.53: a drop half a metre away is a
                // streak thirty pixels wide, which no eye or camera sees in focus, and from the seat the drops fell inside the
                // cabin), lit, and sending a lamp's light back towards it as a drop does.
                Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/rain.gdshader") },
            };
            _rain = new GpuParticles3D
            {
                Name = "Rain",
                Amount = 3600,
                Lifetime = 1.7f,
                ProcessMaterial = process,
                DrawPass1 = streak,
                VisibilityAabb = new Aabb(new Vector3(-40f, -40f, -40f), new Vector3(80f, 80f, 80f)),
                Position = RainBox,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _camera.AddChild(_rain);
        }
        _rain.Emitting = true;
    }

    /// <summary>The water on the windscreen (0.46): how hard it rains (0 to 1) and, on a wet day without rain, what is left on the glass.</summary>
    public void SetGlass(float rain, float wet)
    {
        if (_windscreen is null) return;
        _windscreen.Rain = rain;
        _windscreen.Wet = wet;
    }

    /// <summary>The wipers on the glass before the driver, for their sound (0.54, <see cref="Windscreen.Wipers"/>).</summary>
    public (bool On, float Clock, float Period) Wipers => _windscreen?.Wipers ?? (false, 0f, Windscreen.WiperSweep);

    public void FollowCar(Transform3D car, float speedMps, float dt)
    {
        // The jolt's spring, sub-stepped: at 20 fps (1989 mode) one step of it would overshoot.
        for (var left = dt; left > 0f; left -= 1f / 120f)
        {
            var h = MathF.Min(left, 1f / 120f);
            _joltVelocity += (-_jolt * 160f - _joltVelocity * 16f) * h;
            _jolt += _joltVelocity * h;
        }
        if (Mode == CameraMode.Cockpit)
        {
            // Driver's eyes over the seat (the car says where), the view aimed 3 degrees down; a touch of forward
            // lean at speed.
            var lean = MathUtil.Clamp01(speedMps / 60f) * 0.06f;
            // Road feel: a few millimetres of jitter that grow with speed and on rough ground, a touch more field of view at speed.
            var jitter = (0.0006f + 0.0018f * MathUtil.Clamp01(speedMps / 60f) + 0.010f * Roughness) * MathUtil.Clamp01(speedMps / 4f);
            _vibrationTime += dt;
            var shake = Shake ? new Vector3(_vibration.GetNoise1D(_vibrationTime * 11f), _vibration.GetNoise1D(_vibrationTime * 11f + 57f), 0f) * jitter : Vector3.Zero;
            // The head on the neck lags the car: it sways out of a corner and nods forward under braking by a few
            // centimetres, then settles; the cabin seems to move around the eyes the way the 1989 cabinet slid its
            // painted dashboard (US 5,005,148).
            var headTarget = new Vector3(
                Mathf.Clamp(-Acceleration.X * 0.0035f, -0.045f, 0.045f), 0f,
                Mathf.Clamp(Acceleration.Y * 0.0028f, -0.04f, 0.03f));
            _head += (headTarget - _head) * MathUtil.Clamp01(dt * 6f);
            var eye = car * (Eye + _head + _jolt + new Vector3(0f, 0f, -lean) + shake);
            var ahead = new Vector3(_head.X * 0.5f, -0.70f, -14f);
            if (SeatLook != Vector2.Zero) ahead = new Basis(Vector3.Up, Mathf.DegToRad(SeatLook.X)) * new Basis(Vector3.Right, Mathf.DegToRad(SeatLook.Y)) * ahead;
            var look = car * (Eye + ahead);
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(look - eye, car.Basis.Y), eye);
            // 62 degrees vertical is about what a windscreen frames; a little more at speed.
            _camera.Fov = 62f + 6f * MathUtil.Clamp01(speedMps / 75f);
            _windscreen?.Update(!WorldBuilder.Retro, speedMps, _camera.GlobalBasis.Inverse() * WorldBuilder.TowardsSun, WorldBuilder.SunShare, dt);
            return;
        }
        // Outside the car there is no glass before the eye.
        if (_windscreen is not null) _windscreen.Visible = false;

        if (Mode == CameraMode.Orbit)
        {
            var yaw = Mathf.DegToRad(Orbit.X);
            var pitch = Mathf.DegToRad(Orbit.Y);
            var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * Orbit.Z;
            var centre = car.Origin + Vector3.Up * (0.6f - CgHeight);
            var eye = centre + car.Basis * offset;
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(centre - eye, Vector3.Up), eye);
            _camera.Fov = 40f;
            return;
        }

        // A little further back and lower since 0.11, with the longer lens below, so the car fills the frame as before.
        // On the loop's wall and over its top the eye moves in towards the loop's centre with the tilt (0.35: straight back
        // along the car's axis it sat inside the deck for the whole climb).
        var carUp = car.Basis.Y;
        var tilt = 1f - MathF.Max(carUp.Dot(Vector3.Up), -1f);
        var target = car * new Vector3(0f, 1.7f - CgHeight + 2.2f * tilt, 7.4f);
        if (!_chaseInitialised)
        {
            _chasePosition = target;
            _chaseInitialised = true;
        }
        _chasePosition = _chasePosition.Lerp(target, MathUtil.Clamp01(dt * 6f));
        var focus = car * new Vector3(0f, 0.7f - CgHeight, -2.0f);
        var chase = _chasePosition + car.Basis * (_jolt * 2.5f);
        // The car's own up is the view's up once the road tilts past 50 degrees, and the eye keeps clear of the ground.
        var up = carUp.Dot(Vector3.Up) < 0.64f ? carUp : Vector3.Up;
        if (Ground is { } ground) chase.Y = MathF.Max(chase.Y, ground(chase) + 0.9f);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(focus - chase, up), chase);
        // A longer lens than the seat's: at 62 degrees the car outside was drawn in a fish-eye and read as a toy (0.11).
        _camera.Fov = 54f;
    }

    /// <summary>
    /// Replay: a high crane shot that keeps the crash car centred, like the "God's eye" view of the original, coming
    /// down to a few metres off the car for the crash itself (<paramref name="closeUp"/> 1, CrashScene).
    /// </summary>
    public void Replay(Vector3 carPosition, Vector3 viewForward, float closeUp = 0f, bool low = false)
    {
        var back = (viewForward * -1f).Normalized();
        var reach = Mathf.Lerp(1f, low ? 0.45f : 0.3f, closeUp);
        // Close by, the eye is 3.2 m up; for a car that fell under a bridge it stays low (1.6 m, a little further back),
        // under the deck it would otherwise look through (0.35).
        var eye = carPosition + back * (26f * reach) + Vector3.Up * Mathf.Lerp(16f, low ? 1.6f : 3.2f, closeUp) + new Vector3(back.Z, 0f, -back.X) * (10f * reach);
        if (Ground is { } ground) eye.Y = MathF.Max(eye.Y, ground(eye) + 1.5f);   // never under the ground beside a cutting (0.35)
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(carPosition + Vector3.Up * 0.8f - eye, Vector3.Up), eye);
        _camera.Fov = Mathf.Lerp(50f, 56f, closeUp);
        if (_windscreen is not null) _windscreen.Visible = false;
    }

    public void ResetChase() => _chaseInitialised = false;

    /// <summary>Points the camera from <paramref name="eye"/> at <paramref name="target"/> (the --look= inspection shots).</summary>
    public void Look(Vector3 eye, Vector3 target, float fov)
    {
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(target - eye, Vector3.Up), eye);
        _camera.Fov = fov;
        if (_windscreen is not null) _windscreen.Visible = false;
    }

    /// <summary>Where the camera is: the listener of the placed sounds (<see cref="WorldAudio"/>).</summary>
    public Vector3 Listener => _camera.GlobalPosition;

    private float Jitter() => (float)(_rng.NextDouble() * 2.0 - 1.0);
}
