using System.Numerics;
using GrudgeLap.Physics.Vehicle;
using Xunit;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The wind on the car (0.61): a crosswind pushes a car across the road and turns its nose away from it, a headwind adds
/// to the drag and a tailwind takes from it; still air leaves everything as it was.
/// </summary>
public sealed class CrosswindTests
{
    [Fact]
    public void A_crosswind_pushes_a_car_across_the_road_and_turns_its_nose_away()
    {
        var right = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving()).Right;
        var (stillAt, stillNose) = Coast3s(Vector3.Zero);
        // 10 m/s from the car's right towards its left.
        var (windyAt, windyNose) = Coast3s(-right * 10f);
        var pushed = Vector3.Dot(windyAt - stillAt, -right);
        Assert.True(pushed > 0.3f, $"pushed {pushed:0.00} m to the left in 3 s");
        Assert.True(pushed < 5f, $"pushed {pushed:0.00} m: more than a 10 m/s wind does to a ton of car in 3 s");
        // The centre of pressure ahead of the centre of mass: the nose turns downwind, to the left.
        var turned = Vector3.Dot(windyNose - stillNose, -right);
        Assert.True(turned > 0.002f, $"the nose turned {turned:0.0000} towards the left");
    }

    [Fact]
    public void A_headwind_slows_a_coasting_car_and_a_tailwind_carries_it()
    {
        var forward = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving()).Forward;
        var still = Coast5s(Vector3.Zero);
        var head = Coast5s(-forward * 10f);
        var tail = Coast5s(forward * 10f);
        Assert.True(head < still - 0.5f, $"headwind {head:0.0} against still {still:0.0} km/h");
        Assert.True(tail > still + 0.5f, $"tailwind {tail:0.0} against still {still:0.0} km/h");
    }

    /// <summary>Three seconds' coasting in neutral down the straight from 100 km/h, the wheel held: where the car is and where its nose points.</summary>
    private static (Vector3 At, Vector3 Nose) Coast3s(Vector3 wind)
    {
        var car = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving()) { Wind = wind };
        car.LaunchAt(100f / MathUtil.KmhPerMps);
        for (var t = 0f; t < 3f; t += HandlingRig.Dt) car.Step(new DriverInputs { Gear = 0 }, HandlingRig.Dt);
        return (car.Position, car.Forward);
    }

    /// <summary>The speed after five seconds' coasting in neutral from 100 km/h, km/h.</summary>
    private static float Coast5s(Vector3 wind)
    {
        var car = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving()) { Wind = wind };
        car.LaunchAt(100f / MathUtil.KmhPerMps);
        for (var t = 0f; t < 5f; t += HandlingRig.Dt) car.Step(new DriverInputs { Gear = 0 }, HandlingRig.Dt);
        return car.Speed * MathUtil.KmhPerMps;
    }
}
