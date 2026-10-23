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
/// Two banks: one recorded at idle and one recorded running up through a gear. Between the top of the idle
/// recording and the bottom of the run-up both play, crossfaded. Load shapes the sound like an open or closed
/// throttle (a low-pass that opens and a level that rises); <see cref="Interior"/> hears it through the cabin's
/// bulkhead. The voice renders at its banks' rate, allocates nothing per call and is used from one thread.
/// </remarks>
public sealed class GranularEngine
{
    private const int RingSize = 16384;
    private const int Mask = RingSize - 1;
    /// <summary>A grain is chosen from this many of the pulses nearest in firing frequency when the recording's own next pulse no longer fits.</summary>
    private const int Spread = 32;

    private readonly GrainBank? _idle;
    private readonly GrainBank _drive;
    private readonly int _cylinders;
    private readonly Random _random;
    private readonly float[] _sum = new float[RingSize];
    private readonly float[] _weight = new float[RingSize];
    private readonly int _latency;
    private readonly float _idleTopRpm;
    private readonly float _driveBottomRpm;
    private long _time;
    private double _nextPulse;
    private int _idlePulse = 1;
    private int _drivePulse = 1;
    private bool _started;
    private float _rpm;
    private float _load;
    private float _smoothLoad;
    private float _level;
    private float _interior;
    private Biquad _throttle;
    private Biquad _cabinHigh;
    private Biquad _cabinBody;

    public GranularEngine(GrainBank? idle, GrainBank drive, int cylinders, int seed = 7)
    {
        if (idle is not null && idle.SampleRate != drive.SampleRate) throw new ArgumentException("the banks are at different rates");
        _idle = idle;
        _drive = drive;
        _cylinders = cylinders;
        _random = new Random(seed);
        // A grain reaches back over the period before its pulse: pulses are laid that far ahead of the output, which
        // is also how late the voice follows the revs (about 18 ms at idle).
        _latency = Math.Max(idle?.LongestLead ?? 0, drive.LongestLead) + 1;
        if (_latency + Math.Max(idle?.LongestGrain ?? 0, drive.LongestGrain) > RingSize) throw new ArgumentException("a grain is too long for the ring");
        _idleTopRpm = idle is null ? 0f : idle.MaxFiring * 120f / cylinders * 1.12f;
        _driveBottomRpm = MathF.Max(drive.MinFiring * 120f / cylinders, _idleTopRpm + 200f);
        _nextPulse = _latency;
    }

    public int SampleRate => _drive.SampleRate;

    /// <summary>Heard from the driver's seat: through the bulkhead the engine loses its top end and booms a little in the cabin.</summary>
    public bool Interior { get; set; }

    /// <summary>Overall level of the voice.</summary>
    public float Volume { get; set; } = 1f;

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

            if ((i & 31) == 0) UpdateFilters(nowRpm, rate);
            value = _throttle.Process(value);
            _interior += ((Interior ? 1f : 0f) - _interior) * cabinFollow;
            if (_interior > 0.001f)
            {
                var cabin = _cabinBody.Process(_cabinHigh.Process(value));
                value += (cabin - value) * _interior;
            }
            var target = running ? Volume * (0.42f + 0.58f * _smoothLoad) * (0.72f + 0.28f * Math.Clamp((nowRpm - 1000f) / 6000f, 0f, 1f)) : 0f;
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
        var drive = _idle is null ? 1f : SmoothStep((rpm - _idleTopRpm) / (_driveBottomRpm - _idleTopRpm));
        if (drive > 0.002f) Add(_drive, Pick(_drive, ref _drivePulse, firing), centre, drive);
        if (drive < 0.998f && _idle is not null) Add(_idle, Pick(_idle, ref _idlePulse, firing), centre, 1f - drive);
    }

    /// <summary>The recording's next pulse while it still fits the revs (its natural sequence), otherwise one of the nearest.</summary>
    private int Pick(GrainBank bank, ref int pulse, float firing)
    {
        var next = pulse + 1;
        if (next < bank.Marks.Length - 1 && bank.Usable[next] && MathF.Abs(bank.Firing[next] / firing - 1f) < 0.06f) return pulse = next;
        return pulse = bank.Nearest(firing, Spread, _random);
    }

    private void Add(GrainBank bank, int pulse, long centre, float weight)
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
            _sum[slot] += weight * w * samples[mark + j];
            _weight[slot] += weight * w;
        }
    }

    private void UpdateFilters(float rpm, float rate)
    {
        // A closed throttle muffles the exhaust; open, it is bright. The cutoff follows the smoothed load.
        _throttle.SetLowPass(1100f + 11000f * MathF.Pow(_smoothLoad, 1.5f), 0.6f, rate);
        _cabinHigh.SetLowPass(2600f, 0.7f, rate);
        _cabinBody.SetPeak(130f, 0.9f, 5f, rate);
    }

    private static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}

/// <summary>A biquad (RBJ cookbook), transposed direct form II.</summary>
internal struct Biquad
{
    private float _b0, _b1, _b2, _a1, _a2, _z1, _z2;

    public void SetLowPass(float cutoff, float q, float rate)
    {
        var w = 2f * MathF.PI * MathF.Min(cutoff, rate * 0.45f) / rate;
        var alpha = MathF.Sin(w) / (2f * q);
        var cos = MathF.Cos(w);
        var a0 = 1f + alpha;
        Set((1f - cos) * 0.5f / a0, (1f - cos) / a0, (1f - cos) * 0.5f / a0, -2f * cos / a0, (1f - alpha) / a0);
    }

    public void SetPeak(float centre, float q, float gainDb, float rate)
    {
        var a = MathF.Pow(10f, gainDb / 40f);
        var w = 2f * MathF.PI * centre / rate;
        var alpha = MathF.Sin(w) / (2f * q);
        var cos = MathF.Cos(w);
        var a0 = 1f + alpha / a;
        Set((1f + alpha * a) / a0, -2f * cos / a0, (1f - alpha * a) / a0, -2f * cos / a0, (1f - alpha / a) / a0);
    }

    private void Set(float b0, float b1, float b2, float a1, float a2)
    {
        _b0 = b0;
        _b1 = b1;
        _b2 = b2;
        _a1 = a1;
        _a2 = a2;
    }

    public float Process(float x)
    {
        var y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }
}
