namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Torque-curve engine with idle control, rev limiter and stall. Angular velocity is integrated by the
/// drivetrain, which also decides whether the crankshaft is rigidly coupled to the wheels. The curve and the
/// speeds come from an <see cref="EngineSpec"/>, so the same model serves the flat-twelve and the V8.
/// </summary>
public sealed class EngineModel
{
    public const float RpmPerRadPerSec = 60f / MathF.Tau;
    /// <summary>Flat-twelve figures, kept for callers that only know the default car.</summary>
    public const float IdleRpm = 950f;
    public const float RedlineRpm = 6900f;
    public const float StallRpm = 420f;

    private readonly EngineSpec _spec;
    private float _limiterCut;
    private float _lowRpmTime;
    private float _lastThrottle;

    public EngineModel() : this(EngineSpec.FlatTwelve) { }

    public EngineModel(EngineSpec spec) => _spec = spec;

    public EngineSpec Spec => _spec;
    public bool Running { get; private set; }
    public float AngularVelocity { get; set; }               // rad/s
    public float Rpm => AngularVelocity * RpmPerRadPerSec;
    public float LastTorque { get; private set; }
    /// <summary>The turbo's boost, 0 to 1 (always 0 without a turbo): it builds with the throttle above the threshold revs.</summary>
    public float Boost { get; private set; }

    public void Start()
    {
        Running = true;
        AngularVelocity = _spec.IdleRpm / RpmPerRadPerSec;
        _lowRpmTime = 0f;
    }

    public void Stop()
    {
        Running = false;
        AngularVelocity = 0f;
        Boost = 0f;
    }

    /// <summary>Crankshaft torque for the current throttle; negative values model engine braking.</summary>
    public float Torque(float throttle)
    {
        if (!Running)
        {
            // A dead engine only resists through pumping losses.
            LastTorque = -0.02f * AngularVelocity;
            return LastTorque;
        }

        var rpm = Rpm;
        throttle = MathUtil.Clamp01(throttle);

        // Idle governor: opens the throttle a little when the revs sag so the car does not stall by itself.
        var idleThrottle = MathUtil.Clamp((_spec.IdleRpm + 80f - rpm) / 400f, 0f, 0.35f);
        throttle = MathF.Max(throttle, idleThrottle);

        // Rev limiter: cut fuel above the redline for a short burst, like a real ignition cut.
        if (rpm > _spec.RedlineRpm) _limiterCut = 0.06f;
        if (_limiterCut > 0f) throttle = 0f;

        _lastThrottle = throttle;
        // A turbo's share of the torque comes with the boost, which lags the throttle (see Tick).
        var full = SampleCurve(rpm) * (1f - _spec.BoostShare * (1f - Boost));
        var braking = -(_spec.BrakingTorque + _spec.BrakingPerRpm * rpm);
        LastTorque = MathUtil.Lerp(braking, full, throttle);
        return LastTorque;
    }

    /// <summary>Advances limiter and stall bookkeeping; the drivetrain integrates the angular velocity itself.</summary>
    public void Tick(float dt)
    {
        if (_limiterCut > 0f) _limiterCut -= dt;
        if (!Running) return;
        if (_spec.BoostShare > 0f)
        {
            // Boost builds over BoostSeconds once the revs are past the threshold (fully 600 rpm above it): the lag of a
            // turbo, not an on-off switch. It falls away two and a half times slower, as the turbine runs on: through a
            // gear change or a moment of traction control the throttle shuts and opens again with most of the boost
            // still there (falling as fast as it rose, every shift cost half a second of lag).
            var target = _lastThrottle * MathUtil.Clamp01((Rpm - _spec.BoostThresholdRpm) / 600f);
            var rate = target > Boost ? 1f / _spec.BoostSeconds : 1f / (_spec.BoostSeconds * 2.5f);
            Boost = MathUtil.MoveToward(Boost, target, rate * dt);
        }

        if (Rpm < _spec.StallRpm)
        {
            _lowRpmTime += dt;
            if (_lowRpmTime > 0.12f) Stop();
        }
        else
        {
            _lowRpmTime = 0f;
        }
    }

    private float SampleCurve(float rpm)
    {
        var curve = _spec.FullThrottleCurve;
        if (rpm <= curve[0].Rpm) return curve[0].Torque;
        for (var i = 1; i < curve.Length; i++)
        {
            if (rpm > curve[i].Rpm) continue;
            var (r0, t0) = curve[i - 1];
            var (r1, t1) = curve[i];
            return MathUtil.Lerp(t0, t1, (rpm - r0) / (r1 - r0));
        }
        return 0f;
    }
}

