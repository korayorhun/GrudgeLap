using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The brake-based differentials (dyn5): the V8's brake steer, the V6 hatch's EDS and the fleet saloon's BLD, each a
/// stability system's brake holding a driven wheel that spins ahead of its partner on an open differential; and that the
/// cars that had nothing of the kind still have nothing.
/// </summary>
/// <remarks>
/// The runs step the car once per internal step (240 Hz), so a brake or a wheel that hunted at the step rate would show
/// as speeds going up and down on alternate steps.
/// </remarks>
public sealed class ElectronicDifferentialTests(ITestOutputHelper output)
{
    private const float Step = VehicleSim.FixedStep;

    /// <summary>
    /// Only the three cars whose makers fitted one carry a brake-based differential: McLaren's brake steer on the 570S,
    /// EDS on the Golf R32 (with its ESP), BLD on the Egea's ESC. The 205 GTi had no such system, the Delta Integrale its
    /// viscous centre and Torsen rear, the flat-twelve its limited-slip unit, and the 1990s saloon and estate nothing.
    /// </summary>
    [Fact]
    public void Only_the_cars_that_had_one_brake_a_spinning_wheel()
    {
        var with = VehicleSpec.Catalogue.Where(s => s.ElectronicDifferential is not null).Select(s => s.Style).ToArray();
        Assert.Equal([CarStyle.V8, CarStyle.TurboHatch, CarStyle.Fleet], with);
    }

    /// <summary>
    /// A standing start with the left wheels on the grass beside the pad and the right ones on its asphalt, the assisted
    /// clutch and the throttle floored, steered along the pad's edge. With an open differential and no brake the wheel on
    /// the grass spins away, the launch assist backs the clutch off for it and the car crawls; with the brake holding it
    /// within its lead, the wheel on the asphalt takes its share and the car pulls away. Both the brake's torque and the
    /// held wheel run smooth at the step rate: the brake never hunts up and down on alternate steps, and the wheel does
    /// so no more than without the function (the R32's Haldex makes its front wheels do it on split grip either way: see
    /// design.md, the dynamics audit's item 14).
    /// </summary>
    [Theory]
    [InlineData(1, 2f)]
    [InlineData(4, 1.05f)]
    [InlineData(7, 1.3f)]
    public void A_wheel_spinning_on_split_grip_is_braked_and_the_car_pulls_away(int car, float gainAtLeast)
    {
        var spec = VehicleSpec.Catalogue[car];
        var braked = SplitGripStart(spec);
        var open = SplitGripStart(spec with { ElectronicDifferential = null });
        output.WriteLine($"{spec.Name}, split grip from rest: {braked.Speed:F2} m/s after 2.5 s, the grass wheel {braked.MeanLead:F2} m/s ahead on average (at most {braked.MaxLead:F2}), brake up to {braked.MostBrake:F0} N m, alternating steps: brake {braked.BrakeZigZag}, wheel {braked.WheelZigZag}; open differential {open.Speed:F2} m/s, {open.MeanLead:F2} m/s ahead (at most {open.MaxLead:F2}), wheel alternating {open.WheelZigZag}");
        var lead = spec.ElectronicDifferential!.SlipLead;
        Assert.True(braked.MostBrake > 50f, $"the brake should hold the wheel on the grass ({braked.MostBrake:F0} N m)");
        Assert.True(braked.MeanLead < lead + 0.5f, $"the wheel on the grass should be held near its lead of {lead:F1} m/s ({braked.MeanLead:F2})");
        Assert.True(braked.MaxLead < open.MaxLead * 0.8f, $"the brake should limit the spin ({braked.MaxLead:F2} against {open.MaxLead:F2} m/s)");
        Assert.True(braked.Speed > open.Speed * gainAtLeast, $"the car should pull away harder ({braked.Speed:F2} against {open.Speed:F2} m/s)");
        Assert.Equal(0, braked.BrakeZigZag);
        Assert.True(braked.WheelZigZag <= open.WheelZigZag, $"the brake should not set the wheel hunting at the step rate ({braked.WheelZigZag} against {open.WheelZigZag})");
    }

