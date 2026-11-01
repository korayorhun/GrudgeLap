using System.Text.Json;

namespace GrudgeLap.Audio;

/// <summary>
/// Firing pulses cut from an engine recording (tools/make-engine-bank.py): the recording as samples, and for every
/// pulse its position and the firing frequency there. A grain is the stretch from one pulse's predecessor to its
/// successor, so the grains of neighbouring pulses overlap by exactly a period, cut at the same point of a pulse.
/// </summary>
/// <remarks>
/// A bank joins segments recorded apart (another gear, another take, another microphone position), and each segment
/// has its own spectral balance: played across the revs the voice turned brighter or duller at every join (0.19 audit,
/// finding 9: the turbo hatch was brighter at 3350 rpm than at 4700). With <c>evenTimbre</c> the bank is evened out
/// when it is made: every grain's brightness is measured, a trend of brightness against the firing frequency is fitted
/// through them (its slope from within the segments, <see cref="BrightnessTrend"/>), and each segment is tilted (a
/// gentle shelf about 1 kHz, level kept) until its grains sit on the trend on average. The rise of brightness with the
/// revs, which is the engine's own, is kept; the steps at the joins go. The segments' levels are then evened the same
/// way onto a trend of level (<see cref="LevelTrend"/>): the fleet saloon's run-up joins a Punto's to a Bravo's recorded
/// 4 dB quieter, which left it buried by the wind at the top of its revs. No segment's peaks are raised past the raw
/// recording's peak, though, but for its rarest samples (<see cref="PeakMeter"/>: a click, which the voice rounds off):
/// tilted 6 dB, the fleet saloon's Bravo segment is peakier than it was, and with its whole gain its typical peaks
/// would have gone over the recording's (0.20 review: the bank peaked at 1.49, the voice at +2.8 dBFS). A bank of one
/// segment is left as it is.
/// </remarks>
public sealed class GrainBank
{
    /// <summary>The pivot of the timbre correction's tilt, Hz.</summary>
    private const float TiltPivot = 1000f;
    /// <summary>The largest tilt and gain a segment is given either way, dB: more than that is a recording to replace, not to correct.</summary>
    private const float MaxTilt = 6f, MaxGain = 9f;

    private readonly int[] _byFiring;
    /// <summary>The samples above the tilt's pivot, for <see cref="Brightness"/> (made when first needed).</summary>
    private float[]? _high;

    public GrainBank(float[] samples, int sampleRate, int[] marks, float[] firing, int[] segment, bool evenTimbre = false)
    {
        if (marks.Length != firing.Length || marks.Length != segment.Length) throw new ArgumentException("marks, firing and segment differ in length");
        Samples = samples;
        SampleRate = sampleRate;
        Marks = marks;
        Firing = firing;
        Segment = segment;
        // A pulse can be a grain when both neighbours belong to its own segment (the bank joins several recordings).
        Usable = new bool[marks.Length];
        for (var i = 1; i < marks.Length - 1; i++)
        {
            Usable[i] = segment[i - 1] == segment[i] && segment[i + 1] == segment[i] && marks[i - 1] >= 0 && marks[i + 1] < samples.Length;
        }
        _byFiring = Enumerable.Range(0, marks.Length).Where(i => Usable[i]).OrderBy(i => firing[i]).ToArray();
        if (_byFiring.Length == 0) throw new ArgumentException("the bank has no usable grain");
        MinFiring = firing[_byFiring[0]];
        MaxFiring = firing[_byFiring[^1]];
        if (evenTimbre) Evened = EvenTimbre();
    }

    public float[] Samples { get; }
    public int SampleRate { get; }
    public int[] Marks { get; }
    public float[] Firing { get; }
    public bool[] Usable { get; }
    /// <summary>The segment (the recording) every pulse was cut from.</summary>
    public int[] Segment { get; }
    /// <summary>
    /// The tilt and the gain each segment was given to even out the timbre and the level, dB (none when it was not evened);
    /// the gain is held so that the segment's peaks, but for its rarest samples, stay under the raw recording's peak.
    /// </summary>
    public IReadOnlyDictionary<int, (float Tilt, float Gain)> Evened { get; } = new Dictionary<int, (float, float)>();
    public float MinFiring { get; }
    public float MaxFiring { get; }
    public int Count => _byFiring.Length;

