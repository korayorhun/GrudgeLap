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

    public CameraMode Mode { get; set; } = CameraMode.Cockpit;
    /// <summary>Orbit view: yaw around the car (0 = from behind, 90 = from its right), pitch above the horizon, distance; degrees and metres.</summary>
    public Vector3 Orbit { get; set; } = new(35f, 18f, 7f);
    /// <summary>Height of the car node's origin (its centre of gravity) above the road at rest; offsets below are ground-relative.</summary>
    public float CgHeight { get; set; } = 0.61f;
    /// <summary>0 on asphalt, 1 on grass: how much the cockpit camera jitters.</summary>
    public float Roughness { get; set; }
    /// <summary>The driver's eyes in the car node's space (<see cref="CarView.Eye"/>).</summary>
    public Vector3 Eye { get; set; } = new(-0.38f, 0.39f, -0.05f);
    /// <summary>The car's lateral and longitudinal acceleration, m/s², which the driver's head lags.</summary>
    public Vector2 Acceleration { get; set; }
    private readonly Random _rng = new(3);
    private Vector3 _head;

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 72f, Near = 0.15f, Far = 3000f, Current = true };
        AddChild(_camera);
    }

    public void FollowCar(Transform3D car, float speedMps, float dt)
    {
        if (Mode == CameraMode.Cockpit)
        {
            // Driver's eyes over the seat (the car says where), the view aimed 3 degrees down; a touch of forward
            // lean at speed.
            var lean = MathUtil.Clamp01(speedMps / 60f) * 0.06f;
            // Road feel: a few millimetres of jitter that grow with speed and on rough ground, a touch more field of view at speed.
            var jitter = (0.0012f + 0.0035f * MathUtil.Clamp01(speedMps / 60f) + 0.012f * Roughness) * MathUtil.Clamp01(speedMps / 4f);
            var shake = new Vector3(Jitter() * jitter, Jitter() * jitter, 0f);
            // The head on the neck lags the car: it sways out of a corner and nods forward under braking by a few
            // centimetres, then settles; the cabin seems to move around the eyes the way the 1989 cabinet slid its
            // painted dashboard (US 5,005,148).
            var headTarget = new Vector3(
                Mathf.Clamp(-Acceleration.X * 0.0035f, -0.045f, 0.045f), 0f,
                Mathf.Clamp(Acceleration.Y * 0.0028f, -0.04f, 0.03f));
            _head += (headTarget - _head) * MathUtil.Clamp01(dt * 6f);
            var eye = car * (Eye + _head + new Vector3(0f, 0f, -lean) + shake);
            var look = car * (Eye + new Vector3(_head.X * 0.5f, -0.70f, -14f));
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(look - eye, car.Basis.Y), eye);
            // 62 degrees vertical is about what a windscreen frames; a little more at speed.
            _camera.Fov = 62f + 6f * MathUtil.Clamp01(speedMps / 75f);
            return;
        }

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

        var target = car * new Vector3(0f, 1.9f - CgHeight, 6.3f);
        if (!_chaseInitialised)
        {
            _chasePosition = target;
            _chaseInitialised = true;
        }
        _chasePosition = _chasePosition.Lerp(target, MathUtil.Clamp01(dt * 6f));
        var focus = car * new Vector3(0f, 0.7f - CgHeight, -2.0f);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(focus - _chasePosition, Vector3.Up), _chasePosition);
        _camera.Fov = 62f;
    }

    /// <summary>Replay: a high crane shot that keeps the crash car centred, like the "God's eye" view of the original.</summary>
    public void Replay(Vector3 carPosition, Vector3 carForward)
    {
        var back = (carForward * -1f).Normalized();
        var eye = carPosition + back * 26f + Vector3.Up * 16f + new Vector3(back.Z, 0f, -back.X) * 10f;
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(carPosition + Vector3.Up * 0.8f - eye, Vector3.Up), eye);
        _camera.Fov = 50f;
    }

    public void ResetChase() => _chaseInitialised = false;

    private float Jitter() => (float)(_rng.NextDouble() * 2.0 - 1.0);
}
