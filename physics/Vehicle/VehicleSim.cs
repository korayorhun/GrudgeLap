using System.Numerics;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Rigid-body car on four spring/damper "raycast" wheels. Each wheel probes the track for the surface
/// below it, so the same code drives on flat road, a banked curve, the inside of the loop and the grass.
/// Runs at a fixed 240 Hz internally regardless of the caller's frame rate.
/// </summary>
public sealed class VehicleSim
{
    public const float FixedStep = 1f / 240f;
    private const float AirDensity = 1.2f;

    private readonly VehicleSpec _spec;
    private float _accumulator;

    public VehicleSim(VehicleSpec spec, TrackGeometry track)
    {
        _spec = spec;
        Track = track;
        Drivetrain = new Drivetrain(spec);
        Wheels =
        [
            new WheelState(new Vector3(-spec.HalfTrack, spec.HubHeight, spec.FrontAxle), true),
            new WheelState(new Vector3(spec.HalfTrack, spec.HubHeight, spec.FrontAxle), true),
            new WheelState(new Vector3(-spec.HalfTrack, spec.HubHeight, -spec.RearAxle), false),
            new WheelState(new Vector3(spec.HalfTrack, spec.HubHeight, -spec.RearAxle), false),
        ];
        Reset(track.StartFrame, track.StartCursor);
    }

    public VehicleSpec Spec => _spec;
    public TrackGeometry Track { get; }
    public Drivetrain Drivetrain { get; }
    public WheelState[] Wheels { get; }

