namespace GrudgeLap.Audio;

/// <summary>
/// A car's engine rebuilt pulse by pulse from recordings: every firing pulse at the current revs is a grain of a
/// recording (<see cref="GrainBank"/>) taken at nearly the same revs, laid one firing period after the last and
/// overlap-added (pitch-synchronous overlap-add, PSOLA). Nothing loops and nothing is stretched: running up and
/// down the revs walks through the recording's own pulses, a steady rpm strings together the pulses recorded
/// there in their natural order and jumps among them when the recording moves on, and revs the bank does not
/// reach space its nearest pulses further apart or closer together, so the exhaust's character stays while the
/// rate changes (where a slowed-down loop turned to mud).
/// </summary>
/// <remarks>
/// <para>
/// Two banks, three where a car has one: one recorded at idle, one recorded running up through a gear, and an
/// optional one recorded on the overrun (throttle shut, the revs falling), which takes over from the run-up as
/// the load comes off. Between the top of the idle recording and the bottom of the run-up both play, crossfaded.
/// Load shapes the sound like an open or closed throttle (a low-pass that opens and a level that rises);
/// <see cref="Interior"/> hears it through the cabin, and <see cref="Voicing"/> says how that car's cabin and tailpipe
/// pass it (0.20). The voice renders at its banks' rate, allocates nothing per call and is used from one thread.
/// </para>
/// <para>
/// The rev limiter's cut (<see cref="Cut"/>, 0.20): an ignition or fuel cut stops the combustion from the next firing
/// on, so the pulses laid while it lasts are the overrun's (the cylinders only pump air) at the closed throttle's level,
/// and they fire again from the first pulse after it. The switch is made pulse by pulse, not through the load, whose
/// smoothing blurred the 60 ms cut into a dip (audit finding 12): the limiter now stutters.
/// </para>
/// <para>
/// Loudness (0.20): the recordings were made at whatever level their microphones gave, and in 0.19 the estate played
/// some 5 LU louder than the flat-twelve. The voice measures itself when it is made, pulling at full throttle between
/// 45 and 90 % of the redline (BS.1770 loudness, outside and in the cabin), and trims both to <see cref="ReferenceLufs"/>:
/// every car is about as loud at comparable driving, and what differs is the sound, not the level. The idle recording is
/// brought to <see cref="IdleLufs"/> the same way.
/// </para>
/// </remarks>
public sealed class GranularEngine
{
    /// <summary>
    /// The loudness every voice is trimmed to at full throttle and full volume, LUFS. At -15 (the 0.19 voices' mean at
    /// full throttle) the game in attract mode came out 2.4 LU under 0.19's mean level, the loud small cars having been
    /// brought down; -14 gives back half of that and lifts the engine over the wind at top speed, and any higher the
    /// turbo hatch's run-up recording, its sharpest edges brought up with it, starts to click.
    /// </summary>
    public const double ReferenceLufs = -14.0;
    /// <summary>
    /// The loudness the idle is brought to, outside, LUFS: 10 LU under <see cref="ReferenceLufs"/>, the middle of the
    /// 0.19 cars (their idles lay 7 to 15 LU under them pulling at 60 % of the redline, the saloon's 25: its idle recording
    /// is a Niva's, made quietly).
    /// </summary>
    public const double IdleLufs = ReferenceLufs - 10.0;
    /// <summary>The level a closed throttle plays at against an open one; a cut pulse plays at it too.</summary>
    public const float CutLevel = 0.42f;

    private const int RingSize = 16384;
    private const int Mask = RingSize - 1;
    /// <summary>A grain is chosen from this many of the pulses nearest in firing frequency when the recording's own next pulse no longer fits.</summary>
    private const int Spread = 32;

