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
    private const float LateralRelaxationLength = 0.3f;     // m

    private readonly VehicleSpec _spec;
    private readonly WheelContact[] _contacts = new WheelContact[4];
    /// <summary>Spring and damper rate of each wheel: the spec's rates scaled by the wheel's share of the static load.</summary>
    private readonly float[] _springRate = new float[4];
    private readonly float[] _damperRate = new float[4];
    private Vector3 _centreOfMass;
    private Vector3 _massVelocity;
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
        // Each axle is sprung for its share of the weight, as cars are, so a car whose mass sits towards one end still
        // rides level and both ends ride at the same frequency; the spec's rates are those of an evenly loaded corner.
        var wheelbase = spec.FrontAxle + spec.RearAxle;
        var frontShare = (spec.RearAxle + spec.CentreOfMassOffset.Z) / wheelbase;
        for (var i = 0; i < 4; i++)
        {
            var share = (i < 2 ? frontShare : 1f - frontShare) * 2f;
            _springRate[i] = spec.SuspensionStiffness * share;
            _damperRate[i] = spec.SuspensionDamping * share;
        }
        Reset(track.StartFrame, track.StartCursor);
    }

    public VehicleSpec Spec => _spec;
    public TrackGeometry Track { get; }
    public Drivetrain Drivetrain { get; }
    public WheelState[] Wheels { get; }

    /// <summary>
    /// The body's reference point (world): where the view and the rules place the car, with the hubs, the body's
    /// footprint and the probe all measured from it. The mass sits at <see cref="CentreOfMass"/>, which the spec may put
    /// lower or further back (see <see cref="VehicleSpec.CentreOfMassOffset"/>).
    /// </summary>
    public Vector3 Position
    {
        get => _centreOfMass - Vector3.Transform(_spec.CentreOfMassOffset, Orientation);
        private set => _centreOfMass = value + Vector3.Transform(_spec.CentreOfMassOffset, Orientation);
    }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    /// <summary>Velocity of the body's reference point (<see cref="Position"/>), world space.</summary>
    public Vector3 Velocity
    {
        get => _massVelocity + Vector3.Cross(AngularVelocity, Position - _centreOfMass);
        private set => _massVelocity = value - Vector3.Cross(AngularVelocity, Position - _centreOfMass);
    }
    /// <summary>Where the car's mass is centred (world): every force and torque of the rigid body is taken about it.</summary>
    public Vector3 CentreOfMass => _centreOfMass;
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
    /// <summary>The weather the car drives in: it scales every surface's grip (<see cref="WeatherModel.Grip"/>).</summary>
    public Weather Weather { get; set; } = Weather.Dry;

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
        AngularVelocity = Vector3.Zero;
        Position = frame.Origin + up * (hubHeight - _spec.HubHeight);
        Velocity = Vector3.Zero;
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
        AngularVelocity = Vector3.Zero;
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

    /// <summary>Velocity of a point fixed to the body (world space).</summary>
    public Vector3 VelocityAt(Vector3 point) => _massVelocity + Vector3.Cross(AngularVelocity, point - _centreOfMass);

    /// <summary>
    /// Inverse of the mass the body presents to an impulse along <paramref name="direction"/> at
    /// <paramref name="point"/>: 1/m plus the share that goes into turning the body about its centre of gravity.
    /// </summary>
    public float InverseMassAlong(Vector3 point, Vector3 direction)
    {
        var inv = Quaternion.Inverse(Orientation);
        var r = Vector3.Transform(point - _centreOfMass, inv);
        var n = Vector3.Transform(direction, inv);
        var turn = Vector3.Cross(r, n) / BodyInertia;
        return 1f / _spec.MassKg + Vector3.Dot(n, Vector3.Cross(turn, r));
    }

    /// <summary>
    /// A knock: an instantaneous impulse (N s) at a world point, from an obstacle the car ran into. It changes the
    /// velocity and, off the centre of gravity, the spin; the wheels catch up through the tyres over the next steps.
    /// </summary>
    public void ApplyImpulse(Vector3 point, Vector3 impulse)
    {
        _massVelocity += impulse / _spec.MassKg;
        var inv = Quaternion.Inverse(Orientation);
        var angular = Vector3.Transform(Vector3.Cross(point - _centreOfMass, impulse), inv) / BodyInertia;
        AngularVelocity += Vector3.Transform(angular, Orientation);
    }

    /// <summary>Moves the car without changing its motion: out of an obstacle it has sunk into.</summary>
    public void Displace(Vector3 offset) => _centreOfMass += offset;

    private Vector3 BodyInertia => new(_spec.InertiaPitch, _spec.InertiaYaw, _spec.InertiaRoll);

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
        var speed = Vector3.Dot(_massVelocity, forward);

        SteerAngle = MathUtil.Clamp(input.Steer, -1f, 1f) * MaxSteerAngleAt(speed);

        // Drivetrain first: it needs the driven axle's speed from the previous step (the rear wheels, 2 and 3, or on a
        // front-wheel-drive car the front ones, 0 and 1). Traction control, where the car has it, eases the throttle as
        // the driven wheels spin up (slip ratio from the previous step).
        var (driven0, driven1) = _spec.FrontWheelDrive ? (Wheels[0], Wheels[1]) : (Wheels[2], Wheels[3]);
        var axleSpeed = 0.5f * (driven0.AngularVelocity + driven1.AngularVelocity);
        UpdateTractionControl(driven0, driven1, input, dt);
        // The inertia the clutch sees over one step: the driven wheels and, through their tyres' stiffness, what the
        // road holds them to (the terms of SpinGroundedWheel's balance); and the torque the road holds them back with.
        var axleInertia = 2f * _spec.WheelInertia + (driven0.InContact ? driven0.SpinStiffness * dt : 0f) + (driven1.InContact ? driven1.SpinStiffness * dt : 0f);
        var axleLoad = ((driven0.InContact ? driven0.LongitudinalForce : 0f) + (driven1.InContact ? driven1.LongitudinalForce : 0f)) * _spec.WheelRadius;
        Drivetrain.Step(input, axleSpeed, speed, dt, axleInertia, axleLoad);

        var force = new Vector3(0f, -_spec.MassKg * MathUtil.Gravity, 0f);
        var torque = Vector3.Zero;

        // Aerodynamic drag.
        var vLen = _massVelocity.Length();
        if (vLen > 0.01f)
        {
            force -= _massVelocity * (0.5f * AirDensity * _spec.DragCoefficient * _spec.FrontalArea * vLen);
        }

        var anyContact = false;
        var rayDir = -up;
        var maxLen = _spec.SuspensionRest + _spec.WheelRadius;

        // First pass: where each wheel touches and how far its spring is compressed. The anti-roll bars need both
        // wheels of an axle before either wheel's load is known.
        for (var i = 0; i < Wheels.Length; i++)
        {
            var w = Wheels[i];
            ref var c = ref _contacts[i];
            c.Hub = Position + Vector3.Transform(w.LocalPosition, Orientation);
            var probeCursor = Cursor;
            c.Hit = Track.Probe(c.Hub, ref probeCursor, false);
            c.WasInContact = w.InContact;
            c.Compression = 0f;
            w.InContact = false;
            w.Surface = SurfaceType.None;
            w.Load = 0f;
            if (!c.Hit.HasSurface) continue;
            var facing = Vector3.Dot(c.Hit.SurfaceNormal, rayDir);
            if (facing >= -0.2f) continue;
            var t = Vector3.Dot(c.Hit.SurfaceNormal, c.Hit.SurfacePoint - c.Hub) / facing;
            if (t <= 0f || t >= maxLen) continue;
            w.InContact = true;
            w.Surface = c.Hit.Surface;
            anyContact = true;
            c.Ray = t;
            c.Compression = maxLen - t;
        }

        // Anti-roll bars: each axle's bar pushes its more compressed wheel down onto the road and lifts the other by the
        // same force. A wheel in the air hangs at full droop, so the bar then presses only the wheel on the ground.
        _contacts[0].Bar = _spec.FrontAntiRoll * (_contacts[0].Compression - _contacts[1].Compression);
        _contacts[1].Bar = -_contacts[0].Bar;
        _contacts[2].Bar = _spec.RearAntiRoll * (_contacts[2].Compression - _contacts[3].Compression);
        _contacts[3].Bar = -_contacts[2].Bar;

        // Torque the differential sends to each driven wheel: half the axle's each (an open differential), less or
        // plus what a limited-slip unit passes from the faster wheel to the slower one.
        var lsdTorque = LimitedSlipTransfer(dt);
        var pedal = MathUtil.Clamp01(input.Brake);

        for (var i = 0; i < Wheels.Length; i++)
        {
            var w = Wheels[i];
            ref var c = ref _contacts[i];
            var steer = w.IsFront ? SteerAngle : 0f;
            // Rotating +Z about +Y by a positive angle swings it toward +X, i.e. a positive steer turns the wheel right.
            var wheelForward = MathUtil.RotateAround(forward, up, steer);

            var driven = w.IsFront == _spec.FrontWheelDrive;
            var driveTorque = driven ? Drivetrain.AxleTorque * 0.5f + (w.LocalPosition.X < 0f ? -lsdTorque : lsdTorque) : 0f;
            var wheelInertia = _spec.WheelInertia + (driven ? Drivetrain.ReflectedInertia * 0.5f : 0f);
            UpdateAntiLock(w, pedal, dt);
            var brakeMax = (w.IsFront ? _spec.FrontBrakeTorque : _spec.RearBrakeTorque) * pedal * w.BrakePressure;

            if (!w.InContact)
            {
                SpinFreeWheel(w, driveTorque, wheelInertia, brakeMax, dt);
                continue;
            }

            var hit = c.Hit;
            var n = hit.SurfaceNormal;
            var contact = c.Hub + rayDir * c.Ray;
            var r = contact - _centreOfMass;
            var vContact = _massVelocity + Vector3.Cross(AngularVelocity, r);

            // Suspension: spring + damper + bump stop + anti-roll bar, pushing only.
            var compression = c.Compression;
            var approach = -Vector3.Dot(vContact, n);
            var fs = _springRate[i] * compression + _damperRate[i] * approach + c.Bar;
            if (compression > _spec.MaxCompression)
            {
                fs += _spec.BumpStopStiffness * (compression - _spec.MaxCompression);
            }
            fs = MathF.Max(fs, 0f);
            w.Load = fs;
            w.Compression = compression;
            force += n * fs;
            torque += Vector3.Cross(r, n * fs);

            if (!c.WasInContact)
            {
                LandingImpact = MathF.Max(LandingImpact, approach);
            }

            // Tire forces in the contact plane.
            var fwd = MathUtil.SafeNormalize(MathUtil.ProjectOnPlane(wheelForward, n), forward);
            var side = Vector3.Cross(n, fwd);
            var vLong = Vector3.Dot(vContact, fwd);
            var vLat = Vector3.Dot(vContact, side);
            // Lateral relaxation: the contact patch needs about a third of a metre of rolling (a tyre's lateral
            // relaxation length is of the order of its radius) to build up side force after a change of slip angle.
            var relax = MathUtil.Clamp01(dt * MathF.Max(MathF.Abs(vLong), 1.5f) / LateralRelaxationLength);
            w.SlipAngle += (TireModel.SlipAngle(vLat, vLong) - w.SlipAngle) * relax;
            w.Friction = TireModel.FrictionFor(hit.Surface) * _spec.TireGrip * WeatherModel.Grip(Weather, hit.Surface);
            w.GroundSpeed = vLong;
            var tire = w.IsFront ? _spec.FrontTire : _spec.RearTire;
            var (fx, fy) = SpinGroundedWheel(w, driveTorque, wheelInertia, brakeMax, vLong, fs, tire, dt);

            // Rolling resistance (much higher on grass: the original's off-road "mud" feel, eased in 0.9 from
            // 0.16 with the grass grip raised from 0.55, players found the car hard to bring back) acts on the
            // body only. Feeding it back into the wheel spin would just make the wheel wind up to cancel it.
            var rolling = (hit.Surface == SurfaceType.Grass ? 0.11f : _spec.RollingResistance) * fs;
            var drag = MathF.Sign(vLong) * MathF.Min(rolling, MathF.Abs(vLong) * 400f);

            var tireForce = fwd * (fx - drag) + side * fy;
            force += tireForce;
            torque += Vector3.Cross(r, tireForce);
            w.LongitudinalForce = fx;
            w.LateralForce = fy;
            w.ContactPoint = contact;
            w.ContactNormal = n;
        }

        // Body touching the ground means a roll-over or a landing on the roof.
        BodyHit = Track.Probe(Position, ref Cursor, true);
        BodyGrounded = BodyHit.HasSurface && BodyHit.Height < 0.28f && Vector3.Dot(up, BodyHit.SurfaceNormal) < 0.55f;

        // Integrate the rigid body (semi-implicit Euler, body-frame inertia).
        var acceleration = force / _spec.MassKg;
        _massVelocity += acceleration * dt;
        _centreOfMass += _massVelocity * dt;

        var inv = Quaternion.Inverse(Orientation);
        var omegaBody = Vector3.Transform(AngularVelocity, inv);
        var torqueBody = Vector3.Transform(torque, inv);
        var inertia = BodyInertia;
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

    /// <summary>
    /// Traction control, where the car has it: the engine's torque is cut while a driven wheel spins faster over the
    /// road than its tyre can use (a tenth of the road speed plus half a metre a second, the slip at the tyre's peak with
    /// a little to spare) and fed back in once it stops. It acts on the engine (ignition and throttle, as the systems of
    /// the 2000s did), not on the driver's pedal, so the clutch and the gearbox see the pedal the driver holds. Up to
    /// 0.16 it eased the pedal itself, by the slip ratio, which near standstill opened the assisted clutch on every cut.
    /// </summary>
    private void UpdateTractionControl(WheelState driven0, WheelState driven1, in DriverInputs input, float dt)
    {
        if (!_spec.TractionControl || Drivetrain.Gear <= 0 || input.Throttle <= 0f)
        {
            Drivetrain.TorqueCut = 1f;
            return;
        }
        // While the clutch slips the wheels get the clutch's torque whatever the engine makes, so a cut would only drag
        // the revs down (into a bog at the launch) without easing the wheelspin.
        var excess = Drivetrain.ClutchLocked ? MathF.Max(SpinExcess(driven0), SpinExcess(driven1)) : 0f;
        Drivetrain.TorqueCut = excess > 0f
            ? MathF.Max(0f, Drivetrain.TorqueCut - dt * (2f + excess * 4f))
            : MathF.Min(1f, Drivetrain.TorqueCut + dt / 0.35f);

        float SpinExcess(WheelState w)
        {
            if (!w.InContact) return 0f;
            var slipSpeed = w.AngularVelocity * _spec.WheelRadius - w.GroundSpeed;
            return slipSpeed - (0.1f * MathF.Abs(w.GroundSpeed) + 0.5f);
        }
    }

    /// <summary>
    /// Torque (N m) the limited-slip differential passes from the left driven wheel to the right one this step; zero
    /// for an open differential. A clutch-type unit: it resists any speed difference with up to its preload plus its
    /// locking share of the drive torque, never more than would bring the two wheels to one speed (half of that, so it
    /// cannot chatter against the tyres at the fixed step).
    /// </summary>
    private float LimitedSlipTransfer(float dt)
    {
        if (_spec.LimitedSlipLocking <= 0f && _spec.LimitedSlipPreload <= 0f) return 0f;
        var (left, right) = _spec.FrontWheelDrive ? (Wheels[0], Wheels[1]) : (Wheels[2], Wheels[3]);
        var capacity = _spec.LimitedSlipPreload + _spec.LimitedSlipLocking * MathF.Abs(Drivetrain.AxleTorque);
        var sync = 0.5f * (left.AngularVelocity - right.AngularVelocity) * _spec.WheelInertia / (2f * dt);
        return MathUtil.Clamp(sync, -capacity, capacity);
    }

    /// <summary>
    /// Advances a wheel on the ground by one step and returns the tyre's (longitudinal, lateral) force at its new speed.
    /// </summary>
    /// <remarks>
    /// The wheel's speed is solved implicitly: the new speed is the one at which the wheel's inertia, the drive, the brake
    /// and the tyre force the new speed itself produces balance over the step. A road tyre is so stiff lengthways (a few
    /// tenths of a radian a second separate rolling from spinning at walking pace) that any explicit step overshoots;
    /// up to 0.16 the slip ratio was relaxed over a third of a metre to keep it in check, a lag that, once the tyre's
    /// force fell past its peak, let the wheel ring against the tyre and the clutch at the step rate on every launch.
    /// The balance rises with the speed (the inertia term outgrows the gentle fall of the tyre past its peak), so a
    /// bracketing search finds its one root; the brake is Coulomb friction that can hold the wheel at rest.
    /// </remarks>
    private (float Fx, float Fy) SpinGroundedWheel(WheelState w, float driveTorque, float inertia, float brakeMax, float vLong, float load, TireSize tire, float dt)
    {
        var radius = _spec.WheelRadius;
        var omega0 = w.AngularVelocity;
        var k = inertia / dt;
        var slipAngle = w.SlipAngle;
        var friction = w.Friction;

        // Balance without the brake: g(omega) = I (omega - omega0) / dt - drive + Fx(omega) r, rising with omega.
        float Balance(float omega) =>
            k * (omega - omega0) - driveTorque + TireModel.Forces(TireModel.SlipRatio(omega * radius, vLong), slipAngle, load, friction, tire).Fx * radius;

        // The brake opposes the rotation: g(omega) + brake sign(omega) = 0, or the wheel is held at rest when |g(0)| <= brake.
        float omega;
        var atRest = Balance(0f);
        if (brakeMax > 0f && MathF.Abs(atRest) <= brakeMax)
        {
            omega = 0f;
        }
        else
        {
            // Spinning forward (the balance at rest negative) the brake adds +brake; backward, -brake.
            var target = brakeMax <= 0f ? 0f : (atRest > 0f ? -brakeMax : brakeMax);
            // The root lies within what every torque at its largest could do in one step.
            var reach = (MathF.Abs(driveTorque) + brakeMax + 1.2f * friction * load * radius) / k + 1f;
            float lo, hi;
            if (brakeMax <= 0f) { lo = omega0 - reach; hi = omega0 + reach; }
            else if (atRest > 0f) { lo = MathF.Min(omega0, 0f) - reach; hi = 0f; }
            else { lo = 0f; hi = MathF.Max(omega0, 0f) + reach; }
            // Regula falsi with the Illinois modification: the bracket always holds the root, and halving the weight of an
            // end that stays put keeps it converging fast (a handful of tyre evaluations, where bisection took 22).
            var gLo = Balance(lo) + target;
            var gHi = Balance(hi) + target;
            omega = 0.5f * (lo + hi);
            var side = 0;
            for (var iteration = 0; iteration < 40 && hi - lo > 1e-3f; iteration++)
            {
                omega = gHi - gLo > 1e-6f ? (lo * gHi - hi * gLo) / (gHi - gLo) : 0.5f * (lo + hi);
                if (!(omega > lo && omega < hi)) omega = 0.5f * (lo + hi);
                var g = Balance(omega) + target;
                if (MathF.Abs(g) < 0.5f) break;
                if (g > 0f)
                {
                    hi = omega;
                    gHi = g;
                    if (side == 1) gLo *= 0.5f;
                    side = 1;
                }
                else
                {
                    lo = omega;
                    gLo = g;
                    if (side == -1) gHi *= 0.5f;
                    side = -1;
                }
            }
        }

        w.AngularVelocity = omega;
        w.SpinAngle += omega * dt;
        w.SlipRatio = TireModel.SlipRatio(omega * radius, vLong);
        var forces = TireModel.Forces(w.SlipRatio, slipAngle, load, friction, tire);
        // How fast the tyre's drag on the wheel grows with the wheel's speed (N m per rad/s, never negative): what the
        // clutch has to drag along over one step besides the wheels themselves (see the drivetrain call).
        const float probe = 0.05f;
        var ahead = TireModel.Forces(TireModel.SlipRatio((omega + probe) * radius, vLong), slipAngle, load, friction, tire).Fx;
        w.SpinStiffness = MathF.Max((ahead - forces.Fx) / probe * radius, 0f);
        return forces;
    }

    /// <summary>A wheel in the air: drive, brake (Coulomb, holding it still at most) and a little bearing drag.</summary>
    private static void SpinFreeWheel(WheelState w, float driveTorque, float inertia, float brakeMax, float dt)
    {
        w.SpinStiffness = 0f;
        var net = driveTorque - 0.05f * w.AngularVelocity;
        var brake = MathF.Min(brakeMax, MathF.Abs(net) + MathF.Abs(w.AngularVelocity) * inertia / dt);
        net -= MathF.Sign(w.AngularVelocity != 0f ? w.AngularVelocity : net) * brake;
        var next = w.AngularVelocity + net / inertia * dt;
        if (brakeMax > 0f && MathF.Sign(next) != MathF.Sign(w.AngularVelocity) && w.AngularVelocity != 0f) next = 0f;
        w.AngularVelocity = next;
        w.SpinAngle += w.AngularVelocity * dt;
    }

    /// <summary>
    /// Anti-lock brakes, where the car has them: a wheel braked past the tyre's peak slip has its pressure dumped
    /// quickly and fed back more slowly once it spins up again, the release-hold-reapply cycle of a real modulator
    /// (several times a second). Below walking pace, in the air and off the pedal the system stands aside.
    /// </summary>
    private void UpdateAntiLock(WheelState w, float pedal, float dt)
    {
        if (!_spec.AntiLockBrakes || pedal <= 0f || !w.InContact || MathF.Abs(w.GroundSpeed) < 2f)
        {
            w.BrakePressure = 1f;
            return;
        }
        // Braking slip is negative going forward; mirror it when reversing so the same thresholds apply.
        var slip = w.SlipRatio * MathF.Sign(w.GroundSpeed);
        var peak = TireModel.PeakSlipRatio(w.Friction);
        if (slip < -1.15f * peak) w.BrakePressure -= dt / 0.05f;
        else if (slip > -0.8f * peak) w.BrakePressure += dt / 0.15f;
        w.BrakePressure = MathUtil.Clamp(w.BrakePressure, 0.1f, 1f);
    }

    /// <summary>What the first pass of a step learns about one wheel, for the second.</summary>
    private struct WheelContact
    {
        public Vector3 Hub;
        public SurfaceHit Hit;
        public bool WasInContact;
        public float Ray;
        public float Compression;
        public float Bar;
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
    /// <summary>Peak friction of the surface under the tyre this step (surface, tyre compound and weather).</summary>
    public float Friction;
    /// <summary>Speed of the ground along the wheel's heading at the contact patch, m/s.</summary>
    public float GroundSpeed;
    /// <summary>Share of the driver's brake pressure the anti-lock modulator lets through, 1 without ABS.</summary>
    public float BrakePressure = 1f;
    /// <summary>Growth of the tyre's drag torque on the wheel with the wheel's speed this step, N m s/rad (never negative).</summary>
    public float SpinStiffness;

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
        Friction = 0f;
        GroundSpeed = 0f;
        BrakePressure = 1f;
        SpinStiffness = 0f;
    }

    /// <summary>Slip magnitude used for skid audio and marks: 0 = gripping, 1 = fully sliding.</summary>
    public float SlipIntensity => MathUtil.Clamp01(MathF.Max(MathF.Abs(SlipRatio) * 2.5f, MathF.Abs(SlipAngle) * 4f) - 0.25f);
}
