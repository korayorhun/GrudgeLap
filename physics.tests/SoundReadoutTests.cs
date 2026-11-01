using GrudgeLap.Physics.Vehicle;
using Xunit;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// What the sound reads from the simulation (0.19): the engine's throttle as its load, and how far along its force
/// curve a tyre is (<see cref="TireModel.SlipToPeak"/>), which must put 1 where the tyre model's force peaks.
/// </summary>
public sealed class SoundReadoutTests
{
    /// <summary>
    /// Every engine reports full throttle as 1 whatever its torque: the 1.4 makes a fifth of the V12's torque, and the
    /// torque over 500 N m the sound used until 0.18 read 0.2 for it. The idle governor opens the throttle a little at
    /// idle, the rev limiter's cut shuts it, and a stopped engine has none.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void The_throttle_is_the_engines_load_whatever_its_size(int car)
    {
        var spec = VehicleSpec.Catalogue[car].Engine;
        var engine = new EngineModel(spec);
        engine.Start();
        engine.AngularVelocity = spec.RedlineRpm * 0.6f / EngineModel.RpmPerRadPerSec;
        engine.Torque(1f);
        Assert.Equal(1f, engine.Throttle);
        engine.Torque(0f);
        Assert.Equal(0f, engine.Throttle);
        engine.AngularVelocity = (spec.IdleRpm - 150f) / EngineModel.RpmPerRadPerSec;
        engine.Torque(0f);
        Assert.InRange(engine.Throttle, 0.2f, 0.35f);
        engine.AngularVelocity = (spec.RedlineRpm + 50f) / EngineModel.RpmPerRadPerSec;
        engine.Torque(1f);
        Assert.Equal(0f, engine.Throttle);
        engine.Stop();
        Assert.Equal(0f, engine.Throttle);
    }

    [Theory]
    [InlineData(1.08f)]
    [InlineData(0.73f)]
    public void Slip_to_peak_is_one_where_the_force_peaks(float friction)
    {
        var tyre = TireModel.DefaultTire;
        var load = tyre.NominalLoad;
        // Braking: the slip ratio of the largest braking force.
        var bestRatio = 0f;
        var bestForce = 0f;
        for (var s = 0.005f; s < 0.6f; s += 0.0005f)
        {
            var (fx, _) = TireModel.Forces(-s, 0f, load, friction, tyre);
            if (-fx > bestForce) (bestForce, bestRatio) = (-fx, s);
        }
        Assert.InRange(TireModel.SlipToPeak(-bestRatio, 0f, load, friction, tyre), 0.97f, 1.03f);
        // Cornering: the slip angle of the largest side force.
        var bestAngle = 0f;
        bestForce = 0f;
        for (var a = 0.002f; a < 0.6f; a += 0.0005f)
        {
            var (_, fy) = TireModel.Forces(0f, a, load, friction, tyre);
            if (-fy > bestForce) (bestForce, bestAngle) = (-fy, a);
        }
        Assert.InRange(TireModel.SlipToPeak(0f, bestAngle, load, friction, tyre), 0.97f, 1.03f);
        // A locked wheel slides far past the peak; rolling freely, the tyre is nowhere near it.
        Assert.InRange(TireModel.SlipToPeak(-1f, 0f, load, friction, tyre), 6f, 14f);
        Assert.Equal(0f, TireModel.SlipToPeak(0f, 0f, load, friction, tyre));
        Assert.Equal(0f, TireModel.SlipToPeak(-1f, 0f, 0f, friction, tyre));
    }
}