    public Vector3 Position { get; private set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public Vector3 Velocity { get; private set; }
    /// <summary>World-space angular velocity, rad/s.</summary>
    public Vector3 AngularVelocity { get; private set; }
    /// <summary>Road-wheel steering angle in radians, positive to the right.</summary>
    public float SteerAngle { get; private set; }
    public PathCursor Cursor;
    /// <summary>Probe at the centre of gravity during the last step.</summary>
    public SurfaceHit BodyHit { get; private set; }
    public bool Airborne { get; private set; }
    public float AirborneTime { get; private set; }
    /// <summary>Largest normal approach speed (m/s) at which a wheel regained contact during the last step; zero otherwise.</summary>
    public float LandingImpact { get; private set; }
    /// <summary>True when the car body itself is about to touch the surface (landing on the roof or side).</summary>
    public bool BodyGrounded { get; private set; }
    /// <summary>Lateral acceleration felt by the driver, m/s^2, positive to the right. Used by the scrolling dashboard.</summary>
    public float LateralAcceleration { get; private set; }
    public float LongitudinalAcceleration { get; private set; }

    public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Orientation);
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);
    public float Speed => Vector3.Dot(Velocity, Forward);
    public float SpeedMph => MathF.Abs(Speed) * MathUtil.MphPerMps;
    public bool OnRoad => BodyHit.Surface is SurfaceType.Road or SurfaceType.Pad;

    /// <summary>
    /// Road-wheel lock (radians) available at a given speed. The lock shrinks with speed, which is the
    /// keyboard-friendly stand-in for the fine inputs a three-turn wheel allows at high speed.
    /// </summary>
    public float MaxSteerAngleAt(float speed) =>
        MathUtil.Lerp(_spec.MaxSteerLowSpeedDeg, _spec.MaxSteerHighSpeedDeg,
            MathUtil.Clamp01(MathF.Abs(speed) / _spec.SteerSpeedBlendMps)) * MathUtil.Deg2Rad;

    /// <summary>Places the car at rest on a road frame, facing along it, engine off.</summary>
    public void Reset(in RoadFrame frame, PathCursor cursor)
    {
        var forward = frame.Tangent;
        var up = frame.Normal;
        var right = Vector3.Cross(up, forward);
        var m = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        Orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        // Static ride height: hubs sit at rest-length minus the sag of a quarter of the mass.
        var sag = _spec.MassKg * MathUtil.Gravity / 4f / _spec.SuspensionStiffness;
        var hubHeight = _spec.SuspensionRest + _spec.WheelRadius - sag;
        Position = frame.Origin + up * (hubHeight - _spec.HubHeight);
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        SteerAngle = 0f;
        Cursor = cursor;
        Airborne = false;
        AirborneTime = 0f;
        LandingImpact = 0f;
        BodyGrounded = false;
        LateralAcceleration = 0f;
        LongitudinalAcceleration = 0f;
        _accumulator = 0f;
        Drivetrain.Reset();
        foreach (var w in Wheels) w.Reset();
        BodyHit = Track.Probe(Position, ref Cursor, true);
    }

    /// <summary>
    /// Test/debug helper: puts the car in motion at <paramref name="speedMps"/> from its current pose, engine
    /// running and clutch locked in a sensible gear, optionally shifted sideways by <paramref name="lateralOffset"/> metres.
    /// </summary>
    public void LaunchAt(float speedMps, float lateralOffset = 0f)
    {
        Position += Right * lateralOffset;
        Velocity = Forward * speedMps;
        var wheelSpeed = speedMps / _spec.WheelRadius;
        foreach (var w in Wheels) w.AngularVelocity = wheelSpeed;

        // Highest gear that keeps the engine in its working band; falls back to top gear.
        var gear = _spec.ForwardGearCount;
        for (var g = 1; g <= _spec.ForwardGearCount; g++)
        {
            var rpm = wheelSpeed * _spec.RatioForGear(g) * _spec.FinalDrive * EngineModel.RpmPerRadPerSec;
            if (rpm <= _spec.Engine.RedlineRpm * 0.87f) { gear = g; break; }
        }
        Drivetrain.ForceLocked(gear, wheelSpeed * _spec.RatioForGear(gear) * _spec.FinalDrive);
        BodyHit = Track.Probe(Position, ref Cursor, true);
    }

    /// <summary>Advances the simulation by <paramref name="dt"/> seconds using fixed sub-steps.</summary>
    public void Step(in DriverInputs input, float dt)
    {
        _accumulator += MathF.Min(dt, 0.1f);
        LandingImpact = 0f;
        while (_accumulator >= FixedStep)
        {
            SubStep(input, FixedStep);
            _accumulator -= FixedStep;
        }
    }

    private void SubStep(in DriverInputs input, float dt)
    {
        var forward = Forward;
        var up = Up;
        var right = Right;
        var speed = Vector3.Dot(Velocity, forward);

        SteerAngle = MathUtil.Clamp(input.Steer, -1f, 1f) * MaxSteerAngleAt(speed);

        // Drivetrain first: it needs the rear axle speed from the previous step. Traction control, where the car
        // has it, eases the throttle as the driven wheels spin up (slip ratio from the previous step).
        var axleSpeed = 0.5f * (Wheels[2].AngularVelocity + Wheels[3].AngularVelocity);
        var drive = input;
        if (_spec.TractionControl && Drivetrain.Gear > 0 && input.Throttle > 0f)
        {
            var rearSlip = MathF.Max(Wheels[2].SlipRatio, Wheels[3].SlipRatio);
            drive.Throttle = input.Throttle * (1f - MathUtil.Clamp01((rearSlip - 0.12f) / 0.25f));
        }
        Drivetrain.Step(drive, axleSpeed, speed, dt);

        var force = new Vector3(0f, -_spec.MassKg * MathUtil.Gravity, 0f);
        var torque = Vector3.Zero;

        // Aerodynamic drag.
        var vLen = Velocity.Length();
        if (vLen > 0.01f)
        {
            force -= Velocity * (0.5f * AirDensity * _spec.DragCoefficient * _spec.FrontalArea * vLen);
        }

        var anyContact = false;
        var rayDir = -up;
        var maxLen = _spec.SuspensionRest + _spec.WheelRadius;

        for (var i = 0; i < Wheels.Length; i++)
        {
            var w = Wheels[i];
            var hub = Position + Vector3.Transform(w.LocalPosition, Orientation);
            var probeCursor = Cursor;
            var hit = Track.Probe(hub, ref probeCursor, false);
            var wasInContact = w.InContact;
            w.InContact = false;
            w.Surface = SurfaceType.None;
            w.Load = 0f;

            var steer = w.IsFront ? SteerAngle : 0f;
            // Rotating +Z about +Y by a positive angle swings it toward +X, i.e. a positive steer turns the wheel right.
            var wheelForward = MathUtil.RotateAround(forward, up, steer);

            if (hit.HasSurface)
            {
                var n = hit.SurfaceNormal;
                var facing = Vector3.Dot(n, rayDir);
                if (facing < -0.2f)
                {
                    var t = Vector3.Dot(n, hit.SurfacePoint - hub) / facing;
                    if (t > 0f && t < maxLen)
                    {
                        w.InContact = true;
                        w.Surface = hit.Surface;
                        anyContact = true;
                        var contact = hub + rayDir * t;
                        var r = contact - Position;
                        var vContact = Velocity + Vector3.Cross(AngularVelocity, r);

                        // Suspension: spring + damper + bump stop, pushing only.
                        var compression = maxLen - t;
                        var approach = -Vector3.Dot(vContact, n);
                        var fs = _spec.SuspensionStiffness * compression + _spec.SuspensionDamping * approach;
                        if (compression > _spec.MaxCompression)
                        {
                            fs += _spec.BumpStopStiffness * (compression - _spec.MaxCompression);
                        }
                        fs = MathF.Max(fs, 0f);
                        w.Load = fs;
                        w.Compression = compression;
                        force += n * fs;
                        torque += Vector3.Cross(r, n * fs);

                        if (!wasInContact)
                        {
                            LandingImpact = MathF.Max(LandingImpact, approach);
                        }

                        // Tire forces in the contact plane.
                        var fwd = MathUtil.SafeNormalize(MathUtil.ProjectOnPlane(wheelForward, n), forward);
                        var side = Vector3.Cross(n, fwd);
                        var vLong = Vector3.Dot(vContact, fwd);
                        var vLat = Vector3.Dot(vContact, side);
                        var surfaceSpeed = w.AngularVelocity * _spec.WheelRadius;
                        var targetSlipRatio = TireModel.SlipRatio(surfaceSpeed, vLong);
                        var targetSlipAngle = TireModel.SlipAngle(vLat, vLong);
                        // Tire relaxation: the contact patch needs about a third of a metre of rolling to build up
                        // force. Without it the wheel spin oscillates against a stiff instantaneous tire and bleeds energy.
                        var relax = MathUtil.Clamp01(dt * MathF.Max(MathF.Abs(vLong), 1.5f) / 0.3f);
                        w.SlipRatio += (targetSlipRatio - w.SlipRatio) * relax;
                        w.SlipAngle += (targetSlipAngle - w.SlipAngle) * relax;
                        var (fx, fy) = TireModel.Forces(w.SlipRatio, w.SlipAngle, fs, TireModel.FrictionFor(hit.Surface) * _spec.TireGrip);

                        // Rolling resistance (much higher on grass: the original's off-road "mud" feel) acts on the body
                        // only. Feeding it back into the wheel spin would just make the wheel wind up to cancel it.
                        var rolling = (hit.Surface == SurfaceType.Grass ? 0.16f : _spec.RollingResistance) * fs;
                        var drag = MathF.Sign(vLong) * MathF.Min(rolling, MathF.Abs(vLong) * 400f);

                        var tireForce = fwd * (fx - drag) + side * fy;
                        force += tireForce;
                        torque += Vector3.Cross(r, tireForce);
                        w.LongitudinalForce = fx;
                        w.LateralForce = fy;
                        w.ContactPoint = contact;
                        w.ContactNormal = n;
                    }
                }
            }

            IntegrateWheelSpin(w, input, dt);
        }

        // Body touching the ground means a roll-over or a landing on the roof.
        BodyHit = Track.Probe(Position, ref Cursor, true);
        BodyGrounded = BodyHit.HasSurface && BodyHit.Height < 0.28f && Vector3.Dot(up, BodyHit.SurfaceNormal) < 0.55f;

        // Integrate the rigid body (semi-implicit Euler, body-frame inertia).
        var acceleration = force / _spec.MassKg;
        Velocity += acceleration * dt;
        Position += Velocity * dt;

        var inv = Quaternion.Inverse(Orientation);
        var omegaBody = Vector3.Transform(AngularVelocity, inv);
        var torqueBody = Vector3.Transform(torque, inv);
        var inertia = new Vector3(_spec.InertiaPitch, _spec.InertiaYaw, _spec.InertiaRoll);
        var angularMomentum = omegaBody * inertia;
        var gyro = Vector3.Cross(omegaBody, angularMomentum);
        var alphaBody = (torqueBody - gyro) / inertia;
        omegaBody += alphaBody * dt;
        if (!anyContact)
        {
            // A little aerodynamic damping in the air keeps a launched car from spinning forever.
            omegaBody *= 1f - 0.4f * dt;
        }
        AngularVelocity = Vector3.Transform(omegaBody, Orientation);

        var omegaQuat = new Quaternion(AngularVelocity.X, AngularVelocity.Y, AngularVelocity.Z, 0f);
        var dq = Quaternion.Multiply(omegaQuat, Orientation);
        Orientation = Quaternion.Normalize(new Quaternion(
            Orientation.X + 0.5f * dt * dq.X,
            Orientation.Y + 0.5f * dt * dq.Y,
            Orientation.Z + 0.5f * dt * dq.Z,
            Orientation.W + 0.5f * dt * dq.W));

        var accelNoGravity = acceleration + new Vector3(0f, MathUtil.Gravity, 0f);
        LateralAcceleration = Vector3.Dot(accelNoGravity, right);
        LongitudinalAcceleration = Vector3.Dot(accelNoGravity, forward);

        Airborne = !anyContact;
        AirborneTime = Airborne ? AirborneTime + dt : 0f;
    }

    private void IntegrateWheelSpin(WheelState w, in DriverInputs input, float dt)
    {
        var inertia = _spec.WheelInertia;
        var driveTorque = 0f;
        if (!w.IsFront)
        {
            driveTorque = Drivetrain.AxleTorque * 0.5f;
            inertia += Drivetrain.ReflectedInertia * 0.5f;
        }
        var brakeMax = (w.IsFront ? _spec.FrontBrakeTorque : _spec.RearBrakeTorque) * MathUtil.Clamp01(input.Brake);
        var reaction = w.InContact ? -w.LongitudinalForce * _spec.WheelRadius : 0f;
        var net = driveTorque + reaction;

        // Brakes as Coulomb friction: they can hold the wheel still but never reverse it.
        var brake = MathF.Min(brakeMax, MathF.Abs(net) + MathF.Abs(w.AngularVelocity) * inertia / dt);
        net -= MathF.Sign(w.AngularVelocity != 0f ? w.AngularVelocity : net) * brake;

        if (!w.InContact) net -= 0.05f * w.AngularVelocity; // bearing drag in the air

        var next = w.AngularVelocity + net / inertia * dt;
        if (brakeMax > 0f && MathF.Sign(next) != MathF.Sign(w.AngularVelocity) && w.AngularVelocity != 0f) next = 0f;
        w.AngularVelocity = next;
        w.SpinAngle += w.AngularVelocity * dt;
    }
}