    /// <summary>The longest grain in samples (the two periods around the slowest pulse).</summary>
    public int LongestGrain
    {
        get
        {
            var longest = 0;
            for (var i = 1; i < Marks.Length - 1; i++)
            {
                if (Usable[i]) longest = Math.Max(longest, Marks[i + 1] - Marks[i - 1]);
            }
            return longest;
        }
    }

    /// <summary>The farthest a grain reaches before its pulse, in samples (the period before the slowest pulse).</summary>
    public int LongestLead
    {
        get
        {
            var longest = 0;
            for (var i = 1; i < Marks.Length - 1; i++)
            {
                if (Usable[i]) longest = Math.Max(longest, Marks[i] - Marks[i - 1]);
            }
            return longest;
        }
    }

    /// <summary>
    /// The brightness of a pulse's grain, dB: its energy above <see cref="TiltPivot"/> against its energy below, the
    /// balance the correction's tilt turns about. (The energy of the first difference against the signal's, a mean-square
    /// frequency, was tried first: it is ruled by the recordings' hiss, and evening it made the turbo hatch duller still
    /// at 4700 rpm.)
    /// </summary>
    public float Brightness(int pulse)
    {
        var high = _high ??= HighPassed(Samples, 0, Samples.Length);
        double above = 0, below = 0;
        for (var j = Marks[pulse - 1]; j < Marks[pulse + 1]; j++)
        {
            var h = high[j];
            var l = Samples[j] - h;
            above += h * h;
            below += l * l;
        }
        return (float)(10.0 * Math.Log10((above + 1e-20) / (below + 1e-20)));
    }

    /// <summary>
    /// The samples [from..to) of <paramref name="x"/> through a second-order high-pass at <see cref="TiltPivot"/> (the
    /// rest of the array zero), settled on the ten milliseconds before.
    /// </summary>
    private float[] HighPassed(float[] x, int from, int to, float[]? into = null)
    {
        into ??= new float[x.Length];
        var filter = new Biquad();
        filter.SetHighPass(TiltPivot, 0.7071f, SampleRate);
        for (var j = Math.Max(from - SampleRate / 100, 0); j < from; j++) filter.Process(x[j]);
        for (var j = from; j < to; j++) into[j] = filter.Process(x[j]);
        return into;
    }

    /// <summary>The level of a pulse's grain, dB RMS over its two periods.</summary>
    public float Level(int pulse)
    {
        double energy = 0;
        for (var j = Marks[pulse - 1]; j < Marks[pulse + 1]; j++) energy += Samples[j] * Samples[j];
        return (float)(10.0 * Math.Log10(energy / (Marks[pulse + 1] - Marks[pulse - 1]) + 1e-20));
    }

    /// <summary>
    /// The trend of a grain measure against the firing frequency, measure = A + B log2(firing): the slope B is how the
    /// measure rises with the revs within each segment (least squares with an intercept of its own for every segment, so
    /// a step between segments cannot pass for a slope), held between 0 and <paramref name="maxSlope"/> per octave; A is
    /// the segments' intercepts averaged over their grains, so the bank keeps its overall value.
    /// </summary>
    public (float A, float B) Trend(Func<int, float> measure, float maxSlope)
    {
        var stats = new Dictionary<int, (double N, double X, double Y, double Xx, double Xy)>();
        foreach (var i in _byFiring)
        {
            var x = Math.Log2(Firing[i]);
            var y = (double)measure(i);
            stats.TryGetValue(Segment[i], out var s);
            stats[Segment[i]] = (s.N + 1, s.X + x, s.Y + y, s.Xx + x * x, s.Xy + x * y);
        }
        double covariance = 0, variance = 0, count = 0;
        foreach (var s in stats.Values)
        {
            covariance += s.Xy - s.X * s.Y / s.N;
            variance += s.Xx - s.X * s.X / s.N;
            count += s.N;
        }
        var b = variance > 1e-9 ? Math.Clamp(covariance / variance, 0.0, maxSlope) : 0.0;
        // Each segment's intercept is its mean less the slope at its mean; weighted by its grains they average to this.
        var a = stats.Values.Sum(s => s.Y - b * s.X) / count;
        return ((float)a, (float)b);
    }

