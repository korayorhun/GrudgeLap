namespace GrudgeLap.Audio;

/// <summary>
/// What the engine's voice (<see cref="GranularEngine"/>) is given from the simulated engine, once a frame: the revs,
/// the load and the rev limiter's cut, smoothed as the voice wants them. Kept out of the game so that the mapping from
/// the simulation to the sound can be tested headless (0.20).
/// </summary>
/// <remarks>
/// The load is the throttle the engine runs at (<c>EngineModel.Throttle</c>: the driver's, the idle governor's, shut
/// through an assisted shift), followed in 1/16 s. Until 0.18 it was the torque over 500 N m, which a V12 reaches and a
/// 1.4 four never comes near, and the small cars driven flat out played as if coasting (0.19 audit, finding 1). The
/// limiter's cut also shuts that throttle, for 60 ms at a time; the load holds through a cut, which the voice plays
/// pulse by pulse instead (<see cref="GranularEngine.Cut"/>): followed through the load, the cut was blurred into a dip.
/// </remarks>
public sealed class EngineFollower
{
    /// <summary>How fast the revs and the load are followed, per second.</summary>
    public const float RpmRate = 12f, LoadRate = 16f;

    public float Rpm { get; private set; }
    public float Load { get; private set; }
    /// <summary>The limiter cuts the ignition this frame.</summary>
    public bool Cut { get; private set; }

    public EngineFollower(float idleRpm = 950f) => Rpm = idleRpm;

    /// <summary>
    /// Takes the engine at the end of a frame of <paramref name="dt"/> seconds: its revs, the throttle it ran at (0 to 1)
    /// and whether the limiter has cut it.
    /// </summary>
    public void Update(float rpm, float throttle, bool limiterCut, float dt)
    {
        Rpm += (MathF.Max(rpm, 0f) - Rpm) * Math.Clamp(dt * RpmRate, 0f, 1f);
        Cut = limiterCut;
        if (!limiterCut) Load += (Math.Clamp(throttle, 0f, 1f) - Load) * Math.Clamp(dt * LoadRate, 0f, 1f);
    }
}
