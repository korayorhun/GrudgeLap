namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// A tyre's published size, e.g. 205/45 R16: section width in millimetres, aspect ratio in per cent and rim diameter in
/// inches. The handling model takes two things from it: the nominal load the tyre is built for (wider tyres carry more
/// before their grip and their cornering stiffness start to fall away with load) and its cornering stiffness per unit
/// of load (a low, stiff sidewall builds side force at a smaller slip angle than a tall balloon one).
/// </summary>
public readonly record struct TireSize(float WidthMm, float AspectPercent, float RimInches)
{
    /// <summary>Unloaded radius from the size, m: half the rim plus one sidewall.</summary>
    public float Radius => RimInches * 0.0254f * 0.5f + WidthMm * 0.001f * AspectPercent * 0.01f;

    /// <summary>
    /// Nominal load (N) of the Magic Formula's load scaling: about 4 kN for a 205-section tyre, the Fz0 of Pacejka's
    /// passenger-car example data (Pacejka, Tyre and Vehicle Dynamics, appendix 3), scaled with the width.
    /// </summary>
    public float NominalLoad => 4000f * WidthMm / 205f;

    /// <summary>
    /// Cornering stiffness per unit load at the nominal load, 1/rad. Measured values run from about 0.16/deg for
    /// 70-series touring tyres to 0.25/deg for 35-40 series performance tyres (Gillespie, Fundamentals of Vehicle
    /// Dynamics, ch. 10; Milliken, Race Car Vehicle Dynamics, ch. 2): a straight line through those by aspect ratio.
    /// </summary>
    public float CorneringCoefficient => MathUtil.Clamp(9f + (70f - AspectPercent) * 0.17f, 8f, 15.5f);
}

/// <summary>
/// Pacejka "Magic Formula" tyre with combined slip. Both slips are normalised by what the tyre's own stiffness and
/// friction make of them (u = slip x stiffness / (mu Fz), so u = 1 is where a linear tyre would reach the friction
/// limit) and combined as one vector: the force points along that vector and its size follows the Magic Formula of its
/// length. That is a friction ellipse which also gets the direction right: a locked wheel slides along its velocity and
/// gives almost no side force, which is why a car without ABS goes straight on with its front wheels locked.
/// Past the peak the force falls away: a locked wheel brakes with about three quarters of the peak friction and a tyre
/// sliding sideways keeps about nine tenths, the measured behaviour of road tyres on dry asphalt.
/// </summary>
/// <remarks>
/// Up to 0.17 the curves never fell after their peak (a locked wheel braked with 99 % of the peak and wheelspin drove
/// as hard as the ideal slip), the cornering stiffness was proportional to the load (which makes every car steer
/// neutrally whatever its weight distribution) and the friction circle only clipped the two pure forces, so a locked
/// front wheel still steered with two thirds of its side force.
/// </remarks>
public static class TireModel
{
    // Shapes of the normalised curves: slope 1 at the origin (B = 1/C). The lateral one peaks at u = 1.96 and keeps 0.89
    // of the peak at u = 6; the longitudinal one peaks at u = 2.0 and keeps 0.76 at u = 16 (a locked wheel, dry road).
    private const float LatC = 1.4f, LatE = -1.5f;
    private const float LongC = 1.5f, LongE = -1.0f;
    /// <summary>
    /// Longitudinal slip stiffness per unit load: a road tyre makes about 15 to 20 times its load per unit slip ratio
    /// (Pacejka, ch. 1; Wong, Theory of Ground Vehicles, ch. 1), which puts the peak near 10-12 % slip on dry asphalt.
    /// </summary>
    public const float LongitudinalStiffness = 18f;
    /// <summary>Normalised slip at which the longitudinal curve peaks: what an anti-lock system aims for.</summary>
    public const float LongitudinalPeak = 2.0f;
    /// <summary>Peak friction falls by this share for every nominal load added: the load sensitivity that makes weight transfer cost grip.</summary>
    private const float FrictionLoadSensitivity = 0.12f;

