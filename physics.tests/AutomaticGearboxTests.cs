using System.Numerics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The BRZ as its owner had it (0.76): a 2017-20 European BRZ with the six-speed automatic (Aisin A960E), driven through
/// its torque converter in <see cref="TransmissionMode.Automatic"/> (<see cref="VehicleSpec.Automatic"/>), and the corrections
/// the 0.70 review found for every gearbox mode: the FA20's real curve, the EPA's road load, the brakes' build-up. The
/// figures: Subaru Deutschland's technical data of the 2017 model year (0-100 km/h in 8.2 s and 210 km/h for the
/// automatic, 7.6 s and 226 km/h for the manual), MotorWeek's automatic BRZ (60 mph in 7.7 s), ADAC's, Motor Trend's and
/// Car and Driver's stops, the converter's stall speed and torque ratio and the EPA's coast-down (VehicleSpec.Boxer). The
/// 0.76 review's: part pedal in D held against the manual at moderate revs, rolling back in D, the manual's launch revs.
/// </summary>
public sealed class AutomaticGearboxTests(ITestOutputHelper output)
{
    private const float Dt = 1f / 120f;

    /// <summary>Pure pursuit down the straight, as the handling tests' drivers keep a car on it.</summary>
    private static float Follow(VehicleSim car)
    {
        var path = car.Cursor.Path;
        var lookAhead = MathF.Max(8f, MathF.Abs(car.Speed) * 0.7f);
        var target = path.FrameAt(MathF.Min(car.Cursor.Distance + lookAhead, path.Length)).Origin;
        var to = target - car.Position;
        var angle = MathF.Atan2(Vector3.Dot(to, car.Right), MathF.Max(Vector3.Dot(to, car.Forward), 0.1f));
        angle -= car.BodyHit.Lateral * 0.015f;
        return MathUtil.Clamp(angle / car.MaxSteerAngleAt(car.Speed), -1f, 1f);
    }

