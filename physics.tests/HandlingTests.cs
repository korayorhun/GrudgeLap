using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The handling as a proving ground would measure it (0.18): stopping distances, sprints, steady-state cornering,
/// weight transfer, anti-lock brakes, the limited-slip differential and lift-off oversteer. Each figure is checked
/// against the range the real cars the catalogue follows were measured in, or against the textbook law that governs it.
/// </summary>
public sealed class HandlingTests(ITestOutputHelper output)
{
    /// <summary>
    /// 0-100 km/h and the stop from 100 km/h, dry and in the rain, pedal hard down. The sprint is a road test's
    /// standing start (<see cref="HandlingRig.Sprint(VehicleSpec, float, float)"/>: revs up on the brake, timed from its
    /// release); since 0.19 the assisted clutch launches at the engine's launch revs with the driven tyres just short of
    /// their peak, the V8 with its launch control and its dual-clutch box handing the drive from gear to gear, which
    /// brought the ranges in to about a tenth either side of the published figures. The cars with anti-lock
    /// brakes stop at the tyres' peak; the others lock their wheels, which costs them a sixth or so on the dry road, as
    /// a locked tyre slides at about three quarters of its peak friction. Ranges: published sprints (see the specs);
    /// stops of contemporary road tests (Car and Driver's 570S: 144 ft from 70 mph, about 35 m from 100 km/h; road tests
    /// of non-ABS cars of the 1980s and 1990s on 13-16 in tyres: 42-60 m locked). Rain takes a third of the grip, so
    /// the stop grows by roughly half (a little less with the wheels locked, which slide on either surface).
    /// </summary>
    [Theory]
    // Flat-twelve: Ferrari quoted 5.8 s; Car and Driver timed 5.0 s to 60 mph (September 1986), which is about 5.5 s to
    // 100 km/h without the magazine's one-foot rollout. The assist's launch is cleaner than a test driver's on a heavy
    // single-plate clutch, so the model sits at the quick end (about 5.05 s, with the gated gearbox's 0.45 s shifts). No ABS.
    [InlineData(0, 4.9f, 5.9f, 39f, 50f)]
    // V8: McLaren quotes 3.2 s for the 570S with its launch control (Car and Driver: 2.9 s to 60 mph); about 3.4 here,
    // traction-limited at 0.89 g throughout. ABS.
    [InlineData(1, 3.1f, 3.7f, 29f, 37f)]
    // The three hatchbacks, since they follow the cars the game draws: a tenth of the published 0-100 km/h either side,
    // plainly that and no tighter; the stops a tenth either side of the one published stop (the R32's) or of the class.
    // Hot hatch: Peugeot quoted 7.8 s for the 205 GTi 1.9 (about 8.4 here, on its eight-valve's broad but modest
    // torque); no ABS, so the wheels lock (non-ABS cars of the late 1980s on 15 in tyres: 42-52 m).
    [InlineData(2, 7.0f, 8.6f, 42f, 52f)]
    // Rally hatch: Lancia quoted 5.7 s for the Delta Integrale Evoluzione (6.27 here: the T3's lag and a launch the
    // engine limits, not the four tyres). The upper bound is 6.3 s, the tenth over (6.27 s) rounded up to the printed
    // tenth, and the car sits at its very top: a change that slows the Delta at all takes it out of the range, as it
    // should, since it is already a tenth slower than Lancia's figure. Bosch ABS, no published stop (34-42 m, the ABS
    // cars of its time).
    [InlineData(3, 5.1f, 6.3f, 34f, 42f)]
    // V6 hatch: VW quoted 6.6 s for the Golf R32 (about 6.6 here: the launch assist counts on the Haldex, whose fronts
    // run ahead to close it, as the first generation's did); ABS; the Washington Times stopped one from 60 mph in
    // 117 ft, about 38 m from 100 km/h.
    [InlineData(4, 5.9f, 7.3f, 34f, 42f)]
    [InlineData(5, 12.8f, 15.0f, 46f, 58f)]  // 1.6 saloon: 13.9 s (FIA form 5608's car); discs and drums, no ABS
    [InlineData(6, 15.5f, 18.5f, 48f, 62f)]  // 1.4 estate: about 16-17 s; no ABS
    // Fleet saloon: Mercedes-Benz quotes 7.7 s for the E 250 CDI with its six-speed manual (about 7.9 here: a rear-drive
    // saloon of 53/47 is traction-limited in first). ESP with ABS; auto motor und sport stopped the E 250 CGI Coupe of the
    // same platform from 100 km/h in 36.4 m (03/2010), on wider 17 in tyres: a tenth either side of that.
    [InlineData(7, 6.9f, 8.5f, 33f, 40f)]
    // The five cars of cars13, a tenth of the published figure either side. Flat-plane V8: Ferrari quotes under 3.4 s
    // for the 458 Italia with its launch control and 32.5 m from 100 km/h on its carbon-ceramic brakes. ABS.
    [InlineData(8, 3.0f, 3.8f, 29f, 36f)]
    // Five-cylinder hatch: Fiat quoted 8.5 s for the Bravo HGT 2.0 20V (cars-data's table has 8.0); ABS, no published
    // stop (the ABS hatchbacks of the late 1990s on 15 in tyres, the Delta's and the R32's class: 34-44 m).
    [InlineData(9, 7.6f, 9.4f, 34f, 44f)]
    // 1.6 hatch: 11.0 s for the Bravo 1.6 16V (cars-data); ABS, drums behind, no published stop (the class: 38-47 m).
    [InlineData(10, 9.9f, 12.1f, 38f, 47f)]
    // Hot hatch of 2018: SEAT quotes 5.8 s for the Leon Cupra 300 five-door the spec follows (5.7 for the three-door);
    // ESC with ABS, no published stop (a hot hatch of 2018 on 235/35 R19 performance tyres with 340 mm discs: 31-38 m).
    [InlineData(11, 5.2f, 6.4f, 31f, 38f)]
    // Boxer coupe: Subaru quotes 7.6 s for the BRZ; ABS; Car and Driver stopped a 2013 BRZ from 70 mph in 165 ft, about
    // 39.6 m from 100 km/h on its touring tyres.
    [InlineData(12, 6.8f, 8.4f, 35f, 44f)]
    public void Sprints_and_stops_within_the_real_cars_ranges(int car, float sprintMin, float sprintMax, float stopMin, float stopMax)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sprint = HandlingRig.Sprint(spec, 100f);
        var dry = HandlingRig.Stop(spec, 100f);
        var rain = HandlingRig.Stop(spec, 100f, Weather.Rain);
        output.WriteLine($"{spec.Name}: 0-100 km/h {sprint:F2} s, 100-0 dry {dry.Distance:F1} m ({dry.Seconds:F2} s, locked {dry.Locked}), rain {rain.Distance:F1} m");
        Assert.InRange(sprint, sprintMin, sprintMax);
        Assert.InRange(dry.Distance, stopMin, stopMax);
        Assert.InRange(rain.Distance / dry.Distance, 1.25f, 1.65f);
        Assert.Equal(!spec.AntiLockBrakes, dry.Locked);
    }

    /// <summary>
    /// The launch itself (0.19): with the brake and the throttle held at a standstill the engine waits at its launch revs
    /// (the lowest giving 95 % of peak torque; full boost on the turbo) and the car does not creep; on the release the
    /// clutch slips with the engine held there (never bogged below nine tenths of it) and the driven tyres at a slip short
    /// of their peak (the peak slip ratio of a road tyre on dry asphalt is about 0.1-0.15, Pacejka ch. 1), until the
    /// clutch locks. Launch control (the V8) holds them closer to the peak than the assist's driver's foot does. Up to
    /// 0.18 the assisted clutch bit from about 2,000 rpm whatever the car: the revs collapsed, the tyres spun up to a slip
    /// of 1.5 and the driver had to feather the throttle back to a fifth.
    /// </summary>
    /// <remarks>
    /// The slip read is that of the driven axle that slips most. The V6 hatch's Haldex closes only once its fronts lead
    /// the rears, by 0.5 rad/s at least, which near a standstill is past their peak slip (0.16 m/s on a slip ratio read
    /// against 1.2 m/s): its fronts run ahead there so that the rears can drive, and may reach 1.4 times their peak, its
    /// rears no more than any other car's. The rally hatch is left out: its launch is the engine's (the T3 builds its
    /// boost on the clutch), its four tyres far short of their peak.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Standing_starts_hold_the_launch_revs_and_the_tyres_short_of_their_peak(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        sim.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, HandlingRig.Dt);
        sim.Step(new DriverInputs { Gear = 1, Brake = 1f }, HandlingRig.Dt);
        for (var t = 0f; t < 1.5f; t += HandlingRig.Dt) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f, Throttle = 1f }, HandlingRig.Dt);
        var launchRpm = spec.Engine.LaunchRpm;
        Assert.True(sim.Drivetrain.PreRevving);
        Assert.InRange(sim.Drivetrain.Engine.Rpm, launchRpm, launchRpm + 600f);
        Assert.InRange(sim.Speed, -0.05f, 0.05f);

        float lowestRpm = float.MaxValue, mostSlip = 0f, slipSum = 0f, mostLeadSlip = 0f;
        var leadsToClose = spec.FourWheelDrive is { Coupling: CentreCoupling.Haldex };
        var samples = 0;
        var t0 = 0f;
        for (; t0 < 6f; t0 += HandlingRig.Dt)
        {
            sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f }, HandlingRig.Dt);
            if (!sim.Drivetrain.Launching) break;
            if (t0 < 0.25f) continue;  // the first bite, as the load comes onto the driven wheels
            lowestRpm = MathF.Min(lowestRpm, sim.Drivetrain.Engine.Rpm);
            var slip = 0f;
            foreach (var front in new[] { true, false })
            {
                if (!spec.Drives(front)) continue;
                var (a, b) = front ? (sim.Wheels[0], sim.Wheels[1]) : (sim.Wheels[2], sim.Wheels[3]);
                var axleSlip = 0.5f * (a.SlipRatio + b.SlipRatio) / TireModel.PeakSlipRatio(a.Friction);
                slip = MathF.Max(slip, axleSlip);
                if (leadsToClose && front) mostLeadSlip = MathF.Max(mostLeadSlip, axleSlip);
                else mostSlip = MathF.Max(mostSlip, axleSlip);
            }
            slipSum += slip;
            samples++;
        }
        var meanSlip = slipSum / samples;
        output.WriteLine($"{spec.Name}: launch revs {launchRpm:F0}, held at least {lowestRpm:F0} rpm for {t0:F2} s to {sim.Speed * MathUtil.KmhPerMps:F0} km/h; driven slip {meanSlip:P0} of the peak on average, at most {mostSlip:P0}" + (leadsToClose ? $" (the leading fronts {mostLeadSlip:P0})" : ""));
        Assert.InRange(t0, 0.5f, 5f);
        Assert.True(lowestRpm > 0.9f * launchRpm, $"the engine bogged to {lowestRpm:F0} rpm");
        Assert.InRange(meanSlip, spec.LaunchControl ? 0.8f : 0.5f, 1.0f);
        Assert.True(mostSlip < 1.15f, $"a driven tyre spun past its peak ({mostSlip:P0})");
        Assert.True(mostLeadSlip < 1.4f, $"the fronts ran too far ahead to close the coupling ({mostLeadSlip:P0})");
    }

    /// <summary>
    /// A launch out of a tight turn with launch control (cars13 review): brake and throttle held, then released with 60 %
    /// of the steering lock on and the throttle held down, each 240 Hz step watched for two seconds. The driven wheels'
    /// speed difference does not change sign from one step to the next (both sides over 0.5 rad/s) for more than 8
    /// steps running, dry or wet: the launch control's clutch and the differential's lock feeding each other at the step
    /// rate is a hunt no real axle makes. The 458 on the default 1.3 kg m^2 of wheel inertia, a 16 in wheel's,
    /// alternated for 31 steps (13 in the rain); on its 20 in corners' 1.8 it alternates for 1 (none in the rain), the
    /// 570S for 5 (none).
    /// </summary>
    [Theory]
    [InlineData(1, Weather.Dry)]
    [InlineData(1, Weather.Rain)]
    [InlineData(8, Weather.Dry)]
    [InlineData(8, Weather.Rain)]
    public void A_launch_out_of_a_tight_turn_does_not_set_the_driven_wheels_hunting(int car, Weather weather)
    {
        var spec = VehicleSpec.Catalogue[car];
        Assert.True(spec.LaunchControl);
        var sim = new VehicleSim(spec, HandlingRig.Proving()) { Weather = weather };
        var dt = VehicleSim.FixedStep;
        sim.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f }, dt);
        sim.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, dt);
        for (var t = 0f; t < 1.5f; t += dt) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f, Throttle = 1f }, dt);
        var (left, right) = spec.FrontWheelDrive ? (0, 1) : (2, 3);
        float before = 0f;
        int run = 0, longest = 0;
        for (var i = 0; i < 480; i++)
        {
            sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f, Steer = 0.6f }, dt);
            var apart = sim.Wheels[left].AngularVelocity - sim.Wheels[right].AngularVelocity;
            run = MathF.Abs(apart) > 0.5f && MathF.Abs(before) > 0.5f && MathF.Sign(apart) != MathF.Sign(before) ? run + 1 : 0;
            longest = Math.Max(longest, run);
            before = apart;
        }
        output.WriteLine($"{spec.Name} {weather}: the driven wheels alternated for {longest} steps at most; {sim.Speed * MathUtil.KmhPerMps:F1} km/h after 2 s");
        Assert.Equal(1, sim.Drivetrain.Gear);
        Assert.True(longest <= 8, $"the driven wheels hunted against each other for {longest} steps");
    }

    /// <summary>
    /// A standing start without the brake (the player simply floors it): the revs rise to the launch speed before the
    /// clutch takes up, so the car is under way within half a second and no slower than a few tenths behind the
    /// road-test start; it never stalls or bogs.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Flooring_it_from_rest_launches_without_a_bog(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var tested = HandlingRig.Sprint(spec, 100f);
        var floored = HandlingRig.Sprint(new VehicleSim(spec, HandlingRig.Proving()), 100f, preRev: false);
        output.WriteLine($"{spec.Name}: 0-100 km/h {tested:F2} s revved on the brake, {floored:F2} s floored from idle");
        Assert.InRange(floored - tested, 0f, 0.5f);
    }

    /// <summary>
    /// The same car with and without anti-lock brakes, pedal hard down from 100 km/h: the locked tyres slide past their
    /// peak and lose a sixth to a quarter of the stop, the measured difference on dry asphalt.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Locked_wheels_stop_longer_than_anti_lock_brakes(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var locked = HandlingRig.Stop(spec, 100f);
        var abs = HandlingRig.Stop(spec with { AntiLockBrakes = true }, 100f);
        output.WriteLine($"{spec.Name}: locked {locked.Distance:F1} m, with ABS {abs.Distance:F1} m");
        Assert.True(locked.Locked);
        Assert.False(abs.Locked);
        Assert.InRange(locked.Distance / abs.Distance, 1.08f, 1.35f);
    }

    /// <summary>
    /// Brake balance: pressing the pedal down progressively from 100 km/h, a car without anti-lock brakes locks its
    /// front wheels first, as brake systems are proportioned to; locked rears first would spin it in a hard stop.
    /// The rally hatch, here while it was a five without ABS, now has the Delta's Bosch anti-lock brakes.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    public void The_front_wheels_lock_before_the_rears(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        sim.LaunchAt(100f / MathUtil.KmhPerMps);
        var first = -1;
        var pedalAtLock = 0f;
        for (var t = 0f; t < 3f && first < 0; t += HandlingRig.Dt)
        {
            var pedal = MathUtil.Clamp01(t / 2.5f);
            sim.Step(new DriverInputs { Brake = pedal, Gear = 0 }, HandlingRig.Dt);
            for (var i = 0; i < 4 && first < 0; i++)
            {
                if (sim.Wheels[i].AngularVelocity * spec.WheelRadius < sim.Speed * 0.5f) { first = i; pedalAtLock = pedal; }
            }
        }
        output.WriteLine($"{spec.Name}: first lock wheel {first} at {pedalAtLock:P0} pedal");
        Assert.InRange(first, 0, 1);
    }

    /// <summary>
    /// Steering while braking hard: with the front wheels locked the car slides straight on, whatever the wheel says
    /// (a locked tyre's force lies along its sliding, not across it); with anti-lock brakes it turns.
    /// </summary>
    [Fact]
    public void Locked_front_wheels_do_not_steer_and_anti_lock_brakes_do()
    {
        float Swerve(VehicleSpec spec)
        {
            var car = new VehicleSim(spec, HandlingRig.Proving());
            car.LaunchAt(80f / MathUtil.KmhPerMps);
            var start = car.Position;
            for (var t = 0f; t < 6f && car.Speed > 0.5f; t += HandlingRig.Dt)
            {
                car.Step(new DriverInputs { Brake = 1f, Steer = 0.6f, Gear = 0 }, HandlingRig.Dt);
            }
            return Vector3.Dot(car.Position - start, Vector3.UnitX);
        }
        var spec = VehicleSpec.HotHatch;
        var locked = Swerve(spec);
        var abs = Swerve(spec with { AntiLockBrakes = true });
        output.WriteLine($"sideways while stopping from 80 km/h with the wheel turned: locked {locked:F2} m, with ABS {abs:F2} m");
        Assert.True(abs > 3f, $"with ABS the car should turn ({abs:F2} m)");
        Assert.True(locked < abs * 0.35f, $"locked wheels should barely turn the car ({locked:F2} m against {abs:F2} m)");
    }

    /// <summary>
    /// Load transfer under braking is statics: the front axle carries its static share plus m a h / L. Measured on the
    /// suspension in the middle of an ABS stop at about 1 g, it must agree with the law to within a few per cent of the
    /// weight (the pitch damping and the drag's own moment account for the rest).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    public void Weight_moves_forward_under_braking_as_statics_says(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        sim.LaunchAt(100f / MathUtil.KmhPerMps);
        float frontShare = 0f, decel = 0f;
        var samples = 0;
        for (var t = 0f; t < 2.5f; t += HandlingRig.Dt)
        {
            sim.Step(new DriverInputs { Brake = 1f, Gear = 0 }, HandlingRig.Dt);
            if (t < 1f || t > 1.6f) continue;
            var total = sim.Wheels.Sum(w => w.Load);
            frontShare += (sim.Wheels[0].Load + sim.Wheels[1].Load) / total;
            decel += -sim.LongitudinalAcceleration;
            samples++;
        }
        frontShare /= samples;
        decel /= samples;
        var wheelbase = spec.FrontAxle + spec.RearAxle;
        var staticFront = (spec.RearAxle + spec.CentreOfMassOffset.Z) / wheelbase;
        var height = sim.CentreOfMass.Y - HandlingRig.Proving().StartFrame.Origin.Y;
        var expected = staticFront + height / wheelbase * decel / MathUtil.Gravity;
        output.WriteLine($"{spec.Name}: {decel / MathUtil.Gravity:F2} g, front axle {frontShare:P1} (static {staticFront:P1}, statics says {expected:P1}, centre of mass {height:F2} m up)");
        Assert.InRange(decel / MathUtil.Gravity, 0.85f, 1.35f);
        Assert.InRange(frontShare, expected - 0.04f, expected + 0.04f);
    }

    /// <summary>
    /// At rest each axle carries the share of the weight the real car does: 40/60 for the flat-twelve (the Testarossa
    /// was quoted at 40/60), 42/58 for the V8 (McLaren quotes 42:58 for the 570S), about 63 % in front for the 205,
    /// about 60 % for the two four-wheel-drive hatchbacks (their propshafts and rear differentials behind), 53 % for
    /// the rear-wheel-drive saloon and about 53 % for the fleet saloon (auto motor und sport weighed the E 250 CGI Coupe
    /// of its platform at 53.4/46.6); of cars13, 42/58 for the 458 (Ferrari), 53/47 for the BRZ (Subaru), about 62 % in
    /// front for the two Bravos and 60 % for the Leon Cupra (no axle loads published; transverse engines ahead of the axle).
    /// </summary>
    [Theory]
    [InlineData(0, 0.38f, 0.42f)]
    [InlineData(1, 0.40f, 0.44f)]
    [InlineData(2, 0.61f, 0.64f)]
    [InlineData(3, 0.59f, 0.62f)]
    [InlineData(4, 0.59f, 0.62f)]
    [InlineData(5, 0.51f, 0.55f)]
    [InlineData(7, 0.51f, 0.55f)]
    [InlineData(8, 0.40f, 0.44f)]
    [InlineData(9, 0.60f, 0.64f)]
    [InlineData(10, 0.60f, 0.64f)]
    [InlineData(11, 0.58f, 0.62f)]
    [InlineData(12, 0.51f, 0.55f)]
    public void Static_weight_distribution_matches_the_car(int car, float min, float max)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        for (var i = 0; i < 240; i++) sim.Step(DriverInputs.Idle, HandlingRig.Dt);
        var front = (sim.Wheels[0].Load + sim.Wheels[1].Load) / sim.Wheels.Sum(w => w.Load);
        var pitch = MathF.Asin(Vector3.Dot(sim.Forward, Vector3.UnitY)) * MathUtil.Rad2Deg;
        output.WriteLine($"{spec.Name}: {front:P1} on the front axle, pitch {pitch:F2} deg");
        Assert.InRange(front, min, max);
        // Each axle is sprung for its load, so the car still sits level.
        Assert.InRange(pitch, -0.3f, 0.3f);
    }

    /// <summary>
    /// Steady-state cornering on a 40 m circle (SAE J266's constant-radius method): the understeer gradient is the
    /// extra road-wheel angle the car needs per g of lateral acceleration beyond the geometric L / R. Front-heavy
    /// front-wheel-drive cars understeer (about 1 to 4 deg/g, the textbook range for such cars); the mid-engined cars,
    /// with their wide rear tyres and the roll stiffness set towards the front, stay close to neutral. Body roll per g
    /// follows the cars too: little for the sports cars, a lot for the soft saloons of the 1990s. Since 0.19 the steering
    /// gives under the tyres' aligning torque (<see cref="VehicleSpec.SteeringCompliance"/>), which adds the half a degree
    /// per g or so that steering compliance gives real cars: the hot hatch, at 0.8 deg/g before, below its class, now
    /// understeers as front-heavy hatchbacks do (1.4). No understeer gradient or roll rate is published for the three
    /// hatchbacks or the fleet saloon: their ranges are the class's (1-4 deg/g for front-heavy cars, Gillespie ch. 6; a
    /// comfortable rear-drive saloon of the 2010s understeers mildly and rolls 3-4 deg/g) narrowed round each.
    /// </summary>
    [Theory]
    [InlineData(0, 0.3f, 1.5f, 1.2f, 2.8f)]
    [InlineData(1, 0.2f, 1.2f, 0.8f, 2.0f)]
    [InlineData(2, 1.0f, 2.2f, 2.2f, 3.4f)]
    [InlineData(3, 1.4f, 3.0f, 2.4f, 3.6f)]
    [InlineData(4, 1.2f, 2.6f, 2.0f, 3.0f)]
    [InlineData(5, 0.8f, 3.0f, 3.8f, 6.5f)]
    [InlineData(6, 1.5f, 5.0f, 4.5f, 7.5f)]
    [InlineData(7, 1.4f, 2.6f, 3.0f, 4.2f)]
    // cars13 (no understeer gradient or roll rate is published for any of the five): the class's range narrowed round
    // each car's own figure (0.6 deg/g either side of it), so a change shows, as the fleet saloon's was. Measured: the
    // 458 1.10 and 1.50 deg/g, the Bravo HGT 1.78 and 2.99, the Bravo 1.6 2.16 and 3.26, the Leon Cupra 1.73 and 2.31,
    // the BRZ 1.25 and 2.12.
    [InlineData(8, 0.5f, 1.7f, 1.0f, 2.1f)]
    [InlineData(9, 1.2f, 2.4f, 2.4f, 3.6f)]
    [InlineData(10, 1.5f, 2.8f, 2.7f, 3.9f)]
    [InlineData(11, 1.1f, 2.3f, 1.8f, 2.9f)]
    [InlineData(12, 0.7f, 1.9f, 1.6f, 2.7f)]
    public void Understeer_and_roll_on_the_skid_pad(int car, float gradientMin, float gradientMax, float rollMin, float rollMax)
    {
        var spec = VehicleSpec.Catalogue[car];
        var track = HandlingRig.Proving();
        var slow = HandlingRig.Circle(spec, 40f, 8f, track: track);
        var brisk = HandlingRig.Circle(spec, 40f, 15f, track: track);
        Assert.True(slow.Held && brisk.Held, $"{spec.Name} could not hold the circle");
        var gradient = (brisk.SteerDeg - slow.SteerDeg) / (brisk.LateralG - slow.LateralG);
        var roll = brisk.RollDeg / brisk.LateralG;
        output.WriteLine($"{spec.Name}: understeer {gradient:F2} deg/g, roll {roll:F2} deg/g (at {brisk.LateralG:F2} g, body slip {brisk.BodySlipDeg:F1} deg)");
        Assert.InRange(gradient, gradientMin, gradientMax);
        Assert.InRange(roll, rollMin, rollMax);
    }

    /// <summary>
    /// The steering goes light before the front tyres let go (0.19): turning the wheel in slowly at a steady 20 m/s, the
    /// front tyres' aligning torque (side force times caster and pneumatic trail) first grows faster than the cornering
    /// force, then peaks before the car reaches its greatest lateral acceleration, by when the torque per g has fallen to
    /// well under three quarters of what it was at 0.3 g: the pneumatic trail shrinks to nothing as the contact patch
    /// starts to slide, and only the caster trail is left (Milliken, ch. 2; Pacejka, ch. 3). A driver feels the rim
    /// lighten there; here the steering's give under that torque fades with it.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void The_steering_goes_light_before_the_front_tyres_let_go(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), 0f);
        sim.LaunchAt(20f);
        float peakTorque = 0f, ayAtPeak = 0f, mostAy = 0f, torqueAtMostAy = 0f, perGEarly = 0f;
        for (var t = 0f; t < 7f; t += HandlingRig.Dt)
        {
            var steer = MathUtil.Clamp01(t / 7f) * 0.8f;
            var throttle = MathUtil.Clamp01((20f - sim.Speed) * 0.5f + 0.25f);
            sim.Step(new DriverInputs { Steer = steer, Throttle = throttle, Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
            var ay = MathF.Abs(sim.LateralAcceleration) / MathUtil.Gravity;
            if (perGEarly == 0f && ay >= 0.3f) perGEarly = sim.AligningTorque / ay;
            if (sim.AligningTorque > peakTorque) { peakTorque = sim.AligningTorque; ayAtPeak = ay; }
            if (ay > mostAy) { mostAy = ay; torqueAtMostAy = sim.AligningTorque; }
        }
        var perGAtLimit = torqueAtMostAy / mostAy;
        output.WriteLine($"{spec.Name}: aligning torque {perGEarly:F0} N m per g at 0.3 g, peaks at {peakTorque:F0} N m at {ayAtPeak:F2} g; at the limit, {mostAy:F2} g, {torqueAtMostAy:F0} N m ({perGAtLimit:F0} per g)");
        Assert.True(peakTorque > 100f, $"the front tyres should push the steering back ({peakTorque:F0} N m)");
        Assert.True(ayAtPeak < mostAy, "the aligning torque should peak short of the limit");
        Assert.True(perGAtLimit < 0.75f * perGEarly, "at the limit the steering should have gone light for the cornering force it carries");
    }

    /// <summary>
    /// Ackermann steering (0.19): creeping round on full lock, the inner front wheel turns more than the outer one, so both
    /// roll round the same centre with almost no slip angle; with parallel steer (both wheels at the same angle, as every
    /// car had up to 0.18) the two fight each other with slip angles of opposite sign, scrubbing the tyres.
    /// </summary>
    [Fact]
    public void Ackermann_lets_both_front_wheels_roll_round_at_parking_speed()
    {
        (float Inner, float Outer, float InnerAngle, float OuterAngle) Creep(VehicleSpec spec)
        {
            var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), 0f);
            sim.LaunchAt(3f);
            float inner = 0f, outer = 0f, innerAngle = 0f, outerAngle = 0f;
            var n = 0;
            for (var t = 0f; t < 6f; t += HandlingRig.Dt)
            {
                var throttle = MathUtil.Clamp01((3f - sim.Speed) * 0.4f + 0.12f);
                sim.Step(new DriverInputs { Steer = 1f, Throttle = throttle, Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
                if (t < 4f) continue;
                // Turning right, the right front (1) is the inner wheel.
                inner += sim.Wheels[1].SlipAngle;
                outer += sim.Wheels[0].SlipAngle;
                innerAngle += sim.Wheels[1].SteerAngle;
                outerAngle += sim.Wheels[0].SteerAngle;
                n++;
            }
            return (inner / n * MathUtil.Rad2Deg, outer / n * MathUtil.Rad2Deg, innerAngle / n * MathUtil.Rad2Deg, outerAngle / n * MathUtil.Rad2Deg);
        }
        var ackermann = Creep(VehicleSpec.HotHatch);
        var parallel = Creep(VehicleSpec.HotHatch with { AckermannShare = 0f });
        output.WriteLine($"full lock at 3 m/s: Ackermann wheels at {ackermann.InnerAngle:F1}/{ackermann.OuterAngle:F1} deg, slip {ackermann.Inner:F2}/{ackermann.Outer:F2} deg; parallel at {parallel.InnerAngle:F1}/{parallel.OuterAngle:F1} deg, slip {parallel.Inner:F2}/{parallel.Outer:F2} deg (inner/outer)");
        Assert.True(ackermann.InnerAngle > ackermann.OuterAngle + 3f);
        Assert.True(MathF.Abs(ackermann.Inner - ackermann.Outer) < 1f, "with Ackermann the front wheels should not fight");
        Assert.True(MathF.Abs(parallel.Inner - parallel.Outer) > 2f, "parallel steer should scrub the front tyres against each other");
    }

    /// <summary>
    /// Lift-off oversteer of the mid-engined cars: near the limit in a steady bend, the driver lifts off. The weight
    /// moves forward, the rear tyres lose load and with it grip while the fronts gain, the engine braking pulls on the
    /// rear wheels, and the car turns in more tightly than the wheel asks: its yaw rate rises while the speed falls.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public void Lifting_off_at_the_limit_tucks_the_tail_of_a_mid_engined_car(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        const float radius = 60f;
        // Close to the limit: 92 % of the speed at which the car stops holding the circle.
        var limit = 12f;
        while (HandlingRig.Circle(spec, radius, limit + 0.5f).Held && limit < 40f) limit += 0.5f;
        var speed = limit * 0.92f;
        var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), radius);
        HandlingRig.HoldCircle(sim, radius, speed, 16f);
        var steer = sim.SteerAngle / sim.MaxSteerAngleAt(sim.Speed);
        var before = sim.AngularVelocity.Y / sim.Speed;
        var most = before;
        for (var t = 0f; t < 1.2f; t += HandlingRig.Dt)
        {
            sim.Step(new DriverInputs { Steer = steer, Throttle = 0f, Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
            most = MathF.Max(most, sim.AngularVelocity.Y / MathF.Max(sim.Speed, 1f));
        }
        output.WriteLine($"{spec.Name}: limit {limit:F1} m/s on R {radius} m, lifted at {speed:F1}: path curvature {before * 1000f:F1} -> {most * 1000f:F1} /km");
        Assert.True(most > before * 1.12f, $"the car should tighten its line on lifting off ({before * 1000f:F1} -> {most * 1000f:F1} /km)");
    }

    /// <summary>
    /// Aerodynamic lift at speed: at 250 km/h the V8 presses itself onto the road (its tyres carry more than the car's
    /// weight, mostly at the back) while the 1980s car lifts a little, mostly at the front.
    /// </summary>
    [Fact]
    public void At_speed_the_V8_presses_down_and_the_eighties_car_lifts()
    {
        (float Total, float Front) Loads(VehicleSpec spec)
        {
            var sim = new VehicleSim(spec, HandlingRig.Proving(6000f));
            sim.LaunchAt(250f / MathUtil.KmhPerMps);
            float total = 0f, front = 0f;
            var samples = 0;
            for (var t = 0f; t < 2f; t += HandlingRig.Dt)
            {
                // Hold the speed against the drag with a force on the body's balance only: no throttle, no gear.
                sim.Step(new DriverInputs { Gear = 0 }, HandlingRig.Dt);
                if (t < 1f) continue;
                total += sim.Wheels.Sum(w => w.Load);
                front += sim.Wheels[0].Load + sim.Wheels[1].Load;
                samples++;
            }
            var weight = spec.MassKg * MathUtil.Gravity;
            return (total / samples / weight, (front / samples - (spec.RearAxle + spec.CentreOfMassOffset.Z) / (spec.FrontAxle + spec.RearAxle) * weight) / weight);
        }
        var v8 = Loads(VehicleSpec.V8Evo);
        var flat = Loads(VehicleSpec.FlatTwelve);
        output.WriteLine($"tyre load at speed as a share of the weight: V8 {v8.Total:P1} (front {v8.Front:+0.0%;-0.0%}), flat-twelve {flat.Total:P1} (front {flat.Front:+0.0%;-0.0%})");
        Assert.True(v8.Total > 1.03f, "the V8 should carry downforce");
        Assert.True(flat.Total < 0.99f, "the flat-twelve should lift");
        Assert.True(flat.Front < 0f, "the flat-twelve's lift is mostly at the front");
    }

    /// <summary>
    /// The flat-twelve's limited-slip differential: powering out of a tight bend, the unloaded inside rear wheel of an
    /// open differential spins up (the differential can only give the loaded outside wheel as much torque as the
    /// spinning one takes); the limited-slip unit holds the inside wheel near the outside one's speed. Which car gains
    /// more speed in that second is not asserted: the locked axle also pushes the car wide, as limited-slip axles do.
    /// </summary>
    [Fact]
    public void Limited_slip_differential_holds_the_inside_wheel()
    {
        (float SpinGap, float Gained) Exit(VehicleSpec spec)
        {
            const float radius = 22f;
            var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), radius);
            HandlingRig.HoldCircle(sim, radius, 9f, 10f);
            var start = sim.Speed;
            var gap = 0f;
            for (var t = 0f; t < 1.2f; t += HandlingRig.Dt)
            {
                var steer = HandlingRig.CircleSteer(sim, radius);
                sim.Step(new DriverInputs { Steer = steer, Throttle = 1f, Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
                // Turning right, the right rear (3) is the inside wheel.
                gap = MathF.Max(gap, (sim.Wheels[3].AngularVelocity - sim.Wheels[2].AngularVelocity) * spec.WheelRadius);
            }
            return (gap, sim.Speed - start);
        }
        var lsd = Exit(VehicleSpec.FlatTwelve);
        var open = Exit(VehicleSpec.FlatTwelve with { LimitedSlipLocking = 0f, LimitedSlipPreload = 0f });
        output.WriteLine($"inside wheel ahead of the outside by up to: open {open.SpinGap:F1} m/s, LSD {lsd.SpinGap:F1} m/s; speed gained: open {open.Gained:F2}, LSD {lsd.Gained:F2} m/s");
        Assert.True(open.SpinGap > 2f, $"the open differential should let the inside wheel spin ({open.SpinGap:F1} m/s)");
        Assert.True(lsd.SpinGap < open.SpinGap * 0.4f);
    }

    /// <summary>
    /// A car sliding broadside scrubs its speed off on all four tyres. Until 0.19 the tyre model faded the slip angle by
    /// the speed along the wheel alone, so a wheel moving straight sideways had no slip angle, no side force, and a car
    /// spun across the road slid on as if on ice; the deceleration fell from about 0.6 g at 60 degrees to nothing at 90.
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    public void A_car_sliding_broadside_scrubs_its_speed_off(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        float Slowing(float angleDeg)
        {
            var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), 0f);
            sim.LaunchAt(0f);
            for (var t = 0f; t < 0.5f; t += HandlingRig.Dt) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
            var a = angleDeg * MathUtil.Deg2Rad;
            const float start = 15f;
            sim.SetVelocity((sim.Forward * MathF.Cos(a) + sim.Right * MathF.Sin(a)) * start);
            foreach (var w in sim.Wheels) w.AngularVelocity = start * MathF.Cos(a) / spec.WheelRadius;
            const float seconds = 0.6f;
            for (var t = 0f; t < seconds; t += HandlingRig.Dt) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
            var flat = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
            return (start - flat) / seconds / MathUtil.Gravity;
        }
        var at60 = Slowing(60f);
        var at80 = Slowing(80f);
        var at90 = Slowing(90f);
        output.WriteLine($"{spec.Style}: slowing at 60 deg {at60:F2} g, 80 deg {at80:F2} g, 90 deg {at90:F2} g");
        Assert.True(at90 > 0.5f, $"a broadside slide should scrub speed off ({at90:F2} g)");
        Assert.True(at90 > 0.7f * at80, "the grip should not fall away as the slide nears broadside");
    }

    /// <summary>
    /// The other side of the broadside fix: a parked car nudged sideways at walking pace comes to rest and stays there.
    /// The slip angle now fades with the contact patch's whole speed, so a slow sideways creep meets side force where
    /// it met none; that force must not set the car rocking from side to side.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void A_parked_car_nudged_sideways_settles(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), 0f);
        sim.LaunchAt(0f);
        for (var t = 0f; t < 0.5f; t += HandlingRig.Dt) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
        sim.SetVelocity(sim.Right * 1.2f);
        float late = 0f;
        var stopped = -1f;
        for (var t = 0f; t < 3f; t += HandlingRig.Dt)
        {
            sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, HandlingRig.Dt);
            var speed = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
            if (stopped < 0f && speed < 0.05f) stopped = t;
            if (t > 2f) late = MathF.Max(late, speed);
        }
        output.WriteLine($"{spec.Style}: nudged sideways at 1.2 m/s, under 0.05 m/s after {stopped:F2} s, at most {late:F3} m/s in the last second");
        Assert.True(stopped >= 0f && stopped < 1.5f, "the car should come to rest");
        Assert.True(late < 0.05f, "the car should stay at rest, not rock");
    }
}