    /// <summary>Friction available on each surface; load sensitivity is applied in <see cref="Forces"/>.</summary>
    public static float FrictionFor(SurfaceType surface) => surface switch
    {
        SurfaceType.Road => 1.08f,
        SurfaceType.Pad => 1.02f,
        SurfaceType.Grass => 0.66f,
        _ => 0f,
    };

    /// <summary>The 1980s sports car's 225/50 R16: the tyre of a caller that does not name one.</summary>
    public static readonly TireSize DefaultTire = new(225f, 50f, 16f);

    /// <summary>Peak friction coefficient under <paramref name="load"/>: the surface's, less the load sensitivity, kept within reason.</summary>
    public static float PeakFriction(float load, float friction, in TireSize tire) =>
        friction * MathUtil.Clamp(1f - FrictionLoadSensitivity * (load / tire.NominalLoad - 1f), 0.6f, 1.15f);

    /// <summary>
    /// Cornering stiffness (N/rad) under <paramref name="load"/>, after Pacejka's load dependence
    /// K = c Fz0 sin(2 atan(Fz / (2 Fz0))): nearly proportional to the load when light, levelling off towards twice the
    /// nominal load. It is why the heavier end of a car and the outer tyre in a bend use their load less well, and so
    /// why a front-heavy car understeers and an anti-roll bar moves the balance.
    /// </summary>
    public static float CorneringStiffness(float load, in TireSize tire)
    {
        var fz0 = tire.NominalLoad;
        // Normalised so that K / Fz equals the tyre's coefficient at the nominal load (sin(2 atan(0.5)) = 0.8).
        return tire.CorneringCoefficient / 0.8f * fz0 * MathF.Sin(2f * MathF.Atan(load / (2f * fz0)));
    }

    /// <summary>Returns (longitudinal, lateral) force in newtons for a wheel on the default tyre.</summary>
    public static (float Fx, float Fy) Forces(float slipRatio, float slipAngle, float load, float friction) =>
        Forces(slipRatio, slipAngle, load, friction, DefaultTire);

    /// <summary>
    /// Returns (longitudinal, lateral) force in newtons for a wheel.
    /// </summary>
    /// <param name="slipRatio">(wheel surface speed - ground speed) / ground speed, positive when driving.</param>
    /// <param name="slipAngle">Angle between wheel heading and contact velocity, radians; positive when the contact patch slides to the right.</param>
    /// <param name="load">Vertical load in newtons.</param>
    /// <param name="friction">Peak friction coefficient of the surface (before load sensitivity).</param>
    /// <param name="tire">The tyre's size, which sets its nominal load and cornering stiffness.</param>
    public static (float Fx, float Fy) Forces(float slipRatio, float slipAngle, float load, float friction, in TireSize tire)
    {
        if (load <= 0f || friction <= 0f) return (0f, 0f);

        var mu = PeakFriction(load, friction, tire);
        var peak = mu * load;
        var ux = slipRatio * LongitudinalStiffness / mu;
        // tan(slip angle) is the lateral slip proper; SlipAngle never returns 90 degrees, the clamp only bounds it.
        var uy = MathF.Tan(MathUtil.Clamp(slipAngle, -1.4f, 1.4f)) * CorneringStiffness(load, tire) / peak;
        var u2 = ux * ux + uy * uy;
        if (u2 < 1e-12f) return (0f, 0f);
        var u = MathF.Sqrt(u2);

        // The curve's shape blends from the longitudinal one to the lateral one with the direction of the slip.
        var lateralShare = uy * uy / u2;
        var f = Magic(u, LongC, LongE) * (1f - lateralShare) + Magic(u, LatC, LatE) * lateralShare;
        var force = peak * f / u;
        // The lateral force opposes the sideways sliding of the contact patch.
        return (force * ux, -force * uy);
    }

