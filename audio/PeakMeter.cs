namespace GrudgeLap.Audio;

/// <summary>
/// The peak of a signal but for its rarest samples: the level that no more than <see cref="RareShare"/> of the samples
/// pass. A recording's absolute peak is often one click (the fleet saloon's run-up reaches 0.99 where one sample in
/// 10,000 passes 0.64), and a gain held under it would hold a whole car down for the sake of a sample; the click is
/// rounded off by the voice instead (<see cref="GranularEngine.PeakKnee"/>). Used while a bank or a voice is made, off
/// the audio thread.
/// </summary>
internal sealed class PeakMeter
{
    /// <summary>The share of the samples allowed past the peak: one in 10,000.</summary>
    public const double RareShare = 1e-4;
    private const int Bins = 4096;
    /// <summary>The top of the histogram: anything louder counts as this loud.</summary>
    private const float Top = 4f;

    private readonly long[] _bins = new long[Bins];
    private long _count;

    public void Add(ReadOnlySpan<float> x)
    {
        foreach (var s in x) _bins[Math.Min((int)(MathF.Abs(s) * (Bins / Top)), Bins - 1)]++;
        _count += x.Length;
    }

    /// <summary>The peak, to the histogram's step (a thousandth of full scale), rounded up.</summary>
    public float Peak
    {
        get
        {
            var allowed = (long)(_count * RareShare);
            long above = 0;
            for (var b = Bins - 1; b > 0; b--)
            {
                above += _bins[b];
                if (above > allowed) return (b + 1) * (Top / Bins);
            }
            return Top / Bins;
        }
    }

    /// <summary>The peak of <paramref name="x"/> but for its rarest samples.</summary>
    public static float Of(ReadOnlySpan<float> x)
    {
        var meter = new PeakMeter();
        meter.Add(x);
        return meter.Peak;
    }
}