    private readonly GrainBank? _idle;
    private readonly GrainBank _drive;
    private readonly GrainBank? _overrun;
    private readonly int _cylinders;
    private readonly Random _random;
    private readonly float[] _sum = new float[RingSize];
    private readonly float[] _weight = new float[RingSize];
    private readonly int _latency;
    private readonly float _idleTopRpm;
    private readonly float _driveBottomRpm;
    private readonly float _outsideTrim = 1f, _cabinTrim = 1f;
    /// <summary>Brings the overrun recording to the run-up's loudness for a cut pulse (the recordings were made at their own levels).</summary>
    private readonly float _cutMatch = 1f;
    /// <summary>Brings the idle recording to <see cref="IdleLufs"/> (see the remarks).</summary>
    private readonly float _idleGain = 1f;
    private long _time;
    private double _nextPulse;
    private int _idlePulse = 1;
    private int _drivePulse = 1;
    private int _overrunPulse = 1;
    private int _cylinder;

    /// <summary>
    /// The weight of each cylinder's pulse in an inline five. Its cylinders fire 144 degrees apart and their exhaust
    /// pulses do not merge evenly, so the five sound a little differently and the cycle of five beats at half the
    /// crank's frequency: the five's warble. Laid over pulses of a four-cylinder recording (no recording of a five
    /// under load with a usable licence was found), the pattern gives that beat back.
    /// </summary>
    private static readonly float[] FivePattern = [1.00f, 0.82f, 0.94f, 0.78f, 0.90f];
    private bool _started;
    private float _rpm;
    private float _load;
    private float _smoothLoad;
    private float _level;
    private float _interior;
    private Biquad _throttle;
    private Biquad _cabinHigh;
    private Biquad _cabinBody;
    private Biquad _cabinIntake;
    private Biquad _outsideIntake;
    private Biquad _tailpipe;

    public GranularEngine(GrainBank? idle, GrainBank drive, int cylinders, int seed = 7, GrainBank? overrun = null)
        : this(idle, drive, cylinders, seed, overrun, EngineVoicing.FrontEngined, trim: true, redlineRpm: 0f)
    {
    }

    /// <param name="redlineRpm">
    /// The engine's redline: the voice is trimmed pulling between 45 and 90 % of it, the revs a car is driven at. Without
    /// one (0) it is trimmed across its run-up's recording, which may reach past the redline or stop short of it.
    /// </param>
    public GranularEngine(GrainBank? idle, GrainBank drive, int cylinders, EngineVoicing voicing, float redlineRpm = 0f, int seed = 7, GrainBank? overrun = null)
        : this(idle, drive, cylinders, seed, overrun, voicing, trim: true, redlineRpm)
    {
    }

    private GranularEngine(GrainBank? idle, GrainBank drive, int cylinders, int seed, GrainBank? overrun, EngineVoicing voicing, bool trim, float redlineRpm)
    {
        if (idle is not null && idle.SampleRate != drive.SampleRate || overrun is not null && overrun.SampleRate != drive.SampleRate)
        {
            throw new ArgumentException("the banks are at different rates");
        }
        _idle = idle;
        _drive = drive;
        _overrun = overrun;
        _cylinders = cylinders;
        _random = new Random(seed);
        Voicing = voicing;
        // A grain reaches back over the period before its pulse: pulses are laid that far ahead of the output, which
        // is also how late the voice follows the revs (about 18 ms at idle).
        _latency = Math.Max(Math.Max(idle?.LongestLead ?? 0, overrun?.LongestLead ?? 0), drive.LongestLead) + 1;
        var longest = Math.Max(Math.Max(idle?.LongestGrain ?? 0, overrun?.LongestGrain ?? 0), drive.LongestGrain);
        if (_latency + longest > RingSize) throw new ArgumentException("a grain is too long for the ring");
        _idleTopRpm = idle is null ? 0f : idle.MaxFiring * 120f / cylinders * 1.12f;
        _driveBottomRpm = MathF.Max(drive.MinFiring * 120f / cylinders, _idleTopRpm + 200f);
        _nextPulse = _latency;
        if (overrun is not null) _cutMatch = Math.Clamp((float)Math.Pow(10.0, (drive.Lufs() - overrun.Lufs()) / 20.0), 0.25f, 4f);
        if (trim)
        {
            // The revs it is trimmed at: four points over 45-90 % of the redline, or five over the run-up's recording.
            float[] revs = redlineRpm > 0f
                ? [0.45f * redlineRpm, 0.6f * redlineRpm, 0.75f * redlineRpm, 0.9f * redlineRpm]
                : RunUpRevs();
            _outsideTrim = Trim(interior: false, revs);
            _cabinTrim = Trim(interior: true, revs);
            if (idle is not null) _idleGain = IdleGain(idle);
        }
    }