public sealed class WheelState
{
    public WheelState(Vector3 localPosition, bool isFront)
    {
        LocalPosition = localPosition;
        IsFront = isFront;
    }

    public Vector3 LocalPosition { get; }
    public bool IsFront { get; }
    public float AngularVelocity;   // rad/s, positive rolling forward
    public float SpinAngle;         // accumulated, for rendering
    public bool InContact;
    public SurfaceType Surface;
    public float Load;
    public float Compression;
    public float SlipRatio;
    public float SlipAngle;
    public float LongitudinalForce;
    public float LateralForce;
    public Vector3 ContactPoint;
    public Vector3 ContactNormal;

    public void Reset()
    {
        AngularVelocity = 0f;
        SpinAngle = 0f;
        InContact = false;
        Surface = SurfaceType.None;
        Load = 0f;
        Compression = 0f;
        SlipRatio = 0f;
        SlipAngle = 0f;
        LongitudinalForce = 0f;
        LateralForce = 0f;
    }

    /// <summary>Slip magnitude used for skid audio and marks: 0 = gripping, 1 = fully sliding.</summary>
    public float SlipIntensity => MathUtil.Clamp01(MathF.Max(MathF.Abs(SlipRatio) * 2.5f, MathF.Abs(SlipAngle) * 4f) - 0.25f);
}