    /// <summary>The trend of brightness (<see cref="Trend"/>, <see cref="Brightness"/>).</summary>
    /// <remarks>
    /// A spectrum that keeps its shape in engine orders brightens by 6 dB an octave of revs, one shaped by fixed
    /// resonances (the pipes, the body) not at all, and a real engine lies between: within a segment the brightness also
    /// follows the load, the gear and the microphone's distance, and a slope outside that range is those, not the revs.
    /// </remarks>
    public (float A, float B) BrightnessTrend() => Trend(Brightness, 6f);

    /// <summary>The trend of level (<see cref="Trend"/>, <see cref="Level"/>).</summary>
    /// <remarks>
    /// The slope is held to 3 dB an octave of revs. A petrol engine's noise at full load grows faster than that, by up
    /// to 50 log10 of its speed (15 dB an octave; Anderton, Relation between combustion system and noise, SAE 790270,
    /// 1979), but within a run-up recorded in one gear the slope is also the car drawing nearer and the throttle
    /// opening, and the voice adds its own rise with the revs: taken at 6 dB an octave, the flat-twelve's low run-up was
    /// pushed down until pulling at 3000 rpm it was barely louder than its idle.
    /// </remarks>
    public (float A, float B) LevelTrend() => Trend(Level, 3f);

    /// <summary>How far a segment's grains sit above a trend of a measure on average, dB.</summary>
    public float SegmentOffset(int segment, Func<int, float> measure, (float A, float B) trend)
    {
        double sum = 0;
        var count = 0;
        foreach (var i in _byFiring)
        {
            if (Segment[i] != segment) continue;
            sum += measure(i) - (trend.A + trend.B * MathF.Log2(Firing[i]));
            count++;
        }
        return count == 0 ? 0f : (float)(sum / count);
    }

    /// <summary>
    /// Tilts each segment onto the bank's trend of brightness, then sets its level onto the trend of level (see the
    /// remarks); returns the tilt and the gain given to each segment.
    /// </summary>
    private Dictionary<int, (float Tilt, float Gain)> EvenTimbre()
    {
        var done = new Dictionary<int, (float, float)>();
        var segments = _byFiring.Select(i => Segment[i]).Distinct().ToArray();
        if (segments.Length < 2) return done;
        var brightness = BrightnessTrend();
        var original = (float[])Samples.Clone();
        var rawPeak = Peak(original, 0, original.Length);
        var stretches = segments.ToDictionary(s => s, s =>
        {
            // The stretch of samples the segment's grains cover: from its first pulse's predecessor to its last one's successor.
            var members = _byFiring.Where(i => Segment[i] == s).ToArray();
            return (From: Marks[members.Min() - 1], To: Marks[members.Max() + 1]);
        });
        foreach (var s in segments)
        {
            var tilt = 0f;
            if (MathF.Abs(SegmentOffset(s, Brightness, brightness)) >= 0.1f)
            {
                // Bisection on the tilt: the segment's brightness grows with it, so the limits bracket the root.
                var (from, to) = stretches[s];
                float lo = -MaxTilt, hi = MaxTilt;
                for (var step = 0; step < 10; step++)
                {
                    tilt = 0.5f * (lo + hi);
                    Tilt(original, from, to, tilt);
                    if (SegmentOffset(s, Brightness, brightness) > 0f) hi = tilt;
                    else lo = tilt;
                }
            }
            done[s] = (tilt, 0f);
        }
        // The level is evened on the tilted segments: a gain each, straight from its offset, but none that lifts the
        // segment's peaks over the raw recording's (the tilt may already have, by up to half of it, which this takes back).
        var level = LevelTrend();
        foreach (var s in segments)
        {
            var (from, to) = stretches[s];
            var headroom = 20f * MathF.Log10(rawPeak / PeakMeter.Of(Samples.AsSpan(from, to - from)));
            var gain = Math.Clamp(MathF.Min(-SegmentOffset(s, Level, level), headroom), -MaxGain, MaxGain);
            var scale = MathF.Pow(10f, gain / 20f);
            for (var j = from; j < to; j++) Samples[j] *= scale;
            if (_high is not null) for (var j = from; j < to; j++) _high[j] *= scale;
            done[s] = (done[s].Item1, gain);
        }
        return done;
    }

    /// <summary>The largest magnitude among <paramref name="x"/>[from..to).</summary>
    private static float Peak(float[] x, int from, int to)
    {
        var peak = 0f;
        for (var j = from; j < to; j++) peak = MathF.Max(peak, MathF.Abs(x[j]));
        return peak;
    }