    /// <summary>
    /// Out of a tight bend on full throttle (22 m, from 9 m/s, as <c>Limited_slip_differential_holds_the_inside_wheel</c>
    /// drives the flat-twelve), the inside driven wheel, unloaded by the roll, spins ahead of the outside one; the brake
    /// holds it within its lead, with the traction control the car has. The fleet saloon gains speed by it, its engine no
    /// longer cut for one wheel's spin. The V8 does not: at 0.9 g round a 22 m circle its outside rear tyre is already
    /// near the edge of its friction circle sideways, so the drive the brake hands it costs grip across (the tail steps
    /// out a little and the circle driver gives back lock), as with the flat-twelve's limited-slip unit.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(7, true)]
    public void The_inside_wheel_out_of_a_tight_bend_is_held(int car, bool gains)
    {
        var spec = VehicleSpec.Catalogue[car];
        var braked = BendExit(spec);
        var open = BendExit(spec with { ElectronicDifferential = null });
        output.WriteLine($"{spec.Name}, out of a 22 m bend: inside wheel ahead by up to {braked.Lead:F2} m/s, brake up to {braked.MostBrake:F0} N m, {braked.Gained:F2} m/s gained, alternating steps: brake {braked.BrakeZigZag}, wheel {braked.WheelZigZag}; without the brake {open.Lead:F2} m/s, {open.Gained:F2} m/s gained, wheel alternating {open.WheelZigZag}");
        Assert.True(braked.MostBrake > 50f, $"the brake should act ({braked.MostBrake:F0} N m)");
        Assert.True(braked.Lead < open.Lead * 0.85f, $"the brake should hold the inside wheel back ({braked.Lead:F2} against {open.Lead:F2} m/s)");
        Assert.Equal(0, braked.BrakeZigZag);
        Assert.True(braked.WheelZigZag <= open.WheelZigZag);
        if (gains) Assert.True(braked.Gained > open.Gained * 1.05f, $"the car should gain more speed ({braked.Gained:F2} against {open.Gained:F2} m/s)");
    }

