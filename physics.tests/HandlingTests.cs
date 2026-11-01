using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The handling as a proving ground would measure it (0.17): stopping distances, sprints, steady-state cornering,
/// weight transfer, anti-lock brakes, the limited-slip differential and lift-off oversteer. Each figure is checked
/// against the range the real cars the catalogue follows were measured in, or against the textbook law that governs it.
/// </summary>
public sealed class HandlingTests(ITestOutputHelper output)
{
    /// <summary>
    /// 0-100 km/h and the stop from 100 km/h, dry and in the rain, pedal hard down. The three cars with anti-lock
    /// brakes stop at the tyres' peak; the others lock their wheels, which costs them a sixth or so on the dry road, as
    /// a locked tyre slides at about three quarters of its peak friction. Ranges: published sprints (see the specs);
    /// stops of contemporary road tests (Car and Driver's 570S: 144 ft from 70 mph, about 35 m from 100 km/h; road tests
    /// of non-ABS cars of the 1980s and 1990s on 13-16 in tyres: 42-60 m locked). Rain takes a third of the grip, so
    /// the stop grows by roughly half (a little less with the wheels locked, which slide on either surface).
    /// </summary>
    [Theory]
    [InlineData(0, 5.0f, 6.5f, 39f, 50f)]    // flat-twelve: 5.8 s; no ABS
    [InlineData(1, 3.4f, 4.5f, 29f, 37f)]    // V8: 3.2 s with launch control (see the design notes); ABS
    [InlineData(2, 6.8f, 8.6f, 38f, 48f)]    // hot hatch: 7.2 s; no ABS
    [InlineData(3, 8.0f, 10.2f, 41f, 52f)]   // five: 8.5 s; no ABS
    [InlineData(4, 6.2f, 7.9f, 32f, 39f)]    // turbo hatch: 6.4 s; ABS
    [InlineData(5, 12.5f, 15.5f, 46f, 58f)]  // 1.6 saloon: 13.9 s; discs and drums, no ABS
    [InlineData(6, 15f, 19f, 48f, 62f)]      // 1.4 estate: about 16-17 s; no ABS
    [InlineData(7, 11f, 13.5f, 35f, 43f)]    // fleet saloon: 11.5 s; ABS with its ESC
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
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
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
    /// was quoted at 40/60), 42/58 for the V8 (McLaren quotes 42:58 for the 570S), about 60 % in front for the
    /// front-engined hatchbacks and 53 % for the rear-wheel-drive saloon.
    /// </summary>
    [Theory]
    [InlineData(0, 0.38f, 0.42f)]
    [InlineData(1, 0.40f, 0.44f)]
    [InlineData(2, 0.59f, 0.64f)]
    [InlineData(5, 0.51f, 0.55f)]
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
    /// follows the cars too: little for the sports cars, a lot for the soft saloons of the 1990s.
    /// </summary>
    [Theory]
    [InlineData(0, -0.3f, 1.2f, 1.2f, 2.8f)]
    [InlineData(1, -0.3f, 1.0f, 0.8f, 2.0f)]
    [InlineData(2, 0.5f, 3.5f, 2.0f, 3.6f)]
    [InlineData(3, 1.0f, 4.0f, 2.3f, 4.0f)]
    [InlineData(4, 0.6f, 3.5f, 1.8f, 3.4f)]
    [InlineData(5, 0.3f, 3.0f, 3.8f, 6.5f)]
    [InlineData(6, 1.0f, 5.0f, 4.5f, 7.5f)]
    [InlineData(7, 0.8f, 4.0f, 2.8f, 4.8f)]
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
    /// Lift-off oversteer of the mid-engined cars: near the limit in a steady bend, the driver lifts off. The weight
    /// moves forward, the rear tyres lose load and with it grip while the fronts gain, the engine braking pulls on the
    /// rear wheels, and the car turns in more tightly than the wheel asks: its yaw rate rises while the speed falls.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
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
}
