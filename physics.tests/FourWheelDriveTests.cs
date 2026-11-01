using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The four-wheel-drive drivelines: the rally hatch's (the Delta Integrale's epicyclic centre differential, 47 % to the
/// front, with a viscous coupling across it) and the V6 hatch's (the Golf R32's 4MOTION, a Haldex coupling to the rear
/// axle), and that the two-wheel-drive cars send their torque where they always did.
/// </summary>
public sealed class FourWheelDriveTests(ITestOutputHelper output)
{
    private const float Dt = HandlingRig.Dt;

    /// <summary>
    /// A steady pull on a dry road, neither axle spinning: the epicyclic sends its fixed share, 47 % to the front and
    /// 53 % to the rear, and the coupling across it moves only a few per cent (the rear, with more torque on less load,
    /// creeps a little faster than the front, and the coupling hands that back); the Haldex car drives its fronts alone,
    /// their slip under a pull short of the lead the coupling allows.
    /// </summary>
    [Theory]
    [InlineData(3, 0.45f, 0.53f)]
    [InlineData(4, 0.97f, 1.0f)]
    public void A_steady_pull_splits_the_torque_as_the_centre_differential_does(int car, float frontMin, float frontMax)
    {
        var spec = VehicleSpec.Catalogue[car];
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        sim.LaunchAt(25f);
        float front = 0f, rear = 0f;
        var n = 0;
        for (var t = 0f; t < 2f; t += Dt)
        {
            sim.Step(new DriverInputs { Throttle = 0.8f, Gear = DriverInputs.NoGearChange }, Dt);
            if (t < 0.5f) continue;
            front += sim.FrontDriveTorque;
            rear += sim.RearDriveTorque;
            n++;
        }
        var share = front / (front + rear);
        output.WriteLine($"{spec.Name}: front {share:P1} of {(front + rear) / n:F0} N m on average, in gear {sim.Drivetrain.Gear} at {sim.Speed * MathUtil.KmhPerMps:F0} km/h");
        Assert.True(front + rear > 0f, "the car should be pulling");
        Assert.InRange(share, frontMin, frontMax);
    }

