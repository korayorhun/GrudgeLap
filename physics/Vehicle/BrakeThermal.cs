namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// The brakes' heat (0.49): each disc warms with the work its brake does and cools in the air, faster the faster the car
/// goes, and past about 400 °C the pads grip it less (fade): a hard stop from 200 km/h puts some 170 °C into a front
/// disc of a hatchback, so the third such stop in a row starts to fade and the fourth stops a few metres longer, as a
/// road car's iron brakes of the time did; a lap with its straights between the stops never gets near.
/// </summary>
/// <remarks>
/// A disc is a lump: its heat capacity cast iron's 460 J/(kg K) over 8 kg at the front and 5 kg at the rear for a brake of
/// 2700 N m, in proportion for a bigger or smaller one (a car's discs are sized to its brakes); its cooling a convection
/// coefficient growing with the speed, from a time constant of about eight minutes standing to a minute and a quarter at
/// 30 m/s. The work is the brake's torque times the wheel's turning: a locked wheel heats its tyre, not its disc. The pads'
/// friction holds to 400 °C and falls by a quarter at 700 °C, by two fifths at 1000 °C and no further: the brake stays a
/// brake, as the pads of road cars of the eighties and nineties did (organic and semi-metallic compounds).
/// </remarks>
public static class BrakeThermal
{
    /// <summary>The air's temperature, °C: a disc's at rest.</summary>
    public const float Ambient = 20f;

    /// <summary>Where the pads start to lose their grip on the disc, °C.</summary>
    public const float FadeFrom = 400f;

    private const float IronHeat = 460f;
    private const float ReferenceTorque = 2700f;

    /// <summary>The disc's heat capacity, J/K, for a brake of <paramref name="torque"/> N m at full pedal.</summary>
    public static float Capacity(float torque, bool front) => IronHeat * (front ? 8f : 5f) * MathF.Max(torque, 300f) / ReferenceTorque;

    /// <summary>The disc's cooling, W/K, at the car's <paramref name="speed"/> (m/s), for a brake of <paramref name="torque"/> N m.</summary>
    public static float Cooling(float torque, bool front, float speed)
    {
        var size = MathF.Max(torque, 300f) / ReferenceTorque;
        return (front ? 8f + 1.4f * MathF.Abs(speed) : 5f + 0.9f * MathF.Abs(speed)) * size;
    }

    /// <summary>The pads' grip on the disc at <paramref name="temperature"/> °C as a share of a cool one's.</summary>
    public static float Fade(float temperature)
    {
        if (temperature <= FadeFrom) return 1f;
        if (temperature <= 700f) return 1f - 0.25f * (temperature - FadeFrom) / 300f;
        return MathF.Max(0.6f, 0.75f - 0.15f * (temperature - 700f) / 300f);
    }

    /// <summary>
    /// The disc's temperature after <paramref name="dt"/> s of its brake doing <paramref name="power"/> W of work at the
    /// car's <paramref name="speed"/>; exact for the step's constant power (the lump's exponential approach).
    /// </summary>
    public static float Step(float temperature, float power, float torque, bool front, float speed, float dt)
    {
        var capacity = Capacity(torque, front);
        var cooling = Cooling(torque, front, speed);
        var target = Ambient + MathF.Max(power, 0f) / cooling;
        return target + (temperature - target) * MathF.Exp(-cooling / capacity * dt);
    }
}