    public int SampleRate => _drive.SampleRate;

    /// <summary>Heard from the driver's seat: through the cabin, as <see cref="Voicing"/> says this car's cabin passes it.</summary>
    public bool Interior { get; set; }

    /// <summary>How the car's cabin and tailpipe pass the engine's sound (where the engine sits, how long the cabin is).</summary>
    public EngineVoicing Voicing { get; }

    /// <summary>The rev limiter cuts the ignition: the pulses laid while it is set do not fire (see the remarks).</summary>
    public bool Cut { get; set; }

    /// <summary>Overall level of the voice.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>The gains the voice was given to reach <see cref="ReferenceLufs"/> outside and in the cabin.</summary>
    public (float Outside, float Cabin) Trims => (_outsideTrim, _cabinTrim);

    /// <summary>
    /// Fills <paramref name="output"/> with the engine at <paramref name="rpm"/> under <paramref name="load"/> (0 closed
    /// throttle to 1 full), gliding from the values of the previous call across the block. Stopped engines fade out.
    /// </summary>
    public void Render(Span<float> output, float rpm, float load, bool running)
    {
        if (!_started)
        {
            _rpm = rpm;
            _load = _smoothLoad = load;
            _interior = Interior ? 1f : 0f;
            _started = true;
        }
        var fromRpm = _rpm;
        var fromLoad = _load;
        var rate = (float)SampleRate;
        var loadFollow = 1f - MathF.Exp(-1f / (0.06f * rate));
        var levelFollow = 1f - MathF.Exp(-1f / ((running ? 0.04f : 0.25f) * rate));
        var cabinFollow = 1f - MathF.Exp(-1f / (0.15f * rate));
        for (var i = 0; i < output.Length; i++)
        {
            var t = (i + 1f) / output.Length;
            var nowRpm = fromRpm + (rpm - fromRpm) * t;
            _smoothLoad += (fromLoad + (load - fromLoad) * t - _smoothLoad) * loadFollow;
            while (_nextPulse <= _time + _latency)
            {
                var centre = (long)Math.Round(_nextPulse);
                PlacePulse(nowRpm, centre);
                _nextPulse += rate / (MathF.Max(nowRpm, 300f) * _cylinders / 120f);
            }

            var slot = (int)(_time & Mask);
            // Where the pulses are spread apart (revs below the bank) the gaps stay quiet instead of being blown up.
            var value = _sum[slot] / MathF.Max(_weight[slot], 0.35f);
            _sum[slot] = 0f;
            _weight[slot] = 0f;

            if ((i & 31) == 0) UpdateFilters(rate);
            value = _throttle.Process(value);
            _interior += ((Interior ? 1f : 0f) - _interior) * cabinFollow;
            // Both paths run all the time, so a change of viewpoint crossfades without a filter starting cold.
            var cabin = _cabinIntake.Process(_cabinBody.Process(_cabinHigh.Process(value))) * _cabinTrim;
            var outside = _outsideIntake.Process(_tailpipe.Process(value)) * _outsideTrim;
            value = outside + (cabin - outside) * _interior;
            // The level: CutLevel of full at a closed throttle, rising with the load, and a little with the revs.
            var target = running ? Volume * (CutLevel + (1f - CutLevel) * _smoothLoad) * (0.72f + 0.28f * Math.Clamp((nowRpm - 1000f) / 6000f, 0f, 1f)) : 0f;
            _level += (target - _level) * levelFollow;
            output[i] = value * _level;
            _time++;
        }
        _rpm = rpm;
        _load = load;
    }