    /// <summary>
    /// EDS comes in only below 40 km/h and lets go at 80 (a four-wheel-drive car's window): the V6 hatch rolling on the pad
    /// in first, its left front wheel held 5 m/s ahead of its partner for a moment, has it braked at 30 km/h and not at 45; from rest on
    /// split grip, the brake that holds its wheel on the grass lets go before 80 km/h. And none of the functions brakes
    /// while the driver does (the V8 left-foot braked on split grip, its wheel on the grass spinning).
    /// </summary>
    [Fact]
    public void EDS_works_at_low_speed_only_and_no_function_brakes_under_the_pedal()
    {
        float Kicked(float kmh)
        {
            var spec = VehicleSpec.TurboHatch;
            var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), 0f);
            sim.LaunchAt(kmh / MathUtil.KmhPerMps);
            sim.Drivetrain.ForceLocked(1, sim.Wheels[1].AngularVelocity * spec.RatioForGear(1) * spec.FinalDrive);
            var most = 0f;
            for (var t = 0f; t < 0.3f; t += Step)
            {
                // Held ahead for the first 0.05 s, as a wheel that loses the road over a bump spins up.
                if (t < 0.05f) sim.Wheels[0].AngularVelocity = sim.Wheels[1].AngularVelocity + 5f / spec.WheelRadius;
                sim.Step(new DriverInputs { Throttle = 0.5f, Gear = DriverInputs.NoGearChange }, Step);
                most = MathF.Max(most, sim.Wheels[0].DifferentialBrake);
            }
            return most;
        }
        var at30 = Kicked(30f);
        var at45 = Kicked(45f);
        output.WriteLine($"a front wheel held 5 m/s ahead: braked up to {at30:F0} N m at 30 km/h, {at45:F0} N m at 45 km/h");
        Assert.True(at30 > 50f, $"EDS should brake the wheel at 30 km/h ({at30:F0} N m)");
        Assert.Equal(0f, at45);

        var launch = AtPadEdge(VehicleSpec.TurboHatch);
        launch.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        launch.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f }, Step);
        launch.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, Step);
        var lastBraked = 0f;
        for (var t = 0f; t < 6f; t += Step)
        {
            launch.Step(new DriverInputs { Throttle = 1f, Gear = DriverInputs.NoGearChange, Steer = HandlingRig.CircleSteer(launch, HandlingRig.PadRadius) }, Step);
            if (launch.Wheels.Any(w => w.DifferentialBrake > 1f)) lastBraked = launch.Speed * MathUtil.KmhPerMps;
        }
        output.WriteLine($"from rest: braked last at {lastBraked:F0} km/h");
        Assert.True(lastBraked is > 0f and < 80f, $"the brake should act from rest and let go by 80 km/h ({lastBraked:F0} km/h)");

        // Left-foot braking with the throttle floored: the clutch let in from the limiter with the brake pedal a fifth of
        // the way down, the wheel on the grass spins, and the function leaves it to the driver's foot.
        var footed = AtPadEdge(VehicleSpec.V8Evo);
        footed.Drivetrain.Mode = TransmissionMode.ManualClutch;
        footed.Step(new DriverInputs { Gear = 0, Ignition = true, Clutch = 1f }, Step);
        footed.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, Step);
        for (var t = 0f; t < 1f; t += Step) footed.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Clutch = 1f, Brake = 1f, Throttle = 1f }, Step);
        var footSpin = 0f;
        for (var t = 0f; t < 0.5f; t += Step)
        {
            footed.Step(new DriverInputs { Throttle = 1f, Brake = 0.2f, Gear = DriverInputs.NoGearChange }, Step);
            footSpin = MathF.Max(footSpin, Lead(footed.Wheels[2], footed.Wheels[3], footed.Spec.WheelRadius));
            Assert.All(footed.Wheels, w => Assert.Equal(0f, w.DifferentialBrake));
        }
        output.WriteLine($"left-foot braking: the grass wheel up to {footSpin:F1} m/s ahead, no brake from the function");
        Assert.True(footSpin > VehicleSpec.V8Evo.ElectronicDifferential!.SlipLead, $"the grass wheel should spin ({footSpin:F1} m/s)");
    }

    /// <summary>The car at rest astride the pad's edge, facing along it clockwise: its left wheels on the grass, its right ones on the pad.</summary>
    private static VehicleSim AtPadEdge(VehicleSpec spec)
    {
        var track = HandlingRig.Proving();
        var sim = new VehicleSim(spec, track);
        var origin = HandlingRig.PadCentre - Vector3.UnitX * HandlingRig.PadRadius;
        sim.Reset(new RoadFrame(origin, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 0f, 4f, SurfaceType.Pad, SegmentKind.Straight, 0), track.StartCursor);
        return sim;
    }

    private readonly record struct Run(float Speed, float MeanLead, float MaxLead, float MostBrake, int BrakeZigZag, int WheelZigZag);

    /// <summary>
    /// The split-grip start: engine running, first gear in, the brake released and the throttle floored for 2.5 s with the
    /// assisted clutch, steered along the pad's edge so the left wheels stay on the grass. Reports the driven left wheel's
    /// lead over its partner (its slip speed less the partner's; the mean over the last two thirds).
    /// </summary>
    private static Run SplitGripStart(VehicleSpec spec)
    {
        var sim = AtPadEdge(spec);
        sim.Drivetrain.Mode = TransmissionMode.ManualAutoClutch;
        sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f }, Step);
        sim.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, Step);
        for (var t = 0f; t < 1f; t += Step) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 1f }, Step);
        var grass = sim.Wheels[spec.Drives(true) ? 0 : 2];
        var road = sim.Wheels[spec.Drives(true) ? 1 : 3];
        Assert.Equal(SurfaceType.Grass, grass.Surface);
        Assert.Equal(SurfaceType.Pad, road.Surface);
        var leads = new List<float>();
        var trace = new Trace();
        const float seconds = 2.5f;
        for (var t = 0f; t < seconds; t += Step)
        {
            sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f, Steer = HandlingRig.CircleSteer(sim, HandlingRig.PadRadius) }, Step);
            leads.Add(Lead(grass, road, spec.WheelRadius));
            trace.Add(grass);
        }
        Assert.Equal(SurfaceType.Grass, grass.Surface);
        Assert.Equal(SurfaceType.Pad, road.Surface);
        return new Run(sim.Speed, leads.Skip(leads.Count / 3).Average(), leads.Max(), trace.MostBrake, trace.BrakeZigZag, trace.WheelZigZag);
    }

    /// <summary>Out of a 22 m bend: held on the circle at 9 m/s, then 1.2 s of full throttle, steering to hold the circle.</summary>
    private static (float Lead, float Gained, float MostBrake, int BrakeZigZag, int WheelZigZag) BendExit(VehicleSpec spec)
    {
        const float radius = 22f;
        var sim = HandlingRig.OnCircle(spec, HandlingRig.Proving(), radius);
        HandlingRig.HoldCircle(sim, radius, 9f, 10f);
        var start = sim.Speed;
        // Turning right, the right-hand wheel of the driven axle is the inside one.
        var (outside, inside) = spec.Drives(false) ? (sim.Wheels[2], sim.Wheels[3]) : (sim.Wheels[0], sim.Wheels[1]);
        var lead = 0f;
        var trace = new Trace();
        for (var t = 0f; t < 1.2f; t += Step)
        {
            sim.Step(new DriverInputs { Steer = HandlingRig.CircleSteer(sim, radius), Throttle = 1f, Gear = DriverInputs.NoGearChange }, Step);
            lead = MathF.Max(lead, Lead(inside, outside, spec.WheelRadius));
            trace.Add(inside);
        }
        return (lead, sim.Speed - start, trace.MostBrake, trace.BrakeZigZag, trace.WheelZigZag);
    }

    /// <summary>How far <paramref name="wheel"/>'s slip speed (its speed over the road less the road's) runs ahead of its partner's, m/s.</summary>
    private static float Lead(WheelState wheel, WheelState partner, float radius) =>
        wheel.AngularVelocity * radius - wheel.GroundSpeed - (partner.AngularVelocity * radius - partner.GroundSpeed);

    /// <summary>
    /// Watches a wheel step by step for hunting at the step rate: three changes in a row that alternate in sign, each
    /// larger than a floor (0.05 rad/s for the wheel's speed, 2 N m for the brake), is one count. A brake building and
    /// letting go over a few hundredths of a second turns over once at a time and does not count.
    /// </summary>
    private sealed class Trace
    {
        private readonly List<float> _wheel = [];
        private readonly List<float> _brake = [];
        public float MostBrake => _brake.Count == 0 ? 0f : _brake.Max();
        public int WheelZigZag => ZigZags(_wheel, 0.05f);
        public int BrakeZigZag => ZigZags(_brake, 2f);

        public void Add(WheelState w)
        {
            _wheel.Add(w.AngularVelocity);
            _brake.Add(w.DifferentialBrake);
        }

        private static int ZigZags(List<float> x, float floor)
        {
            var count = 0;
            for (var i = 3; i < x.Count; i++)
            {
                var a = x[i - 2] - x[i - 3];
                var b = x[i - 1] - x[i - 2];
                var c = x[i] - x[i - 1];
                if (a * b < 0f && b * c < 0f && MathF.Abs(a) > floor && MathF.Abs(b) > floor && MathF.Abs(c) > floor) count++;
            }
            return count;
        }
    }
}
