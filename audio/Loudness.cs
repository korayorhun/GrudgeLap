namespace GrudgeLap.Audio;

/// <summary>
/// Loudness as ITU-R BS.1770 measures it (the LUFS of EBU R 128): the mean square of the signal through the K-weighting
/// filter, a high shelf of +4 dB above about 1.7 kHz (the head's effect) and a high-pass at 38 Hz, in dB less 0.691.
/// Ungated: what it measures here are steady renders and recordings, not programmes with silences.
/// </summary>
public static class Loudness
{
    /// <summary>The ungated K-weighted loudness of <paramref name="x"/>, LUFS (mono).</summary>
    public static double Lufs(ReadOnlySpan<float> x, int rate)
    {
        var filter = new KWeighting(rate);
        var sum = 0.0;
        foreach (var v in x)
        {
            var y = filter.Process(v);
            sum += y * y;
        }
        return -0.691 + 10.0 * Math.Log10(sum / Math.Max(x.Length, 1) + 1e-20);
    }

    /// <summary>
    /// The two stages of BS.1770's K-weighting, at any rate: the coefficients are those of the standard's 48 kHz
    /// filters re-derived for the rate by the bilinear transform (as libebur128 does), in double precision.
    /// </summary>
    internal struct KWeighting
    {
        private readonly double _b0, _b1, _b2, _a1, _a2, _c1, _c2;
        private double _x1, _x2, _y1, _y2, _u1, _u2, _w1, _w2;

        public KWeighting(int rate)
        {
            // Stage 1, the shelf: 1681.97 Hz, +4.0 dB, Q 0.7072.
            var k = Math.Tan(Math.PI * 1681.974450955533 / rate);
            var vh = Math.Pow(10.0, 3.999843853973347 / 20.0);
            var vb = Math.Pow(vh, 0.4996667741545416);
            const double q = 0.7071752369554196;
            var a0 = 1.0 + k / q + k * k;
            _b0 = (vh + vb * k / q + k * k) / a0;
            _b1 = 2.0 * (k * k - vh) / a0;
            _b2 = (vh - vb * k / q + k * k) / a0;
            _a1 = 2.0 * (k * k - 1.0) / a0;
            _a2 = (1.0 - k / q + k * k) / a0;
            // Stage 2, the high-pass: 38.14 Hz, Q 0.5003; numerator 1, -2, 1.
            k = Math.Tan(Math.PI * 38.13547087602444 / rate);
            const double q2 = 0.5003270373238773;
            var d0 = 1.0 + k / q2 + k * k;
            _c1 = 2.0 * (k * k - 1.0) / d0;
            _c2 = (1.0 - k / q2 + k * k) / d0;
            _x1 = _x2 = _y1 = _y2 = _u1 = _u2 = _w1 = _w2 = 0.0;
        }

        public double Process(double x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            var w = y - 2.0 * _u1 + _u2 - _c1 * _w1 - _c2 * _w2;
            _u2 = _u1;
            _u1 = y;
            _w2 = _w1;
            _w1 = w;
            return w;
        }
    }
}
