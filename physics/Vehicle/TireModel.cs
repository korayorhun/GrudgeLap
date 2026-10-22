using System.Numerics;

namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Simplified Pacejka ("magic formula") tire with a friction-circle combination of longitudinal and
/// lateral force. The shape constants were picked for a readable, forgiving-but-honest arcade sim:
/// lateral grip peaks near 7 degrees of slip and falls off gently, so a car that is pushed too hard
/// slides progressively instead of snapping.
/// </summary>
public static class TireModel
{
    private const float LatB = 9.5f, LatC = 1.35f, LatE = 0.95f;
    private const float LongB = 11f, LongC = 1.6f, LongE = 0.97f;

    /// <summary>Friction available on each surface; load sensitivity is applied in <see cref="Forces"/>.</summary>
    public static float FrictionFor(SurfaceType surface) => surface switch
    {
        SurfaceType.Road => 1.08f,
        SurfaceType.Pad => 1.02f,
        SurfaceType.Grass => 0.55f,
        _ => 0f,
    };

    /// <summary>
    /// Returns (longitudinal, lateral) force in newtons for a wheel.
    /// </summary>
    /// <param name="slipRatio">(wheel surface speed - ground speed) / ground speed, positive when driving.</param>
    /// <param name="slipAngle">Angle between wheel heading and contact velocity, radians; positive when the contact patch slides to the right.</param>
    /// <param name="load">Vertical load in newtons.</param>
    /// <param name="friction">Peak friction coefficient of the surface.</param>
    public static (float Fx, float Fy) Forces(float slipRatio, float slipAngle, float load, float friction)
    {
        if (load <= 0f || friction <= 0f) return (0f, 0f);

        // Tires lose a little peak friction as load rises; this is what makes weight transfer matter.
        const float nominalLoad = 3900f;
        var mu = friction * MathUtil.Clamp(1f - 0.12f * (load / nominalLoad - 1f), 0.6f, 1.15f);
        var peak = mu * load;

        var fx0 = peak * Magic(slipRatio, LongB, LongC, LongE);
        // The lateral force opposes the sideways sliding of the contact patch.
        var fy0 = -peak * Magic(slipAngle, LatB, LatC, LatE);

        var total = MathF.Sqrt(fx0 * fx0 + fy0 * fy0);
        if (total > peak)
        {
            var scale = peak / total;
            fx0 *= scale;
            fy0 *= scale;
        }
        return (fx0, fy0);
    }

    private static float Magic(float x, float b, float c, float e)
    {
        var bx = b * x;
        return MathF.Sin(c * MathF.Atan(bx - e * (bx - MathF.Atan(bx))));
    }

    /// <summary>Slip ratio with a floor on the denominator so the model stays finite at standstill.</summary>
    public static float SlipRatio(float wheelSurfaceSpeed, float groundSpeed)
    {
        var denom = MathF.Max(MathF.Abs(groundSpeed), 1.2f);
        return MathUtil.Clamp((wheelSurfaceSpeed - groundSpeed) / denom, -3f, 3f);
    }

    public static float SlipAngle(float lateralSpeed, float longitudinalSpeed)
    {
        // Below walking pace the slip angle is meaningless; fade it out so the car does not jitter when parked.
        var fade = MathUtil.Clamp01((MathF.Abs(longitudinalSpeed) - 0.3f) / 1.5f);
        return MathF.Atan2(lateralSpeed, MathF.Max(MathF.Abs(longitudinalSpeed), 0.5f)) * fade;
    }
}

public enum SurfaceType
{
    /// <summary>No surface under the probe: the wheel is in the air.</summary>
    None,
    Road,
    Grass,
    /// <summary>The gap of the open drawbridge; touching the bottom is a crash.</summary>
    Chasm,
    /// <summary>The skid pad: asphalt off the road graph, no off-road clock, no wrong-way rule.</summary>
    Pad,
}