    /// <summary>
    /// How far along its force curve a tyre is, as a share of the slip where the force peaks: below 1 it grips (the force
    /// still grows with slip), about 1 it is at the limit, beyond it slides, and a locked wheel on dry asphalt is at
    /// 8 to 11. The slips are combined as <see cref="Forces"/> combines them, so cornering and braking add up as the force does.
    /// The sound takes the squeal of a tyre at its limit and the scrub of one sliding from it (0.19).
    /// </summary>
    public static float SlipToPeak(float slipRatio, float slipAngle, float load, float friction, in TireSize tire)
    {
        if (load <= 0f || friction <= 0f) return 0f;
        var mu = PeakFriction(load, friction, tire);
        var ux = slipRatio * LongitudinalStiffness / mu;
        var uy = MathF.Tan(MathUtil.Clamp(slipAngle, -1.4f, 1.4f)) * CorneringStiffness(load, tire) / (mu * load);
        var u2 = ux * ux + uy * uy;
        if (u2 < 1e-12f) return 0f;
        // The peak sits at u = 2.0 on the longitudinal curve and 1.96 on the lateral one; blend as Forces does.
        var lateralShare = uy * uy / u2;
        return MathF.Sqrt(u2) / (LongitudinalPeak * (1f - lateralShare) + LateralPeak * lateralShare);
    }

    /// <summary>Normalised slip at which the lateral curve peaks (see the shape constants).</summary>
    private const float LateralPeak = 1.96f;

    /// <summary>
    /// Pneumatic trail at zero slip, m: how far behind the centre of the contact patch the side force acts while the patch
    /// still grips along most of its length. The brush model puts it at a sixth of the patch's length (Pacejka, Tyre and
    /// Vehicle Dynamics, ch. 3), about 2.5 cm for a road tyre near its nominal load.
    /// </summary>
    public const float PneumaticTrailAtRest = 0.03f;
    /// <summary>Normalised lateral slip at which the pneumatic trail has gone (u = 2.5, just past the side force's peak at u = 1.96).</summary>
    private const float TrailVanishes = 2.5f;

    /// <summary>
    /// Pneumatic trail (m) at <paramref name="slipAngle"/>: it shrinks as the rear of the contact patch starts to slide
    /// and is gone just past the peak of the side force, so the aligning torque (side force times trail) peaks at about
    /// two thirds of the peak slip angle and falls away before the grip does. That is the "wheel going light" a driver
    /// feels as the front tyres reach their limit (Milliken, Race Car Vehicle Dynamics, ch. 2). Linear in the normalised
    /// slip, as the brush model's trail very nearly is.
    /// </summary>
    public static float PneumaticTrail(float slipAngle, float load, float friction, in TireSize tire)
    {
        if (load <= 0f || friction <= 0f) return 0f;
        var peak = PeakFriction(load, friction, tire) * load;
        var uy = MathF.Abs(MathF.Tan(MathUtil.Clamp(slipAngle, -1.4f, 1.4f)) * CorneringStiffness(load, tire) / peak);
        return PneumaticTrailAtRest * MathF.Max(0f, 1f - uy / TrailVanishes);
    }

    /// <summary>
    /// Slip ratio at which the longitudinal force peaks on a surface of <paramref name="friction"/> (before load
    /// sensitivity, which moves it by a few per cent): where an anti-lock system holds a braked wheel.
    /// </summary>
    public static float PeakSlipRatio(float friction) => LongitudinalPeak * friction / LongitudinalStiffness;

    /// <summary>Normalised Magic Formula: slope 1 at the origin, peak 1; sin(C atan(Bu - E(Bu - atan Bu))) with B = 1/C.</summary>
    private static float Magic(float u, float c, float e)
    {
        var bu = u / c;
        return MathF.Sin(c * MathF.Atan(bu - e * (bu - MathF.Atan(bu))));
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
