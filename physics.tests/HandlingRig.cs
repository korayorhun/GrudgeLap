using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// Proving-ground instruments for the handling tests: a long straight with a large flat asphalt pad beside it, a
/// constant-radius circle driver (SAE J266 style: steer to hold the radius, throttle to hold the speed), straight-line
/// stops and standing-start sprints. They return the numbers a magazine test or a proving ground would quote.
/// </summary>
internal static class HandlingRig
{
    public const float Dt = 1f / 120f;
    /// <summary>The pad's centre: far enough from the straight that the road probe never claims a wheel on it.</summary>
    public static readonly Vector3 PadCentre = new(-600f, 0f, 600f);
    public const float PadRadius = 160f;

    public static TrackGeometry Proving(float length = 3000f)
    {
        var common = new TrackBuilder(PathKind.Common, Vector3.Zero, 0f).Straight(length);
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed).Straight(50f);
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt).Straight(50f);
        var final = TrackBuilder.ContinueFrom(speed.Path, PathKind.Final).Straight(50f);
        return new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, [new SkidPad(PadCentre, PadRadius)], rollingGround: 0f);
    }

    /// <summary>A car on the pad, on the circle of the given radius about its centre, heading clockwise (turning right), rolling at 8 m/s in gear.</summary>
    public static VehicleSim OnCircle(VehicleSpec spec, TrackGeometry track, float radius, Weather weather = Weather.Dry)
    {
        var car = new VehicleSim(spec, track) { Weather = weather };
        var origin = PadCentre - Vector3.UnitX * radius;
        var frame = new RoadFrame(origin, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 0f, 4f, SurfaceType.Pad, SegmentKind.Straight, 0);
        car.Reset(frame, track.StartCursor);
        car.LaunchAt(8f);
        return car;
    }

    public readonly record struct CirclePoint(float SpeedMps, float LateralG, float SteerDeg, float BodySlipDeg, float RollDeg, bool Held);

    /// <summary>
    /// Holds the car on a circle of <paramref name="radius"/> at <paramref name="speedMps"/> and reports the averaged
    /// steady state: lateral acceleration, road-wheel steer angle, body slip angle at the centre of gravity and roll.
    /// <see cref="CirclePoint.Held"/> is false when the car could not hold the line (ran wide, spun or fell short of the speed).
    /// </summary>
    public static CirclePoint Circle(VehicleSpec spec, float radius, float speedMps, Weather weather = Weather.Dry, TrackGeometry? track = null)
    {
        track ??= Proving();
        var car = OnCircle(spec, track, radius, weather);
        const float settle = 14f, measure = 4f;
        float sumAy = 0f, sumSteer = 0f, sumSlip = 0f, sumRoll = 0f;
        var n = 0;
        var held = HoldCircle(car, radius, speedMps, settle);
        HoldCircle(car, radius, speedMps, measure, c =>
        {
            var v = c.Velocity;
            sumAy += c.LateralAcceleration;
            sumSteer += c.SteerAngle;
            sumSlip += MathF.Atan2(Vector3.Dot(v, c.Right), Vector3.Dot(v, c.Forward));
            sumRoll += MathF.Asin(MathUtil.Clamp(Vector3.Dot(c.Right, Vector3.UnitY), -1f, 1f));
            n++;
        }, ref held);
        if (MathF.Abs(car.Speed - speedMps) > 1.5f) held = false;
        return new CirclePoint(car.Speed, sumAy / n / MathUtil.Gravity, sumSteer / n * MathUtil.Rad2Deg, sumSlip / n * MathUtil.Rad2Deg, sumRoll / n * MathUtil.Rad2Deg, held);
    }

    /// <summary>
    /// Drives the circle for <paramref name="seconds"/>, building up to <paramref name="speedMps"/> at 2 m/s^2 from
    /// wherever the car is; returns false when it ran more than 6 m off the circle or off the pad.
    /// </summary>
    public static bool HoldCircle(VehicleSim car, float radius, float speedMps, float seconds)
    {
        var held = true;
        HoldCircle(car, radius, speedMps, seconds, null, ref held);
        return held;
    }

    private static void HoldCircle(VehicleSim car, float radius, float speedMps, float seconds, Action<VehicleSim>? each, ref bool held)
    {
        var integral = 0f;
        var start = car.Speed;
        for (var t = 0f; t < seconds; t += Dt)
        {
            var steer = CircleSteer(car, radius);
            var targetSpeed = MathF.Min(speedMps, MathF.Max(start, 8f) + t * 2f);
            var error = targetSpeed - car.Speed;
            integral = MathUtil.Clamp(integral + error * Dt, -2f, 4f);
            var throttle = MathUtil.Clamp01(error * 0.8f + integral * 0.3f + 0.1f);
            var rpm = car.Drivetrain.Engine.Rpm;
            var redline = car.Spec.Engine.RedlineRpm;
            var locked = car.Drivetrain.ClutchLocked;
            var up = rpm > redline * 0.8f && car.Drivetrain.Gear < car.Spec.ForwardGearCount && locked;
            var down = !up && rpm < redline * 0.35f && car.Drivetrain.Gear > 1 && locked;
            car.Step(new DriverInputs { Steer = steer, Throttle = throttle, Gear = DriverInputs.NoGearChange, ShiftUp = up, ShiftDown = down }, Dt);
            var toCentre = PadCentre - car.Position;
            toCentre.Y = 0f;
            if (MathF.Abs(toCentre.Length() - radius) > 6f || car.BodyHit.Surface != SurfaceType.Pad) held = false;
            each?.Invoke(car);
        }
    }

    /// <summary>Steer input that keeps the car on the circle: pure pursuit on a point a little ahead, plus the radius error.</summary>
    public static float CircleSteer(VehicleSim car, float radius)
    {
        var toCentre = PadCentre - car.Position;
        toCentre.Y = 0f;
        var r = toCentre.Length();
        var radial = toCentre / r;
        var ahead = MathF.Max(6f, car.Speed * 0.5f) / radius;
        var aheadPoint = PadCentre + Vector3.Transform(-radial * radius, Quaternion.CreateFromAxisAngle(Vector3.UnitY, ahead));
        var to = aheadPoint - car.Position;
        var angle = MathF.Atan2(Vector3.Dot(to, car.Right), MathF.Max(Vector3.Dot(to, car.Forward), 0.1f));
        angle += (r - radius) * 0.02f;
        return MathUtil.Clamp(angle / car.MaxSteerAngleAt(car.Speed), -1f, 1f);
    }

    /// <summary>
    /// Straight-line stop from <paramref name="fromKmh"/> with the pedal at <paramref name="pedal"/> and the gearbox in
    /// neutral: distance (m), time (s) and whether any wheel locked on the way.
    /// </summary>
    public static (float Distance, float Seconds, bool Locked) Stop(VehicleSpec spec, float fromKmh, Weather weather = Weather.Dry, float pedal = 1f)
    {
        var car = new VehicleSim(spec, Proving()) { Weather = weather };
        var v0 = fromKmh / MathUtil.KmhPerMps;
        car.LaunchAt(v0);
        var start = car.Position;
        var t = 0f;
        var locked = false;
        while (car.Speed > 0.05f && t < 15f)
        {
            car.Step(new DriverInputs { Brake = pedal, Gear = 0 }, Dt);
            t += Dt;
            foreach (var w in car.Wheels)
            {
                if (car.Speed > 3f && w.InContact && w.AngularVelocity * spec.WheelRadius < car.Speed * 0.1f) locked = true;
            }
        }
        return (Vector3.Distance(start, car.Position), t, locked);
    }

    /// <summary>
    /// Standing start as a road test makes it, flat out with shifts at the redline: engine running and first gear in,
    /// brake and throttle held together for a moment (the revs wait at the launch speed, the clutch open), then the brake
    /// released; seconds from the release to <paramref name="kmh"/>. Up to 0.18 the clock started as first gear was
    /// selected, so the shift into first (a quarter of a second on a manual) and the revs rising from idle were timed too.
    /// </summary>
    public static float Sprint(VehicleSpec spec, float kmh, float limit = 40f) => Sprint(new VehicleSim(spec, Proving()), kmh, limit);

    /// <summary>The standing start of <see cref="Sprint(VehicleSpec, float, float)"/> with a car already placed (on the pad, in the rain...).</summary>
    public static float Sprint(VehicleSim car, float kmh, float limit = 40f, bool preRev = true)
    {
        car.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f }, Dt);
        car.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, Dt);
        for (var t = 0f; t < 1.5f; t += Dt) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f, Throttle = preRev ? 1f : 0f }, Dt);
        var lastGear = car.Drivetrain.Gear;
        for (var t = 0f; t < limit; t += Dt)
        {
            var shift = car.Drivetrain.Engine.Rpm > car.Spec.Engine.RedlineRpm * 0.93f && car.Drivetrain.ClutchLocked && car.Drivetrain.Gear < car.Spec.ForwardGearCount && car.Drivetrain.Gear == lastGear;
            car.Step(new DriverInputs { Throttle = GrudgeLap.Physics.Race.ScriptedDriver.FeatherThrottle(car, 1f), Gear = DriverInputs.NoGearChange, ShiftUp = shift }, Dt);
            if (shift) lastGear = car.Drivetrain.Gear;
            if (car.Speed * MathUtil.KmhPerMps >= kmh) return t;
        }
        return float.PositiveInfinity;
    }
}
