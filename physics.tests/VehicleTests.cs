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

    /// <summary>
    /// Drives flat out with keyboard-style sequential shifts near the redline (key pressed once per shift), the throttle
    /// fed in against wheelspin the way a magazine tester launches (<see cref="ScriptedDriver.FeatherThrottle"/>): since
    /// the tyre loses force when it spins past its peak, a launch on the stop with the wheels at three times the road
    /// speed is no longer nearly as quick as a clean one. The driver keeps the car in the middle of the road: left alone
    /// for a two-minute top-speed run, the potholes' knocks walked it off the road.
    /// </summary>
    private static void DriveFlatOut(VehicleSim car, float seconds, Action<VehicleSim>? each = null)
    {
        var steps = (int)(seconds / Dt);
        var lastGear = car.Drivetrain.Gear;
        for (var i = 0; i < steps; i++)
        {
            var shift = car.Drivetrain.Engine.Rpm > car.Spec.Engine.RedlineRpm * 0.93f && car.Drivetrain.ClutchLocked && car.Drivetrain.Gear < car.Spec.ForwardGearCount && car.Drivetrain.Gear == lastGear;
            car.Step(new DriverInputs { Throttle = ScriptedDriver.FeatherThrottle(car, 1f), Steer = FollowRoad(car), Gear = DriverInputs.NoGearChange, ShiftUp = shift }, Dt);
            if (shift) lastGear = car.Drivetrain.Gear;
            each?.Invoke(car);
        }
    }

    /// <summary>
    /// Seconds to 60 mph from a standing start made as the road tests that publish the figures make it: first gear in,
    /// brake and throttle held a moment so the revs wait at the launch speed with the clutch open, the brake released,
    /// the throttle fed in against wheelspin (<see cref="ScriptedDriver.FeatherThrottle"/>), and each gear held until
    /// <see cref="RoadTestShiftDue"/>.
    /// </summary>
    private static float RoadTestZeroToSixty(VehicleSpec spec, float limit = 20f) => RoadTestTimes(spec, limit, 60f)[0];

    /// <summary>
    /// Seconds from the brake's release to each of <paramref name="mph"/> (ascending) in the standing start of
    /// <see cref="RoadTestZeroToSixty"/>, infinite for a speed not reached within <paramref name="limit"/>: the marks a
    /// road test reads off one run, such as 30 and 70 mph for a time through the gears (cars/phys2). The run ends at the
    /// last mark, so to 60 mph it is step for step the run the 0-60 time always was.
    /// </summary>
    private static float[] RoadTestTimes(VehicleSpec spec, float limit, params float[] mph)
    {
        var times = Enumerable.Repeat(float.PositiveInfinity, mph.Length).ToArray();
        var car = RoadTestLaunch(spec);
        var lastGear = car.Drivetrain.Gear;
        var next = 0;
        for (var t = 0f; t < limit && next < mph.Length; t += Dt)
        {
            RoadTestStep(car, ref lastGear);
            while (next < mph.Length && car.SpeedMph >= mph[next]) times[next++] = t + Dt;
        }
        return times;
    }

    /// <summary>The standing quarter mile, the distance a road test's quarter-mile time is taken over.</summary>
    private const float QuarterMileMetres = 402.336f;

    /// <summary>
    /// Seconds from the brake's release to the end of the standing quarter mile in the standing start of
    /// <see cref="RoadTestZeroToSixty"/>, with the speed there (the trap speed a road test gives with it); infinite for a
    /// quarter mile not run within <paramref name="limit"/> (cars/phys2). It is the same run as the 0-60 time's, step
    /// for step, carried on past 60 mph.
    /// </summary>
    private static (float Seconds, float TrapMph) RoadTestQuarterMile(VehicleSpec spec, float limit = 30f)
    {
        var car = RoadTestLaunch(spec);
        var start = car.Cursor.Distance;
        var lastGear = car.Drivetrain.Gear;
        for (var t = 0f; t < limit; t += Dt)
        {
            RoadTestStep(car, ref lastGear);
            if (car.Cursor.Distance - start >= QuarterMileMetres) return (t + Dt, car.SpeedMph);
        }
        return (float.PositiveInfinity, car.SpeedMph);
    }

    /// <summary>
    /// The car of a road test's standing start at the brake's release: the ignition on, first gear in with the clutch
    /// down, then brake and throttle held for a second and a half so the revs wait at the launch speed with the clutch
    /// open (<see cref="RoadTestZeroToSixty"/>).
    /// </summary>
    private static VehicleSim RoadTestLaunch(VehicleSpec spec)
    {
        var car = new VehicleSim(spec, DragStrip());
        car.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f }, Dt);
        car.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, Dt);
        for (var t = 0f; t < 1.5f; t += Dt) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f, Throttle = 1f }, Dt);
        return car;
    }

    /// <summary>
    /// One step of a road test's run after <see cref="RoadTestLaunch"/>: the throttle fed in against wheelspin and the
    /// change up when <see cref="RoadTestShiftDue"/> says, pressed once the clutch has locked in the gear
    /// <paramref name="lastGear"/> (the gear the last change landed in, kept by the caller from step to step).
    /// </summary>
    private static void RoadTestStep(VehicleSim car, ref int lastGear)
    {
        var shift = RoadTestShiftDue(car) && car.Drivetrain.ClutchLocked && car.Drivetrain.Gear == lastGear;
        car.Step(new DriverInputs { Throttle = ScriptedDriver.FeatherThrottle(car, 1f), Steer = FollowRoad(car), Gear = DriverInputs.NoGearChange, ShiftUp = shift }, Dt);
        if (shift) lastGear = car.Drivetrain.Gear;
    }

    /// <summary>
    /// Whether a road tester changes up now: at the limiter (within 1.5 % of it, the rise of a 60 Hz step in first), or
    /// earlier where the next gear already pulls harder (the full-throttle curve times the ratio at the revs the change
    /// lands on). A fixed fraction of the limiter suits a curve that falls away before it; the BRZ's power holds to its
    /// limiter, and its second gear reaches 60 mph only there.
    /// </summary>
    private static bool RoadTestShiftDue(VehicleSim car)
    {
        var spec = car.Spec;
        var gear = car.Drivetrain.Gear;
        if (gear < 1 || gear >= spec.ForwardGearCount) return false;
        var rpm = car.Drivetrain.Engine.Rpm;
        if (rpm > spec.Engine.RedlineRpm * 0.985f) return true;
        var landing = rpm * spec.RatioForGear(gear + 1) / spec.RatioForGear(gear);
        return spec.Engine.CurveTorque(landing) * spec.RatioForGear(gear + 1) >= spec.Engine.CurveTorque(rpm) * spec.RatioForGear(gear);
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

    /// <summary>
    /// The hatchbacks against the figures of the cars they follow: 0-60 mph and top speed, driven flat out from a
    /// standing start with the shifts at the redline and the throttle fed in against wheelspin. Top speed a tenth of the
    /// published figure either side. The sprint starts at idle and counts the shift into first (no revs on the brake, as
    /// the road-test start of <see cref="HandlingTests"/> makes them): 0-60 mph is taken as the published 0-100 km/h
    /// less a twentieth, a tenth below it to a fifth above (a turbo engine floored from idle waits for its boost).
    /// </summary>
    [Theory]
    [InlineData(2, false, 6.7f, 8.9f, 115f, 140f)]   // 205 GTi 1.9: 7.8 s to 62 mph, 205 km/h (127 mph); front-wheel drive
    // Delta Integrale Evoluzione: 5.7 s, 220 km/h (137 mph). It takes 6.48 s from idle, at the very top of its range
    // (6.5 s, a fifth over 5.4): the T3 waits for its boost, and the road-test start is already a tenth slow.
    [InlineData(3, true, 4.9f, 6.5f, 123f, 151f)]
    [InlineData(4, true, 5.6f, 7.5f, 138f, 169f)]    // Golf R32: 6.6 s, 247 km/h (153 mph)
    public void Hatchbacks_match_their_published_figures(int car, bool fourWheelDrive, float zeroToSixtyMin, float zeroToSixtyMax, float topMin, float topMax)
    {
        var spec = VehicleSpec.Catalogue[car];
        Assert.Equal(fourWheelDrive, spec.FourWheelDrive is not null);
        Assert.True(spec.Drives(front: true));
        var sprint = StartedCar(DragStrip(), spec: spec);
        var t = 0f;
        var reached = -1f;
        DriveFlatOut(sprint, 15f, c =>
        {
            t += Dt;
            if (reached < 0f && c.SpeedMph >= 60f) reached = t;
        });
        var top = StartedCar(DragStrip(6000f), spec: spec);
        DriveFlatOut(top, 90f);
        _out.WriteLine($"{spec.Name}: 0-60 mph {reached:F2} s, top speed {top.SpeedMph:F0} mph in gear {top.Drivetrain.Gear} at {top.Drivetrain.Engine.Rpm:F0} rpm");
        Assert.InRange(reached, zeroToSixtyMin, zeroToSixtyMax);
        Assert.InRange(top.SpeedMph, topMin, topMax);
        Assert.True(top.OnRoad);
    }

    /// <summary>
    /// The everyday cars of Turkey's roads against their published figures: 0-60 mph and top speed, driven flat out
    /// from a standing start with the shifts at the redline. They take their time, so the runs are longer.
    /// </summary>
    [Theory]
    [InlineData(5, false, 11.8f, 15.5f, 94f, 104f)]   // 1.6 saloon: 13.9 s to 62 mph, 160 km/h
    [InlineData(6, true, 13.5f, 18.0f, 87f, 97f)]     // 1.4 estate: about 16 s to 62 mph, about 150 km/h
    [InlineData(7, false, 6.6f, 8.1f, 144f, 154f)]    // 2.1 diesel fleet saloon: 7.7 s to 62 mph, 240 km/h (149 mph)
    public void Everyday_cars_match_their_published_figures(int car, bool frontWheelDrive, float zeroToSixtyMin, float zeroToSixtyMax, float topMin, float topMax)
    {
        var spec = VehicleSpec.Catalogue[car];
        Assert.Equal(frontWheelDrive, spec.FrontWheelDrive);
        var sprint = StartedCar(DragStrip(), spec: spec);
        var t = 0f;
        var reached = -1f;
        DriveFlatOut(sprint, 25f, c =>
        {
            t += Dt;
            if (reached < 0f && c.SpeedMph >= 60f) reached = t;
        });
        var top = StartedCar(DragStrip(9000f), spec: spec);
        DriveFlatOut(top, 150f);
        _out.WriteLine($"{spec.Name}: 0-60 mph {reached:F2} s, top speed {top.SpeedMph:F0} mph in gear {top.Drivetrain.Gear} at {top.Drivetrain.Engine.Rpm:F0} rpm");
        Assert.InRange(reached, zeroToSixtyMin, zeroToSixtyMax);
        Assert.InRange(top.SpeedMph, topMin, topMax);
        Assert.True(top.OnRoad);
    }

    /// <summary>
    /// The five cars of cars13 against the figures of the cars they follow: 0-60 mph launched as the road tests that
    /// publish the figures launch (<see cref="RoadTestZeroToSixty"/>), within a tenth either side of the published
    /// 0-100 km/h less a twentieth, and top speed a tenth of the published figure either side; the Leon Cupra's 250 km/h
    /// (155 mph) is its limiter's, which holds the car within a few per cent of it rather than the drag, so its window
    /// is the limiter's. The BRZ has none: its 226 km/h is the drag's, and it runs about 8 % past it here
    /// (VehicleSpec.Boxer).
    /// <para>
    /// Until the cars13 review this test drove them as <see cref="Hatchbacks_match_their_published_figures"/> drives the
    /// hatchbacks, floored from idle with the shifts at 0.93 of the limiter, a tenth under the figure to a fifth over:
    /// the shift into first and the revs rising from idle cost the cars about half a second before they move (a fifth
    /// on the 458 with its launch control), and the fifth over, meant for a turbo waiting for its boost, carried the
    /// naturally aspirated cars too. Driven that way the BRZ took 8.28 s, its second gear given up at 87 km/h for a third
    /// gear landing in the FA20's trough, where its second runs on to the limiter just short of 60 mph; launched as Edmunds launched one (7.3 s to 60 mph, the quarter mile in
    /// 15.3 s at 92.1 mph) it takes 7.2 s and runs the quarter in 15.4 s at 94 mph, so the car was right and the method
    /// was not. All five are now held to a tenth either side, the turbo Cupra too.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(8, false, 2.9f, 3.55f, 182f, 222f)]   // 458 Italia: under 3.4 s to 100 km/h, over 325 km/h (202 mph); rear-wheel drive
    [InlineData(9, true, 7.3f, 8.9f, 119f, 146f)]     // Bravo HGT 2.0 20V: 8.5 s, 213 km/h (132 mph)
    [InlineData(10, true, 9.4f, 11.5f, 103f, 126f)]   // Bravo 1.6 16V: 11.0 s, 184 km/h (114 mph)
    // Leon Cupra five-door with the 245 PS tune of its platform (cars/phys2): the Golf GTI Performance's 6.2 s, 250 km/h
    // limited (155 mph). The 300 PS alternative (docs/design.md item 23): 5.0-6.05 s round SEAT's 5.8 s for the five-door.
    [InlineData(11, true, 5.3f, 6.5f, 150f, 158f)]
    [InlineData(12, false, 6.5f, 7.9f, 126f, 154f)]   // BRZ: 7.6 s, 226 km/h (140 mph)
    public void The_cars_of_cars13_match_their_published_figures(int car, bool frontWheelDrive, float zeroToSixtyMin, float zeroToSixtyMax, float topMin, float topMax)
    {
        var spec = VehicleSpec.Catalogue[car];
        Assert.Equal(frontWheelDrive, spec.FrontWheelDrive);
        Assert.Null(spec.FourWheelDrive);
        var reached = RoadTestZeroToSixty(spec);
        var top = StartedCar(DragStrip(9000f), spec: spec);
        // The quick car is at its top speed within a minute and would run off the strip's end in two; the slow ones need the two.
        DriveFlatOut(top, topMax > 170f ? 60f : 120f);
        _out.WriteLine($"{spec.Name}: 0-60 mph {reached:F2} s, top speed {top.SpeedMph:F0} mph ({top.SpeedMph / MathUtil.MphPerMps * MathUtil.KmhPerMps:F0} km/h) in gear {top.Drivetrain.Gear} at {top.Drivetrain.Engine.Rpm:F0} rpm");
        Assert.InRange(reached, zeroToSixtyMin, zeroToSixtyMax);
        Assert.InRange(top.SpeedMph, topMin, topMax);
        Assert.True(top.OnRoad);
    }

    /// <summary>
    /// The Leon Cupra's 245 PS tune against the one independent road test of its platform found (cars/phys2,
    /// VehicleSpec.CupraTsi): Auto Express timed a six-speed manual Octavia vRS 245 hatch from 30 to 70 mph through the
    /// gears in 5.5 s ("Skoda Octavia vRS 245 vs Ford Focus ST", 16 August 2017; 1,365 kg at the kerb against the Leon's
    /// 1,320 without its driver), held here to a tenth either side. Through the gears the launch does not count, so the
    /// time reads the curve, the boost, the gearing and the mass; the magazine's 0-60 mph (6.8 s, slower than Skoda's own
    /// 6.6 s to 62 mph for the car) is left to the makers' figures the test above holds the launch to.
    /// </summary>
    [Fact]
    public void The_Leon_Cupra_runs_30_to_70_mph_through_the_gears_as_its_platforms_road_test()
    {
        var spec = VehicleSpec.Catalogue[11];
        var times = RoadTestTimes(spec, 20f, 30f, 60f, 70f);
        _out.WriteLine($"{spec.Name}: 30 mph at {times[0]:F2} s, 60 at {times[1]:F2} s, 70 at {times[2]:F2} s: 30-70 mph {times[2] - times[0]:F2} s");
        Assert.InRange(times[2] - times[0], 4.95f, 6.05f);
    }

    /// <summary>
    /// The Leon Cupra's 245 PS tune over the standing quarter mile (cars/phys2, VehicleSpec.CupraTsi), launched as the
    /// 0-60 mph run. The one quarter-mile time of the tune found is Wheels' (WhichCar): a 2021 Skoda Octavia RS, the
    /// next generation of the platform with the same 180 kW and 370 N m and the seven-speed DSG, timed with a DriftBox at
    /// 14.47 s and 163.08 km/h (101.3 mph), its best run, with 6.15 s to 97 km/h and 6.41 s to 100 (Scott Newman,
    /// "2021 Skoda Octavia RS review: performance tested", whichcar.com.au, 24 September 2021). Held here to a tenth
    /// either side, the time and the trap speed alike, as every cars13 figure is: a larger car on the other gearbox, so
    /// the window checks the tune's pace and speed over the distance rather than the Leon's own figure (no quarter-mile
    /// time was found for the Mk3 platform's 245 PS cars).
    /// </summary>
    [Fact]
    public void The_Leon_Cupra_runs_the_quarter_mile_as_its_tune_was_timed()
    {
        var spec = VehicleSpec.Catalogue[11];
        var (seconds, trapMph) = RoadTestQuarterMile(spec);
        _out.WriteLine($"{spec.Name}: the quarter mile in {seconds:F2} s at {trapMph:F1} mph ({trapMph / MathUtil.MphPerMps * MathUtil.KmhPerMps:F1} km/h)");
        Assert.InRange(seconds, 13.0f, 15.9f);
        Assert.InRange(trapMph, 91.2f, 111.5f);
    }

    /// <summary>
    /// Every car is the size of the car it follows (dims6): its wheelbase within a hundredth of the published one, and on
    /// the four respecified to the cars the game draws (the 205 GTi 1.9, the Delta Integrale Evoluzione, the Golf R32 and
    /// the E 250 CDI) its one track within a hundredth of the published front and rear tracks' mean. Until dims6 those
    /// four kept the wheelbase and track of the cars they had followed before, and the drawn models, scaled to the
    /// simulation's wheelbase, came out 2-9 % off: the E-Class at 92 % of its size.
    /// </summary>
    [Theory]
    [InlineData(0, 2.550f, 0f)]           // Testarossa: 2550 mm (getoutlines.com's drawing, as BodyStyle.FlatTwelve's keys); tracks 1520 / 1660 as usually published (the drawing's 1677 behind), not modelled apart
    [InlineData(1, 2.670f, 0f)]           // McLaren 570S: 2670 mm
    [InlineData(2, 2.420f, 1.3605f)]      // 205 GTi 1.9: 2420 mm, tracks 1382 / 1339 (auto motor und sport)
    [InlineData(3, 2.480f, 1.501f)]       // Delta Integrale Evoluzione: 2480 mm, tracks 1502 / 1500 (supercars.net)
    [InlineData(4, 2.518f, 1.500f)]       // Golf R32 (2003): 2518 mm, tracks 1513 / 1487 (auto-data.net, CarExpert)
    [InlineData(5, 2.490f, 0f)]           // Tofas Sahin (the Fiat 131 body): 2490 mm
    [InlineData(6, 2.441f, 0f)]           // Renault 12: 2441 mm
    [InlineData(7, 2.874f, 1.606f)]       // E 250 CDI (W212 facelift): 2874 mm, tracks 1598 / 1614 (auto-data.net, thecarspec, cararac; 1604 behind at cars-data)
    [InlineData(8, 2.650f, 1.639f)]       // 458 Italia: 2650 mm, tracks 1672 / 1606 (Ferrari)
    [InlineData(9, 2.540f, 1.4505f)]      // Bravo HGT 2.0 20V: 2540 mm, tracks 1471 / 1430 (auto-data.net, cars-data)
    [InlineData(10, 2.540f, 1.440f)]      // Bravo 1.6 16V: 2540 mm, tracks 1439 / 1441 on 14 in wheels (cars-data)
    [InlineData(11, 2.631f, 1.523f)]      // Leon Cupra five-door: 2631 mm, tracks 1536 / 1510 on 19 in wheels (SEAT's sheet: 1534 / 1511 on 18 in)
    [InlineData(12, 2.570f, 1.530f)]      // BRZ: 2570 mm, tracks 1520 / 1540 (Subaru's 2013 sheet: 59.8 / 60.6 in)
    public void Every_car_has_the_size_of_the_car_it_follows(int car, float wheelbase, float meanTrack)
    {
        var spec = VehicleSpec.Catalogue[car];
        var simulated = spec.FrontAxle + spec.RearAxle;
        _out.WriteLine($"{spec.Name}: wheelbase {simulated:F3} m against {wheelbase:F3}, track {2f * spec.HalfTrack:F3} m");
        Assert.InRange(simulated / wheelbase, 0.99f, 1.01f);
        if (meanTrack > 0f) Assert.InRange(2f * spec.HalfTrack / meanTrack, 0.99f, 1.01f);
    }

    /// <summary>
    /// The two model preparations, tools/car_models.py for the game and unreal/Scripts/prepare_car_model.py for the Unreal
    /// level, scale each drawn model to the simulation's wheelbase and set its hubs at the rest hub height from their own
    /// copy of the geometry (SPECS): a spec changed without them draws the car at the size it had before, which is how the
    /// respecified cars came to be drawn off their size until dims6. The game only logs a model whose hubs are off.
    /// </summary>
    [Theory]
    [InlineData("tools/car_models.py")]
    [InlineData("unreal/Scripts/prepare_car_model.py")]
    public void The_model_preparations_have_the_simulations_geometry(string file)
    {
        var text = File.ReadAllText(Path.Combine(TrackTests.FindRepoRoot(), file));
        var keys = new Dictionary<CarStyle, string>
        {
            [CarStyle.FlatTwelve] = "flat12", [CarStyle.V8] = "v8", [CarStyle.HotHatch] = "hothatch", [CarStyle.FiveHatch] = "five",
            [CarStyle.TurboHatch] = "turbohatch", [CarStyle.Saloon] = "saloon", [CarStyle.Estate] = "estate", [CarStyle.Fleet] = "fleet",
            // cars13: the model file keys the parallel model track delivers (game/assets/models/<key>.glb).
            [CarStyle.FlatPlaneV8] = "458", [CarStyle.HgtFive] = "bravohgt", [CarStyle.HatchSixteen] = "bravo16", [CarStyle.CupraTsi] = "leoncupra", [CarStyle.Boxer] = "brz",
        };
        foreach (var spec in VehicleSpec.Catalogue)
        {
            var key = keys[spec.Style];
            var match = System.Text.RegularExpressions.Regex.Match(text, $@"""{key}"": dict\(([^)]*)\)");
            Assert.True(match.Success, $"{file} has no SPECS entry for {key}");
            var values = match.Groups[1].Value.Split(',').Select(p => p.Split('='))
                .ToDictionary(p => p[0].Trim(), p => float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
            void Same(string name, float expected, float tolerance) =>
                Assert.True(MathF.Abs(values[name] - expected) <= tolerance, $"{file} {key}.{name} = {values[name]}, the simulation's {expected}");
            Same("front", spec.FrontAxle, 0.0005f);
            Same("rear", spec.RearAxle, 0.0005f);
            Same("track", spec.HalfTrack, 0.0005f);
            Same("hub", spec.HubHeight, 0.0005f);
            Same("rest", spec.SuspensionRest, 0.0005f);
            Same("mass", spec.MassKg, 0.5f);
            Same("stiffness", spec.SuspensionStiffness, 0.5f);
            Same("radius", spec.WheelRadius, 0.0005f);
        }
    }

    /// <summary>
    /// Points given by hand in the car frame in tools/car_models.json (the eyes, eye_at, and a mirror glass found by hand,
    /// mirror_box) were read off a model as it was prepared at the geometry of the time, and the preparation scales a
    /// model to its spec's wheelbase about the front hubs and the hub plane: kept as bare numbers they land elsewhere on
    /// the model when the spec changes. dims6 lengthened the E-Class and its mirror box, read at the Egea's wheelbase,
    /// hung the live glass 7.5 cm under the housing. So each entry with such points names the geometry they were read in
    /// (frame: front, rear, hub_y), from which tools/prepare_car_models.py (in_present_frame) carries them to the present
    /// one.
    /// </summary>
    [Fact]
    public void Hand_given_car_frame_points_name_the_geometry_they_were_read_in()
    {
        var root = TrackTests.FindRepoRoot();
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/car_models.json")));
        var given = 0;
        foreach (var car in config.RootElement.EnumerateObject())
        {
            if (car.Value.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            if (!car.Value.TryGetProperty("eye_at", out _) && !car.Value.TryGetProperty("mirror_box", out _)) continue;
            given++;
            Assert.True(car.Value.TryGetProperty("frame", out var frame), $"{car.Name} gives car-frame points without the geometry they were read in");
            var front = frame.GetProperty("front").GetSingle();
            var rear = frame.GetProperty("rear").GetSingle();
            var hub = frame.GetProperty("hub_y").GetSingle();
            _out.WriteLine($"{car.Name}: read at front {front:F3}, rear {rear:F3}, hub {hub:F4} m");
            // A wheelbase and a rest hub height in the range of the catalogue's, not a slip of the pen.
            Assert.InRange(front + rear, 2.2f, 3.0f);
            Assert.InRange(hub, -0.45f, -0.25f);
        }
        Assert.True(given >= 3, "the estate, the Delta and the E-Class give car-frame points");
        Assert.Contains("in_present_frame(entry, spec)", File.ReadAllText(Path.Combine(root, "tools/prepare_car_models.py")));
    }

    /// <summary>
    /// The preparation sets the eyes 13 cm under whatever lies over them and passes over a seat by its shape, since a
    /// backrest stands at head height just behind the eyes. The 570S model's two bucket seats are one mesh 1.34 m across,
    /// wider than the single seat the rule knew (0.7 m): their backs had its eyes set at 0.845 m over the road, where the
    /// wheel and the dashboard hid the road (v8seat). The rule takes a seat pair now, and the McLaren's entry raises the
    /// eyes over the preparation's default to where a reclined supercar seat has them, 0.33 m over the wheel's centre
    /// and 0.55 m behind it, and corrects its dashboard's finish from the model's gloss black to the car's matt leather
    /// and Alcantara. The models are build output, not in the repository: the preparation writes where it put each car's
    /// eyes, dials and mirror to tools/car_cockpits.json, and this reads what it did there (v8seat review: a check of the
    /// rule's source text passed whether or not the rule worked).
    /// </summary>
    [Fact]
    public void A_seat_pair_in_one_mesh_does_not_lower_the_eyes()
    {
        var root = TrackTests.FindRepoRoot();
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/car_models.json")));
        using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/car_cockpits.json")));
        static float[] Point(System.Text.Json.JsonElement e) => e.EnumerateArray().Select(v => v.GetSingle()).ToArray();

        // Every car's eyes about where a driver's are: a metre over the road in the low cars, a little more in the tall
        // ones, and never closer than 13 cm under what is over them (the top of the head is some 12 cm over the eyes).
        foreach (var car in record.RootElement.EnumerateObject().Where(c => !c.Name.StartsWith('_')))
        {
            var cockpit = car.Value;
            var over = cockpit.GetProperty("eye_over_road").GetSingle();
            var lining = cockpit.GetProperty("under_lining");
            _out.WriteLine($"{car.Name}: eyes {over:F3} m over the road, " +
                (lining.ValueKind == System.Text.Json.JsonValueKind.Null ? "nothing over them" : $"{lining.GetSingle():F3} m under the lining"));
            Assert.InRange(over, 0.95f, 1.30f);
            if (lining.ValueKind != System.Text.Json.JsonValueKind.Null) Assert.True(lining.GetSingle() >= 0.129f, $"{car.Name}'s eyes are in the roof");
        }

        // The 570S: the seat pair passed over, not taken for the roof, and the eyes where the entry puts them (the record
        // in step with tools/car_models.json: a change to the entry's eyes or dials without preparing again fails here).
        var v8 = record.RootElement.GetProperty("v8");
        var entry = config.RootElement.GetProperty("v8");
        Assert.False(v8.GetProperty("lowered").GetBoolean());
        var seats = v8.GetProperty("seats_passed_over").EnumerateArray().ToArray();
        Assert.Contains(seats, s => s.GetProperty("width").GetSingle() is > 0.7f and < 1.4f
                                    && s.GetProperty("top_over_road").GetSingle() > v8.GetProperty("eye_over_road").GetSingle());
        Assert.InRange(v8.GetProperty("eye_over_road").GetSingle(), 0.96f, 1.01f);
        var fromWheel = Point(v8.GetProperty("eye_from_wheel"));
        var correction = Point(entry.GetProperty("eye"));
        _out.WriteLine($"570S eyes {fromWheel[1]:F3} m over the wheel's centre, {fromWheel[2]:F3} m behind it");
        Assert.InRange(fromWheel[1], 0.32f, 0.36f);
        Assert.InRange(fromWheel[2], 0.54f, 0.60f);
        Assert.Equal(0.28f + correction[1], fromWheel[1], 0.0011f);
        Assert.Equal(0.52f + correction[2], fromWheel[2], 0.0011f);

        // Its dials and their gear readout where the entry says (item 22: the rim crosses the panel just under the
        // readout's usual place, so the readout sits higher on it).
        var dials = v8.GetProperty("dials");
        var dialsEntry = entry.GetProperty("dials");
        Assert.Equal(dialsEntry.GetProperty("width").GetSingle(), dials.GetProperty("width").GetSingle(), 0.0011f);
        Assert.Equal(dialsEntry.GetProperty("gear_top").GetInt32(), dials.GetProperty("gear_top").GetInt32());
        Assert.InRange(dials.GetProperty("gear_top").GetInt32(), 0, 119);

        // Its dashboard matt, as the leather and Alcantara of the real car are: at 0.55-0.6 the sun ahead still spread
        // over it as a grey sheen (v8seat review), at 0.88 it does not.
        foreach (var material in new[] { "plastic.black", "McLaren.black" })
            Assert.True(entry.GetProperty("materials").GetProperty(material).GetProperty("roughness").GetSingle() >= 0.8f, material);

        // The 458 (cars13 stage 2) borrows the 570S's whole cabin (cabin_from: its tub, dashboard, binnacle, wheel and the
        // seat pair, named as seats so that nothing of it is taken for the roof), and with it the 570S's driving position,
        // dials and matt dashboard. On the preparation's default eyes, 5 cm lower and 3 cm nearer the wheel than the
        // McLaren's, the rim crossed the panel higher: projected from them the rev counter read only 4,000-7,000 rpm and
        // the speedometer 70-230 km/h over the head's sway, the gear readout 43-80 % in view, and a panel raised 12 mm or
        // more to the window between the hood and the rim stood out of the binnacle over the hood. Its record must put
        // the eyes, the dials and the readout where the 570S's are against the same wheel.
        var ferrari = record.RootElement.GetProperty("458");
        var ferrariEntry = config.RootElement.GetProperty("458");
        Assert.Equal("v8", ferrariEntry.GetProperty("cabin_from").GetProperty("car").GetString());
        Assert.False(ferrari.GetProperty("lowered").GetBoolean());
        var ferrariOver = ferrari.GetProperty("eye_over_road").GetSingle();
        _out.WriteLine($"458 eyes {ferrariOver:F3} m over the road, {ferrari.GetProperty("under_lining").GetSingle():F3} m under the lining");
        Assert.InRange(ferrariOver, 0.97f, 1.03f);
        Assert.True(ferrari.GetProperty("under_lining").GetSingle() >= 0.129f);
        var ferrariFromWheel = Point(ferrari.GetProperty("eye_from_wheel"));
        Assert.Equal(fromWheel[1], ferrariFromWheel[1], 0.0011f);
        Assert.Equal(fromWheel[2], ferrariFromWheel[2], 0.0011f);
        var ferrariDials = ferrari.GetProperty("dials");
        var ferrariDialsEntry = ferrariEntry.GetProperty("dials");
        Assert.Equal(dialsEntry.GetProperty("width").GetSingle(), ferrariDialsEntry.GetProperty("width").GetSingle());
        Assert.Equal(dialsEntry.GetProperty("gear_top").GetInt32(), ferrariDials.GetProperty("gear_top").GetInt32());
        Assert.Equal(dials.GetProperty("width").GetSingle(), ferrariDials.GetProperty("width").GetSingle(), 0.0011f);
        // The panel against the wheel's centre as in the 570S, within 2 mm (it sits 6 mm proud of the binnacle towards
        // the eyes, so it moves as little as the eyes' direction does).
        var panel = Point(dials.GetProperty("centre")).Zip(Point(v8.GetProperty("wheel_centre")), (a, b) => a - b).ToArray();
        var ferrariPanel = Point(ferrariDials.GetProperty("centre")).Zip(Point(ferrari.GetProperty("wheel_centre")), (a, b) => a - b).ToArray();
        for (var axis = 0; axis < 3; axis++) Assert.Equal(panel[axis], ferrariPanel[axis], 0.002f);
        foreach (var material in new[] { "plastic.black", "McLaren.black" })
            Assert.True(ferrariEntry.GetProperty("materials").GetProperty(material).GetProperty("roughness").GetSingle() >= 0.8f, $"458 {material}");
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

    /// <summary>Rain (0.12): a wet road takes about a third of the grip, and the stop from 60 mph grows by a fifth or more.</summary>
    [Fact]
    public void A_wet_road_lengthens_the_stop_from_60_mph()
    {
        float Stop(Weather weather)
        {
            var car = StartedCar(DragStrip());
            car.Weather = weather;
            DriveFlatOut(car, 9f);
            Assert.True(car.SpeedMph > 60f, $"{weather}: only {car.SpeedMph:F0} mph after 9 s");
            while (car.SpeedMph > 60f) car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, Dt);
            var start = car.Position;
            var t = 0f;
            while (car.SpeedMph > 1f && t < 12f)
            {
                car.Step(new DriverInputs { Brake = 1f, Clutch = 1f, Gear = DriverInputs.NoGearChange }, Dt);
                t += Dt;
            }
            return Vector3.Distance(start, car.Position);
        }
        var dry = Stop(Weather.Dry);
        var rain = Stop(Weather.Rain);
        _out.WriteLine($"60-0 mph: dry {dry:F1} m, rain {rain:F1} m");
        Assert.True(rain > dry * 1.2f, $"rain {rain:F1} m should be well past dry {dry:F1} m");
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
    [InlineData(90f, true)]
    [InlineData(100f, true)]
    [InlineData(110f, true)]
    [InlineData(125f, false)]
    public void Drawbridge_wants_a_60_mph_run_up_and_wrecks_a_car_landing_past_120(float entryMph, bool shouldSurvive)
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
            if (car.LandingImpact > RaceOptions.Medium.LandingCrashSpeed) { crashed = $"hard landing {car.LandingImpact:F1} m/s"; break; }
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
