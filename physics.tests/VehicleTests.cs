using System.Numerics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The "feel checklist" as numbers: each test pins one behaviour the original cabinet was known for.
/// They drive the simulation headless with scripted inputs, so tuning the car never needs the engine.
/// </summary>
public sealed class VehicleTests
{
    private const float Dt = 1f / 60f;
    private readonly ITestOutputHelper _out;

    public VehicleTests(ITestOutputHelper output) => _out = output;

    private static TrackGeometry DragStrip(float length = 3000f)
    {
        var common = new TrackBuilder(PathKind.Common, Vector3.Zero, 0f).Straight(length);
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed).Straight(50f);
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt).Straight(50f);
        var final = TrackBuilder.ContinueFrom(speed.Path, PathKind.Final).Straight(50f);
        return new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, rollingGround: 0f);
    }

    private static TrackGeometry WithFeature(Action<TrackBuilder> feature, float approach = 120f, float exit = 300f)
    {
        var common = new TrackBuilder(PathKind.Common, Vector3.Zero, 0f).Straight(approach);
        feature(common);
        common.Straight(exit);
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed).Straight(50f);
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt).Straight(50f);
        var final = TrackBuilder.ContinueFrom(speed.Path, PathKind.Final).Straight(50f);
        return new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, rollingGround: 0f);
    }

    /// <summary>
    /// Pure-pursuit steering toward a point ahead on the road, the way a player keeps the car in its lane.
    /// Returns a steer input in -1..1.
    /// </summary>
    private static float FollowRoad(VehicleSim car, float lookAheadSeconds = 0.7f)
    {
        var path = car.Cursor.Path;
        var lookAhead = MathF.Max(8f, MathF.Abs(car.Speed) * lookAheadSeconds);
        var target = path.FrameAt(MathF.Min(car.Cursor.Distance + lookAhead, path.Length)).Origin;
        var toTarget = target - car.Position;
        var angle = MathF.Atan2(Vector3.Dot(toTarget, car.Right), MathF.Max(Vector3.Dot(toTarget, car.Forward), 0.1f));
        // Lateral-error term pulls the car back to the centre line on long curves where pure pursuit lags.
        angle -= car.BodyHit.Lateral * 0.015f;
        var max = car.MaxSteerAngleAt(car.Speed);
        return MathUtil.Clamp(angle / max, -1f, 1f);
    }

    /// <summary>Simple cruise control so feature tests arrive at a known speed.</summary>
    private static (float Throttle, float Brake) HoldSpeed(VehicleSim car, float targetMps)
    {
        var error = targetMps - car.Speed;
        return (MathUtil.Clamp01(error * 0.6f + 0.15f), MathUtil.Clamp01(-error * 0.4f - 0.2f));
    }

    private static DriverInputs Follow(VehicleSim car, float throttle, float brake = 0f) => new()
    {
        Throttle = throttle, Brake = brake, Steer = FollowRoad(car), Gear = DriverInputs.NoGearChange,
    };

    private void Trace(VehicleSim car, float t, string extra = "")
    {
        var w = car.Wheels;
        _out.WriteLine($"t={t,5:F2} s={car.Cursor.Distance,6:F1} y={car.Position.Y,5:F2} v={car.SpeedMph,5:F1}mph rpm={car.Drivetrain.Engine.Rpm,5:F0} g={car.Drivetrain.Gear} " +
                       $"contact={w.Count(x => x.InContact)} loadRL={w[2].Load,6:F0} kR={w[2].SlipRatio,6:F2} aF={w[0].SlipAngle * MathUtil.Rad2Deg,5:F1} lat={car.BodyHit.Lateral,5:F1} {extra}");
    }

    /// <summary>Drives flat out with keyboard-style sequential shifts near the redline (key pressed once per shift).</summary>
    private static void DriveFlatOut(VehicleSim car, float seconds, Action<VehicleSim>? each = null)
    {
        var steps = (int)(seconds / Dt);
        var lastGear = car.Drivetrain.Gear;
        for (var i = 0; i < steps; i++)
        {
            var shift = car.Drivetrain.Engine.Rpm > car.Spec.Engine.RedlineRpm * 0.93f && car.Drivetrain.ClutchLocked && car.Drivetrain.Gear < car.Spec.ForwardGearCount && car.Drivetrain.Gear == lastGear;
            car.Step(new DriverInputs { Throttle = 1f, Gear = DriverInputs.NoGearChange, ShiftUp = shift }, Dt);
            if (shift) lastGear = car.Drivetrain.Gear;
            each?.Invoke(car);
        }
    }

    private static VehicleSim StartedCar(TrackGeometry track, TransmissionMode mode = TransmissionMode.ManualAutoClutch, VehicleSpec? spec = null)
    {
        var car = new VehicleSim(spec ?? new VehicleSpec(), track);
        car.Drivetrain.Mode = mode;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f }, Dt);
        Assert.True(car.Drivetrain.Engine.Running);
        car.Step(new DriverInputs { Gear = 1, Clutch = 1f }, Dt);
        return car;
    }

    [Fact]
    public void Sits_still_at_rest_on_the_start_line()
    {
        var track = DragStrip();
        var car = new VehicleSim(new VehicleSpec(), track);
        for (var i = 0; i < 120; i++) car.Step(DriverInputs.Idle, Dt);
        Assert.True(car.Velocity.Length() < 0.05f, $"drifted at {car.Velocity.Length():F3} m/s");
        Assert.True(Vector3.Dot(car.Up, MathUtil.Up) > 0.999f);
        Assert.All(car.Wheels, w => Assert.True(w.InContact));
        _out.WriteLine($"ride height {car.Position.Y:F3} m, wheel load {car.Wheels[0].Load:F0} N");
    }

    [Fact]
    public void Positive_steer_turns_right()
    {
        var car = new VehicleSim(new VehicleSpec(), DragStrip());
        car.LaunchAt(15f);
        for (var i = 0; i < 90; i++) car.Step(new DriverInputs { Steer = 0.5f, Throttle = 0.3f, Gear = DriverInputs.NoGearChange }, Dt);
        Assert.True(car.BodyHit.Lateral > 1f, $"lateral {car.BodyHit.Lateral:F2} m (positive = right of the centre line)");
    }

    [Fact]
    public void Accelerates_to_60_mph_in_a_sports_car_time()
    {
        var car = StartedCar(DragStrip());
        var t = 0f;
        var reached = -1f;
        var step = 0;
        DriveFlatOut(car, 15f, c =>
        {
            t += Dt;
            if (reached < 0f && c.SpeedMph >= 60f) reached = t;
            if (step++ % 60 == 0 && t < 8f) Trace(c, t);
        });
        _out.WriteLine($"0-60 mph: {reached:F2} s, speed after 15 s: {car.SpeedMph:F0} mph, gear {car.Drivetrain.Gear}");
        Assert.InRange(reached, 4.0f, 7.5f);
    }

    [Fact]
    public void Top_speed_lands_in_the_150_mph_class()
    {
        var car = StartedCar(DragStrip(6000f));
        DriveFlatOut(car, 70f);
        _out.WriteLine($"top speed {car.SpeedMph:F0} mph in gear {car.Drivetrain.Gear} at {car.Drivetrain.Engine.Rpm:F0} rpm");
        Assert.InRange(car.SpeedMph, 135f, 175f);
        Assert.True(car.OnRoad);
    }

    [Fact]
    public void V8_launches_and_tops_out_like_a_modern_supercar()
    {
        var spec = VehicleSpec.V8Evo;
        Assert.Equal(7, spec.ForwardGearCount);
        var car = StartedCar(DragStrip(6000f), spec: spec);
        var t = 0f;
        var reached = -1f;
        var maxRpm = 0f;
        var step = 0;
        DriveFlatOut(car, 60f, c =>
        {
            t += Dt;
            if (reached < 0f && c.SpeedMph >= 60f) reached = t;
            maxRpm = MathF.Max(maxRpm, c.Drivetrain.Engine.Rpm);
            if (step++ % 12 == 0 && t < 5f) Trace(c, t, $"lock={c.Drivetrain.ClutchLocked} eng={c.Drivetrain.Engagement:F2} T={c.Drivetrain.AxleTorque:F0}");
        });
        _out.WriteLine($"V8: 0-60 mph {reached:F2} s, top speed {car.SpeedMph:F0} mph in gear {car.Drivetrain.Gear} at {car.Drivetrain.Engine.Rpm:F0} rpm, max rpm {maxRpm:F0}");
        Assert.InRange(reached, 3.0f, 5.0f);
        Assert.InRange(car.SpeedMph, 180f, 215f);
        Assert.InRange(maxRpm, 8000f, 9400f);
        Assert.True(car.OnRoad);
    }

    [Fact]
    public void Brakes_from_60_mph_in_a_reasonable_distance()
    {
        var car = StartedCar(DragStrip());
        DriveFlatOut(car, 9f);
        Assert.True(car.SpeedMph > 60f);
        while (car.SpeedMph > 60f) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, Dt);
        var start = car.Position;
        var t = 0f;
        while (car.SpeedMph > 1f && t < 10f)
        {
            car.Step(new DriverInputs { Brake = 1f, Clutch = 1f, Gear = DriverInputs.NoGearChange }, Dt);
            t += Dt;
        }
        var distance = Vector3.Distance(start, car.Position);
        _out.WriteLine($"60-0 mph in {distance:F1} m, {t:F2} s");
        Assert.InRange(distance, 30f, 70f);
    }

    [Fact]
    public void Dumping_the_clutch_at_idle_stalls_the_engine()
    {
        var car = StartedCar(DragStrip(), TransmissionMode.ManualClutch);
        var stalledAt = -1f;
        for (var i = 0; i < 180; i++)
        {
            car.Step(new DriverInputs { Gear = 1, Clutch = 0f, Throttle = 0f }, Dt);
            if (stalledAt < 0f && !car.Drivetrain.Engine.Running) stalledAt = i * Dt;
        }
        _out.WriteLine($"stalled after {stalledAt:F2} s, rolled {car.Position.Z:F2} m, {car.SpeedMph:F1} mph");
        Assert.False(car.Drivetrain.Engine.Running, "engine should have stalled");
        Assert.True(car.Position.Z < 3f, "the car should barely creep when the clutch is dumped at idle");
    }

    [Fact]
    public void Clutch_fed_in_with_revs_launches_without_stalling()
    {
        var car = StartedCar(DragStrip(), TransmissionMode.ManualClutch);
        var t = 0f;
        for (var i = 0; i < 300; i++)
        {
            t += Dt;
            var clutch = MathUtil.Clamp01(1f - t / 1.5f); // release over 1.5 s
            car.Step(new DriverInputs { Gear = 1, Clutch = clutch, Throttle = 0.6f }, Dt);
        }
        Assert.True(car.Drivetrain.Engine.Running);
        Assert.True(car.SpeedMph > 15f, $"only {car.SpeedMph:F1} mph");
    }

    [Theory]
    [InlineData(68f, true)]
    [InlineData(42f, false)]
    public void Vertical_loop_needs_about_60_mph(float entryMph, bool shouldComplete)
    {
        var track = WithFeature(b => b.Loop(15f, 11f));
        var car = new VehicleSim(new VehicleSpec(), track);
        var loopStart = track.Common.Frames.First(f => f.Kind == SegmentKind.Loop).Distance;
        var loopEnd = track.Common.Frames.Last(f => f.Kind == SegmentKind.Loop).Distance;
        var target = entryMph / MathUtil.MphPerMps;
        car.LaunchAt(target);
        var completed = false;
        var fell = false;
        var maxHeight = 0f;
        var minSpeed = float.MaxValue;
        for (var i = 0; i < 60 * 12; i++)
        {
            var inLoop = car.Cursor.Distance > loopStart - 2f;
            var (throttle, brake) = inLoop ? (0.35f, 0f) : HoldSpeed(car, target);
            car.Step(Follow(car, throttle, brake), Dt);
            maxHeight = MathF.Max(maxHeight, car.Position.Y);
            if (inLoop) minSpeed = MathF.Min(minSpeed, car.SpeedMph);
            if (car.Cursor.Distance > loopEnd + 20f && car.OnRoad && Vector3.Dot(car.Up, MathUtil.Up) > 0.9f) { completed = true; break; }
            if (inLoop && car.Cursor.Distance < loopEnd && (car.BodyGrounded || car.LandingImpact > 7.5f)) { fell = true; break; }
            if (car.Position.Y < -1f) { fell = true; break; }
        }
        _out.WriteLine($"entry {entryMph} mph: completed={completed} fell={fell} maxHeight={maxHeight:F1} m, slowest {minSpeed:F0} mph, exit speed {car.SpeedMph:F0} mph");
        Assert.Equal(shouldComplete, completed);
        if (!shouldComplete) Assert.True(fell);
    }

    [Theory]
    [InlineData(76f, true)]
    [InlineData(62f, true)]
    [InlineData(48f, false)]
    [InlineData(90f, false)]
    public void Drawbridge_wants_a_60_to_80_mph_run_up(float entryMph, bool shouldSurvive)
    {
        var track = WithFeature(b => b.Drawbridge(30f, 7f, 18f, 34f));
        var car = new VehicleSim(new VehicleSpec(), track);
        var rampStart = track.Common.Frames.First(f => f.Kind == SegmentKind.RampUp).Distance;
        var landingStart = track.Common.Frames.First(f => f.Kind == SegmentKind.RampDown).Distance;
        var landingEnd = track.Common.Frames.Last(f => f.Kind == SegmentKind.RampDown).Distance;
        var target = entryMph / MathUtil.MphPerMps;
        car.LaunchAt(target);
        var landed = false;
        var crashed = "";
        var hardest = 0f;
        var launchMph = 0f;
        for (var i = 0; i < 60 * 10; i++)
        {
            var onRamp = car.Cursor.Distance > rampStart - 2f;
            var (throttle, brake) = onRamp ? (0.4f, 0f) : HoldSpeed(car, target);
            car.Step(Follow(car, throttle, brake), Dt);
            if (car.Cursor.Distance < rampStart) launchMph = car.SpeedMph;
            hardest = MathF.Max(hardest, car.LandingImpact);
            var frame = car.BodyHit.Frame;
            if (frame.Kind == SegmentKind.RampDown && frame.Distance - landingStart < 3f && car.BodyHit.Height < RaceOptions.BridgeEdgeClearance) { crashed = "hit the bridge edge"; break; }
            if (car.LandingImpact > 7.5f) { crashed = $"hard landing {car.LandingImpact:F1} m/s"; break; }
            if (car.Position.Y < -2.5f) { crashed = "fell into the gap"; break; }
            if (car.Cursor.Distance > landingEnd + 15f && car.OnRoad && !car.Airborne) { landed = true; break; }
        }
        _out.WriteLine($"entry {entryMph} mph (launch {launchMph:F0}): landed={landed} crashed='{crashed}' hardest landing {hardest:F1} m/s");
        Assert.Equal(shouldSurvive, landed && crashed == "");
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("overpass")]
    public void Engine_keeps_running_over_jumps_in_top_gear(string feature)
    {
        var track = feature == "bridge"
            ? WithFeature(b => b.Drawbridge(30f, 7f, 18f, 34f))
            : WithFeature(b => b.Overpass(140f, 4.5f));
        var car = new VehicleSim(new VehicleSpec(), track);
        car.LaunchAt(65f / MathUtil.MphPerMps);
        car.Drivetrain.ForceLocked(4, 65f / MathUtil.MphPerMps / car.Spec.WheelRadius * car.Spec.RatioForGear(4) * car.Spec.FinalDrive);
        var minRpm = float.MaxValue;
        var minWheel = float.MaxValue;
        for (var i = 0; i < 60 * 10; i++)
        {
            car.Step(Follow(car, 0.15f), Dt);
            minRpm = MathF.Min(minRpm, car.Drivetrain.Engine.Rpm);
            minWheel = MathF.Min(minWheel, MathF.Min(car.Wheels[2].AngularVelocity, car.Wheels[3].AngularVelocity));
            if (i % 6 == 0 && car.Cursor.Distance > 100f && car.Cursor.Distance < 320f)
            {
                Trace(car, i * Dt, $"{car.BodyHit.Frame.Kind} rpm={car.Drivetrain.Engine.Rpm:F0} lock={car.Drivetrain.ClutchLocked} wRL={car.Wheels[2].AngularVelocity:F1} wRR={car.Wheels[3].AngularVelocity:F1} running={car.Drivetrain.Engine.Running}");
            }
            if (car.Cursor.Distance > 320f) break;
        }
        _out.WriteLine($"{feature}: min rpm {minRpm:F0}, min rear wheel speed {minWheel:F1} rad/s, running={car.Drivetrain.Engine.Running}");
        Assert.True(car.Drivetrain.Engine.Running, "engine stalled");
        Assert.True(minRpm > 1500f, $"rpm dipped to {minRpm:F0}");
    }

    [Fact]
    public void Banked_180_holds_90_mph()
    {
        var track = WithFeature(b => b.Curve(180f, 85f, 32f));
        var car = new VehicleSim(new VehicleSpec(), track);
        var curveStart = track.Common.Frames.First(f => f.Kind == SegmentKind.BankedCurve).Distance;
        var curveEnd = track.Common.Frames.Last(f => f.Kind == SegmentKind.BankedCurve).Distance;
        var target = 90f / MathUtil.MphPerMps;
        car.LaunchAt(target);
        var leftRoad = false;
        var maxLateral = 0f;
        for (var i = 0; i < 60 * 15; i++)
        {
            var (throttle, brake) = HoldSpeed(car, target);
            car.Step(Follow(car, throttle, brake), Dt);
            if (car.Cursor.Distance > curveStart) maxLateral = MathF.Max(maxLateral, MathF.Abs(car.BodyHit.Lateral));
            if (!car.OnRoad && !car.Airborne) { leftRoad = true; break; }
            if (car.Cursor.Distance > curveEnd + 10f) break;
        }
        _out.WriteLine($"banked: leftRoad={leftRoad} max lateral offset {maxLateral:F1} m, exit speed {car.SpeedMph:F0} mph");
        Assert.False(leftRoad);
        Assert.True(maxLateral < 3.5f, "should track the lane through the banking");
    }

    [Fact]
    public void Full_lock_at_high_speed_spins_the_car()
    {
        var car = new VehicleSim(new VehicleSpec(), DragStrip());
        car.LaunchAt(95f / MathUtil.MphPerMps);
        var maxYawRate = 0f;
        var maxSlip = 0f;
        for (var i = 0; i < 60 * 4; i++)
        {
            car.Step(new DriverInputs { Throttle = 0.8f, Steer = 1f, Gear = DriverInputs.NoGearChange }, Dt);
            maxYawRate = MathF.Max(maxYawRate, MathF.Abs(car.AngularVelocity.Y));
            maxSlip = MathF.Max(maxSlip, MathF.Abs(car.Wheels[2].SlipAngle));
        }
        _out.WriteLine($"max yaw rate {maxYawRate * MathUtil.Rad2Deg:F0} deg/s, max rear slip {maxSlip * MathUtil.Rad2Deg:F0} deg");
        Assert.True(maxSlip > 20f * MathUtil.Deg2Rad, "rear should break away");
    }

    [Fact]
    public void Grass_is_like_mud()
    {
        var track = DragStrip();
        var road = new VehicleSim(new VehicleSpec(), track);
        road.LaunchAt(60f / MathUtil.MphPerMps);
        var grass = new VehicleSim(new VehicleSpec(), track);
        grass.LaunchAt(60f / MathUtil.MphPerMps, lateralOffset: 12f);

        var roadLoss = SpeedLossOver(road, 3f);
        var grassLoss = SpeedLossOver(grass, 3f);
        _out.WriteLine($"coasting 3 s: road lost {roadLoss:F1} mph, grass lost {grassLoss:F1} mph");
        Assert.Equal(SurfaceType.Road, road.BodyHit.Surface);
        Assert.Equal(SurfaceType.Grass, grass.BodyHit.Surface);
        Assert.True(grassLoss > roadLoss * 2.5f);
    }

    private static float SpeedLossOver(VehicleSim car, float seconds)
    {
        var start = car.SpeedMph;
        for (var i = 0; i < seconds * 60; i++)
        {
            car.Step(new DriverInputs { Gear = 0 }, Dt);
        }
        return start - car.SpeedMph;
    }
}
