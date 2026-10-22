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

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 72f, Near = 0.15f, Far = 3000f, Current = true };
        AddChild(_camera);
    }

    public void FollowCar(Transform3D car, float speedMps, float dt)
    {
        if (Mode == CameraMode.Cockpit)
        {
            // Driver's eye: left seat, 1.02 m above the road and right at the windscreen base, as the cabinet
            // framed it, so the bonnet and its lamp lids fill the bottom of the picture; a touch of forward
            // lean at speed, and the view aimed 3 degrees down.
            var lean = MathUtil.Clamp01(speedMps / 60f) * 0.06f;
            var eye = car * new Vector3(-0.38f, 1.02f - CgHeight, -0.25f - lean);
            var look = car * new Vector3(-0.38f, 0.30f - CgHeight, -14f);
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(look - eye, car.Basis.Y), eye);
            _camera.Fov = 72f;
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
}
