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
    /// <summary>Share of a crawling contact patch's sideways speed the standing grip takes off each step.</summary>
    private const float StandingGripShare = 0.1f;

    private readonly VehicleSpec _spec;
    private readonly WheelContact[] _contacts = new WheelContact[4];
    /// <summary>Spring and damper rate of each wheel: the spec's rates scaled by the wheel's share of the static load.</summary>
    private readonly float[] _springRate = new float[4];
    private readonly float[] _damperRate = new float[4];
    /// <summary>What the driveline gives each wheel this step: its drive torque (N m) and the inertia it turns with (kg m^2, the engine's share included while the clutch is locked).</summary>
    private readonly float[] _driveTorque = new float[4];
    private readonly float[] _driveInertia = new float[4];
    /// <summary>The integral share of each wheel's electronic-differential brake (N m), see <see cref="UpdateElectronicDifferential"/>.</summary>
    private readonly float[] _differentialBrakeHeld = new float[4];
    /// <summary>Each wheel's lead over its partner at the last step (m/s), for the electronic differential's two-step average.</summary>
    private readonly float[] _differentialLastLead = new float[4];
    /// <summary>The inertia (kg m^2) each wheel's electronic-differential loop scales its gains by, smoothed over the modulator's lag.</summary>
    private readonly float[] _differentialInertia = new float[4];
    /// <summary>The share of each wheel's brake torque its electronic-differential loop may use (its partner's grip), smoothed over the modulator's lag.</summary>
    private readonly float[] _differentialReach = new float[4];
    /// <summary>Whether the electronic differential has come in (within its speed window) and not yet let go.</summary>
    private bool _differentialEngaged;
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
    /// <summary>
    /// The steering's command in radians, positive to the right: the angle the steering wheel asks of the road wheels (what
    /// the cockpit's wheel shows, times the steering ratio). Each front wheel's own angle, after the Ackermann geometry and
    /// the give of the steering under the aligning torque, is <see cref="WheelState.SteerAngle"/>.
    /// </summary>
    public float SteerAngle { get; private set; }
    /// <summary>
    /// Aligning torque of the two front tyres about their steering axes, N m (0.19): side force times caster and
    /// pneumatic trail, positive when it turns the wheels to the left. It is what a steering wheel would push back with;
    /// it builds with the cornering force and falls away as the front tyres reach their limit, before their grip does.
    /// </summary>
    public float AligningTorque { get; private set; }
    public PathCursor Cursor;
    /// <summary>Probe at the centre of gravity during the last step.</summary>
    public SurfaceHit BodyHit { get; private set; }
    /// <summary>Times the ground guard has lifted the car back over a surface it had gone through (0.34), for the tests and the pose log.</summary>
    public int GroundGuardLifts { get; private set; }
    public bool Airborne { get; private set; }
    public float AirborneTime { get; private set; }
    /// <summary>Largest normal approach speed (m/s) at which a wheel regained contact during the last step; zero otherwise.</summary>
    public float LandingImpact { get; private set; }
    /// <summary>True when the car body itself is about to touch the surface (landing on the roof or side).</summary>
    public bool BodyGrounded { get; private set; }
    /// <summary>Lateral acceleration felt by the driver, m/s^2, positive to the right. Used by the scrolling dashboard.</summary>
    public float LateralAcceleration { get; private set; }
    public float LongitudinalAcceleration { get; private set; }
    /// <summary>Drive torque (N m) the driveline sent to the two front wheels in the last step, and to the two rear ones.</summary>
    public float FrontDriveTorque { get; private set; }
    public float RearDriveTorque { get; private set; }
    /// <summary>On a four-wheel-drive car, the torque (N m at the wheels) the centre coupling passed from the front axle to the rear one in the last step (negative the other way); zero otherwise.</summary>
    public float CouplingTorque { get; private set; }

    public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Orientation);
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);
    public float Speed => Vector3.Dot(Velocity, Forward);
    public float SpeedMph => MathF.Abs(Speed) * MathUtil.MphPerMps;
    public bool OnRoad => BodyHit.Surface is SurfaceType.Road or SurfaceType.Pad;
    /// <summary>The weather the car drives in: it scales every surface's grip (<see cref="WeatherModel.Grip"/>).</summary>
    public Weather Weather { get; set; } = Weather.Dry;

    /// <summary>
    /// Where water stands on the road (0.56): the view's puddle tile, so the tyres meet the puddles the driver sees. Null
    /// (the tests' default) leaves a wet road without standing water: its lower grip alone.
    /// </summary>
    public PuddleMap? Puddles { get; set; }

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
        AligningTorque = 0f;
        Cursor = cursor;
        Airborne = false;
        AirborneTime = 0f;
        LandingImpact = 0f;
        BodyGrounded = false;
        LateralAcceleration = 0f;
        LongitudinalAcceleration = 0f;
        FrontDriveTorque = 0f;
        RearDriveTorque = 0f;
        CouplingTorque = 0f;
        Array.Clear(_differentialBrakeHeld);
        Array.Clear(_differentialLastLead);
        Array.Clear(_differentialInertia);
        Array.Clear(_differentialReach);
        _differentialEngaged = false;
        _accumulator = 0f;
        Drivetrain.Reset();
        foreach (var w in Wheels) w.Reset();
        BodyHit = Track.Probe(Position, ref Cursor, true);
    }

    /// <summary>
    /// Test/debug helper: puts the car in motion at <paramref name="speedMps"/> from its current pose, engine
    /// running and clutch locked in a sensible gear, optionally shifted sideways by <paramref name="lateralOffset"/> metres.
    /// </summary>
    /// <summary>Test rigs only: sets the body's velocity as it stands, keeping its spin and the wheels' speeds.</summary>
    internal void SetVelocity(Vector3 velocity) => Velocity = velocity;

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
        SteerFrontWheels(dt);

        // Drivetrain first: it needs the driven axle's speed from the previous step (the rear wheels, 2 and 3, or on a
        // front-wheel-drive car the front ones, 0 and 1). Traction control, where the car has it, eases the throttle as
        // the driven wheels spin up (slip ratio from the previous step).
        var differentialLead = ElectronicDifferentialLead(input);
        UpdateTractionControl(input, differentialLead, dt);
        float axleSpeed, axleInertia, axleLoad;
        if (_spec.FourWheelDrive is { } awd)
        {
            (axleSpeed, axleInertia, axleLoad) = CentreDifferentialInput(awd, dt);
            Drivetrain.TractionTorque = LaunchTraction(awd, differentialLead);
        }
        else
        {
            var (driven0, driven1) = _spec.FrontWheelDrive ? (Wheels[0], Wheels[1]) : (Wheels[2], Wheels[3]);
            axleSpeed = 0.5f * (driven0.AngularVelocity + driven1.AngularVelocity);
            Drivetrain.TractionTorque = LaunchTraction(driven0, driven1, differentialLead);
            // The inertia the clutch sees over one step: the driven wheels and, through their tyres' stiffness, what the
            // road holds them to (the terms of SpinGroundedWheel's balance); and the torque the road holds them back with.
            axleInertia = AxleInertia(driven0, driven1, dt);
            axleLoad = AxleLoad(driven0, driven1);
        }
        Drivetrain.Step(input, axleSpeed, speed, dt, axleInertia, axleLoad);

        var force = new Vector3(0f, -_spec.MassKg * MathUtil.Gravity, 0f);
        var torque = Vector3.Zero;

        // Aerodynamic drag.
        var vLen = _massVelocity.Length();
        if (vLen > 0.01f)
        {
            force -= _massVelocity * (0.5f * AirDensity * _spec.DragCoefficient * _spec.FrontalArea * vLen);
        }

        // Aerodynamic lift (or downforce) over each axle, from the dynamic pressure of the air along the car.
        var air = MathF.Max(Vector3.Dot(_massVelocity, forward), 0f);
        var dynamicPressure = 0.5f * AirDensity * air * air;
        if (dynamicPressure > 0f)
        {
            var frontLift = up * (dynamicPressure * _spec.FrontLiftArea);
            var rearLift = up * (dynamicPressure * _spec.RearLiftArea);
            var frontAt = Position + Vector3.Transform(new Vector3(0f, 0f, _spec.FrontAxle), Orientation);
            var rearAt = Position + Vector3.Transform(new Vector3(0f, 0f, -_spec.RearAxle), Orientation);
            force += frontLift + rearLift;
            torque += Vector3.Cross(frontAt - _centreOfMass, frontLift) + Vector3.Cross(rearAt - _centreOfMass, rearLift);
        }

        var anyContact = false;
        var rayDir = -up;
        var maxLen = _spec.SuspensionRest + _spec.WheelRadius;
        // The ground guard (0.34): how far the deepest hub sits under its surface this step, and that surface's normal.
        // A hub under its surface got no contact at all until 0.34 (the ray to the surface came out negative), so nothing
        // held the car up once a hard landing, a bank steeper than the wheel could follow or a knock had put a hub
        // through the road, and the car sank through the asphalt or the grass and out of the world (the owner's "the car
        // sometimes falls under the grass and the asphalt"; the replay of such a fall was the car "under the road"). Now
        // such a hub rides its bump stop and the body is lifted back out along the normal at the end of the step.
        var sunk = 0f;
        var sunkNormal = up;
        // A lifted hub ends with its spring on the bump stop, not with the tyre squashed to nothing: the bump stop's
        // force at that squash threw the car a metre into the air.
        var hubClearance = maxLen - _spec.MaxCompression;

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
            w.WaterDepth = 0f;
            w.Aquaplaning = 0f;
            if (!c.Hit.HasSurface) continue;
            var facing = Vector3.Dot(c.Hit.SurfaceNormal, rayDir);
            if (facing >= -0.2f) continue;
            var t = Vector3.Dot(c.Hit.SurfaceNormal, c.Hit.SurfacePoint - c.Hub) / facing;
            if (t >= maxLen) continue;
            if (t <= 0f)
            {
                // The hub is under the surface: on the bump stop, and the body comes out by the depth below the plane.
                // Only a hub a little under a surface the car is the right way up on: a car arriving under the
                // drawbridge's landing deck hits its edge (RaceSession.HitBridgeEdge) or falls into the gap, as before,
                // and a car that has fallen outside the loop is not pulled onto its deck.
                var depth = Vector3.Dot(c.Hit.SurfaceNormal, c.Hit.SurfacePoint - c.Hub);
                if (depth > 0.6f || Vector3.Dot(up, c.Hit.SurfaceNormal) < 0.3f || (c.Hit.Frame.Kind is SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Overpass or SegmentKind.Gap && depth > 0.1f)) continue;
                if (depth + hubClearance > sunk)
                {
                    sunk = depth + hubClearance;
                    sunkNormal = c.Hit.SurfaceNormal;
                }
                t = 0f;
            }
            w.InContact = true;
            w.Surface = c.Hit.Surface;
            anyContact = true;
            c.Ray = t;
            // A hub under the surface rides its bump stop, no deeper: the lift puts the car right, not a launch.
            c.Compression = t > 0f ? maxLen - t : _spec.MaxCompression;
        }

        // Anti-roll bars: each axle's bar pushes its more compressed wheel down onto the road and lifts the other by the
        // same force. A wheel in the air hangs at full droop, so the bar then presses only the wheel on the ground.
        _contacts[0].Bar = _spec.FrontAntiRoll * (_contacts[0].Compression - _contacts[1].Compression);
        _contacts[1].Bar = -_contacts[0].Bar;
        _contacts[2].Bar = _spec.RearAntiRoll * (_contacts[2].Compression - _contacts[3].Compression);
        _contacts[3].Bar = -_contacts[2].Bar;

        var pedal = MathUtil.Clamp01(input.Brake);
        // Torque the differential sends to each driven wheel: half the axle's each (an open differential), less or
        // plus what a limited-slip unit passes from the faster wheel to the slower one. On a four-wheel-drive car the
        // centre of the driveline first shares the gearbox's torque between the axles (DistributeFourWheelDrive).
        if (_spec.FourWheelDrive is { } fourWheelDrive)
        {
            DistributeFourWheelDrive(fourWheelDrive, pedal, dt);
        }
        else
        {
            var (leftWheel, rightWheel) = _spec.FrontWheelDrive ? (Wheels[0], Wheels[1]) : (Wheels[2], Wheels[3]);
            var lsdTorque = LimitedSlipTransfer(leftWheel, rightWheel, _spec.LimitedSlipLocking, _spec.LimitedSlipPreload, Drivetrain.AxleTorque, dt);
            for (var i = 0; i < Wheels.Length; i++)
            {
                var w = Wheels[i];
                var driven = w.IsFront == _spec.FrontWheelDrive;
                _driveTorque[i] = driven ? Drivetrain.AxleTorque * 0.5f + (w.LocalPosition.X < 0f ? -lsdTorque : lsdTorque) : 0f;
                _driveInertia[i] = _spec.WheelInertia + (driven ? Drivetrain.ReflectedInertia * 0.5f : 0f);
            }
        }
        FrontDriveTorque = _driveTorque[0] + _driveTorque[1];
        RearDriveTorque = _driveTorque[2] + _driveTorque[3];
        if (_spec.ElectronicDifferential is { } electronicDifferential) UpdateElectronicDifferential(electronicDifferential, pedal, speed, dt);

        for (var i = 0; i < Wheels.Length; i++)
        {
            var w = Wheels[i];
            ref var c = ref _contacts[i];
            var steer = w.SteerAngle;
            // Rotating +Z about +Y by a positive angle swings it toward +X, i.e. a positive steer turns the wheel right.
            var wheelForward = MathUtil.RotateAround(forward, up, steer);

            var driveTorque = _driveTorque[i];
            var wheelInertia = _driveInertia[i];
            UpdateAntiLock(w, pedal, dt);
            var brakeMax = (w.IsFront ? _spec.FrontBrakeTorque : _spec.RearBrakeTorque) * pedal * w.BrakePressure;
            // The electronic differential's brake works only while the driver's foot is off the pedal, so the two never add.
            if (_spec.ElectronicDifferential is not null) brakeMax += w.DifferentialBrake;
            // A hot disc gives the pads less to grip (0.49, BrakeThermal): the driver's brake and the differential's alike.
            brakeMax *= BrakeThermal.Fade(w.BrakeTemperature);
            var brakeSize = w.IsFront ? _spec.FrontBrakeTorque : _spec.RearBrakeTorque;

            if (!w.InContact)
            {
                // The bar still reaches a wheel hanging at full droop: it pulls that corner of the body down through the
                // droop limit with the force it pushes the grounded wheel up by (c.Bar is the negative of its partner's),
                // so the two cancel vertically and only roll the body. Dropping it here left a net push up from nothing,
                // a few kN for a hatchback lifting an inside wheel.
                if (c.Bar < 0f)
                {
                    var barForce = up * c.Bar;
                    force += barForce;
                    torque += Vector3.Cross(c.Hub - _centreOfMass, barForce);
                }
                SpinFreeWheel(w, driveTorque, wheelInertia, brakeMax, dt);
                w.BrakeTemperature = BrakeThermal.Step(w.BrakeTemperature, brakeMax * MathF.Abs(w.AngularVelocity), brakeSize, w.IsFront, speed, dt);
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
            // The damper resists rebound harder than bump (DamperReboundRatio), the two averaging the spec's rate.
            var damping = _damperRate[i] * 2f / (1f + _spec.DamperReboundRatio) * (approach < 0f ? _spec.DamperReboundRatio : 1f);
            var fs = _springRate[i] * compression + damping * approach + c.Bar;
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
            // Standing water (0.56): the puddle under the patch, as deep as the weather leaves it; at speed the tyre rides up
            // on it and its grip goes (TireModel.Aquaplaning), and it pushes the water aside (the drag below).
            w.WaterDepth = Puddles is { } puddles && hit.Surface == SurfaceType.Road
                ? WeatherModel.WaterDepth(Weather, puddles.At(hit.Lateral, hit.Frame.HalfWidth, hit.Frame.Distance))
                : 0f;
            w.Aquaplaning = TireModel.Aquaplaning(vLong, w.WaterDepth);
            w.Friction *= 1f - 0.85f * w.Aquaplaning;
            w.GroundSpeed = vLong;
            w.LateralSpeed = vLat;
            var tire = w.IsFront ? _spec.FrontTire : _spec.RearTire;
            var (fx, fy) = SpinGroundedWheel(w, driveTorque, wheelInertia, brakeMax, vLong, fs, tire, dt);
            // The brake's work goes into its disc while the wheel turns against it; a wheel it holds still heats its tyre.
            w.BrakeTemperature = BrakeThermal.Step(w.BrakeTemperature, brakeMax * MathF.Abs(w.AngularVelocity), brakeSize, w.IsFront, speed, dt);
            // Standing grip: where the slip angle fades out at a crawl, the tyre still holds the road sideways by static
            // friction. It takes a share of the patch's sideways speed off each step (as much force as stops a quarter
            // wheel's mass by that share), capped by the grip: without it a parked car nudged sideways slid on, and one
            // stopped on a bank crept down it. The share stays well inside the explicit step's limit of one, with four
            // wheels acting on the one body.
            var standing = 1f - TireModel.SlipAngleFade(vLat, vLong);
            if (standing > 0f)
            {
                var hold = standing * StandingGripShare * fs / MathUtil.Gravity / dt * vLat;
                var grip = w.Friction * fs;
                fy = MathUtil.Clamp(fy - hold, -grip, grip);
            }

            // Rolling resistance (much higher on grass: the original's off-road "mud" feel, eased in 0.9 from
            // 0.16 with the grass grip raised from 0.55, players found the car hard to bring back) acts on the
            // body only. Feeding it back into the wheel spin would just make the wheel wind up to cancel it.
            var rolling = (hit.Surface == SurfaceType.Grass ? 0.11f : _spec.RollingResistance) * fs;
            if (w.WaterDepth > 0f) rolling += TireModel.WaterDrag(vLong, w.WaterDepth, w.Aquaplaning, tire);
            var drag = MathF.Sign(vLong) * MathF.Min(rolling, MathF.Abs(vLong) * 400f);

            var tireForce = fwd * (fx - drag) + side * fy;
            force += tireForce;
            torque += Vector3.Cross(r, tireForce);
            w.LongitudinalForce = fx;
            w.LateralForce = fy;
            w.ContactPoint = contact;
            w.ContactNormal = n;
        }

        // The body's corners against the ground (0.35): a wreck on its roof or its side, a car bottoming on a bank. Looked
        // at only with a wheel off the ground or the car tilted: on its wheels the corners stay clear.
        var wheelsDown = 0;
        foreach (var w in Wheels)
        {
            if (w.InContact) wheelsDown++;
        }
        var bodyContacts = wheelsDown < 4 || up.Y < 0.85f ? BodyContacts(dt) : 0;

        // Body touching the ground means a roll-over or a landing on the roof: the centre of gravity down near the surface
        // with the car tilted, or (0.35) the body's corners carrying a tilted car.
        BodyHit = Track.Probe(Position, ref Cursor, true);
        var tilted = BodyHit.HasSurface && Vector3.Dot(up, BodyHit.SurfaceNormal) < 0.55f;
        BodyGrounded = tilted && (BodyHit.Height < 0.28f || bodyContacts > 0);
        // The centre of gravity itself under the surface (0.34): the whole car has gone through (a fall at speed onto
        // a bank, a step it was set down into); it comes back to a hand's breadth over the surface.
        // Not under a deck (a car fallen under the drawbridge or into a cutting under an overpass is under a surface it was
        // never on, and was lifted onto it).
        if (BodyHit.HasSurface && BodyHit.Height < -0.1f && BodyHit.Height > -1.5f && Vector3.Dot(up, BodyHit.SurfaceNormal) > 0.2f
            && BodyHit.Frame.Kind is not (SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Overpass or SegmentKind.Gap))
        {
            var sag = _spec.MassKg * MathUtil.Gravity / 4f / _spec.SuspensionStiffness;
            var rest = _spec.SuspensionRest + _spec.WheelRadius - sag - _spec.HubHeight;   // the centre of gravity's height at rest (Reset)
            if (-BodyHit.Height + rest * 0.9f > sunk)
            {
                sunk = -BodyHit.Height + rest * 0.9f;
                sunkNormal = BodyHit.SurfaceNormal;
            }
        }

        // Integrate the rigid body (semi-implicit Euler, body-frame inertia).
        var acceleration = force / _spec.MassKg;
        _massVelocity += acceleration * dt;
        _centreOfMass += _massVelocity * dt;
        if (sunk > 0f)
        {
            // The ground guard's lift: out along the surface's normal, and the speed into the surface taken away (the
            // springs and the bump stops have already slowed it; what is left would carry it through again).
            _centreOfMass += sunkNormal * (sunk + 0.005f);
            var into = Vector3.Dot(_massVelocity, sunkNormal);
            if (into < 0f) _massVelocity -= sunkNormal * into;
            GroundGuardLifts++;
        }

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

    private const float BodyFriction = 0.6f;

    /// <summary>
    /// The body's eight corners (the sills at the axles and the roof, inset for the cabin) against the surface each is
    /// over (0.35): a corner under its surface has its speed into it taken away at once (a tenth of it back: a body does
    /// not bounce like a tyre), friction on its slide, and is moved back out by a share of its depth each step, with no
    /// spring to store the blow and throw the car up again. So a wreck tumbles, slides and comes to rest on its roof or
    /// its side instead of sinking through the ground (the simulation never carried a car past its crash before). A
    /// corner far through is left to the ground guard. Returns how many corners touch.
    /// </summary>
    private int BodyContacts(float dt)
    {
        var touching = 0;
        var sag = _spec.MassKg * MathUtil.Gravity / 4f / _spec.SuspensionStiffness;
        // The sills at the axles (not the overhangs: the valance of a car landing nose-down touched first and threw the
        // tail down harder) and the roof, inset for the cabin.
        var floor = _spec.HubHeight - 0.05f;
        var roof = _spec.RoofHeight - (_spec.SuspensionRest + _spec.WheelRadius - sag - _spec.HubHeight);
        var halfWidth = _spec.BodyHalfWidth;
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(-halfWidth * 0.95f, floor, _spec.FrontAxle);
        corners[1] = new Vector3(halfWidth * 0.95f, floor, _spec.FrontAxle);
        corners[2] = new Vector3(-halfWidth * 0.95f, floor, -_spec.RearAxle);
        corners[3] = new Vector3(halfWidth * 0.95f, floor, -_spec.RearAxle);
        corners[4] = new Vector3(-halfWidth * 0.8f, roof, _spec.BodyFront * 0.3f);
        corners[5] = new Vector3(halfWidth * 0.8f, roof, _spec.BodyFront * 0.3f);
        corners[6] = new Vector3(-halfWidth * 0.8f, roof, -_spec.BodyRear * 0.45f);
        corners[7] = new Vector3(halfWidth * 0.8f, roof, -_spec.BodyRear * 0.45f);
        var share = _spec.MassKg * 0.25f;
        foreach (var local in corners)
        {
            var p = Position + Vector3.Transform(local, Orientation);
            var cursor = Cursor;
            var hit = Track.Probe(p, ref cursor, false);
            if (!hit.HasSurface) continue;
            var n = hit.SurfaceNormal;
            var depth = Vector3.Dot(n, hit.SurfacePoint - p);
            // Only a shallow penetration is a contact: a corner can sink no further than it moved. A corner far under a
            // surface is under something it never touched (the roof of a wreck sliding under the drawbridge's landing
            // ramp, which this pushed up through the deck).
            if (depth <= 0f || depth > 0.2f) continue;
            touching++;
            var v = VelocityAt(p);
            var vn = Vector3.Dot(v, n);
            // The weight the corner carries over the step, and the blow it takes if it arrives.
            var load = share * MathUtil.Gravity * dt;
            if (vn < 0f)
            {
                var push = 1.1f * -vn / InverseMassAlong(p, n);
                ApplyImpulse(p, n * push);
                load += push;
                v = VelocityAt(p);
                vn = Vector3.Dot(v, n);
            }
            var slide = v - n * vn;
            var slideSpeed = slide.Length();
            if (slideSpeed > 1e-3f)
            {
                var t = slide / slideSpeed;
                ApplyImpulse(p, -t * MathF.Min(BodyFriction * load, slideSpeed / InverseMassAlong(p, t)));
            }
            _centreOfMass += n * (depth * 0.25f);
        }
        return touching;
    }

    /// <summary>
    /// Each front wheel's angle from the steering's command (0.19): the Ackermann linkage turns the inner wheel more (its
    /// angle from the turning radius of the bicycle model, R = L / tan(steer), less or plus half the track), and the
    /// steering gives under each tyre's aligning torque (side force times caster plus pneumatic trail, from the last
    /// step), steering it out of the bend by <see cref="VehicleSpec.SteeringCompliance"/> per N m. The give follows within
    /// a few hundredths of a second, as a steering system's does, which also keeps the loop through the tyre stable.
    /// </summary>
    private void SteerFrontWheels(float dt)
    {
        var wheelbase = _spec.FrontAxle + _spec.RearAxle;
        var command = SteerAngle;
        var aligning = 0f;
        for (var i = 0; i < 2; i++)
        {
            var w = Wheels[i];
            var angle = command;
            if (MathF.Abs(command) > 1e-4f && _spec.AckermannShare > 0f)
            {
                var radius = wheelbase / MathF.Tan(MathF.Abs(command));
                // Turning right (a positive command) the right wheel (+x) is the inner one.
                var inner = (w.LocalPosition.X > 0f) == (command > 0f);
                var ackermann = MathF.Atan(wheelbase / (radius + (inner ? -_spec.HalfTrack : _spec.HalfTrack)));
                angle = MathF.CopySign(MathUtil.Lerp(MathF.Abs(command), ackermann, _spec.AckermannShare), command);
            }
            var torque = 0f;
            if (w.InContact)
            {
                var trail = _spec.CasterTrail + TireModel.PneumaticTrail(w.SlipAngle, w.Load, w.Friction, _spec.FrontTire);
                // A side force to the right acting behind the steering axis turns the wheel's nose to the left.
                torque = w.LateralForce * trail;
            }
            aligning += torque;
            w.Compliance += (-torque * _spec.SteeringCompliance - w.Compliance) * MathUtil.Clamp01(dt / 0.03f);
            w.SteerAngle = angle + w.Compliance;
        }
        AligningTorque = aligning;
    }

    /// <summary>
    /// Traction control, where the car has it: the engine's torque is cut while a driven wheel spins faster over the
    /// road than its tyre can use (a tenth of the road speed plus half a metre a second, the slip at the tyre's peak with
    /// a little to spare) and fed back in once it stops. It acts on the engine (ignition and throttle, as the systems of
    /// the 2000s did), not on the driver's pedal, so the clutch and the gearbox see the pedal the driver holds. Up to
    /// 0.17 it eased the pedal itself, by the slip ratio, which near standstill opened the assisted clutch on every cut.
    /// On a four-wheel-drive car it watches all four wheels.
    /// </summary>
    private void UpdateTractionControl(in DriverInputs input, float differentialLead, float dt)
    {
        if (!_spec.TractionControl || Drivetrain.Gear <= 0 || input.Throttle <= 0f)
        {
            Drivetrain.TorqueCut = 1f;
            return;
        }
        // While the clutch slips the wheels get the clutch's torque whatever the engine makes, so a cut would only drag
        // the revs down (into a bog at the launch) without easing the wheelspin.
        var excess = 0f;
        if (Drivetrain.ClutchLocked && float.IsFinite(differentialLead) && _differentialEngaged)
        {
            // With a brake-based differential at work the two systems share the job as the real ones do: the brake holds a
            // wheel that spins on its own, up to the lead it allows over its partner, and the engine is cut when both
            // wheels of an axle spin (the lesser of their excesses) or when one spins past what the brake holds. Only once
            // the function has engaged (from the last step): until then nothing brakes the wheel, and leaving its spin to
            // a brake that is not on cost the V8's launch control 0.03 s to 60 mph on the drag strip.
            excess = float.NegativeInfinity;
            for (var axle = 0; axle < 4; axle += 2)
            {
                if (!_spec.Drives(Wheels[axle].IsFront)) continue;
                var a = SpinExcess(Wheels[axle]);
                var b = SpinExcess(Wheels[axle + 1]);
                excess = MathF.Max(excess, MathF.Max(MathF.Min(a, b), MathF.Max(a, b) - differentialLead));
            }
        }
        else if (Drivetrain.ClutchLocked)
        {
            excess = float.NegativeInfinity;
            foreach (var w in Wheels)
            {
                if (_spec.Drives(w.IsFront)) excess = MathF.Max(excess, SpinExcess(w));
            }
        }
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
    /// The lead (m/s) the electronic differential lets a driven wheel's slip speed run over its partner's at this speed,
    /// while it can act (the driver off the brake, within its speed window); NaN on a car without one or when it cannot.
    /// </summary>
    private float ElectronicDifferentialLead(in DriverInputs input)
    {
        if (_spec.ElectronicDifferential is not { } ed || input.Brake > 0f) return float.NaN;
        var speed = MathF.Abs(Vector3.Dot(_massVelocity, Forward));
        var kmh = speed * MathUtil.KmhPerMps;
        if (kmh > ed.CutOutKmh || (kmh > ed.EngageBelowKmh && !_differentialEngaged)) return float.NaN;
        return ed.SlipLead;
    }

    /// <summary>
    /// The axle torque (N m) the driven tyres can put down now, for the launch assist (0.19): each grounded driven tyre's
    /// peak force at its load from the last step, times the share the launch aims at (all but a few per cent of the peak
    /// with launch control, which reads the wheel speeds a hundred times a second; nine tenths for a driver's foot, which
    /// has to stay on the safe side of it), cut back while a wheel already spins past its peak slip so it comes back.
    /// A tyre asked for its whole peak has no margin: the wheel's own inertia then carries it over the top and it spins
    /// away, which is the bogged, smoking start the assisted clutch made up to 0.18.
    /// </summary>
    private float LaunchTraction(WheelState a, WheelState b, float differentialLead)
    {
        if (!a.InContact && !b.InContact) return float.PositiveInfinity;
        var aim = LaunchAim;
        // The wheels themselves take a little torque to speed up with the car (two wheels' inertia at the car's
        // acceleration): what reaches the road is the rest.
        var spinUp = 2f * _spec.WheelInertia * MathF.Max(LongitudinalAcceleration, 0f) / _spec.WheelRadius;
        return AxleLaunchForce(a, b, aim, differentialLead) * _spec.WheelRadius + spinUp;
    }

    /// <summary>
    /// The launch assist's torque on a four-wheel-drive car: what the four tyres can take as the centre of the driveline
    /// shares it out. An epicyclic centre differential sends its fixed shares s and 1 - s and its coupling moves up to C
    /// from the axle at its limit to the other, so the whole is the least of what the four tyres take together, of the
    /// front's limit plus C over s and of the rear's limit plus C over 1 - s. Behind an on-demand coupling (s = 1) that
    /// is the front's limit plus the rear's, the rear's never more than C. With an axle in the air the figure is
    /// unknown, as with two-wheel drive.
    /// </summary>
    /// <remarks>
    /// C is what the coupling can pass at the slip the launch allows. A viscous coupling passes its rate times the axles'
    /// speed difference, and the assist lets one axle run at its tyres' peak slip while the other grips. An on-demand
    /// coupling passes nothing until the fronts lead the rears by its lead, which the assist, keeping the fronts short of
    /// their peak, would never let them do: counted only for what it already passed, the coupling never closed on a
    /// launch, and the V6 hatch with its fronts on grass pulled away at a front-wheel-drive car's 0.3 g. So the assist
    /// counts on the clutch pack's whole capacity and the fronts run ahead to close it, as a first Haldex's did.
    /// </remarks>
    private float LaunchTraction(FourWheelDrive awd, float differentialLead)
    {
        if (!(Wheels[0].InContact || Wheels[1].InContact) || !(Wheels[2].InContact || Wheels[3].InContact)) return float.PositiveInfinity;
        var aim = LaunchAim;
        var spinUp = 4f * _spec.WheelInertia * MathF.Max(LongitudinalAcceleration, 0f) / _spec.WheelRadius;
        var front = AxleLaunchForce(Wheels[0], Wheels[1], aim, differentialLead) * _spec.WheelRadius;
        var rear = AxleLaunchForce(Wheels[2], Wheels[3], aim, differentialLead) * _spec.WheelRadius;
        var s = awd.FrontShare;
        var passes = awd.CouplingCapacity;
        if (awd.Coupling == CentreCoupling.Viscous)
        {
            // The axles part by the faster one's peak slip at the car's speed (the slip ratio's floor of 1.2 m/s at a
            // standstill, as the tyre reads it).
            var ground = MathF.Max(MathF.Abs(0.5f * (Wheels[2].GroundSpeed + Wheels[3].GroundSpeed)), 1.2f);
            var peakSlip = TireModel.PeakSlipRatio(MathF.Max(Wheels[0].Friction, Wheels[2].Friction));
            passes = MathF.Min(passes, awd.CouplingRate * peakSlip * ground / _spec.WheelRadius);
        }
        var total = MathF.Min(front + rear, (front + passes) / s);
        if (s < 1f) total = MathF.Min(total, (rear + passes) / (1f - s));
        return total + spinUp;
    }

    private float LaunchAim => _spec.LaunchControl ? 0.995f : DriverLaunchAim;
    private const float DriverLaunchAim = 0.92f;

    /// <summary>
    /// The force (N) an axle's two tyres can put down at the launch through its open differential: the sum of the two,
    /// or, where an electronic differential can hold the wheel with less grip by its brake (<paramref name="differentialLead"/>
    /// finite), up to twice the better tyre's, no more than the other's plus what its brake holds. The differential sends
    /// both wheels the same torque, and the brake takes up what the lesser tyre cannot: the split-grip start the systems
    /// were made for. Counted as the sum, the assist let the spinning wheel's share swing past its peak and back each time
    /// the brake caught it, and the clutch pulsed with it at 30 Hz.
    /// </summary>
    /// <remarks>
    /// The better tyre then carries the axle alone, so it is asked for no more than a driver's foot asks of a tyre: at the
    /// launch control's 99.5 % the V8's rear tyre on the asphalt went over its peak every few steps and the assist cut the
    /// clutch back each time. Where the brake cannot hold the difference (the R32's rear brake, 420 N m), twice the lesser
    /// tyre's force and the brake's is the bound, under the sum.
    /// On even grip (both tyres on the ground, their surfaces' grip within 5 %) the brake is not counted at all and the sum
    /// is the bound, as on a car without the function: counting it there, a one-step blip of slip on one rear wheel at the
    /// V8's asphalt launch (a third of a second in) had the assist ask for twice the better tyre's force, before any brake
    /// was on, and the wheel ran away to 3 m/s of slip and was braked at 600 N m, where without the function it recovers on
    /// the next step.
    /// </remarks>
    private float AxleLaunchForce(WheelState a, WheelState b, float aim, float differentialLead)
    {
        var sum = LaunchShare(a, aim) + LaunchShare(b, aim);
        if (!float.IsFinite(differentialLead)) return sum;
        if (a.InContact == b.InContact && MathF.Abs(a.Friction - b.Friction) < 0.05f * MathF.Max(a.Friction, b.Friction)) return sum;
        var brake = (a.IsFront ? _spec.FrontBrakeTorque : _spec.RearBrakeTorque) / _spec.WheelRadius;
        var held = MathF.Min(aim, DriverLaunchAim);
        return MathF.Max(Braked(held), MathF.Min(sum, Braked(aim)));

        // Twice the better tyre's force, no more than the lesser one's plus the brake's.
        float Braked(float share)
        {
            var forceA = LaunchShare(a, share);
            var forceB = LaunchShare(b, share);
            return 2f * MathF.Min(MathF.Max(forceA, forceB), MathF.Min(forceA, forceB) + brake);
        }
    }

    /// <summary>The force (N) a driven tyre can put down at the launch: its peak at its load, times the aim, cut back while it spins past its peak.</summary>
    private float LaunchShare(WheelState w, float aim)
    {
        if (!w.InContact || w.Load <= 0f) return 0f;
        var tire = w.IsFront ? _spec.FrontTire : _spec.RearTire;
        var peak = TireModel.PeakFriction(w.Load, w.Friction, tire) * w.Load;
        var over = w.SlipRatio / MathF.Max(TireModel.PeakSlipRatio(w.Friction), 0.02f) - 1f;
        return peak * aim * MathUtil.Clamp(1f - 0.6f * MathF.Max(over, 0f), 0.3f, 1f);
    }

    /// <summary>
    /// The inertia (kg m^2) an axle presents over one step: its two wheels and, through their tyres' stiffness, what the
    /// road holds them to (the terms of SpinGroundedWheel's balance, from the last step).
    /// </summary>
    private float AxleInertia(WheelState a, WheelState b, float dt) =>
        2f * _spec.WheelInertia + (a.InContact ? a.SpinStiffness * dt : 0f) + (b.InContact ? b.SpinStiffness * dt : 0f);

    /// <summary>The torque (N m) the road holds an axle's wheels back with through their tyres, from the last step.</summary>
    private float AxleLoad(WheelState a, WheelState b) =>
        ((a.InContact ? a.LongitudinalForce : 0f) + (b.InContact ? b.LongitudinalForce : 0f)) * _spec.WheelRadius;

    /// <summary>
    /// What the gearbox's output sees on a four-wheel-drive car, in the axles' terms: its speed, the inertia the clutch
    /// drags along over a step and the torque the road holds it back with. Through an epicyclic centre differential that
    /// sends the share s to the front, the output turns at s w_f + (1 - s) w_r (the balance of power across a
    /// differential), the two axles act on it as one inertia of 1 / (s^2 / I_f + (1 - s)^2 / I_r) and their loads in the
    /// same proportion; where the gearbox drives the front axle and a coupling feeds the rear (s = 1), it sees the front axle.
    /// </summary>
    private (float Speed, float Inertia, float Load) CentreDifferentialInput(FourWheelDrive awd, float dt)
    {
        var s = awd.FrontShare;
        var frontSpeed = 0.5f * (Wheels[0].AngularVelocity + Wheels[1].AngularVelocity);
        var rearSpeed = 0.5f * (Wheels[2].AngularVelocity + Wheels[3].AngularVelocity);
        var frontInertia = AxleInertia(Wheels[0], Wheels[1], dt);
        var rearInertia = AxleInertia(Wheels[2], Wheels[3], dt);
        var frontLoad = AxleLoad(Wheels[0], Wheels[1]);
        var rearLoad = AxleLoad(Wheels[2], Wheels[3]);
        if (s >= 1f) return (frontSpeed, frontInertia, frontLoad);
        var inertia = 1f / (s * s / frontInertia + (1f - s) * (1f - s) / rearInertia);
        return (s * frontSpeed + (1f - s) * rearSpeed, inertia, inertia * (s * frontLoad / frontInertia + (1f - s) * rearLoad / rearInertia));
    }

    /// <summary>
    /// Each wheel's drive torque and inertia on a four-wheel-drive car: the centre differential's share of the gearbox's
    /// torque for each axle, the coupling's torque taken from the faster axle and given to the slower one, then each
    /// axle's differential between its wheels. With the clutch locked the engine's inertia is shared out as the torque is,
    /// which is exact while the two axles turn together.
    /// </summary>
    private void DistributeFourWheelDrive(FourWheelDrive awd, float pedal, float dt)
    {
        var s = awd.FrontShare;
        var coupling = CouplingTransfer(awd, pedal, dt);
        var front = s * Drivetrain.AxleTorque - coupling;
        var rear = (1f - s) * Drivetrain.AxleTorque + coupling;
        var frontLsd = LimitedSlipTransfer(Wheels[0], Wheels[1], awd.FrontLocking, awd.FrontPreload, front, dt);
        var rearLsd = LimitedSlipTransfer(Wheels[2], Wheels[3], awd.RearLocking, awd.RearPreload, rear, dt);
        CouplingTorque = coupling;
        for (var i = 0; i < Wheels.Length; i++)
        {
            var w = Wheels[i];
            var (axle, lsd, share) = w.IsFront ? (front, frontLsd, s) : (rear, rearLsd, 1f - s);
            _driveTorque[i] = axle * 0.5f + (w.LocalPosition.X < 0f ? -lsd : lsd);
            _driveInertia[i] = _spec.WheelInertia + Drivetrain.ReflectedInertia * 0.5f * share;
        }
    }

    /// <summary>Speed difference between the axles (rad/s) an on-demand coupling always allows, so a car creeping off does not close it on the tyres' creep.</summary>
    private const float CouplingLeadFloor = 0.5f;

    /// <summary>
    /// Torque (N m) the centre coupling passes from the front axle to the rear one this step, negative the other way: its
    /// rate times the axles' speed difference at the end of the step, no more than its capacity (an on-demand coupling's
    /// clutch pack), and an on-demand coupling's open while the brakes are on. The law is taken at the step's end
    /// (implicitly), with each axle's speed moved over the step by its drive, the torque the road holds it back with and
    /// the coupling's own torque, against the inertia it presents through its tyres (as the clutch reckons its lock):
    /// taken at the step's start, the coupling lagged the clutch by a step, an axle the clutch had just spun up met it a
    /// step late, and the two hammered the axle back and forth at the step rate. So computed it can never pass more than would bring the
    /// two axles to one speed, which is where a real coupling, viscous or multi-plate, stops.
    /// </summary>
    private float CouplingTransfer(FourWheelDrive awd, float pedal, float dt)
    {
        if (awd.Coupling == CentreCoupling.Haldex && pedal > 0f) return 0f;
        var (fl, fr, rl, rr) = (Wheels[0], Wheels[1], Wheels[2], Wheels[3]);
        var frontInertia = AxleInertia(fl, fr, dt);
        var rearInertia = AxleInertia(rl, rr, dt);
        // The axles' speed difference at the end of the step without the coupling, and how much a newton metre of
        // coupling torque closes it.
        var frontPull = (awd.FrontShare * Drivetrain.AxleTorque - AxleLoad(fl, fr)) / frontInertia;
        var rearPull = ((1f - awd.FrontShare) * Drivetrain.AxleTorque - AxleLoad(rl, rr)) / rearInertia;
        var gap = 0.5f * (fl.AngularVelocity + fr.AngularVelocity) - 0.5f * (rl.AngularVelocity + rr.AngularVelocity) + dt * (frontPull - rearPull);
        var closing = dt * (1f / frontInertia + 1f / rearInertia);
        // An on-demand coupling lets the axles part a little before it closes (CouplingLead): the tyres' own slip under
        // a part-throttle pull and the fronts' longer path in a bend.
        var lead = awd.CouplingLead * MathF.Abs(0.5f * (rl.AngularVelocity + rr.AngularVelocity)) + (awd.CouplingLead > 0f ? CouplingLeadFloor : 0f);
        var beyond = MathF.Sign(gap) * MathF.Max(MathF.Abs(gap) - lead, 0f);
        var torque = awd.CouplingRate * beyond / (1f + awd.CouplingRate * closing);
        return MathUtil.Clamp(torque, -awd.CouplingCapacity, awd.CouplingCapacity);
    }

    /// <summary>
    /// Torque (N m) a limited-slip differential passes from an axle's left wheel to its right one this step; zero for an
    /// open differential. A clutch-type unit: it resists any speed difference with up to its preload plus its locking
    /// share of the axle's drive torque, never more than would bring the two wheels to one speed (half of that, so it
    /// cannot chatter against the tyres at the fixed step). A Torsen's torque bias ratio b is the locking (b - 1) / (2 (b + 1)) with no preload.
    /// </summary>
    private float LimitedSlipTransfer(WheelState left, WheelState right, float locking, float preload, float axleTorque, float dt)
    {
        if (locking <= 0f && preload <= 0f) return 0f;
        var capacity = preload + locking * MathF.Abs(axleTorque);
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
    /// up to 0.17 the slip ratio was relaxed over a third of a metre to keep it in check, a lag that, once the tyre's
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

    /// <summary>Closed-loop bandwidth of the electronic differential's brake, rad/s: it brings a spinning wheel back in a few hundredths of a second.</summary>
    private const float DifferentialBandwidth = 25f;
    /// <summary>Lag of the modulator's pressure behind its command, s: the pump's build and the calliper's fill.</summary>
    private const float DifferentialPressureLag = 0.02f;

    /// <summary>
    /// The electronic differential (dyn5): McLaren's brake steer, Volkswagen's EDS, Mercedes's ETS. On each driven axle, a
    /// wheel whose slip speed runs more than <see cref="ElectronicDifferential.SlipLead"/> ahead of its partner's is braked
    /// (<see cref="WheelState.DifferentialBrake"/>, applied with the driver's brake in the wheel's spin), so the open
    /// differential's equal torque is held by the brake instead of spinning the wheel, the engine and the driveline up
    /// towards the limiter and the traction control's cut, and the other wheel goes on driving.
    /// </summary>
    /// <remarks>
    /// The brake is a proportional-integral loop on the lead, both gains scaled by the inertia the wheel turns with this
    /// step (its own and its share of the engine's through a locked clutch), so it closes at the same pace in first gear
    /// as in sixth: the inertia times 2 w (proportional) and w^2 (integral) place the loop's two poles together at
    /// w = 25 rad/s, critically damped, and the modulator's lag of 0.02 s stays well inside them. At 15 rad/s the V8's
    /// rear wheel on grass at a split-grip launch ran 9 m/s ahead of its partner before the brake caught it (6 m/s now).
    /// Proportional alone would leave the wheel spinning tens of radians a second over its lead to hold a first gear's
    /// torque; the integral share holds that torque with the wheel at its lead, and is what the loop lets go of as the
    /// wheel slows below it. Each wheel's command is capped at its brake torque at full pedal (the pressure of a hard
    /// stop), and dumped as soon as the driver brakes or the car passes the function's cut-out speed. The real systems'
    /// cut-out for a hot disc (the Audi A4's EDL reckons the disc's temperature) is left out, as the model keeps no brake
    /// temperature anywhere.
    /// </remarks>
    private void UpdateElectronicDifferential(ElectronicDifferential ed, float pedal, float speed, float dt)
    {
        var kmh = MathF.Abs(speed) * MathUtil.KmhPerMps;
        var radius = _spec.WheelRadius;
        // Leads are taken along the direction the gearbox drives the wheels: a reversing car spins its wheels backwards.
        var direction = Drivetrain.Gear < 0 ? -1f : 1f;
        var allowed = ed.SlipLead;
        // The driver's brake and the cut-out speed dump the pressure at once, as a modulator's outlet valves do.
        var dump = pedal > 0f || kmh > ed.CutOutKmh;
        if (dump) _differentialEngaged = false;
        else if (!_differentialEngaged && kmh <= ed.EngageBelowKmh)
        {
            for (var i = 0; i < 4 && !_differentialEngaged; i++)
            {
                if (_spec.Drives(Wheels[i].IsFront) && Lead(i) > allowed) _differentialEngaged = true;
            }
        }
        var anyHeld = false;
        for (var i = 0; i < 4; i++)
        {
            var w = Wheels[i];
            var command = 0f;
            var lead = _spec.Drives(w.IsFront) ? Lead(i) : float.NaN;
            // The loop reads the lead averaged over the last two steps, as a controller reads its wheel-speed sensors
            // through a filter: a jitter at the step rate from elsewhere in the driveline (the R32's Haldex on split grip
            // sets its front wheels alternating) cancels out instead of reaching the brake through the proportional term.
            var reading = 0.5f * (lead + _differentialLastLead[i]);
            _differentialLastLead[i] = float.IsFinite(lead) ? lead : 0f;
            // The gains follow the wheel's inertia, and the brake's reach the partner's grip, both smoothed over the
            // modulator's lag: a clutch that locks and slips on alternate steps (it does on split grip at a standing start)
            // would otherwise swing the gains fifteenfold in first gear from one step to the next, and a partner wheel
            // flicking past its peak and back the reach, and the brake with them.
            var follow = MathUtil.Clamp01(dt / DifferentialPressureLag);
            _differentialInertia[i] += (_driveInertia[i] - _differentialInertia[i]) * follow;
            // Only a wheel spinning on its own is braked: the brake is worth something only while the partner can take more
            // of the differential's torque. Once the partner too spins past its tyre's peak the brake's reach fades to
            // nothing over half the peak slip again, and the traction control's engine cut does the work, as the systems
            // divide it ("if the slipping only involves one of the drive wheels", Fiat's handbook).
            _differentialReach[i] += (PartnerGrips(Wheels[i ^ 1], _contacts[i ^ 1].WasInContact, direction) - _differentialReach[i]) * follow;
            if (_differentialEngaged && float.IsFinite(lead))
            {
                var cap = (w.IsFront ? _spec.FrontBrakeTorque : _spec.RearBrakeTorque) * _differentialReach[i];
                var excess = (reading - allowed) / radius;
                var inertia = _differentialInertia[i];
                _differentialBrakeHeld[i] = MathUtil.Clamp(_differentialBrakeHeld[i] + inertia * DifferentialBandwidth * DifferentialBandwidth * excess * dt, 0f, cap);
                command = MathUtil.Clamp(_differentialBrakeHeld[i] + inertia * 2f * DifferentialBandwidth * excess, 0f, cap);
            }
            else
            {
                _differentialBrakeHeld[i] = 0f;
            }
            w.DifferentialBrake = dump ? 0f : w.DifferentialBrake + (command - w.DifferentialBrake) * follow;
            anyHeld |= _differentialBrakeHeld[i] > 0f;
        }
        // Once every driven wheel is back within its lead and the loop has let go, the function stands down until a wheel
        // spins again (and, on a car with a speed window, only below the speed it comes in at).
        if (_differentialEngaged && !anyHeld)
        {
            var spinning = false;
            for (var i = 0; i < 4; i++) spinning |= _spec.Drives(Wheels[i].IsFront) && Lead(i) > allowed;
            if (!spinning) _differentialEngaged = false;
        }

        // 1 while the wheel grips short of its tyre's peak slip, falling to 0 by one and a half times the peak; 0 in the air
        // on the last step (whose slip it reads).
        static float PartnerGrips(WheelState partner, bool grounded, float direction)
        {
            if (!grounded) return 0f;
            var over = direction * partner.SlipRatio / MathF.Max(TireModel.PeakSlipRatio(partner.Friction), 0.02f);
            return MathUtil.Clamp01((1.5f - over) / 0.5f);
        }

        // How far wheel i spins ahead of its partner on the axle (0 and 1 in front, 2 and 3 behind), m/s: its slip speed
        // (its tyre's speed over the road less the road's under it) less its partner's, each counted only while it drives
        // (a wheel held back by the engine on the overrun is not spinning, and its partner rolling freely is not ahead of
        // it); NaN with both wheels in the air. A wheel in the air is reckoned over its partner's road. Contact is the last
        // step's, as the wheel speeds and road speeds are: InContact is already this step's by now, and a wheel's road
        // speed is kept from its last step on the ground, so on the step it lands its road speed would be the take-off's.
        float Lead(int i)
        {
            var w = Wheels[i];
            var partner = Wheels[i ^ 1];
            var grounded = _contacts[i].WasInContact;
            var partnerGrounded = _contacts[i ^ 1].WasInContact;
            if (!grounded && !partnerGrounded) return float.NaN;
            var ground = grounded ? w.GroundSpeed : partner.GroundSpeed;
            var partnerGround = partnerGrounded ? partner.GroundSpeed : w.GroundSpeed;
            var slip = direction * (w.AngularVelocity * radius - ground);
            var partnerSlip = direction * (partner.AngularVelocity * radius - partnerGround);
            return MathF.Max(slip, 0f) - MathF.Max(partnerSlip, 0f);
        }
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
    /// <summary>Speed of the ground across the wheel at the contact patch, m/s, positive to the wheel's right: how fast the patch slides sideways.</summary>
    public float LateralSpeed;
    /// <summary>Share of the driver's brake pressure the anti-lock modulator lets through, 1 without ABS.</summary>
    public float BrakePressure = 1f;
    /// <summary>The brake disc's temperature, °C (0.49, <see cref="BrakeThermal"/>): heated by the braking, cooled by the air.</summary>
    public float BrakeTemperature = BrakeThermal.Ambient;
    /// <summary>Water standing under the tyre, mm (0.56, <see cref="VehicleSim.Puddles"/>).</summary>
    public float WaterDepth;
    /// <summary>How far the tyre floats on that water, 0 to 1 (<see cref="TireModel.Aquaplaning"/>).</summary>
    public float Aquaplaning;
    /// <summary>Brake torque (N m) the electronic differential holds this wheel with, besides the driver's brake; zero on a car without one.</summary>
    public float DifferentialBrake;
    /// <summary>Growth of the tyre's drag torque on the wheel with the wheel's speed this step, N m s/rad (never negative).</summary>
    public float SpinStiffness;
    /// <summary>The wheel's own steering angle, rad, positive to the right: Ackermann and compliance included; zero at the rear.</summary>
    public float SteerAngle;
    /// <summary>How far the steering's give under the aligning torque has turned this wheel, rad (part of <see cref="SteerAngle"/>).</summary>
    public float Compliance;

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
        LateralSpeed = 0f;
        BrakePressure = 1f;
        BrakeTemperature = BrakeThermal.Ambient;
        WaterDepth = 0f;
        Aquaplaning = 0f;
        DifferentialBrake = 0f;
        SpinStiffness = 0f;
        SteerAngle = 0f;
        Compliance = 0f;
    }

    /// <summary>Slip magnitude used for skid audio and marks: 0 = gripping, 1 = fully sliding.</summary>
    public float SlipIntensity => MathUtil.Clamp01(MathF.Max(MathF.Abs(SlipRatio) * 2.5f, MathF.Abs(SlipAngle) * 4f) - 0.25f);
}
