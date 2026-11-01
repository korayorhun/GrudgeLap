namespace GrudgeLap.Audio;

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

    public void SetHighPass(float cutoff, float q, float rate)
    {
        var w = 2f * MathF.PI * MathF.Min(cutoff, rate * 0.45f) / rate;
        var alpha = MathF.Sin(w) / (2f * q);
        var cos = MathF.Cos(w);
        var a0 = 1f + alpha;
        Set((1f + cos) * 0.5f / a0, -(1f + cos) / a0, (1f + cos) * 0.5f / a0, -2f * cos / a0, (1f - alpha) / a0);
    }

    /// <summary>Band-pass with 0 dB at the centre (the cookbook's constant peak gain form).</summary>
    public void SetBandPass(float centre, float q, float rate)
    {
        var w = 2f * MathF.PI * MathF.Min(centre, rate * 0.45f) / rate;
        var alpha = MathF.Sin(w) / (2f * q);
        var cos = MathF.Cos(w);
        var a0 = 1f + alpha;
        Set(alpha / a0, 0f, -alpha / a0, -2f * cos / a0, (1f - alpha) / a0);
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
