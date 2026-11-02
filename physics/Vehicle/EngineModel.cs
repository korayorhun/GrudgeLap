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
    /// <summary>
    /// Revs over which a diesel's governor (<see cref="EngineSpec.Governor"/>) takes the injection from full to none, up
    /// to the redline: a game figure, short enough that the curve stands to its breakaway speed.
    /// </summary>
    public const float GovernorSpanRpm = 200f;

    private readonly EngineSpec _spec;
    private float _limiterCut;
    private float _lowRpmTime;
    private float _lastThrottle;
    private bool _lastCut;

    public EngineModel() : this(EngineSpec.FlatTwelve) { }

    public EngineModel(EngineSpec spec) => _spec = spec;

    public EngineSpec Spec => _spec;
    public bool Running { get; private set; }
    public float AngularVelocity { get; set; }               // rad/s
    public float Rpm => AngularVelocity * RpmPerRadPerSec;
    public float LastTorque { get; private set; }
    /// <summary>
    /// The throttle the engine ran at in the last <see cref="Torque"/> call, 0 to 1: the driver's, opened a little by the
    /// idle governor, shut by the rev limiter's cut and cut back by a diesel's governor. It is the engine's load for the
    /// sound (0.19): the torque is not,
    /// since a 1.4 at full throttle makes a fifth of what a V12 does and once sounded as if it were coasting.
    /// </summary>
    public float Throttle => Running ? _lastThrottle : 0f;
    /// <summary>
    /// The rev limiter has cut the ignition and the fuel: no cylinder fires until the cut ends (60 ms after the revs last
    /// passed the redline). The sound reads it to stutter on the limiter (0.20) rather than read the shut throttle.
    /// </summary>
    /// <remarks>
    /// Latched in <see cref="Torque"/> with <see cref="Throttle"/>, so the two always describe the same step. Read from the
    /// countdown itself, the step whose <see cref="Tick"/> ran the cut out reported no cut with the throttle still shut by
    /// it, and the sound's held load sagged at the end of every cut (0.20 review: down to 0.4-0.7, a dip of about 2 dB).
    /// </remarks>
    /// <remarks>An engine with a <see cref="EngineSpec.Governor"/> never cuts: it reports false throughout.</remarks>
    public bool LimiterCut => Running && _lastCut;
    /// <summary>The turbo's boost, 0 to 1 (always 0 without a turbo): it builds with the throttle above the threshold revs.</summary>
    public float Boost { get; private set; }
    /// <summary>
    /// Share of the full-throttle torque the engine still makes, 1 when sound: a car knocked about badly enough loses
    /// some (a cracked radiator, a bent intake), set by the race from the damage it has taken.
    /// </summary>
    public float PowerFactor { get; set; } = 1f;

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

        if (_spec.Governor)
        {
            // A diesel's governor: the injection is cut back steadily from the breakaway speed to none at the redline,
            // so the engine holds there on part of its fuel and never stutters (see EngineSpec.Governor).
            throttle = MathF.Min(throttle, MathUtil.Clamp01((_spec.RedlineRpm - rpm) / GovernorSpanRpm));
        }
        // Rev limiter: cut fuel above the redline for a short burst, like a real ignition cut.
        else if (rpm > _spec.RedlineRpm) _limiterCut = 0.06f;
        _lastCut = _limiterCut > 0f;
        if (_lastCut) throttle = 0f;

        _lastThrottle = throttle;
        // A turbo's share of the torque comes with the boost, which lags the throttle (see Tick).
        var full = FullTorque(rpm);
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

    /// <summary>
    /// Torque (N m) the engine makes at full throttle at <paramref name="rpm"/> with the boost it has now and the damage
    /// it has taken: what the launch assist matches the clutch to. A pure query, unlike <see cref="Torque"/>.
    /// </summary>
    public float FullTorque(float rpm) => _spec.CurveTorque(rpm) * (1f - _spec.BoostShare * (1f - Boost)) * PowerFactor;

    private float SampleCurve(float rpm) => _spec.CurveTorque(rpm);
}

