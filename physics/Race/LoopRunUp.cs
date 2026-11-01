using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// Whether a car has the pull to make the stunt route's loop (0.19). The run-up is short: the road leaves the bend round
/// the gas station at about 47 mph (21 m/s, what a driver keeps through it on the tyres of the 1990s cars) and the loop
/// starts 65 m later, where a car wants about 56 mph (25 m/s; the frictionless figure, the square root of five times g
/// times the radius, is 27 m/s, but the engine keeps pulling up the loop's first half). That asks for
/// (25^2 - 21^2) / (2 x 65), about 1.4 m/s^2 at 50 mph on the flat, in the car's best gear, after drag and rolling
/// resistance. The 1.6 saloon of the 1990s has 1.55 (75 PS, if the driver changes down to third for the run-up); the
/// 1.4 estate has 1.2 (61 PS and a tall, boxy body) and cannot make it, as the real car would not: it falls off the top.
/// <c>RaceSessionTests.Every_car_laps_both_routes</c> holds this verdict to what the scripted driver actually does.
/// </summary>
/// <remarks>
/// The track's geometry is the original's and is not changed for the slower cars; the game tells the player instead
/// (<see cref="RaceSession"/> before the fork and on choosing the stunt route) and the scripted driver takes the speed route.
/// </remarks>
public static class LoopRunUp
{
    /// <summary>Speed (m/s) at which the run-up is judged: 50 mph, between the bend's exit and the loop's foot.</summary>
    public const float JudgedAt = 50f / MathUtil.MphPerMps;
    /// <summary>Acceleration the run-up asks for at <see cref="JudgedAt"/>, m/s^2 (see the class notes), rounded down a little.</summary>
    public const float Needed = 1.35f;
    private const float AirDensity = 1.2f;

    /// <summary>True when <paramref name="spec"/> can gather the loop's speed on the run-up.</summary>
    public static bool CanMakeTheLoop(VehicleSpec spec) => Acceleration(spec, JudgedAt) >= Needed;

    /// <summary>
    /// The most the car can accelerate at <paramref name="speed"/> (m/s) on a flat road at full throttle in its best gear
    /// (one that keeps the engine under its redline): wheel force from the full-throttle curve through the gearing and
    /// the driveline's losses, less drag and rolling resistance, over the mass.
    /// </summary>
    public static float Acceleration(VehicleSpec spec, float speed)
    {
        var wheelSpeed = speed / spec.WheelRadius;
        var best = 0f;
        for (var gear = 1; gear <= spec.ForwardGearCount; gear++)
        {
            var ratio = spec.RatioForGear(gear) * spec.FinalDrive;
            var rpm = wheelSpeed * ratio * EngineModel.RpmPerRadPerSec;
            if (rpm > spec.Engine.RedlineRpm) continue;
            best = MathF.Max(best, spec.Engine.CurveTorque(rpm) * ratio * spec.DrivelineEfficiency / spec.WheelRadius);
        }
        var drag = 0.5f * AirDensity * spec.DragCoefficient * spec.FrontalArea * speed * speed;
        var rolling = spec.RollingResistance * spec.MassKg * MathUtil.Gravity;
        return (best - drag - rolling) / spec.MassKg;
    }
}