/// <summary>Speeds and the full-throttle torque curve of an engine; game numbers tuned for feel.</summary>
public sealed record EngineSpec
{
    public float IdleRpm { get; init; } = 950f;
    public float RedlineRpm { get; init; } = 6900f;
    public float StallRpm { get; init; } = 420f;
    /// <summary>Engine braking at zero throttle: N m plus N m per rpm.</summary>
    public float BrakingTorque { get; init; } = 28f;
    public float BrakingPerRpm { get; init; } = 0.006f;
    /// <summary>(rpm, N m) at full throttle with full boost, ascending.</summary>
    public (float Rpm, float Torque)[] FullThrottleCurve { get; init; } = FlatTwelveCurve;
    /// <summary>Cylinders: the recorded engine voice fires at rpm x cylinders / 120.</summary>
    public int Cylinders { get; init; } = 12;
    /// <summary>The folder of the engine's recordings (assets/audio/&lt;folder&gt;/engine_*).</summary>
    public string SoundFolder { get; init; } = "flat12";
    /// <summary>A turbo's share of the full-throttle torque, which needs boost; zero for an engine without one.</summary>
    public float BoostShare { get; init; }
    /// <summary>Seconds the boost takes to build from nothing; it falls away in a third of that.</summary>
    public float BoostSeconds { get; init; } = 0.6f;
    /// <summary>Revs below which the turbo makes no boost.</summary>
    public float BoostThresholdRpm { get; init; } = 1800f;

    /// <summary>Peak torque around 4500 rpm, peak power near 6300 rpm: a late-80s flat-twelve.</summary>
    private static readonly (float Rpm, float Torque)[] FlatTwelveCurve =
    [
        (0f, 0f), (600f, 220f), (1000f, 300f), (2000f, 380f), (3000f, 430f), (4000f, 470f),
        (4500f, 490f), (5000f, 480f), (6000f, 452f), (6300f, 440f), (6800f, 400f), (7200f, 320f),
    ];

    /// <summary>A 4.5-litre naturally aspirated V8 that spins to 9000: peak torque near 6000, peak power near 8000.</summary>
    private static readonly (float Rpm, float Torque)[] V8Curve =
    [
        (0f, 0f), (600f, 180f), (1000f, 250f), (2000f, 370f), (3000f, 440f), (4000f, 485f), (5000f, 515f),
        (6000f, 528f), (7000f, 520f), (8000f, 505f), (8600f, 478f), (9000f, 440f), (9400f, 330f),
    ];

    /// <summary>A 2.0-litre sixteen-valve four: 200 N m at 5400, 172 hp at 6250, hard to 7200.</summary>
    private static readonly (float Rpm, float Torque)[] HotHatchCurve =
    [
        (0f, 0f), (600f, 95f), (1000f, 135f), (2000f, 160f), (3000f, 172f), (4000f, 184f), (5000f, 196f),
        (5400f, 200f), (6000f, 196f), (6250f, 193f), (6800f, 176f), (7200f, 152f), (7600f, 110f),
    ];

    /// <summary>A 2.0-litre twenty-valve five: 186 N m at 4500, 147 hp at 6100.</summary>
    private static readonly (float Rpm, float Torque)[] FiveCurve =
    [
        (0f, 0f), (600f, 105f), (1000f, 138f), (2000f, 160f), (3000f, 172f), (4000f, 182f), (4500f, 186f),
        (5000f, 184f), (6000f, 174f), (6100f, 172f), (6500f, 160f), (6800f, 146f), (7200f, 105f),
    ];

    /// <summary>A 2.0-litre turbo four with full boost: 300 N m from 2200 to 5500, 240 hp near 6000.</summary>
    private static readonly (float Rpm, float Torque)[] TurboFourCurve =
    [
        (0f, 0f), (600f, 120f), (1000f, 170f), (1500f, 230f), (2000f, 285f), (2200f, 300f), (5500f, 300f),
        (6000f, 282f), (6300f, 270f), (6700f, 236f), (7000f, 180f),
    ];

    public static EngineSpec FlatTwelve => new();

    public static EngineSpec V8 => new()
    {
        IdleRpm = 900f,
        RedlineRpm = 9000f,
        BrakingTorque = 22f,
        BrakingPerRpm = 0.005f,
        FullThrottleCurve = V8Curve,
        Cylinders = 8,
        SoundFolder = "v8",
    };

    public static EngineSpec HotHatch => new()
    {
        IdleRpm = 850f,
        RedlineRpm = 7200f,
        StallRpm = 380f,
        BrakingTorque = 12f,
        BrakingPerRpm = 0.0035f,
        FullThrottleCurve = HotHatchCurve,
        Cylinders = 4,
        SoundFolder = "hothatch",
    };

    public static EngineSpec FiveCylinder => new()
    {
        IdleRpm = 800f,
        RedlineRpm = 6800f,
        StallRpm = 380f,
        BrakingTorque = 13f,
        BrakingPerRpm = 0.0038f,
        FullThrottleCurve = FiveCurve,
        Cylinders = 5,
        SoundFolder = "five",
    };

    public static EngineSpec TurboFour => new()
    {
        IdleRpm = 800f,
        RedlineRpm = 6700f,
        StallRpm = 380f,
        BrakingTorque = 13f,
        BrakingPerRpm = 0.0035f,
        FullThrottleCurve = TurboFourCurve,
        Cylinders = 4,
        SoundFolder = "turbohatch",
        // Off boost the engine makes a little over half its torque: a small turbo on a 2.0 four spools fast.
        BoostShare = 0.45f,
        BoostSeconds = 0.55f,
        BoostThresholdRpm = 1700f,
    };
}