    private void PlacePulse(float rpm, long centre)
    {
        var firing = MathF.Max(rpm, 300f) * _cylinders / 120f;
        _cylinder = (_cylinder + 1) % _cylinders;
        var beat = _cylinders == 5 ? FivePattern[_cylinder] : 1f;
        var drive = _idle is null ? 1f : SmoothStep((rpm - _idleTopRpm) / (_driveBottomRpm - _idleTopRpm));
        // Off the throttle the overrun recording takes the run-up's place: no combustion pulses, the exhaust's rasp.
        var overrun = _overrun is null ? 0f : drive * SmoothStep((0.40f - _smoothLoad) / 0.30f);
        // A cut pulse does not fire: the overrun's pulse in the run-up's place (the run-up's own without an overrun
        // recording), at the closed throttle's level against the level the held load sets, the overrun recording
        // brought to the run-up's loudness so that the cut is as deep on every car.
        var gain = 1f;
        var overrunGain = 1f;
        if (Cut)
        {
            if (_overrun is not null) overrun = drive;
            gain = CutLevel / (CutLevel + (1f - CutLevel) * _smoothLoad);
            overrunGain = gain * _cutMatch;
        }
        drive -= overrun;
        if (drive > 0.002f) Add(_drive, Pick(_drive, ref _drivePulse, firing), centre, drive * beat, gain);
        if (overrun > 0.002f) Add(_overrun!, Pick(_overrun!, ref _overrunPulse, firing), centre, overrun * beat, overrunGain);
        if (drive + overrun < 0.998f && _idle is not null) Add(_idle, Pick(_idle, ref _idlePulse, firing), centre, 1f - drive - overrun, gain * _idleGain);
    }

    /// <summary>The recording's next pulse while it still fits the revs (its natural sequence), otherwise one of the nearest.</summary>
    private int Pick(GrainBank bank, ref int pulse, float firing)
    {
        var next = pulse + 1;
        if (next < bank.Marks.Length - 1 && bank.Usable[next] && MathF.Abs(bank.Firing[next] / firing - 1f) < 0.06f) return pulse = next;
        return pulse = bank.Nearest(firing, Spread, _random);
    }

    /// <summary>
    /// Overlap-adds a grain: <paramref name="weight"/> is its share of the mix (the sum is normalised by the weights),
    /// <paramref name="gain"/> its level, which the normalisation leaves alone.
    /// </summary>
    private void Add(GrainBank bank, int pulse, long centre, float weight, float gain)
    {
        var marks = bank.Marks;
        var samples = bank.Samples;
        var mark = marks[pulse];
        var left = mark - marks[pulse - 1];
        var right = marks[pulse + 1] - mark;
        // Rising half of a Hann window over the period before the pulse, falling half over the one after.
        for (var j = -left; j < right; j++)
        {
            var w = j < 0 ? 0.5f - 0.5f * MathF.Cos(MathF.PI * (j + left) / left) : 0.5f + 0.5f * MathF.Cos(MathF.PI * j / right);
            var slot = (int)((centre + j) & Mask);
            _sum[slot] += weight * w * gain * samples[mark + j];
            _weight[slot] += weight * w;
        }
    }

    private void UpdateFilters(float rate)
    {
        // A closed throttle muffles the exhaust; open, it is bright. The cutoff follows the smoothed load. With an
        // overrun recording the closed-throttle sound is its own and keeps its rasp, so the filter shuts less. The
        // limiter's cut leaves the throttle open: the load the voice is given holds through it.
        var shut = _overrun is null ? 1100f : 2600f;
        _throttle.SetLowPass(shut + (12100f - shut) * MathF.Pow(_smoothLoad, 1.5f), 0.6f, rate);
        var voicing = Voicing;
        _cabinHigh.SetLowPass(voicing.CabinCutoff, 0.7f, rate);
        _cabinBody.SetPeak(voicing.BoomHz, 0.9f, voicing.BoomDb, rate);
        // The intake's roar grows with the air the engine draws: with the throttle.
        _cabinIntake.SetPeak(EngineVoicing.IntakeHz, 0.8f, voicing.CabinIntakeDb * _smoothLoad, rate);
        _outsideIntake.SetPeak(EngineVoicing.IntakeHz, 0.8f, voicing.OutsideIntakeDb * _smoothLoad, rate);
        _tailpipe.SetHighShelf(voicing.TailpipeHz, EngineVoicing.TailpipeDb, rate);
    }

