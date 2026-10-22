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

    public EngineModel() : this(EngineSpec.FlatTwelve) { }

    public EngineModel(EngineSpec spec) => _spec = spec;

    public EngineSpec Spec => _spec;
    public bool Running { get; private set; }
    public float AngularVelocity { get; set; }               // rad/s
    public float Rpm => AngularVelocity * RpmPerRadPerSec;
    public float LastTorque { get; private set; }

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

        var full = SampleCurve(rpm);
        var braking = -(_spec.BrakingTorque + _spec.BrakingPerRpm * rpm);
        LastTorque = MathUtil.Lerp(braking, full, throttle);
        return LastTorque;
    }

    /// <summary>Advances limiter and stall bookkeeping; the drivetrain integrates the angular velocity itself.</summary>
    public void Tick(float dt)
    {
        if (_limiterCut > 0f) _limiterCut -= dt;
        if (!Running) return;

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
    /// <summary>(rpm, N m) at full throttle, ascending.</summary>
    public (float Rpm, float Torque)[] FullThrottleCurve { get; init; } = FlatTwelveCurve;

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

    public static EngineSpec FlatTwelve => new();

    public static EngineSpec V8 => new()
    {
        IdleRpm = 900f,
        RedlineRpm = 9000f,
        BrakingTorque = 22f,
        BrakingPerRpm = 0.005f,
        FullThrottleCurve = V8Curve,
    };
}