    /// <summary>
    /// Writes <paramref name="source"/>[from..to) into the bank tilted by <paramref name="tilt"/> dB: a high shelf of that
    /// gain about <see cref="TiltPivot"/>, the whole lowered by half of it, so the lows and the highs move by half the
    /// tilt each, in opposite directions, and the level stays about where it was.
    /// </summary>
    private void Tilt(float[] source, int from, int to, float tilt)
    {
        var shelf = new Biquad();
        shelf.SetHighShelf(TiltPivot, tilt, SampleRate);
        var gain = MathF.Pow(10f, -tilt / 40f);
        // Ten milliseconds of the recording before the stretch settle the filter, so its first grain starts clean.
        for (var j = Math.Max(from - SampleRate / 100, 0); j < from; j++) shelf.Process(source[j]);
        for (var j = from; j < to; j++) Samples[j] = shelf.Process(source[j]) * gain;
        if (_high is not null) HighPassed(Samples, from, to, _high);
    }

    /// <summary>
    /// The bank's loudness, LUFS (BS.1770, ungated): the K-weighted mean square over the usable grains, each pulse's
    /// period counted once, so a voice made from the bank plays about this loud at full level.
    /// </summary>
    public double Lufs()
    {
        var filter = new Loudness.KWeighting(SampleRate);
        double sum = 0;
        long count = 0;
        var last = -1;
        for (var i = 1; i < Marks.Length - 1; i++)
        {
            if (!Usable[i]) continue;
            if (Marks[i] != last)
            {
                // A stretch that does not continue the last one: settle a fresh filter on what comes before it.
                filter = new Loudness.KWeighting(SampleRate);
                for (var j = Math.Max(Marks[i] - SampleRate / 100, 0); j < Marks[i]; j++) filter.Process(Samples[j]);
            }
            for (var j = Marks[i]; j < Marks[i + 1]; j++)
            {
                var y = filter.Process(Samples[j]);
                sum += y * y;
            }
            count += Marks[i + 1] - Marks[i];
            last = Marks[i + 1];
        }
        return -0.691 + 10.0 * Math.Log10(sum / Math.Max(count, 1) + 1e-20);
    }

    /// <summary>A usable pulse picked at random among the <paramref name="spread"/> whose firing frequency is nearest to <paramref name="firing"/>.</summary>
    internal int Nearest(float firing, int spread, Random random)
    {
        var lo = 0;
        var hi = _byFiring.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Firing[_byFiring[mid]] < firing) lo = mid + 1;
            else hi = mid;
        }
        var first = Math.Clamp(lo - spread / 2, 0, Math.Max(_byFiring.Length - spread, 0));
        var last = Math.Min(first + spread, _byFiring.Length);
        return _byFiring[random.Next(first, last)];
    }

    /// <summary>A bank from the 16-bit mono PCM of its recording and the text of its marks file, its timbre evened across its segments unless asked not to.</summary>
    public static GrainBank FromPcm16(ReadOnlySpan<byte> pcm, string marksJson, bool evenTimbre = true)
    {
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
        using var document = JsonDocument.Parse(marksJson);
        var root = document.RootElement;
        var marks = root.GetProperty("marks").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var firing = root.GetProperty("firing").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        var segment = root.TryGetProperty("segment", out var seg) ? seg.EnumerateArray().Select(e => e.GetInt32()).ToArray() : new int[marks.Length];
        return new GrainBank(samples, root.GetProperty("rate").GetInt32(), marks, firing, segment, evenTimbre);
    }

    /// <summary>A bank from its files: a 16-bit mono WAV and the marks beside it.</summary>
    public static GrainBank FromFiles(string wavPath, string marksPath, bool evenTimbre = true) => FromPcm16(Wav.ReadPcm16(wavPath), File.ReadAllText(marksPath), evenTimbre);
}

/// <summary>The data chunk of a 16-bit PCM WAV file.</summary>
public static class Wav
{
    public static byte[] ReadPcm16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var size = BitConverter.ToInt32(bytes, at + 4);
            if (bytes[at] == 'd' && bytes[at + 1] == 'a' && bytes[at + 2] == 't' && bytes[at + 3] == 'a')
            {
                return bytes.AsSpan(at + 8, Math.Min(size, bytes.Length - at - 8)).ToArray();
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException($"no data chunk in {path}");
    }

    public static void WritePcm16(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var s in samples) writer.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));
    }
}
