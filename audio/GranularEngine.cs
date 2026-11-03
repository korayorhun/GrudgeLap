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
/// Between the idle and the run-up (banks): across the revs between the top of the idle's recording and the bottom of
/// the run-up's the two recordings take over from each other, and held at full throttle the voice sagged there, 3 to 8
/// LU under both ends (the rally hatch at 30 % of its redline, 2,000 rpm, 4 LU under its run-up at 2,400; the V8 at
/// 1,900 rpm 7 LU under its idle and its run-up). Three things did it. The idle's pulses, laid closer than recorded,
/// overlapped several at a time and averaged one another down; now, like the run-up past its top, each is held to the
/// period it is laid at and plays at its recorded strength. Two recordings mixed by their shares in amplitude do not add
/// in phase and lost up to 3 dB halfway; now each is normalised apart and keeps the square root of its share, their
/// powers summing to one. And the idle's level is the engine's idling (<see cref="IdleGain"/>) whatever the throttle; now
/// pulling, its pulses are levelled onto the run-up's trend like the run-up's own and brought to the loudness of the
/// run-up's pulses across the revs where the two take over from each other (<see cref="LoadedIdleGain"/>). Held every
/// 100 rpm from the idle to the redline at full throttle, the voice now moves by at most 1.1 LU from one step to the
/// next, and no point lies more than 1.5 LU under the loudest within 600 rpm on both sides but on the estate, 1.7 LU at
/// 1,200 rpm, where the quieter of its two idle recordings plays (banks review; the tests pin it). The fleet saloon's
/// petrol banks sagged 2.0 LU in its cabin at 1,700 rpm, where their run-up played under the revs its recording started
/// at; its diesel's banks (diesel6) sag 0.9 LU at most.
/// At a light throttle the idle keeps its own level, and between a light throttle and a full one it rises evenly in
/// decibels (<see cref="IdleLevel"/>).
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
/// <para>
/// Level across the revs (soundfix): within each of its recordings a run-up's level drifts (the car draws away, a blip
/// fades at its top), and held at a given rpm the voice played that stretch's level: up to 7 LU from one 5 % of the
/// redline to the next (the estate), the rally hatch 4 LU quieter at 5500 rpm than at 4300. Every grain of the run-up
/// is levelled onto the run-up's trend of level, smoothed over a dozen pulses so the cylinders' own beat stays
/// (<see cref="GrainBank.LevelGains"/>), and every grain of the overrun onto the same trend: at any revs the overrun is
/// then as loud as the run-up recorded there, and what a shut throttle or a cut takes away is the level law
/// (<see cref="CutLevel"/>), the throttle's filter and the overrun's own sound, and past the top of the overrun's
/// recording, where its grains are averaged, more (the cut swings 7 dB on the estate, whose overrun reaches its
/// redline, 11 to 19 dB on the others). Before, a cut pulse was matched by the two recordings' overall loudness and the
/// closed throttle not at all, and once the run-ups were levelled the rally hatch's overrun at 4500 rpm and the V8's at
/// 5600 came within 5 dB of their full throttle.
/// </para>
/// <para>
/// Peaks (0.20 review): trimmed to the reference alone, three cars peaked over full scale at the game's volume of 0.9 (the
/// fleet saloon's petrol banks at +2.8 dBFS, the turbo hatch at +1.1, the V8 at +1.2), and the mix's limiter pulled everything down with
/// them, tyres and wind included, by up to 3.3 dB. Neither trim may now lift the voice's peaks at full throttle and full
/// <see cref="Volume"/> over <see cref="PeakCeiling"/>: the trim's renders, taken on every 5 % from 30 % of the redline
/// to the redline, give the peak (but for its rarest samples, <see cref="PeakMeter"/>), and a car whose recording is
/// peakier than it is loud plays a little under the reference rather than over full scale. A rarer peak (a click in a
/// recording, a grain with a sharper edge than the renders met) is rounded off above <see cref="PeakKnee"/> instead,
/// so the voice never leaves <see cref="PeakCeiling"/>: at full throttle that touches no more than a few samples in
/// 10,000 at the game's volume.
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
    /// <summary>
    /// The voice's peaks: never over <see cref="PeakCeiling"/> (-1 dBFS), and above <see cref="PeakKnee"/> (-1.9 dBFS,
    /// the trimmed peaks at the game's volume of 0.9) softly rounded off towards it (see the remarks).
    /// </summary>
    public const float PeakCeiling = 0.89f, PeakKnee = 0.8f;
    /// <summary>The level a closed throttle plays at against an open one; a cut pulse plays at it too.</summary>
    public const float CutLevel = 0.42f;

    private const int RingSize = 16384;
    private const int Mask = RingSize - 1;
    /// <summary>A grain is chosen from this many of the pulses nearest in firing frequency when the recording's own next pulse no longer fits.</summary>
    private const int Spread = 32;
    /// <summary>
    /// Past the top of the run-up's recording a grain is chosen from this many of its highest pulses: any revs up there
    /// are nearest to the same ones, and the fewer and the likelier from one stretch of the recording, the steadier the
    /// tone. Among 32, from four blips of the throttle on the V6 hatch, their differences filled the gaps between the
    /// firing's harmonics: pulling at 90 % of its redline the harmonics held 0.39 of the sound (-4.1 dB), against 0.47
    /// (-3.3 dB) among 16. Eight and six did the same as 16; four, the last pulses of one blip, did better (-2.1 dB) but
    /// would play four grains over 3,000 rpm.
    /// </summary>
    private const int TopSpread = 16;

    private readonly GrainBank? _idle;
    private readonly GrainBank _drive;
    private readonly GrainBank? _overrun;
    private readonly int _cylinders;
    private readonly Random _random;
    /// <summary>The run-up's and the overrun's grains overlap-added (see <see cref="Add"/>), and the idle's apart from them.</summary>
    private readonly GrainRing _running = new(), _idling = new();
    /// <summary>
    /// The gains that level the run-up's and the overrun's pulses onto the run-up's trend of level, grain by grain
    /// (<see cref="GrainBank.LevelGains"/>, see the remarks).
    /// </summary>
    private readonly float[] _driveLevel;
    private readonly float[]? _overrunLevel;
    private readonly float[]? _idleLevel;
    private readonly int _latency;
    private readonly float _idleTopRpm;
    private readonly float _driveBottomRpm;
    private readonly float _outsideTrim = 1f, _cabinTrim = 1f;
    /// <summary>Brings the idle recording to <see cref="IdleLufs"/> (see the remarks).</summary>
    private readonly float _idleGain = 1f;
    /// <summary>
    /// Brings the idle's pulses, levelled onto the run-up's trend (<see cref="_idleLevel"/>), to the run-up's own loudness
    /// pulling across the revs where the two take over from each other (see the remarks); copied into the twins the trims
    /// are measured on.
    /// </summary>
    private float _loadedIdleGain = 1f;
    /// <summary>
    /// The idle's pulses' levels at full throttle, held under clicks (<see cref="LoadedIdleLevels"/>); none in the twins,
    /// which play <see cref="_idleLevel"/> times <see cref="_loadedIdleGain"/>.
    /// </summary>
    private readonly float[]? _idlePulling;
    /// <summary>Only the idle plays, at any revs: a twin <see cref="LoadedIdleGain"/> hears the idle's pulses on.</summary>
    private bool _idleOnly;
    /// <summary>Peaks are rounded off (<see cref="Round"/>); not in the twins the trims are measured on, which must see them whole.</summary>
    private readonly bool _rounded;
    private long _time;
    private double _nextPulse;
    private int _idlePulse = 1;
    private int _drivePulse = 1;
    private int _overrunPulse = 1;
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
        _rounded = trim;
        var trend = drive.LevelTrend();
        _driveLevel = drive.LevelGains(trend);
        _overrunLevel = overrun?.LevelGains(trend);
        _idleLevel = idle?.LevelGains(trend);
        // The cabin's low-pass and boom and the tailpipe's shelf are the body's and never change: set once, where the
        // throttle's filter and the intake's roar follow the load (UpdateFilters).
        _cabinHigh.SetLowPass(voicing.CabinCutoff, 0.7f, drive.SampleRate);
        _cabinBody.SetPeak(voicing.BoomHz, 0.9f, voicing.BoomDb, drive.SampleRate);
        _tailpipe.SetHighShelf(voicing.TailpipeHz, EngineVoicing.TailpipeDb, drive.SampleRate);
        if (trim)
        {
            // The revs it is trimmed at: four points over 45-90 % of the redline, or five over the run-up's recording; its
            // peaks are taken over those and every 5 % from 30 % of the redline to the redline (or over the recording).
            float[] revs, peakRevs;
            if (redlineRpm > 0f)
            {
                revs = [0.45f * redlineRpm, 0.6f * redlineRpm, 0.75f * redlineRpm, 0.9f * redlineRpm];
                peakRevs = [.. Enumerable.Range(0, 15).Select(k => (0.3f + 0.05f * k) * redlineRpm)];
            }
            else
            {
                revs = RunUpRevs(5);
                peakRevs = RunUpRevs(11);
            }
            if (idle is not null) _loadedIdleGain = LoadedIdleGain(idle);
            _outsideTrim = Trim(interior: false, revs, peakRevs);
            _cabinTrim = Trim(interior: true, revs, peakRevs);
            if (idle is not null)
            {
                _idleGain = IdleGain(idle);
                _idlePulling = LoadedIdleLevels(idle);
            }
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
            // The idle's pulses and the run-up's (or the overrun's) are other recordings and do not add in phase: mixed by
            // their shares in amplitude, their sum lost up to 3 dB halfway between the recordings (half of each, a quarter
            // of the power, twice). Each stream is normalised on its own and keeps the square root of its share: their
            // powers sum to one.
            var pulled = _running.Take(slot, out var pulledShare);
            var idled = _idling.Take(slot, out var idledShare);
            var shares = pulledShare + idledShare;
            var value = shares > 0f ? MathF.Sqrt(pulledShare / shares) * pulled + MathF.Sqrt(idledShare / shares) * idled : 0f;

            if ((i & 31) == 0) UpdateFilters(rate);
            value = _throttle.Process(value);
            _interior += ((Interior ? 1f : 0f) - _interior) * cabinFollow;
            // Both paths run all the time, so a change of viewpoint crossfades without a filter starting cold.
            var cabin = _cabinIntake.Process(_cabinBody.Process(_cabinHigh.Process(value))) * _cabinTrim;
            var outside = _outsideIntake.Process(_tailpipe.Process(value)) * _outsideTrim;
            value = outside + (cabin - outside) * _interior;
            // The level: CutLevel of full at a closed throttle, rising with the load, and a little with the revs.
            var target = running ? Volume * LoadLevel(_smoothLoad) * (0.72f + 0.28f * Math.Clamp((nowRpm - 1000f) / 6000f, 0f, 1f)) : 0f;
            _level += (target - _level) * levelFollow;
            output[i] = _rounded ? Round(value * _level) : value * _level;
            _time++;
        }
        _rpm = rpm;
        _load = load;
    }

    private void PlacePulse(float rpm, long centre)
    {
        var firing = MathF.Max(rpm, 300f) * _cylinders / 120f;
        // Every pulse plays as its recording made it, so an engine's uneven beat is its recording's sequence of pulses
        // (cars13 audio2: the HGT's five, a TT RS's, beats in the voice as in its recording; EngineVoiceTests). A weight
        // per cylinder laid on the shares (the five's FivePattern, 0.9 to cars13 stage 2) did nothing audible:
        // GrainRing.Take divides the shares of overlapping grains back out, and the voice with it differed from the voice
        // without by 27 to 32 dB under its level.
        var drive = _idle is null ? 1f : _idleOnly ? 0f : SmoothStep((rpm - _idleTopRpm) / (_driveBottomRpm - _idleTopRpm));
        // Off the throttle the overrun recording takes the run-up's place: no combustion pulses, the exhaust's rasp.
        var overrun = _overrun is null ? 0f : drive * SmoothStep((0.40f - _smoothLoad) / 0.30f);
        // A cut pulse does not fire: the overrun's pulse in the run-up's place (the run-up's own without an overrun
        // recording), at the closed throttle's level against the level the held load sets. The overrun's grains are
        // levelled onto the run-up's (_overrunLevel), so what the cut takes away is that level and the overrun's own sound.
        var gain = 1f;
        if (Cut)
        {
            if (_overrun is not null) overrun = drive;
            gain = CutLevel / LoadLevel(_smoothLoad);
        }
        var idleShare = 1f - drive;
        drive -= overrun;
        if (drive > 0.002f) Add(_running, _drive, _driveLevel, Pick(_drive, ref _drivePulse, firing), centre, drive, gain, firing);
        if (overrun > 0.002f) Add(_running, _overrun!, _overrunLevel, Pick(_overrun!, ref _overrunPulse, firing), centre, overrun, gain);
        if (idleShare > 0.002f && _idle is not null)
        {
            var pulse = Pick(_idle, ref _idlePulse, firing);
            Add(_idling, _idle, null, pulse, centre, idleShare, gain * IdleLevel(_idlePulling?[pulse] ?? _idleLevel![pulse] * _loadedIdleGain), firing);
        }
    }

    /// <summary>
    /// The recording's next pulse while it still fits the revs (its natural sequence), otherwise one of the nearest (past
    /// the top of the run-up's recording, one of its <see cref="TopSpread"/> highest).
    /// </summary>
    private int Pick(GrainBank bank, ref int pulse, float firing)
    {
        var next = pulse + 1;
        if (next < bank.Marks.Length - 1 && bank.Usable[next] && MathF.Abs(bank.Firing[next] / firing - 1f) < 0.06f) return pulse = next;
        return pulse = bank.Nearest(firing, bank == _drive && firing > bank.MaxFiring ? TopSpread : Spread, _random);
    }

    /// <summary>
    /// Overlap-adds a grain: <paramref name="weight"/> is its share of the mix (the sum is normalised by the weights),
    /// <paramref name="gain"/> its level, which the normalisation leaves alone, times its pulse's gain in
    /// <paramref name="levels"/> (<see cref="_driveLevel"/>, <see cref="_overrunLevel"/>; none for the idle).
    /// </summary>
    /// <param name="runUpFiring">
    /// For a grain of the run-up or of the idle, the firing frequency it is laid at (0 for the overrun's): past the top of
    /// its recording its window is held to that period. Laid closer together than they were recorded, whole
    /// grains overlapped by more than their windows sum to one, and the normalisation averaged several pulses at every
    /// sample, which took each other down (by 4 to 5 dB at the V6 hatch's redline: its recording reaches 3,600 rpm, its
    /// redline 6,600) where a real engine fires as hard at the higher revs, only more often. Held to the period, the
    /// windows sum to one again: each pulse plays at its recorded strength, its own peak whole and its tail, which the
    /// next pulse covers, cut short. (Dividing the overlap out instead kept the whole grains, several at once, and from
    /// pulses picked at random their sum was a noise between the harmonics as loud as the firing tone itself.) The idle's
    /// grains are held the same way past the top of its recording (banks: see the remarks). The overrun keeps its whole
    /// windows: it is to stay the quieter past its top.
    /// </param>
    private void Add(GrainRing stream, GrainBank bank, float[]? levels, int pulse, long centre, float weight, float gain, float runUpFiring = 0f)
    {
        var marks = bank.Marks;
        var samples = bank.Samples;
        var mark = marks[pulse];
        var left = mark - marks[pulse - 1];
        var right = marks[pulse + 1] - mark;
        if (levels is not null) gain *= levels[pulse];
        if (runUpFiring > bank.MaxFiring)
        {
            var period = (int)MathF.Ceiling(SampleRate / runUpFiring);
            left = Math.Min(left, period);
            right = Math.Min(right, period);
        }
        // Rising half of a Hann window over the period before the pulse, falling half over the one after.
        for (var j = -left; j < right; j++)
        {
            var w = j < 0 ? 0.5f - 0.5f * MathF.Cos(MathF.PI * (j + left) / left) : 0.5f + 0.5f * MathF.Cos(MathF.PI * j / right);
            var slot = (int)((centre + j) & Mask);
            stream.Sum[slot] += weight * w * gain * samples[mark + j];
            stream.Weight[slot] += weight * w;
            stream.Cover[slot] += w;
        }
    }

    private void UpdateFilters(float rate)
    {
        // A closed throttle muffles the exhaust; open, it is bright. The cutoff follows the smoothed load. With an
        // overrun recording the closed-throttle sound is its own and keeps its rasp, so the filter shuts less. The
        // limiter's cut leaves the throttle open: the load the voice is given holds through it.
        var shut = _overrun is null ? 1100f : 2600f;
        _throttle.SetLowPass(shut + (12100f - shut) * MathF.Pow(_smoothLoad, 1.5f), 0.6f, rate);
        // The intake's roar grows with the air the engine draws: with the throttle.
        _cabinIntake.SetPeak(EngineVoicing.IntakeHz, 0.8f, Voicing.CabinIntakeDb * _smoothLoad, rate);
        _outsideIntake.SetPeak(EngineVoicing.IntakeHz, 0.8f, Voicing.OutsideIntakeDb * _smoothLoad, rate);
    }

    /// <summary><paramref name="count"/> revs spread evenly over the run-up's recording, half a spacing in from its ends.</summary>
    private float[] RunUpRevs(int count)
    {
        var low = MathF.Max(_drive.MinFiring * 120f / _cylinders, _driveBottomRpm);
        var high = MathF.Max(_drive.MaxFiring * 120f / _cylinders, low);
        return [.. Enumerable.Range(0, count).Select(k => low + (high - low) * (0.5f + k) / count)];
    }

    /// <summary>
    /// The gain that brings this voice to <see cref="ReferenceLufs"/> at full throttle at <paramref name="revs"/>, heard
    /// outside or in the cabin: a twin of it is rendered for a third of a second at each, and its loudness taken over all
    /// of them (BS.1770, ungated). No more than keeps its peaks, there and at <paramref name="peakRevs"/> (a quarter of a
    /// second each), under <see cref="PeakCeiling"/> but for the rarest samples; held between a quarter and four times.
    /// </summary>
    private float Trim(bool interior, float[] revs, float[] peakRevs)
    {
        var twin = new GranularEngine(_idle, _drive, _cylinders, 1, _overrun, Voicing, trim: false, 0f) { Interior = interior, _loadedIdleGain = _loadedIdleGain };
        var rate = SampleRate;
        var settle = rate / 10;
        var each = rate / 3;
        var block = new float[settle + each];
        var heard = new float[revs.Length * each];
        var peaks = new PeakMeter();
        void Hold(float rpm, int length)
        {
            for (var at = 0; at < length; at += 512) twin.Render(block.AsSpan(at, Math.Min(512, length - at)), rpm, 1f, true);
            peaks.Add(block.AsSpan(settle, length - settle));
        }
        for (var k = 0; k < revs.Length; k++)
        {
            Hold(revs[k], block.Length);
            block.AsSpan(settle).CopyTo(heard.AsSpan(k * each));
        }
        foreach (var rpm in peakRevs) Hold(rpm, settle + rate / 4);
        var lufs = Loudness.Lufs(heard, rate);
        var gain = MathF.Min((float)Math.Pow(10.0, (ReferenceLufs - lufs) / 20.0), PeakCeiling / peaks.Peak);
        return Math.Clamp(gain, 0.25f, 4f);
    }

    /// <summary>
    /// The gain that brings the idle's levelled pulses, pulling at full throttle, to the loudness of the run-up's pulses
    /// at the same revs, a quarter, half and three quarters of the way across the revs where the two take over from each
    /// other: a twin playing only the idle against a twin without it, the power mean of the three, the mean of the
    /// decibels outside and in the cabin. Levelled onto the run-up's trend by their RMS level, the pulses of an idle
    /// recorded with its energy under 100 Hz (the saloon's Niva, the estate's Renault) are still quieter in loudness, where
    /// the K-weighting takes their lows off (the level within a recording is left to RMS: levelled by its K-weighted
    /// loudness instead, the run-ups' joins stepped by up to 2 LU). Held between a quarter and four times.
    /// </summary>
    /// <remarks>
    /// Matched at the top of the idle's revs alone, against the run-up's pulses laid there (the V8's 2.5 times further
    /// apart than recorded), the pulling idle came out 1 to 4.5 dB under its own level on five of the eight cars. Matched
    /// against the run-up at its own bottom revs instead (tried in the banks review), the idle was the louder across the
    /// crossfade, where its pulses, held to the period, keep only their loudest part and grow louder with the revs, and
    /// the run-up's grow quieter laid under their recording: at full throttle the voice sagged where the run-up took over
    /// (the hot hatch 1.8 LU at 2,000 rpm, the V6 hatch 1.6 at 1,300, the fleet saloon's petrol banks in its cabin 2.2 at 1,700).
    /// Matched stream against stream at the same revs, they are as loud as each other across the crossfade.
    /// </remarks>
    private float LoadedIdleGain(GrainBank idle)
    {
        double Heard(GrainBank? idleBank, bool interior, float[] revs)
        {
            var twin = new GranularEngine(idleBank, _drive, _cylinders, 1, _overrun, Voicing, trim: false, 0f) { Interior = interior, _idleOnly = idleBank is not null };
            // As the trims are taken: a tenth of a second to settle at each revs, a third heard.
            var settle = SampleRate / 10;
            var heard = new float[settle + SampleRate / 3];
            var energy = 0.0;
            foreach (var rpm in revs)
            {
                for (var at = 0; at < heard.Length; at += 512) twin.Render(heard.AsSpan(at, Math.Min(512, heard.Length - at)), rpm, 1f, true);
                energy += Math.Pow(10.0, Loudness.Lufs(heard.AsSpan(settle), SampleRate) / 10.0);
            }
            return 10.0 * Math.Log10(energy / revs.Length);
        }
        float[] revs = [.. new[] { 0.25f, 0.5f, 0.75f }.Select(k => _idleTopRpm + (_driveBottomRpm - _idleTopRpm) * k)];
        var outside = Heard(null, false, revs) - Heard(idle, false, revs);
        var cabin = Heard(null, true, revs) - Heard(idle, true, revs);
        return Math.Clamp((float)Math.Pow(10.0, 0.5 * (outside + cabin) / 20.0), 0.25f, 4f);
    }

    /// <summary>
    /// The gain that brings the idle to <see cref="IdleLufs"/> outside: a twin rendered at the idle recording's middle
    /// revs under the idle governor's tenth of throttle, its loudness taken with the outside trim. Held between a
    /// quarter and four times, low enough that the recording's sharpest edge stays under a step of 0.45 between samples at
    /// the idle's level (the five-cylinder hatch's idle of 0.9-0.19, recorded under the bonnet, had a valve's tick of
    /// nearly full scale that clicked), and low enough that the rendered idle peaks under <see cref="PeakCeiling"/> (but for its rarest samples).
    /// </summary>
    private float IdleGain(GrainBank idle)
    {
        var twin = new GranularEngine(_idle, _drive, _cylinders, 1, _overrun, Voicing, trim: false, 0f);
        var rpm = 0.5f * (idle.MinFiring + idle.MaxFiring) * 120f / _cylinders;
        var heard = new float[SampleRate];
        for (var at = 0; at < heard.Length; at += 512) twin.Render(heard.AsSpan(at, Math.Min(512, heard.Length - at)), rpm, 0.1f, true);
        var lufs = Loudness.Lufs(heard.AsSpan(SampleRate / 2), SampleRate) + 20.0 * Math.Log10(_outsideTrim);
        var level = LoadLevel(0.1f);
        var trim = MathF.Max(_outsideTrim, _cabinTrim);
        var clickFree = 0.45f / (Sharpest(idle) * level * trim);
        var unclipped = PeakCeiling / (PeakMeter.Of(heard.AsSpan(SampleRate / 2)) * trim);
        var most = MathF.Max(MathF.Min(4f, MathF.Min(clickFree, unclipped)), 0.25f);
        return Math.Clamp((float)Math.Pow(10.0, (IdleLufs - lufs) / 20.0), 0.25f, most);
    }

    /// <summary>
    /// The idle's pulses' levels pulling at full throttle (<see cref="_idleLevel"/> times <see cref="_loadedIdleGain"/>),
    /// each held, as <see cref="IdleGain"/> holds the idle's own level, low enough that its grain's sharpest edge stays
    /// under a step of 0.45 between samples. Brought to the run-up's loudness, the saloon's idle (a Niva's, recorded quietly
    /// and with its energy under 100 Hz, which the loudness hardly counts) was raised by up to 18 dB at full throttle, the
    /// levelling lifting its quietest stretches most, and held there at its idle revs it stepped by 1.0 between samples
    /// at the game's volume (banks review). Pulse by pulse, so a few sharp grains do not hold the whole idle down.
    /// </summary>
    private float[] LoadedIdleLevels(GrainBank idle)
    {
        var samples = idle.Samples;
        var marks = idle.Marks;
        var trim = MathF.Max(_outsideTrim, _cabinTrim);
        var levels = new float[marks.Length];
        for (var i = 1; i < marks.Length - 1; i++)
        {
            levels[i] = _idleLevel![i] * _loadedIdleGain;
            if (!idle.Usable[i]) continue;
            var steepest = 1e-3f;
            for (var j = marks[i - 1] + 1; j < marks[i + 1]; j++) steepest = MathF.Max(steepest, MathF.Abs(samples[j] - samples[j - 1]));
            levels[i] = MathF.Min(levels[i], 0.45f / (steepest * trim));
        }
        return levels;
    }

    /// <summary>The largest step between neighbouring samples of a bank's recording (at least 0.001).</summary>
    private static float Sharpest(GrainBank bank)
    {
        var sharpest = 1e-3f;
        for (var j = 1; j < bank.Samples.Length; j++) sharpest = MathF.Max(sharpest, MathF.Abs(bank.Samples[j] - bank.Samples[j - 1]));
        return sharpest;
    }

    /// <summary>
    /// A sample past <see cref="PeakKnee"/> bent towards <see cref="PeakCeiling"/> (a tanh above the knee, its slope 1 at
    /// the knee so nothing under it changes): the rare peak the trim's renders did not meet.
    /// </summary>
    private static float Round(float x)
    {
        var magnitude = MathF.Abs(x);
        if (magnitude <= PeakKnee) return x;
        const float room = PeakCeiling - PeakKnee;
        return MathF.CopySign(PeakKnee + room * MathF.Tanh((magnitude - PeakKnee) / room), x);
    }

    /// <summary>
    /// The level of an idle's pulse whose level pulling is <paramref name="pulling"/> (levelled onto the run-up's trend and
    /// brought to its loudness, see the remarks): its own (<see cref="_idleGain"/>) up to a fifth of the throttle (the
    /// idle governor's tenth to sixth, and a light throttle), <paramref name="pulling"/> at full throttle, and between them
    /// so that with the voice's level law (<see cref="LoadLevel"/>) the sound rises evenly in decibels with the load.
    /// </summary>
    /// <remarks>
    /// Slid from its own level to the pulling one between 0.2 and 0.6 of the throttle, by the gains alone, the idle worked
    /// against the level law wherever the pulling level lay under its own: at the idle's revs, where the run-up's trend
    /// runs on below its recording, it did on most cars, and the two all but cancelled (the V8 at 900 rpm 1.7 LU louder from
    /// a tenth of the throttle to full and 1.9 LU quieter from 0.3 to 0.6; banks review). Interpolated in decibels from
    /// the light throttle's level to the full throttle's, the sound rises with the load whenever full throttle is the
    /// louder of the two: at the idle's revs from the governor's throttle to full by 3.0 LU (the estate) to 12.2 (the
    /// saloon), the V8 and the rally hatch 3.6 to 3.8. In 0.22 they rose by 6.4 to 7.7 LU, the idle playing its own level
    /// at full throttle, and the voice then sagged by 3 to 8 LU where the run-up took over; held up to its own level
    /// (tried in the banks review) the idle at full throttle lay over the run-up's first revs on the estate, the rally hatch
    /// and the V6 hatch, and the voice fell 1.7 to 3.0 LU into the run-up and rose again. Full throttle at the idle's revs is
    /// as loud as the run-up across the crossfade lets it be.
    /// </remarks>
    private float IdleLevel(float pulling)
    {
        const float light = 0.2f;
        if (_smoothLoad <= light) return _idleGain;
        var from = LoadLevel(light) * _idleGain;
        var to = LoadLevel(1f) * pulling;
        var share = MathF.Min((_smoothLoad - light) / (1f - light), 1f);
        return from * MathF.Pow(to / from, share) / LoadLevel(_smoothLoad);
    }

    /// <summary>The voice's level law against the load: <see cref="CutLevel"/> at a closed throttle, 1 at a full one.</summary>
    private static float LoadLevel(float load) => CutLevel + (1f - CutLevel) * load;

    private static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>A ring of overlap-added grains: their samples by window and share, the shares by window, and the windows.</summary>
    private sealed class GrainRing
    {
        public readonly float[] Sum = new float[RingSize];
        public readonly float[] Weight = new float[RingSize];
        public readonly float[] Cover = new float[RingSize];

        /// <summary>
        /// The grains at <paramref name="slot"/> normalised by their windows, and their share of the mix there (the shares
        /// of the pulses covering it, by window), clearing the slot. Where the pulses are spread apart (revs below a
        /// recording) the gaps between them stay quiet instead of being blown up: the windows count for at least 0.35.
        /// </summary>
        public float Take(int slot, out float share)
        {
            var cover = Cover[slot];
            share = cover > 1e-6f ? Weight[slot] / cover : 0f;
            var value = share > 0f ? Sum[slot] / (share * MathF.Max(cover, 0.35f)) : 0f;
            Sum[slot] = Weight[slot] = Cover[slot] = 0f;
            return value;
        }
    }
}