/// <summary>Speeds and the full-throttle torque curve of an engine; game numbers tuned for feel.</summary>
public sealed record EngineSpec
{
    /// <summary>Full-throttle torque at <paramref name="rpm"/> with full boost, interpolated from <see cref="FullThrottleCurve"/>.</summary>
    public float CurveTorque(float rpm)
    {
        var curve = FullThrottleCurve;
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

    /// <summary>
    /// Revs a standing start is made at (0.19): the lowest at which the engine gives 95 % of its peak torque, and on a
    /// turbo engine also the revs of full boost. The launch assist holds the engine there while the clutch slips, the
    /// way a driver revs and slips a manual and a launch control holds a set speed: below it the engine bogs (until 0.18
    /// the assisted clutch bit from about 2,000 rpm and dragged every car down to a lugging start), far above it the
    /// clutch only burns the difference.
    /// </summary>
    public float LaunchRpm
    {
        get
        {
            var peak = FullThrottleCurve.Max(p => p.Torque);
            var rpm = RedlineRpm * 0.5f;
            for (var r = IdleRpm; r < RedlineRpm; r += 50f)
            {
                if (CurveTorque(r) < 0.95f * peak) continue;
                rpm = r;
                break;
            }
            if (BoostShare > 0f) rpm = MathF.Max(rpm, BoostThresholdRpm + 600f);
            return MathF.Max(rpm, IdleRpm * 2.5f);
        }
    }

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
    /// <summary>
    /// What a turbo engine's boost does when the driver lifts (<see cref="TurboLiftOff"/>): vented through a diverter valve
    /// as production petrol turbo cars have, the default; or, without one, surging against the shut throttle; or, on a
    /// diesel, nothing at all, as no throttle shuts on it. Only the sound reads it.
    /// </summary>
    public TurboLiftOff LiftOff { get; init; } = TurboLiftOff.Vents;
    /// <summary>
    /// A diesel's governor in place of a petrol engine's rev limiter. A diesel has no ignition to cut: its governor (in an
    /// injection pump's flyweights, or in a common-rail diesel's control unit) takes the injected quantity back steadily
    /// from the full-load figure at the breakaway speed to none at the maximum no-load speed (Bosch, Diesel-Engine
    /// Management, the governor's breakaway and high-idle characteristics). Here the redline is that no-load speed and
    /// the injection falls over the last <see cref="EngineModel.GovernorSpanRpm"/> below it: the engine settles on the
    /// governor without the limiter's 60 ms cuts, and the sound hears no stutter.
    /// </summary>
    public bool Governor { get; init; }

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

    /// <summary>
    /// The 205 GTi's 1.9 eight-valve four (XU9JA): 161 N m at 4750, 130 PS at 6000 (152 N m there), a broad, flat curve
    /// that tails off past 6500.
    /// </summary>
    private static readonly (float Rpm, float Torque)[] HotHatchCurve =
    [
        (0f, 0f), (600f, 85f), (1000f, 110f), (2000f, 135f), (3000f, 150f), (4000f, 158f), (4750f, 161f),
        (5500f, 158f), (6000f, 152f), (6500f, 138f), (6800f, 122f), (7200f, 85f),
    ];

    /// <summary>
    /// The Delta Integrale Evoluzione's two-litre sixteen-valve turbo four with full boost: 304 N m at 3500, 210 PS at
    /// 5750 (256.5 N m there); a big T3 on an 8:1 compression ratio, so off boost it makes little more than half.
    /// </summary>
    private static readonly (float Rpm, float Torque)[] RallyTurboCurve =
    [
        (0f, 0f), (600f, 110f), (1000f, 140f), (1500f, 180f), (2000f, 230f), (2500f, 270f), (3000f, 295f),
        (3500f, 304f), (4000f, 300f), (4500f, 290f), (5000f, 277f), (5500f, 263f), (5750f, 256.5f), (6200f, 235f),
        (6500f, 212f), (6800f, 180f), (7100f, 120f),
    ];

    /// <summary>
    /// The Golf R32's 3.2-litre VR6 (BFH): 320 N m from 2800 to 3200, 241 PS at 6250 (271 N m there), a long, even
    /// curve of a big naturally aspirated six.
    /// </summary>
    private static readonly (float Rpm, float Torque)[] VrSixCurve =
    [
        (0f, 0f), (600f, 170f), (1000f, 220f), (1500f, 255f), (2000f, 285f), (2500f, 310f), (2800f, 320f),
        (3200f, 320f), (4000f, 315f), (4500f, 310f), (5000f, 302f), (5500f, 293f), (6000f, 281f), (6250f, 271f),
        (6500f, 255f), (6800f, 215f), (7100f, 150f),
    ];

    /// <summary>The 1.6 pushrod four of the 1990s saloon: 123 N m at 3000, 75 PS DIN at 5400, out of breath past 6000.</summary>
    private static readonly (float Rpm, float Torque)[] SaloonCurve =
    [
        (0f, 0f), (600f, 72f), (1000f, 92f), (1500f, 105f), (2000f, 113f), (2500f, 119f), (3000f, 123f),
        (3500f, 121f), (4000f, 117f), (4500f, 111f), (5000f, 104f), (5400f, 97.6f), (5800f, 88f), (6200f, 74f), (6500f, 58f),
    ];

    /// <summary>The 1.4 Cleon-Fonte of the estate: about 102 N m at 3000, 61.5 PS DIN at 5250.</summary>
    private static readonly (float Rpm, float Torque)[] EstateCurve =
    [
        (0f, 0f), (600f, 62f), (1000f, 78f), (1500f, 88f), (2000f, 95f), (2500f, 100f), (3000f, 102f),
        (3500f, 100f), (4000f, 96f), (4500f, 90f), (5000f, 85f), (5250f, 82f), (5600f, 74f), (6000f, 60f),
    ];

    /// <summary>
    /// The fleet saloon's 2.1-litre common-rail diesel four with its two-stage turbocharger (Mercedes-Benz OM 651, the
    /// E 250 CDI's tune of 2013): 500 N m from 1600 to 1800, 204 PS at 3800 (377 N m there), with full boost
    /// (thecarspec's listing of the W212 facelift E 250 CDI; auto motor und sport's technical data of the E 250 CDI 4MATIC
    /// give the same 3800 rpm). The curve falls away past the rated speed as the full-load injection is taken back; no
    /// figure is published above it, and the governor takes the rest away short of 4800 (see EngineSpec.Governor).
    /// </summary>
    private static readonly (float Rpm, float Torque)[] DieselCurve =
    [
        (0f, 0f), (600f, 200f), (800f, 280f), (1000f, 360f), (1200f, 430f), (1400f, 480f), (1600f, 500f), (1800f, 500f),
        (2000f, 490f), (2500f, 465f), (3000f, 432f), (3500f, 400f), (3800f, 377f), (4200f, 330f), (4500f, 280f), (4800f, 200f),
    ];

    /// <summary>
    /// The 458 Italia's 4.5-litre flat-plane V8 (F136 FB), as Ferrari published it: 540 N m at 6000 rpm, 80 % of it from
    /// 3250, 570 CV at 9000 (445 N m there), a 12.5:1 compression ratio and a 9000 rpm redline (Ferrari's 2009 press
    /// release, as conceptcarz.com and autozine.org carry it; Wikipedia's 458 page). The points between the published
    /// ones follow a naturally aspirated engine with variable valve timing on both camshafts: a curve that climbs almost to
    /// the redline, the power peaking at the limiter.
    /// </summary>
    private static readonly (float Rpm, float Torque)[] FlatPlaneV8Curve =
    [
        (0f, 0f), (600f, 170f), (1000f, 260f), (2000f, 370f), (3000f, 428f), (3250f, 432f), (4000f, 468f), (5000f, 510f),
        (6000f, 540f), (7000f, 532f), (8000f, 495f), (8600f, 462f), (9000f, 445f), (9400f, 340f),
    ];

    /// <summary>
    /// The Bravo HGT's two-litre twenty-valve five (Fiat 182 A1.000, the 155 PS tune of 1998-2001): 186 N m at 3750 and
    /// 155 PS at 6500 (167.5 N m there; auto-data.net gives the peak at 6700, cars-data at 6500), an inline five with a
    /// broad plateau from 3000 to 5500 and a long top end, as the twenty-valve head gave it.
    /// </summary>
    private static readonly (float Rpm, float Torque)[] HgtFiveCurve =
    [
        (0f, 0f), (600f, 95f), (1000f, 125f), (1500f, 150f), (2000f, 165f), (2500f, 175f), (3000f, 182f), (3750f, 186f),
        (4500f, 184f), (5000f, 181f), (5500f, 177f), (6000f, 172f), (6500f, 167.5f), (6900f, 150f), (7200f, 115f),
    ];

    /// <summary>
    /// The Bravo 1.6 16V's four (Fiat 182 A4.000, 1,581 cc): 144 N m at 4000 and 103 PS at 5750 (125.8 N m there), the
    /// figures cars-data.com and auto-data.net list for the 2001 car; an everyday sixteen-valve of its time, even from 2500
    /// to 5000 and out of breath past 6000.
    /// </summary>
    private static readonly (float Rpm, float Torque)[] HatchSixteenCurve =
    [
        (0f, 0f), (600f, 78f), (1000f, 100f), (1500f, 115f), (2000f, 126f), (2500f, 134f), (3000f, 139f), (3500f, 142f),
        (4000f, 144f), (4500f, 141f), (5000f, 135f), (5500f, 129f), (5750f, 125.8f), (6200f, 112f), (6500f, 95f), (6800f, 70f),
    ];

    /// <summary>
    /// The Leon Cupra 300's 2.0 TSI (Volkswagen EA888 generation 3, 1,984 cc, 9.3:1) with full boost: 380 N m from 1800 to
    /// 5500 and 300 PS from 5500 to 6200 (SEAT's technical sheet of the Leon SC CUPRA 300, 02/2017: 221 kW at 5500-6200,
    /// 380 N m at 1800-5500), the flat table of a small turbo held on its wastegate, falling past 5500 as the boost is
    /// taken back for the rated power. Off boost it makes about 45 % (see <see cref="TsiFour"/>).
    /// </summary>
    private static readonly (float Rpm, float Torque)[] TsiFourCurve =
    [
        (0f, 0f), (600f, 120f), (1000f, 200f), (1200f, 250f), (1500f, 330f), (1800f, 380f), (5500f, 380f), (5800f, 363f),
        (6200f, 340f), (6500f, 315f), (6800f, 270f), (7100f, 180f),
    ];

    /// <summary>
    /// The BRZ's two-litre boxer four (Subaru FA20, 12.5:1, port and direct injection): 205 N m (151 lb ft) at 6400-6600
    /// and 200 PS (200 hp) at 7000 (200.6 N m there), as Subaru's 2013 specification sheet gives them; the well-known
    /// trough between 3500 and 4500, where the engine makes about 165 N m before the torque climbs again to its peak
    /// near the redline (road tests of the 86 and BRZ of 2012-16 plot it; the figures between the published points are
    /// game figures).
    /// </summary>
    private static readonly (float Rpm, float Torque)[] BoxerCurve =
    [
        (0f, 0f), (600f, 95f), (1000f, 130f), (1500f, 160f), (2000f, 175f), (2500f, 180f), (3000f, 180f), (3500f, 172f),
        (4000f, 166f), (4500f, 170f), (5000f, 185f), (5500f, 198f), (6000f, 203f), (6400f, 205f), (6600f, 205f),
        (7000f, 200.6f), (7400f, 190f), (7700f, 140f),
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
        IdleRpm = 900f,
        RedlineRpm = 6700f,
        StallRpm = 380f,
        BrakingTorque = 12f,
        BrakingPerRpm = 0.0035f,
        FullThrottleCurve = HotHatchCurve,
        Cylinders = 4,
        SoundFolder = "hothatch",
    };

    /// <summary>
    /// The Delta's turbo four. Its voice is the turbo four of the "turbohatch" recordings (the Pulsar's), with the
    /// whistle and the blow-off valve the view synthesises for any engine with boost.
    /// </summary>
    public static EngineSpec RallyTurboFour => new()
    {
        IdleRpm = 900f,
        RedlineRpm = 6800f,
        StallRpm = 380f,
        BrakingTorque = 13f,
        BrakingPerRpm = 0.0038f,
        FullThrottleCurve = RallyTurboCurve,
        Cylinders = 4,
        SoundFolder = "turbohatch",
        // A large Garrett T3 on an 8:1 four: off boost about half the torque, and a turbo of 1991 spools slowly.
        BoostShare = 0.48f,
        BoostSeconds = 0.7f,
        BoostThresholdRpm = 2000f,
    };

    /// <summary>The R32's VR6. Its voice is a six (the "v6" recordings: a supercharged V6 of the same 120-degree firing interval).</summary>
    public static EngineSpec VrSix => new()
    {
        IdleRpm = 800f,
        RedlineRpm = 6600f,
        StallRpm = 380f,
        BrakingTorque = 16f,
        BrakingPerRpm = 0.004f,
        FullThrottleCurve = VrSixCurve,
        Cylinders = 6,
        SoundFolder = "v6",
    };

    public static EngineSpec SaloonFour => new()
    {
        IdleRpm = 850f,
        RedlineRpm = 6000f,
        StallRpm = 400f,
        BrakingTorque = 11f,
        BrakingPerRpm = 0.0032f,
        FullThrottleCurve = SaloonCurve,
        Cylinders = 4,
        SoundFolder = "saloon",
    };

    public static EngineSpec EstateFour => new()
    {
        IdleRpm = 800f,
        RedlineRpm = 5800f,
        StallRpm = 380f,
        BrakingTorque = 10f,
        BrakingPerRpm = 0.003f,
        FullThrottleCurve = EstateCurve,
        Cylinders = 4,
        SoundFolder = "estate",
    };

    /// <summary>
    /// The fleet saloon's diesel. Its two-stage turbocharger makes three fifths of the curve's torque: breathing on its
    /// own a diesel of its size makes about two fifths of what this one does on the boost of the 150 kW tune, just under
    /// 3 bar absolute (Mercedes-Benz's description of the OM 651, as motor-car.net gives it). The small high-pressure
    /// stage, which takes all the exhaust at low revs, has it on full boost by 1400 rpm and spools in about half a second
    /// (game figures: none is published). Its voice is a diesel four's (the "diesel4" recordings, a VW Group TDI's engine
    /// bay; diesel6), and its turbo spools down when the driver lifts, neither venting nor surging (<see cref="TurboLiftOff"/>).
    /// </summary>
    public static EngineSpec DieselFour => new()
    {
        IdleRpm = 750f,
        // The governor's no-load speed: Mercedes publishes the rated speed (3800) and not the governor's; a diesel of the
        // kind runs out of fuel delivery a thousand rpm or so past its rated speed (a game figure).
        RedlineRpm = 4800f,
        Governor = true,
        StallRpm = 400f,
        // A diesel has no throttle plate to pump against, but its compression and its injection pump's drive cost more
        // friction than a petrol four's: about the same engine braking as the two-litre petrol hatches.
        BrakingTorque = 14f,
        BrakingPerRpm = 0.004f,
        FullThrottleCurve = DieselCurve,
        Cylinders = 4,
        SoundFolder = "diesel4",
        BoostShare = 0.6f,
        BoostSeconds = 0.5f,
        BoostThresholdRpm = 800f,
        // No throttle plate: lifting cuts the fuel, the air goes on through the engine and the turbo spools down.
        LiftOff = TurboLiftOff.SpoolsDown,
    };

    /// <summary>
    /// The 458 Italia's flat-plane V8 (cars13). Its voice is its own "v8flat" banks (cars13 stage 2): the 458's run-up
    /// and overrun from the recording the V8's "v8" run-up has been cut from since 0.9, and a Ferrari 360's flat-plane V8
    /// at idle (game/assets/audio/SOURCES.md), voiced mid-engined as the V8 is (SampledEngine.VoicingOf).
    /// </summary>
    public static EngineSpec FlatPlaneV8 => new()
    {
        // About 1000 rpm, as the 458's tachometer reads at rest in road tests; Ferrari publishes no idle speed.
        IdleRpm = 1000f,
        RedlineRpm = 9000f,
        BrakingTorque = 22f,
        BrakingPerRpm = 0.005f,
        FullThrottleCurve = FlatPlaneV8Curve,
        Cylinders = 8,
        SoundFolder = "v8flat",
    };

    /// <summary>
    /// The Bravo HGT's five (cars13). Its voice is the five-cylinder banks of release 0.20.0, restored and re-cut
    /// ("inline5": a Volvo five's idle, CC BY, and an S2000 run-up laid at a five's firing with the five's uneven exhaust
    /// beat, GranularEngine.FivePattern). Cut as "five", the folder was renamed when the car took it (cars13 stage 2):
    /// --car=five names the Delta, whose model files carry the key of the five-cylinder hatch it replaced.
    /// </summary>
    public static EngineSpec HgtFive => new()
    {
        IdleRpm = 850f,
        // The twenty-valve five's limiter sits a little past its power peak (155 PS at 6500).
        RedlineRpm = 7000f,
        StallRpm = 380f,
        BrakingTorque = 15f,
        BrakingPerRpm = 0.004f,
        FullThrottleCurve = HgtFiveCurve,
        Cylinders = 5,
        SoundFolder = "inline5",
    };

    /// <summary>
    /// The Bravo 1.6 16V's four (cars13). Its voice is a sixteen-valve four's banks of its own ("four16v", cars13 stage 2):
    /// an MX-5's four, as no recording of Fiat's 1.6 16V was found (game/assets/audio/SOURCES.md).
    /// </summary>
    public static EngineSpec HatchSixteen => new()
    {
        IdleRpm = 850f,
        RedlineRpm = 6500f,
        StallRpm = 380f,
        BrakingTorque = 11f,
        BrakingPerRpm = 0.0032f,
        FullThrottleCurve = HatchSixteenCurve,
        Cylinders = 4,
        SoundFolder = "four16v",
    };

    /// <summary>
    /// The Leon Cupra's 2.0 TSI (cars13). Its voice is its own "tsi4" banks (cars13 stage 2: a Golf R's EA888 2.0 TSI, the
    /// same engine), with the whistle and the diverter valve the view synthesises for any engine with boost.
    /// </summary>
    public static EngineSpec TsiFour => new()
    {
        IdleRpm = 800f,
        // Volkswagen's limiter on the EA888 is a little past the rated speed's band (300 PS to 6200).
        RedlineRpm = 6800f,
        StallRpm = 400f,
        BrakingTorque = 14f,
        BrakingPerRpm = 0.004f,
        FullThrottleCurve = TsiFourCurve,
        Cylinders = 4,
        SoundFolder = "tsi4",
        // A 9.3:1 two-litre breathing on its own makes about 170 N m, 45 % of the 380 its IHI turbo gives it; a small
        // modern turbo with a twin-scroll housing is on full boost within about a third of a second and from 1800 rpm
        // (SEAT's 380 N m from 1800), so the threshold sits 600 rpm under that (EngineModel.Tick). Game figures: SEAT
        // publishes neither the spool time nor the off-boost torque.
        BoostShare = 0.55f,
        BoostSeconds = 0.35f,
        BoostThresholdRpm = 1200f,
    };

    /// <summary>
    /// The BRZ's boxer four (cars13). Its voice is its own "boxer4" banks (cars13 stage 2): the FA20 on a dynamometer, its
    /// own note (equal-length headers, unlike the older Subarus' burble), played as the even four it is. Until they landed
    /// it played the V6 hatch's recordings at a four's firing, as no other bank's idle was recorded low enough for its
    /// 700 rpm (the hot hatch's, at 1,140 rpm, left gaps 24-28 dB deep between its pulses laid that slow).
    /// </summary>
    public static EngineSpec BoxerFour => new()
    {
        // The FA20 idles at 650 +/- 100 rpm with the manual gearbox (Subaru's BRZ service manual of 2013; 550-750 in
        // the 2015 service data): 700, within both and what road tests' tachometers show warm.
        IdleRpm = 700f,
        // Subaru's redline, 7400 rpm, where the model's limiter cuts (the FA20's own cut is said to sit a little past it;
        // its figure is not published).
        RedlineRpm = 7400f,
        StallRpm = 380f,
        BrakingTorque = 13f,
        BrakingPerRpm = 0.0038f,
        FullThrottleCurve = BoxerCurve,
        Cylinders = 4,
        SoundFolder = "boxer4",
    };
}

/// <summary>
/// What a turbocharged engine's boost does when the driver lifts off (<see cref="EngineSpec.LiftOff"/>), as its sound
/// hears it (the game's TurboNoise).
/// </summary>
public enum TurboLiftOff
{
    /// <summary>
    /// A petrol engine's throttle shuts on the boost and a diverter (recirculating) valve, opened by the manifold's vacuum,
    /// lets the charge round the compressor: a short rush. Production petrol turbo cars have one.
    /// </summary>
    Vents,

    /// <summary>The throttle shuts on the boost with no valve to relieve it: the compressor surges against it and flutters.</summary>
    Surges,

    /// <summary>
    /// A diesel has no throttle plate (its load is the injected fuel), so lifting shuts nothing on the boost: the air goes on
    /// through the engine, the charge falls with the exhaust's energy and the turbo spools down, neither venting nor
    /// surging. Diesel turbochargers are fitted without a blow-off or diverter valve for that reason.
    /// </summary>
    SpoolsDown,
}
