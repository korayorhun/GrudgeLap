namespace GrudgeLap.Audio;

/// <summary>What is under a tyre, as far as its sound goes.</summary>
public enum TyreGround : byte
{
    /// <summary>In the air: no sound from the tyre.</summary>
    Air,
    /// <summary>Asphalt or concrete: roar, squeal, howl and scrub.</summary>
    Asphalt,
    /// <summary>Grass, dirt or gravel: the crunch of loose ground, no squeal.</summary>
    Loose,
}

/// <summary>
/// One wheel as its sound needs it: the ground under it, how far along its force curve the tyre is
/// (<see cref="SlipToPeak"/>: 1 at the force peak, about 8 to 10 for a locked wheel on dry asphalt), how fast the
/// contact patch slides over the road along and across the wheel, how fast the wheel travels, its load against its
/// share of the car's weight, and the share of the brake pressure the anti-lock modulator lets through.
/// </summary>
public readonly record struct TyreContact(
    TyreGround Ground,
    float SlipToPeak,
    float SlideLongitudinal,
    float SlideLateral,
    float GroundSpeed,
    float LoadShare,
    float BrakePressure)
{
    /// <summary>
    /// A wheel from the simulation's own figures: the slip ratio gives the patch's sliding speed along the wheel (with
    /// the same 1.2 m/s floor under the slip ratio's denominator as the tyre model), and the ground's speed across the
    /// wheel is its sliding speed sideways, as rolling moves the patch only along. The wheel's travel is the ground's
    /// whole speed under it.
    /// </summary>
    /// <remarks>
    /// The sideways slide is taken as measured, not rebuilt from the slip angle: the speed along the wheel times the
    /// tangent of the slip angle collapsed as a slide neared broadside (the speed along falls to nothing and the
    /// simulation fades the angle), so a tyre sliding at 85 degrees went quiet (0.19 review).
    /// </remarks>
    public static TyreContact Of(TyreGround ground, float slipToPeak, float slipRatio, float lateralSpeed, float longitudinalSpeed, float loadShare, float brakePressure)
    {
        var alongSpeed = MathF.Abs(longitudinalSpeed);
        var along = MathF.Abs(slipRatio) * MathF.Max(alongSpeed, 1.2f);
        var across = MathF.Abs(lateralSpeed);
        var travel = MathF.Sqrt(alongSpeed * alongSpeed + across * across);
        return new TyreContact(ground, slipToPeak, along, across, travel, loadShare, brakePressure);
    }

    /// <summary>Speed of the rubber sliding over the road, m/s.</summary>
    public float SlideSpeed => MathF.Sqrt(SlideLongitudinal * SlideLongitudinal + SlideLateral * SlideLateral);
}

/// <summary>A recorded loop's linear gain (before the voice's own volume) and playback rate.</summary>
public readonly record struct TyreLayer(float Level, float Pitch);

/// <summary>
/// The tyres' share of the car's sound, from the simulated state of the four wheels (0.19): the levels and rates of the
/// recorded loops (roar, squeal, howl, scrub, gravel) and the drives of the synthesised layers that go with them (the
/// spray of a wet road, the anti-lock modulator). The player sets its loops from these once a frame.
/// </summary>
/// <remarks>
/// A tyre sings where the simulation says it works. Below about 60 % of the slip of its force peak it is quiet but for
/// the roar of the tread; approaching the peak the rubber in the contact patch starts to stick and slip, the squeal,
/// loudest at the limit; past the peak it slides, and the tonal squeal gives way to the broad howl of a tyre sliding
/// sideways (a drift, a spin) or the scrub of one sliding along (a locked wheel, wheelspin), pitched by how fast the
/// rubber slides over the road. Up to 0.18 the squeal only grew with the slip angle and a locked wheel shrieked as
/// loud as a spinning one whatever its slip; now the 0.18 tyre's force curve sets where each sound starts.
/// <para>
/// Each wheel adds as an independent source (power sum): one tyre at the limit is 3 dB under two, the front axle of an
/// understeering car. Heavier loaded tyres are louder (as the square root of the load). Water on the road lubricates the
/// contact patch: a wet tyre hardly squeals and its scrub is softer and lower, while the tread throws water (the spray).
/// The roar grows as the speed to the power 1.5 (30 dB a decade, the slope of measured tyre/road noise; Sandberg and
/// Ejsmont, Tyre/Road Noise Reference Book, ch. 9), where it grew almost linearly with the speed.
/// </para>
/// <para>
/// Anti-lock brakes hold the braked tyres at their peak, so a stop with them is a squeal at the limit, not a scrub. The
/// modulator's valves cycle the pressure several times a second and the squeal chatters with it; the simulation settles
/// its modulator on a nearly steady pressure within a few hundredths of a second (its cycles are too fine to hear), so
/// the chatter is laid on here at the rate real systems cycle (about seven a second) for as long as the modulator
/// holds pressure back.
/// </para>
/// </remarks>
public sealed class TyreMix
{
    /// <summary>The roar's gain at <see cref="RollReferenceSpeed"/> with all four tyres on asphalt.</summary>
    public const float RollGain = 0.34f;
    /// <summary>The roar recording was made at about motorway speed: 30 m/s plays it as recorded.</summary>
    public const float RollReferenceSpeed = 30f;
    /// <summary>Rate of the anti-lock modulator's pressure cycles heard as chatter, Hz.</summary>
    public const float AbsCycleHz = 7f;