    /// <summary>The BRZ in D at rest on the proving ground's straight: the engine started on the brake, D taken up.</summary>
    private static VehicleSim InDrive(float length = 3000f)
    {
        var car = new VehicleSim(VehicleSpec.Boxer, HandlingRig.Proving(length));
        car.Drivetrain.Mode = TransmissionMode.Automatic;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Brake = 1f }, Dt);
        for (var t = 0f; t < 1f; t += Dt) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f }, Dt);
        return car;
    }

    private static void Drive(VehicleSim car, float throttle, float brake = 0f) =>
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = throttle, Brake = brake, Steer = Follow(car) }, Dt);

    /// <summary>The manual BRZ with the clutch worked for the driver (<see cref="TransmissionMode.ManualAutoClutch"/>), in first at rest.</summary>
    private static VehicleSim InFirst(float length = 3000f)
    {
        var car = new VehicleSim(VehicleSpec.Boxer, HandlingRig.Proving(length));
        car.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Brake = 1f }, Dt);
        car.Step(new DriverInputs { Gear = 1, Brake = 1f }, Dt);
        for (var t = 0f; t < 1f; t += Dt) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f }, Dt);
        return car;
    }

    /// <summary>
    /// The 0.76 review's brisk road driver in the manual: the pedal held at <paramref name="pedal"/>, a change up at 4,000
    /// rpm once the clutch has locked, no higher than <paramref name="topGear"/>. <paramref name="lastGear"/> is the gear
    /// the driver last asked for, so a change is asked for once.
    /// </summary>
    private static void DriveManual(VehicleSim car, float pedal, ref int lastGear, int topGear = 6)
    {
        var drivetrain = car.Drivetrain;
        var up = drivetrain.Engine.Rpm > 4000f && drivetrain.ClutchLocked && drivetrain.Gear == lastGear && drivetrain.Gear < topGear;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = pedal, Steer = Follow(car), ShiftUp = up }, Dt);
        if (up) lastGear = drivetrain.Gear;
    }

    /// <summary>Holds <paramref name="kmh"/> for <paramref name="seconds"/> with a proportional-integral foot, whatever the gearbox.</summary>
    private static void Hold(VehicleSim car, float kmh, float seconds)
    {
        var target = kmh / MathUtil.KmhPerMps;
        var integral = 0f;
        for (var t = 0f; t < seconds; t += Dt)
        {
            var error = target - car.Speed;
            integral = MathUtil.Clamp(integral + error * Dt, -4f, 4f);
            Drive(car, MathUtil.Clamp01(0.3f + 0.15f * error + 0.05f * integral));
        }
    }

    /// <summary>Seconds the car takes to reach <paramref name="kmh"/> with <paramref name="step"/> driving it, or -1 past <paramref name="limit"/>.</summary>
    private static float SecondsTo(VehicleSim car, float kmh, float limit, Action step)
    {
        for (var t = 0f; t < limit; t += Dt)
        {
            step();
            if (car.Speed * MathUtil.KmhPerMps >= kmh) return t + Dt;
        }
        return -1f;
    }

    /// <summary>A straight climbing <paramref name="rise"/> m over <paramref name="length"/> m, eased in and out, so steepest at its middle.</summary>
    private static TrackGeometry Hill(float length, float rise)
    {
        var common = new TrackBuilder(PathKind.Common, Vector3.Zero, 0f).Straight(length, rise);
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed).Straight(50f);
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt).Straight(50f);
        var final = TrackBuilder.ContinueFrom(speed.Path, PathKind.Final).Straight(50f);
        return new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, null, rollingGround: 0f);
    }

    /// <summary>
    /// Brings the car to <paramref name="kmh"/> in D at two thirds throttle, as a driver joins a fast road, then holds it
    /// for <paramref name="seconds"/> with a proportional-integral foot. Returns the gear changes during the last
    /// <paramref name="judged"/> seconds of the hold.
    /// </summary>
    private static int Cruise(VehicleSim car, float kmh, float seconds, float judged)
    {
        var target = kmh / MathUtil.KmhPerMps;
        for (var t = 0f; t < 60f && car.Speed < target - 1f; t += Dt) Drive(car, 0.65f);
        var integral = 0f;
        var changes = 0;
        var gear = car.Drivetrain.Gear;
        for (var t = 0f; t < seconds; t += Dt)
        {
            var error = target - car.Speed;
            integral = MathUtil.Clamp(integral + error * Dt, -4f, 4f);
            Drive(car, MathUtil.Clamp01(0.4f + 0.15f * error + 0.05f * integral));
            if (car.Drivetrain.Gear != gear && t >= seconds - judged) changes++;
            gear = car.Drivetrain.Gear;
        }
        return changes;
    }

    /// <summary>
    /// Subaru's 8.2 s to 100 km/h and 210 km/h for the automatic (Subaru Deutschland's 2017 data), each within a few per
    /// cent, floored in D from rest with the brake let off at once (no stall start); MotorWeek's automatic BRZ took 7.7 s
    /// to 60 mph. Flat out the gearbox changes up at 7,000 rpm and its sixth (0.582) cannot hold what fifth reaches, so the
    /// car tops out in fifth at about 5,300 rpm and stays there: the published 210 is fifth gear's.
    /// </summary>
    [Fact]
    public void The_automatic_runs_to_100_and_flat_out_as_Subaru_publishes()
    {
        var car = InDrive(12000f);
        float to60 = -1f, to100 = -1f;
        var gears = new List<int>();
        for (var t = 0f; t < 120f; t += Dt)
        {
            Drive(car, 1f);
            if (to60 < 0f && car.SpeedMph >= 60f) to60 = t + Dt;
            if (to100 < 0f && car.Speed * MathUtil.KmhPerMps >= 100f) to100 = t + Dt;
            if (gears.Count == 0 || gears[^1] != car.Drivetrain.Gear) gears.Add(car.Drivetrain.Gear);
        }
        var top = car.Speed * MathUtil.KmhPerMps;
        output.WriteLine($"automatic: 0-60 mph {to60:F2} s, 0-100 km/h {to100:F2} s, flat out {top:F1} km/h in {car.Drivetrain.Gear} at {car.Drivetrain.Engine.Rpm:F0} rpm; gears {string.Join(" ", gears)}");
        Assert.InRange(to100, 7.9f, 8.5f);
        Assert.InRange(to60, 7.3f, 8.1f);
        Assert.InRange(top, 204f, 216f);
        Assert.Equal(5, car.Drivetrain.Gear);
        Assert.Equal([1, 2, 3, 4, 5], gears);
        Assert.True(car.Drivetrain.ClutchLocked, "the lock-up clutch should be closed flat out in fifth");
        Assert.True(car.OnRoad);
    }

    /// <summary>
    /// The converter's stall test, as a workshop manual has it: the turbine held, full throttle, the engine settles at the
    /// stall speed (2,540 +/- 150 rpm for this unit) and the turbine's torque is the engine's times the stall torque ratio
    /// (1.95). On the road the multiplication is the launch: floored from rest in D the converter multiplies by most of its
    /// stall ratio while the turbine is slow, the driven wheels get more than the engine's peak torque could give them
    /// through first gear on a rigid coupling, and the multiplication falls to one as the converter couples.
    /// </summary>
    [Fact]
    public void The_converter_stalls_at_its_stall_speed_and_multiplies_the_torque_at_launch()
    {
        var spec = VehicleSpec.Boxer;
        var box = spec.Automatic!;
        var dt = VehicleSim.FixedStep;
        var drivetrain = new Drivetrain(spec) { Mode = TransmissionMode.Automatic };
        drivetrain.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, 0f, 0f, dt);
        for (var t = 0f; t < 3f; t += dt) drivetrain.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f }, 0f, 0f, dt);
        var through = drivetrain.Engine.LastTorque * box.RatioForGear(1) * box.FinalDrive * box.DrivelineEfficiency;
        output.WriteLine($"stall: {drivetrain.Engine.Rpm:F0} rpm, torque ratio {drivetrain.ConverterTorqueRatio:F3}, axle {drivetrain.AxleTorque:F0} N m against {through:F0} through first gear");
        Assert.InRange(drivetrain.Engine.Rpm, 2390f, 2690f);
        Assert.InRange(drivetrain.ConverterTorqueRatio, 1.93f, 1.97f);
        Assert.InRange(drivetrain.AxleTorque / through, 1.93f, 1.97f);

        var car = InDrive();
        var least = float.MaxValue;
        var most = 0f;
        for (var t = 0f; t < 1f; t += Dt)
        {
            Drive(car, 1f);
            if (t < 0.25f) least = MathF.Min(least, car.Drivetrain.ConverterTorqueRatio);
            most = MathF.Max(most, car.Drivetrain.AxleTorque);
        }
        for (var t = 0f; t < 2f; t += Dt) Drive(car, 1f);
        var rigid = spec.Engine.FullThrottleCurve.Max(p => p.Torque) * box.RatioForGear(1) * box.FinalDrive * box.DrivelineEfficiency;
        output.WriteLine($"launch: torque ratio at least {least:F2} over the first 0.25 s, the driven axle up to {most:F0} N m against {rigid:F0} from the peak torque through first gear; at {car.Speed * MathUtil.KmhPerMps:F0} km/h the torque ratio is {car.Drivetrain.ConverterTorqueRatio:F2}");
        Assert.True(least > 1.6f, $"the converter multiplied the launch torque only {least:F2} times");
        Assert.True(most > 1.1f * rigid, $"the launch's axle torque {most:F0} N m is no more than a rigid coupling's {rigid:F0}");
        Assert.Equal(1f, car.Drivetrain.ConverterTorqueRatio, 0.01f);
    }

    /// <summary>
    /// Creep: in D at idle with the brake let off the converter's drag moves the car off and holds it at a walking pace (5-8
    /// km/h on the flat is what automatics do), the idle governor holding the engine; with the brake on it stays put.
    /// </summary>
    [Fact]
    public void At_idle_in_D_the_car_creeps_and_the_brake_holds_it()
    {
        var car = InDrive();
        Assert.Equal(1, car.Drivetrain.Gear);
        Assert.InRange(car.Speed, -0.05f, 0.05f);
        var held = car.Drivetrain.Engine.Rpm;
        for (var t = 0f; t < 12f; t += Dt) Drive(car, 0f);
        var kmh = car.Speed * MathUtil.KmhPerMps;
        output.WriteLine($"creep: {kmh:F1} km/h after 12 s at {car.Drivetrain.Engine.Rpm:F0} rpm (held on the brake at {held:F0}), gear {car.Drivetrain.Gear}");
        Assert.InRange(kmh, 3f, 9f);
        Assert.True(car.Drivetrain.Engine.Running);
        Assert.Equal(1, car.Drivetrain.Gear);
        Assert.InRange(held, 600f, car.Spec.Engine.IdleRpm + 50f);
    }

    /// <summary>
    /// The lock-up clutch: at a steady 100 km/h on the flat the gearbox is in sixth with the lock-up clutch closed, and the
    /// engine turns exactly with the gearbox's input shaft (no converter slip), at about 2,000 rpm as the real car does.
    /// </summary>
    [Fact]
    public void The_lock_up_clutch_ties_the_engine_to_the_shaft_in_top_gear()
    {
        var car = InDrive(6000f);
        Cruise(car, 100f, 60f, 0f);
        var drivetrain = car.Drivetrain;
        var axle = 0.5f * (car.Wheels[2].AngularVelocity + car.Wheels[3].AngularVelocity);
        var shaftRpm = axle * drivetrain.Ratio * EngineModel.RpmPerRadPerSec;
        output.WriteLine($"100 km/h: gear {drivetrain.Gear}, lock-up {drivetrain.ClutchLocked}, engine {drivetrain.Engine.Rpm:F1} rpm, shaft {shaftRpm:F1} rpm");
        Assert.Equal(6, drivetrain.Gear);
        Assert.True(drivetrain.ClutchLocked);
        Assert.Equal(shaftRpm, drivetrain.Engine.Rpm, 1f);
        Assert.InRange(drivetrain.Engine.Rpm, 1900f, 2150f);
    }

    /// <summary>
    /// Hunting: held at a steady 90 km/h on the flat (the throttle about two fifths), the gearbox settles in sixth and
    /// stays there; the downshift lines lie a fifth under the upshift ones, so a steady speed and throttle sit between them.
    /// </summary>
    [Fact]
    public void A_steady_90_km_h_on_the_flat_does_not_hunt_between_gears()
    {
        var car = InDrive(6000f);
        var changes = Cruise(car, 90f, 70f, 40f);
        output.WriteLine($"90 km/h: {car.Speed * MathUtil.KmhPerMps:F1} km/h in {car.Drivetrain.Gear}, lock-up {car.Drivetrain.ClutchLocked}, throttle {car.Drivetrain.Engine.Throttle:F2}; {changes} changes in the last 40 s");
        Assert.Equal(0, changes);
        Assert.Equal(6, car.Drivetrain.Gear);
        Assert.InRange(car.Speed * MathUtil.KmhPerMps, 88f, 92f);
    }

    /// <summary>
    /// Kickdown: cruising at 80 km/h in top gear, the pedal floored, the gearbox goes down as far as pulls hardest under
    /// 6,000 rpm (second, in one shift, at about 5,700 rpm), the engine flaring ahead of the turbine while the drive sags,
    /// and within a second the car pulls at full throttle in that gear.
    /// </summary>
    [Fact]
    public void A_kickdown_from_80_km_h_drops_to_the_gear_that_pulls_hardest()
    {
        var car = InDrive(6000f);
        Cruise(car, 80f, 40f, 0f);
        Assert.Equal(6, car.Drivetrain.Gear);
        var lowest = car.Drivetrain.Gear;
        var reached = -1f;
        var start = car.Speed;
        float to120 = -1f;
        for (var t = 0f; t < 10f && to120 < 0f; t += Dt)
        {
            Drive(car, 1f);
            lowest = Math.Min(lowest, car.Drivetrain.Gear);
            if (reached < 0f && !car.Drivetrain.Shifting && car.Drivetrain.Gear < 6) reached = t + Dt;
            if (car.Speed * MathUtil.KmhPerMps >= 120f) to120 = t + Dt;
        }
        output.WriteLine($"kickdown from {start * MathUtil.KmhPerMps:F0} km/h: down to {lowest} in {reached:F2} s; 80-120 km/h in {to120:F2} s");
        Assert.Equal(2, lowest);
        Assert.InRange(reached, 0.4f, 1.0f);
        Assert.InRange(to120, 4f, 7f);
    }

    /// <summary>
    /// The stop from 100 km/h, pedal hard down, in both gearboxes: the tests' 39 m (ADAC "just under 39 m", December 2014;
    /// Motor Trend's 120 ft from 60 mph and Car and Driver's 165 ft from 70 mph, 39.2 and 39.6 m from 100 km/h), within a
    /// few per cent. The automatic changes down as the car slows and never up, and is in first, its engine running, when
    /// it stops.
    /// </summary>
    [Fact]
    public void The_stop_from_100_km_h_is_the_road_tests_in_both_gearboxes()
    {
        var manual = HandlingRig.Stop(VehicleSpec.Boxer, 100f);

        var car = InDrive(6000f);
        while (car.Speed < 100f / MathUtil.KmhPerMps) Drive(car, 0.8f);
        var start = car.Position;
        var gear = car.Drivetrain.Gear;
        var upshifted = false;
        var t = 0f;
        for (; car.Speed > 0.05f && t < 10f; t += Dt)
        {
            Drive(car, 0f, 1f);
            upshifted |= car.Drivetrain.Gear > gear;
            gear = car.Drivetrain.Gear;
        }
        var automatic = Vector3.Distance(start, car.Position);
        for (var hold = 0f; hold < 1f; hold += Dt) Drive(car, 0f, 1f);
        output.WriteLine($"100-0 km/h: manual {manual.Distance:F2} m in {manual.Seconds:F2} s, automatic {automatic:F2} m in {t:F2} s, stopped in {car.Drivetrain.Gear}");
        Assert.InRange(manual.Distance, 38f, 40f);
        Assert.InRange(automatic, 38f, 40f);
        Assert.False(upshifted, "the automatic changed up while braking");
        Assert.Equal(1, car.Drivetrain.Gear);
        Assert.True(car.Drivetrain.Engine.Running);
    }

    /// <summary>
    /// The automatic round the track: the attract mode's scripted driver laps both routes in D with no crash (the
    /// drawbridge's jump and the loop with the converter's drive and the shift schedule), the engine on its limiter only
    /// for moments (with the driven wheels light over a crest, as any gearbox would let it), and no change of gear undone
    /// within half a second: the gearbox follows the driver's foot (a lift, a kickdown) without hunting.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_automatic_laps_both_routes(bool stunt)
    {
        var session = new RaceSession(OriginalInspiredTrack.Build(), VehicleSpec.Boxer, RaceOptions.MediumNoTraffic);
        var driver = new ScriptedDriver { PreferStunt = stunt };
        session.ChooseTransmission(TransmissionMode.Automatic);
        var changes = 0;
        var undone = 0;
        var gear = 0;
        var lastStep = 0;
        var lastChange = float.NegativeInfinity;
        var cut = 0;
        var t = 0f;
        for (; t < 240f && session.CompletedLaps == 0; t += Dt)
        {
            session.Step(driver.Next(session), Dt);
            var drivetrain = session.Car.Drivetrain;
            if (drivetrain.Gear != gear && gear > 0 && drivetrain.Gear > 0)
            {
                var step = Math.Sign(drivetrain.Gear - gear);
                if (step == -lastStep && t - lastChange < 0.5f) undone++;
                (lastStep, lastChange) = (step, t);
                changes++;
            }
            gear = drivetrain.Gear;
            if (drivetrain.Engine.LimiterCut) cut++;
            if (session.Phase == SessionPhase.Replay) session.Abort();
        }
        output.WriteLine($"{(stunt ? "stunt" : "speed")} route in D: lap {session.BestLapTime:F1} s, {changes} gear changes ({undone} undone within 0.5 s), {cut * Dt:F2} s on the limiter, {session.CrashCount} crashes");
        Assert.Equal(1, session.CompletedLaps);
        Assert.Equal(0, session.CrashCount);
        Assert.Equal(0, undone);
        Assert.True(cut * Dt < 1f, $"{cut * Dt:F2} s on the limiter");
    }

    /// <summary>
    /// The road load is the EPA's coast-down of the car (its test-car data for the 2017-19 BRZ with the automatic: A 32.33
    /// lbf, B 0.1765 lbf/mph, C 0.0177 lbf/mph^2): coasting in neutral at 100 and at 180 km/h the car slows as that force
    /// slows its mass and its wheels, within a few per cent.
    /// </summary>
    [Theory]
    [InlineData(100f)]
    [InlineData(180f)]
    public void The_road_load_is_the_EPA_coast_down(float kmh)
    {
        var spec = VehicleSpec.Boxer;
        var car = new VehicleSim(spec, HandlingRig.Proving(6000f));
        car.LaunchAt(kmh / MathUtil.KmhPerMps + 1f);
        for (var t = 0f; t < 0.5f; t += Dt) car.Step(new DriverInputs { Gear = 0, Steer = Follow(car) }, Dt);
        var mph = car.SpeedMph;
        var speed = car.Speed;
        const float window = 1f;
        for (var t = 0f; t < window; t += Dt) car.Step(new DriverInputs { Gear = 0, Steer = Follow(car) }, Dt);
        var deceleration = (speed - car.Speed) / window;
        var v = 0.5f * (mph + car.SpeedMph);
        var force = (32.33f + 0.1765f * v + 0.0177f * v * v) * 4.448222f;
        // The wheels' own inertia slows with the car.
        var expected = force / (spec.MassKg + 4f * spec.WheelInertia / (spec.WheelRadius * spec.WheelRadius));
        output.WriteLine($"coasting at {v / MathUtil.MphPerMps * MathUtil.KmhPerMps:F0} km/h: {deceleration:F3} m/s^2, the EPA's road load {force:F0} N gives {expected:F3}");
        Assert.InRange(deceleration / expected, 0.95f, 1.05f);
    }

    /// <summary>
    /// The FA20's curve (EngineSpec.BoxerFour): the published points (205 N m at 6,400-6,600, 200 PS at 7,000, where the
    /// power peaks) and the stock dyno's shape between them: the lump at 2,500-3,300 within a few per cent of the peak,
    /// the dip at about 4,000 some 14 % under it, and the climb back to the plateau by 4,600.
    /// </summary>
    [Fact]
    public void The_boxer_four_has_the_FA20s_dyno_shape_through_the_published_points()
    {
        var engine = VehicleSpec.Boxer.Engine;
        var peak = 0f;
        var peakPowerRpm = 0f;
        for (var rpm = 1000f; rpm <= engine.RedlineRpm; rpm += 50f)
        {
            var power = engine.CurveTorque(rpm) * rpm / EngineModel.RpmPerRadPerSec;
            if (power <= peak) continue;
            peak = power;
            peakPowerRpm = rpm;
        }
        output.WriteLine($"FA20: {peak / 735.5f:F1} PS at {peakPowerRpm:F0}; 3000 {engine.CurveTorque(3000f):F0}, 4000 {engine.CurveTorque(4000f):F0}, 4600 {engine.CurveTorque(4600f):F0}, 6500 {engine.CurveTorque(6500f):F0} N m");
        Assert.Equal(205f, engine.CurveTorque(6400f), 0.01f);
        Assert.Equal(205f, engine.CurveTorque(6600f), 0.01f);
        Assert.Equal(7000f, peakPowerRpm);
        Assert.InRange(peak / 735.5f, 199.5f, 200.5f);
        Assert.InRange(engine.CurveTorque(3000f) / 205f, 0.95f, 0.99f);
        Assert.InRange(engine.CurveTorque(4000f) / engine.CurveTorque(3000f), 0.84f, 0.90f);
        Assert.InRange(engine.CurveTorque(4600f) / 205f, 0.96f, 1f);
    }

    /// <summary>
    /// The gearbox modes: in the automatic the BRZ drives through its own six-speed's ratios and final drive (Aisin's
    /// 3.538-0.582 on 4.100) and its converter, in the two manual modes through the six-speed manual's (3.626-0.767 on
    /// 4.10) and its clutch; every other car has no converter, and its automatic shifts its manual gearbox as before.
    /// </summary>
    [Fact]
    public void The_manual_modes_keep_the_manual_gearbox_and_only_the_BRZ_has_a_converter()
    {
        foreach (var mode in new[] { TransmissionMode.Automatic, TransmissionMode.ManualAutoClutch, TransmissionMode.ManualClutch })
        {
            var drivetrain = new Drivetrain(VehicleSpec.Boxer) { Mode = mode };
            var automatic = mode == TransmissionMode.Automatic;
            Assert.Equal(automatic, drivetrain.TorqueConverterDrives);
            Assert.Equal(6, drivetrain.ForwardGearCount);
            Assert.Equal(automatic ? 3.538f : 3.626f, drivetrain.RatioForGear(1));
            Assert.Equal(automatic ? 0.582f : 0.767f, drivetrain.RatioForGear(6));
            Assert.Equal(automatic ? -3.168f : -3.437f, drivetrain.RatioForGear(-1));
            Assert.Equal(automatic ? 4.1f : 4.10f, drivetrain.FinalDrive);
        }
        foreach (var spec in VehicleSpec.Catalogue.Where(s => s.Style != CarStyle.Boxer))
        {
            Assert.Null(spec.Automatic);
            var drivetrain = new Drivetrain(spec) { Mode = TransmissionMode.Automatic };
            Assert.False(drivetrain.TorqueConverterDrives);
            Assert.Equal(spec.RatioForGear(1) * spec.FinalDrive, drivetrain.RatioForGear(1) * drivetrain.FinalDrive);
            // Every other engine launches at the generic rule's revs (EngineSpec.LaunchRpm), as before the 0.76 review.
            Assert.True(float.IsNaN(spec.Engine.LaunchRpmOverride), $"{spec.Name} sets its own launch revs");
        }
        // The FA20 launches above its dip, at 4,500 rpm, as Edmunds launched a BRZ (EngineSpec.BoxerFour), and not on
        // the lump under it, 2,450 rpm, where the generic rule puts it.
        Assert.Equal(4500f, VehicleSpec.Boxer.Engine.LaunchRpm);
        Assert.Equal(2450f, (VehicleSpec.Boxer.Engine with { LaunchRpmOverride = float.NaN }).LaunchRpm);

        // The manual car still launches on its clutch, and changes gear on the driver's request.
        var manual = new VehicleSim(VehicleSpec.Boxer, HandlingRig.Proving());
        manual.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        manual.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        manual.Step(new DriverInputs { Gear = 1 }, Dt);
        for (var t = 0f; t < 3f; t += Dt) Drive(manual, 0.6f);
        Assert.Equal(1, manual.Drivetrain.Gear);
        Assert.True(manual.Drivetrain.ClutchLocked);
        manual.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 0.6f, ShiftUp = true }, Dt);
        Assert.Equal(2, manual.Drivetrain.Gear);
    }

    /// <summary>
    /// The springs (0.76): each axle at its own rate, the BRZ's ride frequencies (1.43 Hz in front, 1.47 behind) on its
    /// corners' masses, preloaded so that the car sits at the height it sat at before on the springs shared by load (the
    /// sag of a quarter of the mass on <see cref="VehicleSpec.SuspensionStiffness"/>, which its drawn model and the model
    /// preparations' copy of the geometry are set to) and level. The bars give the real car's balance and roll: 61 % of
    /// the roll stiffness in front and about 2.3 deg/g on the skid pad.
    /// </summary>
    [Fact]
    public void The_springs_ride_at_the_BRZs_frequencies_at_the_drawn_ride_height()
    {
        var spec = VehicleSpec.Boxer;
        var wheelbase = spec.FrontAxle + spec.RearAxle;
        var front = (spec.RearAxle + spec.CentreOfMassOffset.Z) / wheelbase;
        var frontHz = MathF.Sqrt(spec.FrontWheelRate / (spec.MassKg * front / 2f)) / MathF.Tau;
        var rearHz = MathF.Sqrt(spec.RearWheelRate / (spec.MassKg * (1f - front) / 2f)) / MathF.Tau;
        var halfTrackSquared = 2f * spec.HalfTrack * spec.HalfTrack;
        var frontRoll = spec.FrontWheelRate * halfTrackSquared + spec.FrontAntiRoll * 4f * spec.HalfTrack * spec.HalfTrack;
        var rearRoll = spec.RearWheelRate * halfTrackSquared + spec.RearAntiRoll * 4f * spec.HalfTrack * spec.HalfTrack;

        (float Height, float Pitch) AtRest(VehicleSpec car)
        {
            var sim = new VehicleSim(car, HandlingRig.Proving());
            for (var i = 0; i < 240; i++) sim.Step(DriverInputs.Idle, HandlingRig.Dt);
            return (sim.Position.Y, MathF.Asin(Vector3.Dot(sim.Forward, Vector3.UnitY)) * MathUtil.Rad2Deg);
        }
        var (height, pitch) = AtRest(spec);
        var (shared, _) = AtRest(spec with { FrontWheelRate = float.NaN, RearWheelRate = float.NaN });
        var circle = HandlingRig.Circle(spec, 40f, 15f);
        var roll = circle.RollDeg / circle.LateralG;
        output.WriteLine($"ride {frontHz:F2} / {rearHz:F2} Hz; roll stiffness {frontRoll / (frontRoll + rearRoll):P1} in front, {roll:F2} deg/g; at rest {height:F4} m up (on springs shared by load {shared:F4}), pitch {pitch:F2} deg");
        Assert.InRange(frontHz, 1.41f, 1.45f);
        Assert.InRange(rearHz, 1.45f, 1.49f);
        Assert.InRange(frontRoll / (frontRoll + rearRoll), 0.60f, 0.62f);
        Assert.InRange(roll, 2.1f, 2.5f);
        Assert.Equal(shared, height, 0.001f);
        Assert.InRange(pitch, -0.1f, 0.1f);
    }

    /// <summary>
    /// Part pedal from rest in D (0.76 review; a pad's trigger reaches the car as the player holds it): the car pulls away
    /// to 50 km/h no slower than the manual does for a driver changing up at 4,000 rpm, while the box still changes up
    /// early at a light pedal (under 2,500 rpm at 0.3) as an automatic of its kind does, and at half pedal it reaches
    /// 100 km/h in under half a minute. With the pedal read as a share of the engine's torque, as every car reads it,
    /// the early upshifts left the engine a fifth to two fifths of its torque: 47.7 s to 50 km/h at 0.3 pedal and 12.4 s
    /// at 0.5 (the manual's 18.7 and 8.4), and half pedal never reached 100 km/h (the clutch-shifted automatic of the
    /// manual's gears, before 0.76, took 22.2 s).
    /// </summary>
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.5f)]
    public void Part_pedal_pulls_away_no_slower_than_the_manual_at_moderate_revs(float pedal)
    {
        var car = InDrive();
        var highestUpshift = 0f;
        var gear = car.Drivetrain.Gear;
        var revs = car.Drivetrain.Engine.Rpm;
        void Step()
        {
            Drive(car, pedal);
            if (car.Drivetrain.Gear > gear) highestUpshift = MathF.Max(highestUpshift, revs);
            gear = car.Drivetrain.Gear;
            revs = car.Drivetrain.Engine.Rpm;
        }
        var to50 = SecondsTo(car, 50f, 40f, Step);
        var to100 = SecondsTo(car, 100f, 40f, Step);
        if (to100 > 0f) to100 += to50;

        var manual = InFirst();
        var lastGear = 1;
        var manualTo50 = SecondsTo(manual, 50f, 40f, () => DriveManual(manual, pedal, ref lastGear));
        output.WriteLine($"pedal {pedal:F1}: in D 0-50 km/h {to50:F1} s, 0-100 {to100:F1} s, changes up at {highestUpshift:F0} rpm at most; the manual changing up at 4,000 rpm 0-50 km/h {manualTo50:F1} s");
        Assert.True(to50 > 0f && to50 <= manualTo50, $"0-50 km/h in D {to50:F1} s against the manual's {manualTo50:F1} s");
        if (pedal < 0.4f) Assert.True(highestUpshift < 2500f, $"at a light pedal the box held a gear to {highestUpshift:F0} rpm");
        else Assert.InRange(to100, 0f, 30f);
    }

    /// <summary>
    /// The pedal pressed at a cruise (0.76 review): from a steady 80 km/h in sixth, half pedal and more asks for more drive
    /// than sixth gives at 1,600 rpm, so the box changes down, and the car gathers speed to 100 km/h no slower than the
    /// manual held in fourth at the same pedal. Before, half pedal neither changed down nor reached 100 km/h, and 0.6-0.8
    /// took 18.4, 13.0 and 6.7 s against the manual's 7.8, 6.0 and 4.9 s.
    /// </summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.6f)]
    [InlineData(0.7f)]
    [InlineData(0.8f)]
    public void A_part_pedal_roll_on_changes_down_and_is_no_slower_than_the_manual_in_fourth(float pedal)
    {
        var car = InDrive(6000f);
        Cruise(car, 80f, 30f, 0f);
        Assert.Equal(6, car.Drivetrain.Gear);
        var lowest = car.Drivetrain.Gear;
        var seconds = SecondsTo(car, 100f, 30f, () =>
        {
            Drive(car, pedal);
            lowest = Math.Min(lowest, car.Drivetrain.Gear);
        });

        var manual = InFirst(6000f);
        var lastGear = 1;
        SecondsTo(manual, 80f, 40f, () => DriveManual(manual, 0.7f, ref lastGear, topGear: 4));
        Hold(manual, 80f, 30f);
        Assert.Equal(4, manual.Drivetrain.Gear);
        var manualSeconds = SecondsTo(manual, 100f, 30f, () => Drive(manual, pedal));
        output.WriteLine($"pedal {pedal:F1} from 80 km/h: in D down to {lowest}, 80-100 km/h in {seconds:F1} s; the manual in fourth {manualSeconds:F1} s");
        Assert.True(lowest < 6, "the box held sixth");
        Assert.True(seconds > 0f && seconds <= manualSeconds, $"80-100 km/h in D {seconds:F1} s against the manual's {manualSeconds:F1} s");
    }

    /// <summary>
    /// Rolling back in D (0.76 review): the schedule reads only forward speed, so a car rolling back down a grade at idle
    /// (the converter's creep is no match for 15.7 %) holds first, and pulls up the grade from there; and D taken while
    /// reversing at speed holds first while the converter's pull slows the roll, the engine running. Read as a speed,
    /// rolling back changed the box up, to second on the grade and to fifth from 66 km/h in reverse, each change dividing
    /// the pull.
    /// </summary>
    [Fact]
    public void Rolling_back_in_D_holds_first_gear()
    {
        var hill = Hill(400f, 40f);
        var car = new VehicleSim(VehicleSpec.Boxer, hill);
        var middle = hill.Common.FrameIndexAt(200f);
        car.Reset(hill.Common.Frames[middle], new PathCursor(hill.Common, middle));
        car.Drivetrain.Mode = TransmissionMode.Automatic;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Brake = 1f }, Dt);
        for (var t = 0f; t < 1f; t += Dt) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f }, Dt);
        var highest = car.Drivetrain.Gear;
        var back = 0f;
        for (var t = 0f; t < 8f; t += Dt)
        {
            Drive(car, 0f);
            highest = Math.Max(highest, car.Drivetrain.Gear);
            back = MathF.Min(back, car.Speed);
        }
        var held = highest;
        // Still facing up the grade: the speed's sign is the roll back's, not a car spun round.
        var uphill = Vector3.Dot(car.Forward, hill.Common.Frames[middle].Tangent);
        for (var t = 0f; t < 4f && car.Speed < 0f; t += Dt) Drive(car, 0.6f);
        output.WriteLine($"rolled back to {back * MathUtil.KmhPerMps:F1} km/h at idle in gear {held} at most; at 0.6 pedal {car.Speed * MathUtil.KmhPerMps:F1} km/h in {car.Drivetrain.Gear}");
        Assert.True(uphill > 0.9f, "the car turned round rolling back");
        Assert.True(back < -2f, $"the car rolled back only to {back * MathUtil.KmhPerMps:F1} km/h");
        Assert.Equal(1, held);
        Assert.True(car.Speed >= 0f, "at 0.6 pedal the car still rolled back after 4 s");

        var reversing = InDrive();
        for (var t = 0f; t < 0.5f; t += Dt) reversing.Step(new DriverInputs { Gear = -1, Brake = 1f }, Dt);
        for (var t = 0f; t < 8f; t += Dt) reversing.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f }, Dt);
        var reverseKmh = reversing.Speed * MathUtil.KmhPerMps;
        reversing.Step(new DriverInputs { Gear = 1 }, Dt);
        var highestInD = reversing.Drivetrain.Gear;
        for (var t = 0f; t < 10f; t += Dt)
        {
            reversing.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, Dt);
            highestInD = Math.Max(highestInD, reversing.Drivetrain.Gear);
        }
        output.WriteLine($"D taken at {reverseKmh:F0} km/h in reverse: gear {highestInD} at most, {reversing.Speed * MathUtil.KmhPerMps:F1} km/h after 10 s, engine running {reversing.Drivetrain.Engine.Running}");
        Assert.True(reverseKmh < -50f);
        Assert.Equal(1, highestInD);
        Assert.True(reversing.Drivetrain.Engine.Running);
    }
}