    /// <summary>Five revs spread over the run-up's recording.</summary>
    private float[] RunUpRevs()
    {
        var low = MathF.Max(_drive.MinFiring * 120f / _cylinders, _driveBottomRpm);
        var high = MathF.Max(_drive.MaxFiring * 120f / _cylinders, low);
        return [.. Enumerable.Range(0, 5).Select(k => low + (high - low) * (0.1f + 0.2f * k))];
    }

    /// <summary>
    /// The gain that brings this voice to <see cref="ReferenceLufs"/> at full throttle at <paramref name="revs"/>, heard
    /// outside or in the cabin: a twin of it is rendered for a third of a second at each, and its loudness taken over all
    /// of them (BS.1770, ungated). Held between a quarter and four times.
    /// </summary>
    private float Trim(bool interior, float[] revs)
    {
        var twin = new GranularEngine(_idle, _drive, _cylinders, 1, _overrun, Voicing, trim: false, 0f) { Interior = interior };
        var rate = SampleRate;
        var settle = rate / 10;
        var each = rate / 3;
        var block = new float[settle + each];
        var heard = new float[revs.Length * each];
        for (var k = 0; k < revs.Length; k++)
        {
            for (var at = 0; at < block.Length; at += 512) twin.Render(block.AsSpan(at, Math.Min(512, block.Length - at)), revs[k], 1f, true);
            block.AsSpan(settle).CopyTo(heard.AsSpan(k * each));
        }
        var lufs = Loudness.Lufs(heard, rate);
        return Math.Clamp((float)Math.Pow(10.0, (ReferenceLufs - lufs) / 20.0), 0.25f, 4f);
    }

    /// <summary>
    /// The gain that brings the idle to <see cref="IdleLufs"/> outside: a twin rendered at the idle recording's middle
    /// revs under the idle governor's tenth of throttle, its loudness taken with the outside trim. Held between a
    /// quarter and four times, and low enough that the recording's sharpest edge stays under a step of 0.45 between
    /// samples at the idle's level (the five's idle, recorded under the bonnet, has a valve's tick of nearly full scale
    /// that would click).
    /// </summary>
    private float IdleGain(GrainBank idle)
    {
        var twin = new GranularEngine(_idle, _drive, _cylinders, 1, _overrun, Voicing, trim: false, 0f);
        var rpm = 0.5f * (idle.MinFiring + idle.MaxFiring) * 120f / _cylinders;
        var heard = new float[SampleRate];
        for (var at = 0; at < heard.Length; at += 512) twin.Render(heard.AsSpan(at, Math.Min(512, heard.Length - at)), rpm, 0.1f, true);
        var lufs = Loudness.Lufs(heard.AsSpan(SampleRate / 2), SampleRate) + 20.0 * Math.Log10(_outsideTrim);
        var sharpest = 1e-3f;
        for (var j = 1; j < idle.Samples.Length; j++) sharpest = MathF.Max(sharpest, MathF.Abs(idle.Samples[j] - idle.Samples[j - 1]));
        var level = CutLevel + (1f - CutLevel) * 0.1f;
        var clickFree = 0.45f / (sharpest * level * MathF.Max(_outsideTrim, _cabinTrim));
        return Math.Clamp((float)Math.Pow(10.0, (IdleLufs - lufs) / 20.0), 0.25f, MathF.Max(MathF.Min(4f, clickFree), 0.25f));
    }

    private static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
