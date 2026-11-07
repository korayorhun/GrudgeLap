using System.Numerics;
using GrudgeLap.Physics.Vehicle;
using Xunit;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// Standing water on the road (0.56): a tyre floats on a puddle near NASA's aquaplaning speed and loses its grip, and it
/// pushes the water aside. The road here is flooded from edge to edge (<see cref="PuddleMap.Flooded"/>), 5 mm deep in the
/// rain; the game's own puddles cover about a fifth of its asphalt.
/// </summary>
public sealed class AquaplaningTests
{
    [Fact]
    public void A_tyre_floats_only_on_water_deeper_than_its_tread_drains_and_only_fast()
    {
        Assert.Equal(0f, TireModel.Aquaplaning(40f, 1f));          // a film at 144 km/h: the wet grip alone
        Assert.Equal(0f, TireModel.Aquaplaning(15f, 5f));          // a puddle at 54 km/h: rolled through
        Assert.InRange(TireModel.Aquaplaning(25f, 5f), 0.3f, 0.8f); // at 90 km/h: partly afloat
        Assert.True(TireModel.Aquaplaning(33f, 5f) > 0.95f);        // at 120 km/h: afloat
        Assert.True(TireModel.Aquaplaning(33f, 2.5f) < 0.5f);       // a shallower puddle lifts it less
    }

    [Fact]
    public void Standing_water_lengthens_a_fast_stop_and_hardly_a_slow_one()
    {
        var fast = Stop(120f, null);
        var fastFlooded = Stop(120f, PuddleMap.Flooded);
        var slow = Stop(50f, null);
        var slowFlooded = Stop(50f, PuddleMap.Flooded);
        Assert.True(fastFlooded > fast * 1.15f, $"from 120 km/h: {fastFlooded:0.0} m flooded against {fast:0.0} m");
        Assert.InRange(slowFlooded / slow, 0.9f, 1.05f);
    }

    [Fact]
    public void The_water_pushed_aside_slows_a_coasting_car()
    {
        var open = Coast(null);
        var flooded = Coast(PuddleMap.Flooded);
        Assert.True(flooded < open - 2f, $"after 3 s from 100 km/h: {flooded:0.0} km/h flooded against {open:0.0} km/h");
    }

    /// <summary>A stop in the rain from <paramref name="kmh"/> on the proving ground's straight, metres.</summary>
    private static float Stop(float kmh, PuddleMap? puddles)
    {
        var car = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving()) { Weather = Weather.Rain, Puddles = puddles };
        car.LaunchAt(kmh / MathUtil.KmhPerMps);
        var start = car.Position;
        for (var t = 0f; car.Speed > 0.05f && t < 20f; t += HandlingRig.Dt) car.Step(new DriverInputs { Brake = 1f, Gear = 0 }, HandlingRig.Dt);
        return Vector3.Distance(start, car.Position);
    }

    /// <summary>The speed after three seconds' coasting in neutral in the rain from 100 km/h, km/h.</summary>
    private static float Coast(PuddleMap? puddles)
    {
        var car = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving()) { Weather = Weather.Rain, Puddles = puddles };
        car.LaunchAt(100f / MathUtil.KmhPerMps);
        for (var t = 0f; t < 3f; t += HandlingRig.Dt) car.Step(new DriverInputs { Gear = 0 }, HandlingRig.Dt);
        return car.Speed * MathUtil.KmhPerMps;
    }
}