    private float _squeal, _howl, _scrub, _gravel, _roll, _spray, _abs;
    private float _squealPitch = 1f, _howlPitch = 1f, _scrubPitch = 1f;
    private float _absPhase;

    public TyreLayer Roll { get; private set; }
    public TyreLayer Squeal { get; private set; }
    public TyreLayer Howl { get; private set; }
    public TyreLayer Scrub { get; private set; }
    public TyreLayer Gravel { get; private set; }
    /// <summary>The spray of a wet road thrown by the tread, 0 to about 1.5: drives the synthesised hiss (<see cref="BodyNoise"/>).</summary>
    public float Spray { get; private set; }
    /// <summary>How much the anti-lock modulator works, 0 to 1 (smoothed): drives its pump's buzz.</summary>
    public float Abs { get; private set; }
    /// <summary>The modulator's pressure cycle, 0 to 1, for the pump's pulses (0 when it stands aside).</summary>
    public float AbsCycle { get; private set; }

    /// <summary>
    /// Takes the wheels' state at the end of a frame of <paramref name="dt"/> seconds; <paramref name="wetness"/> is the
    /// road's, 0 dry to 1 streaming.
    /// </summary>
    public void Update(ReadOnlySpan<TyreContact> wheels, float wetness, float dt)
    {
        wetness = Math.Clamp(wetness, 0f, 1f);
        float squeal = 0f, howl = 0f, scrub = 0f, squealSlip = 0f, howlSlide = 0f, scrubSlide = 0f;
        float asphalt = 0f, loose = 0f, looseSpin = 0f, absDeficit = 0f, speed = 0f;
        var count = Math.Max(wheels.Length, 1);
        foreach (var w in wheels)
        {
            speed = MathF.Max(speed, w.GroundSpeed);
            if (w.Ground == TyreGround.Air) continue;
            if (w.Ground == TyreGround.Loose)
            {
                loose += 1f;
                looseSpin = MathF.Max(looseSpin, SmoothStep((w.SlideSpeed - 1f) / 5f));
                continue;
            }
            asphalt += 1f;
            absDeficit = MathF.Max(absDeficit, 1f - w.BrakePressure);
            var load = MathF.Sqrt(Math.Clamp(w.LoadShare, 0f, 2.5f));
            var x = w.SlipToPeak;
            // The patch must travel for the tyre to sing: nothing at a crawl, fully from about 30 km/h.
            var travel = SmoothStep((w.GroundSpeed - 1.5f) / 7f);
            var rise = SmoothStep((x - 0.6f) / 0.45f);
            var slide = SmoothStep((x - 1.25f) / 1.5f);
            var s = rise * (1f - 0.85f * slide) * travel * load;
            var slideSpeed = w.SlideSpeed;
            var across = w.SlideLateral * w.SlideLateral / MathF.Max(slideSpeed * slideSpeed, 1e-6f);
            // Sliding needs rubber moving over the road: a locked wheel at walking pace barely scrubs.
            var moving = SmoothStep((slideSpeed - 0.5f) / 4f);
            var h = slide * across * moving * load;
            var c = slide * (1f - across) * moving * load;
            squeal += s * s;
            howl += h * h;
            scrub += c * c;
            // Energy-weighted means for the pitches: the loudest tyres set them.
            squealSlip += s * s * x;
            howlSlide += h * h * w.SlideLateral;
            scrubSlide += c * c * slideSpeed;
        }
        // Power sum, normalised so two tyres at their limit (an axle) give 1.
        var squealLevel = MathF.Min(MathF.Sqrt(squeal * 0.5f), 1.2f);
        var howlLevel = MathF.Min(MathF.Sqrt(howl * 0.5f), 1.2f);
        var scrubLevel = MathF.Min(MathF.Sqrt(scrub * 0.5f), 1.2f);
        // Water: a wet tyre barely squeals, and slides with a softer, lower scrub.
        squealLevel *= 1f - 0.8f * wetness;
        howlLevel *= 1f - 0.75f * wetness;
        scrubLevel *= 1f - 0.5f * wetness;

        // The anti-lock modulator: active while it holds any braked wheel's pressure back.
        var absTarget = absDeficit > 0.03f ? 1f : 0f;
        Follow(ref _abs, absTarget, dt, 30f, 4f);
        if (_abs > 0.01f) _absPhase = (_absPhase + AbsCycleHz * dt) % 1f;
        else _absPhase = 0f;
        // Each cycle the pressure is dumped (the squeal drops away) and fed back (it returns).
        var cycle = 0.5f + 0.5f * MathF.Cos(MathF.Tau * _absPhase);
        var chatter = 1f - _abs * 0.6f * (1f - cycle);

        Follow(ref _squeal, squealLevel, dt, 25f, 8f);
        Follow(ref _howl, howlLevel, dt, 20f, 6f);
        // A wheel locking is a chirp: the scrub starts at once and stops as soon as the wheel turns again.
        Follow(ref _scrub, scrubLevel, dt, 45f, 18f);
        var gravelTarget = loose / count * SmoothStep(speed / 6f) * (0.5f + 0.5f * Math.Clamp(speed / 30f, 0f, 1f)) + 0.6f * looseSpin * loose / count;
        Follow(ref _gravel, gravelTarget, dt, 6f, 4f);
        // Past 216 km/h the roar grows no more, as the wind does not (BodyNoise): together they would bury the engine.
        var pace = MathF.Min(speed / RollReferenceSpeed, 2f);
        Follow(ref _roll, RollGain * MathF.Pow(pace, 1.5f) * asphalt / count, dt, 4f, 4f);
        Follow(ref _spray, wetness * MathF.Min(MathF.Pow(pace, 1.5f), 2f) * asphalt / count * SmoothStep((speed - 1f) / 4f), dt, 4f, 4f);

        // Pitches: the squeal climbs as the slip deepens, the howl and the scrub with the speed of the sliding rubber.
        // Kept from the last frame that had the sound, so a fading loop does not slide in pitch as it dies.
        if (squeal > 1e-6f) _squealPitch = 0.9f + 0.22f * Math.Clamp((squealSlip / squeal - 0.8f) / 1.5f, 0f, 1f);
        if (howl > 1e-6f) _howlPitch = 0.85f + 0.3f * Math.Clamp(howlSlide / howl / 15f, 0f, 1f);
        if (scrub > 1e-6f) _scrubPitch = (0.7f + 0.5f * Math.Clamp(scrubSlide / scrub / 30f, 0f, 1f)) * (1f - 0.12f * wetness);

        Roll = new TyreLayer(_roll, 0.75f + 0.5f * Math.Clamp(speed / 50f, 0f, 1f));
        Squeal = new TyreLayer(_squeal * chatter, _squealPitch);
        Howl = new TyreLayer(_howl, _howlPitch);
        Scrub = new TyreLayer(_scrub * chatter, _scrubPitch);
        Gravel = new TyreLayer(_gravel, 0.7f + 0.6f * Math.Clamp(speed / 30f, 0f, 1f));
        Spray = _spray;
        Abs = _abs;
        AbsCycle = _abs > 0.01f ? cycle : 0f;
    }

    private static void Follow(ref float value, float target, float dt, float attack, float release)
    {
        var rate = target > value ? attack : release;
        value += (target - value) * Math.Clamp(dt * rate, 0f, 1f);
    }

    private static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