    /// <summary>
    /// One axle on grass, the other on the asphalt of the pad, the clutch let in at high revs: the axle on the grass spins
    /// up and the centre coupling hands its drive to the axle that grips. With the Delta's viscous coupling the rear (on the
    /// pad) gets well over its 53 % and the front spins less than through an open centre differential, which can give
    /// the gripping axle no more than its share of what the spinning one takes (a coupling rate of zero here); the car
    /// gains more speed. With the Golf's Haldex the rear, gripping, takes up to half the drive once the fronts spin, and
    /// the fronts, which keep the other half, spin on.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void The_centre_coupling_hands_the_drive_to_the_axle_that_grips(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        var coupled = FrontsOnGrass(spec);
        var open = FrontsOnGrass(spec with { FourWheelDrive = spec.FourWheelDrive! with { CouplingRate = 0f } });
        output.WriteLine($"{spec.Name}, fronts on grass: coupled rear share {coupled.RearShare:P0}, fronts ahead by {coupled.Gap:F1} m/s, {coupled.Gained:F2} m/s gained; open centre rear share {open.RearShare:P0}, fronts ahead by {open.Gap:F1} m/s, {open.Gained:F2} m/s gained");
        Assert.True(coupled.Coupling > 100f, $"the coupling should pass torque to the rear ({coupled.Coupling:F0} N m)");
        Assert.True(coupled.Gap < open.Gap, "the coupling should hold the spinning fronts back");
        if (spec.FourWheelDrive!.Coupling == CentreCoupling.Viscous)
        {
            Assert.True(coupled.Gap < open.Gap * 0.5f, "the viscous coupling should nearly tie the axles together");
            Assert.True(coupled.RearShare > 0.6f, $"the rear should get more than its 53 % ({coupled.RearShare:P0})");
            Assert.InRange(open.RearShare, 0.5f, 0.56f);
        }
        else
        {
            Assert.True(coupled.Gained > open.Gained, "the car should pull away harder with the rear engaged");
            Assert.InRange(coupled.RearShare, 0.25f, 0.51f);
            Assert.InRange(open.RearShare, 0f, 0.01f);
        }
    }

    /// <summary>
    /// The Haldex car's coupling stays open under braking, whatever the axles do, so that the anti-lock brakes see each axle
    /// on its own, and it closes only once the fronts outrun the rears: at a steady part-throttle pull it passes nothing.
    /// </summary>
    [Fact]
    public void The_Haldex_coupling_opens_under_braking()
    {
        var spec = VehicleSpec.TurboHatch;
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        sim.LaunchAt(25f);
        var most = 0f;
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            sim.Step(new DriverInputs { Brake = 1f, Throttle = 0.3f, Gear = DriverInputs.NoGearChange }, Dt);
            most = MathF.Max(most, MathF.Abs(sim.CouplingTorque));
        }
        Assert.Equal(0f, most);
    }

    /// <summary>
    /// The two-wheel-drive cars send all the gearbox's torque to their driven axle, none to the other and none through a
    /// coupling, through the same arithmetic as before four-wheel drive came (their figures elsewhere are unchanged to the
    /// last printed digit).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(7)]
    public void Two_wheel_drive_cars_drive_one_axle(int car)
    {
        var spec = VehicleSpec.Catalogue[car];
        Assert.Null(spec.FourWheelDrive);
        var sim = new VehicleSim(spec, HandlingRig.Proving());
        sim.LaunchAt(15f);
        for (var t = 0f; t < 2f; t += Dt)
        {
            sim.Step(new DriverInputs { Throttle = 1f, Gear = DriverInputs.NoGearChange }, Dt);
            var (driven, idle) = spec.FrontWheelDrive ? (sim.FrontDriveTorque, sim.RearDriveTorque) : (sim.RearDriveTorque, sim.FrontDriveTorque);
            Assert.Equal(0f, idle);
            Assert.Equal(0f, sim.CouplingTorque);
            Assert.InRange(driven, sim.Drivetrain.AxleTorque - 0.01f, sim.Drivetrain.AxleTorque + 0.01f);
        }
    }

    /// <summary>
    /// The car at the edge of the pad, facing out, its front wheels just on the grass and its rear ones on the pad; the
    /// engine revved to the limiter on the clutch pedal, then the clutch let in (the manual clutch, so no assist holds the
    /// tyres back). Averages over the first 0.4 s, before the rear wheels reach the grass.
    /// </summary>
    private static (float RearShare, float Coupling, float Gap, float Gained) FrontsOnGrass(VehicleSpec spec)
    {
        var track = HandlingRig.Proving();
        var sim = new VehicleSim(spec, track);
        var origin = HandlingRig.PadCentre + Vector3.UnitZ * (HandlingRig.PadRadius - 0.2f);
        sim.Reset(new RoadFrame(origin, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 0f, 4f, SurfaceType.Pad, SegmentKind.Straight, 0), track.StartCursor);
        sim.Drivetrain.Mode = TransmissionMode.ManualClutch;
        sim.Step(new DriverInputs { Gear = 0, Ignition = true, Clutch = 1f }, Dt);
        sim.Step(new DriverInputs { Gear = 1, Clutch = 1f, Brake = 1f }, Dt);
        for (var t = 0f; t < 1.2f; t += Dt) sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Clutch = 1f, Brake = 1f, Throttle = 1f }, Dt);
        Assert.Equal(SurfaceType.Grass, sim.Wheels[0].Surface);
        Assert.Equal(SurfaceType.Pad, sim.Wheels[2].Surface);
        float front = 0f, rear = 0f, coupling = 0f, gap = 0f;
        var n = 0;
        for (var t = 0f; t < 0.4f; t += Dt)
        {
            sim.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f }, Dt);
            if (t < 0.1f) continue;
            front += sim.FrontDriveTorque;
            rear += sim.RearDriveTorque;
            coupling += sim.CouplingTorque;
            gap += 0.5f * (sim.Wheels[0].AngularVelocity + sim.Wheels[1].AngularVelocity - sim.Wheels[2].AngularVelocity - sim.Wheels[3].AngularVelocity) * spec.WheelRadius;
            n++;
        }
        Assert.Equal(SurfaceType.Pad, sim.Wheels[2].Surface);
        return (rear / (front + rear), coupling / n, gap / n, sim.Speed);
    }
}
