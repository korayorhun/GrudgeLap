using GrudgeLap.Physics.Vehicle;
using Xunit;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// Rev-matched changes down in the assisted gearboxes (0.68): with the clutch open the engine is blipped to the lower
/// gear's shaft speed, so the clutch takes up with little slip and does not snatch the driven tyres under braking; a
/// change up still lifts.
/// </summary>
public sealed class DownshiftTests
{
    [Fact]
    public void A_change_down_under_braking_blips_the_engine_to_the_lower_gear()
    {
        var r = ChangeDown(VehicleSpec.FiveHatch, 100f);
        Assert.True(r.PeakThrottle > 0.3f, $"the throttle peaked at {r.PeakThrottle:0.00} during the change");
        Assert.True(r.GapAtLock < 400f, $"the engine was {r.GapAtLock:0} rpm from the shaft when the clutch locked");
        Assert.True(r.Locked, "the clutch never locked again");
    }

    [Fact]
    public void A_change_up_still_lifts_the_throttle()
    {
        var car = new VehicleSim(VehicleSpec.FiveHatch, HandlingRig.Proving());
        car.LaunchAt(50f / MathUtil.KmhPerMps);
        var gear = car.Drivetrain.Gear;
        car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f, ShiftUp = true }, HandlingRig.Dt);
        Assert.Equal(gear + 1, car.Drivetrain.Gear);
        var peak = 0f;
        for (var t = 0f; t < 0.2f; t += HandlingRig.Dt)
        {
            car.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Throttle = 1f }, HandlingRig.Dt);
            peak = MathF.Max(peak, car.Drivetrain.Engine.Throttle);
        }
        Assert.True(peak < 0.01f, $"the throttle reached {peak:0.00} during a change up");
    }

    private readonly record struct Result(float PeakThrottle, float GapAtLock, bool Locked, float PeakAxleTorque);

    /// <summary>One gear down at <paramref name="kmh"/> under half braking: the shift's peak throttle, the engine's gap to the shaft as the clutch locks, the peak axle torque.</summary>
    private static Result ChangeDown(VehicleSpec spec, float kmh)
    {
        var car = new VehicleSim(spec, HandlingRig.Proving());
        car.LaunchAt(kmh / MathUtil.KmhPerMps);
        var braking = new DriverInputs { Gear = DriverInputs.NoGearChange, Brake = 0.5f };
        for (var t = 0f; t < 0.2f; t += HandlingRig.Dt) car.Step(braking, HandlingRig.Dt);
        car.Step(braking with { ShiftDown = true }, HandlingRig.Dt);
        var peakThrottle = 0f;
        var peakTorque = 0f;
        var gap = float.NaN;
        var locked = false;
        for (var t = 0f; t < 1.2f && !locked; t += HandlingRig.Dt)
        {
            var before = car.Drivetrain.Engine.Rpm;
            car.Step(braking, HandlingRig.Dt);
            peakThrottle = MathF.Max(peakThrottle, car.Drivetrain.Engine.Throttle);
            peakTorque = MathF.Max(peakTorque, MathF.Abs(car.Drivetrain.AxleTorque));
            if (car.Drivetrain.ClutchLocked)
            {
                locked = true;
                var shaft = MathF.Abs(car.Wheels[0].AngularVelocity * car.Drivetrain.Ratio) * EngineModel.RpmPerRadPerSec;
                gap = MathF.Abs(before - shaft);
            }
        }
        return new Result(peakThrottle, gap, locked, peakTorque);
    }
}
