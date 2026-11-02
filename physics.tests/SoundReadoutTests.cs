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
    /// idle, the rev limiter's cut (or the diesel's governor) shuts it, and a stopped engine has none.
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

    /// <summary>
    /// Held flat out with nothing to pull (free in neutral, and with a car's inertia behind it), stepped at the physics'
    /// 240 Hz, the fleet saloon's diesel settles on its governor (fleet review): never a cut for the sound to stutter on,
    /// and over the third second the revs steady within its last 200 rpm, the fuel there a small, steady share. A petrol
    /// engine held the same way cuts on its limiter, as before.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_diesel_settles_on_its_governor_without_a_cut(bool inGear)
    {
        static (int Cuts, float Lowest, float Highest, float ThrottleSpread) Hold(EngineSpec spec, float inertia)
        {
            var engine = new EngineModel(spec);
            engine.Start();
            engine.AngularVelocity = 0.95f * spec.RedlineRpm / EngineModel.RpmPerRadPerSec;
            var cuts = 0;
            var (lowest, highest) = (float.MaxValue, 0f);
            var (least, most) = (1f, 0f);
            for (var step = 0; step < 3 * 240; step++)
            {
                engine.AngularVelocity += engine.Torque(1f) / inertia * VehicleSim.FixedStep;
                engine.Tick(VehicleSim.FixedStep);
                if (engine.LimiterCut) cuts++;
                if (step < 2 * 240) continue;
                (lowest, highest) = (MathF.Min(lowest, engine.Rpm), MathF.Max(highest, engine.Rpm));
                (least, most) = (MathF.Min(least, engine.Throttle), MathF.Max(most, engine.Throttle));
            }
            return (cuts, lowest, highest, most - least);
        }
        var fleet = VehicleSpec.Fleet;
        var diesel = Hold(fleet.Engine, fleet.EngineInertia + (inGear ? 3f : 0f));
        Assert.True(fleet.Engine.Governor);
        Assert.Equal(0, diesel.Cuts);
        Assert.InRange(diesel.Lowest, fleet.Engine.RedlineRpm - EngineModel.GovernorSpanRpm, fleet.Engine.RedlineRpm);
        Assert.InRange(diesel.Highest - diesel.Lowest, 0f, 5f);
        Assert.InRange(diesel.ThrottleSpread, 0f, 0.02f);
        var estate = VehicleSpec.Estate;
        Assert.False(estate.Engine.Governor);
        Assert.True(Hold(estate.Engine, estate.EngineInertia + (inGear ? 3f : 0f)).Cuts > 100, "the petrol engine should cut on its limiter");
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
