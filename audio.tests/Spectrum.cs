using System.Numerics;

namespace GrudgeLap.Audio.Tests;

/// <summary>Measurements of rendered sound: RMS levels, Welch power spectra, band energies, centroids and peaks.</summary>
internal static class Spectrum
{
    public static double Rms(ReadOnlySpan<float> x)
    {
        var sum = 0.0;
        foreach (var v in x) sum += v * v;
        return Math.Sqrt(sum / Math.Max(x.Length, 1));
    }

    public static double Db(double linear) => 20.0 * Math.Log10(linear + 1e-12);

    /// <summary>Welch average of Hann-windowed frames of <paramref name="n"/> samples (a power of two), half overlapped.</summary>
    public static double[] Power(ReadOnlySpan<float> x, int n = 4096)
    {
        var sum = new double[n / 2];
        var buffer = new Complex[n];
        var frames = 0;
        for (var start = 0; start + n <= x.Length; start += n / 2)
        {
            for (var i = 0; i < n; i++) buffer[i] = new Complex(x[start + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n)), 0);
            Fft(buffer);
            for (var k = 0; k < n / 2; k++) sum[k] += buffer[k].Real * buffer[k].Real + buffer[k].Imaginary * buffer[k].Imaginary;
            frames++;
        }
        for (var k = 0; k < sum.Length; k++) sum[k] /= Math.Max(frames, 1);
        return sum;
    }

    /// <summary>Energy between <paramref name="lo"/> and <paramref name="hi"/> Hz, in dB (relative: only differences mean anything).</summary>
    public static double BandDb(ReadOnlySpan<float> x, int rate, double lo, double hi, int n = 4096)
    {
        var p = Power(x, n);
        var sum = 0.0;
        for (var k = 1; k < p.Length; k++)
        {
            var hz = k * (double)rate / n;
            if (hz >= lo && hz < hi) sum += p[k];
        }
        return 10.0 * Math.Log10(sum + 1e-30);
    }

    public static double Centroid(ReadOnlySpan<float> x, int rate, int n = 4096)
    {
        var p = Power(x, n);
        double weighted = 0, total = 0;
        for (var k = 1; k < p.Length; k++)
        {
            weighted += k * (double)rate / n * p[k];
            total += p[k];
        }
        return weighted / total;
    }

    /// <summary>The frequency of the strongest bin between <paramref name="lo"/> and <paramref name="hi"/> Hz, refined by a parabola through its neighbours.</summary>
    public static double PeakHz(ReadOnlySpan<float> x, int rate, double lo, double hi, int n = 16384)
    {
        var p = Power(x, n);
        var best = -1;
        for (var k = 1; k < p.Length - 1; k++)
        {
            var hz = k * (double)rate / n;
            if (hz < lo || hz >= hi) continue;
            if (best < 0 || p[k] > p[best]) best = k;
        }
        double a = Math.Log(p[best - 1] + 1e-30), b = Math.Log(p[best] + 1e-30), c = Math.Log(p[best + 1] + 1e-30);
        var offset = 0.5 * (a - c) / (a - 2 * b + c);
        return (best + offset) * rate / n;
    }

    private static void Fft(Complex[] a)
    {
        var n = a.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;
                for (var j = 0; j < length / 2; j++)
                {
                    var u = a[i + j];
                    var v = a[i + j + length / 2] * w;
                    a[i + j] = u + v;
                    a[i + j + length / 2] = u - v;
                    w *= step;
                }
            }
        }
    }
}
