using GrudgeLap.Audio;
using Xunit;

namespace GrudgeLap.Audio.Tests;

/// <summary>
/// The granular engine on the game's own banks (game/assets/audio): it must sound at the firing frequency of the
/// revs asked for, glide without clicks, fall silent when stopped. Set GRUDGELAP_AUDIO_DEMO to a folder to also
/// write listenable sweeps there.
/// </summary>
public class GranularEngineTests
{
    private static readonly string Audio = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "game", "assets", "audio"));

    private static GrainBank Bank(string car, string name) => GrainBank.FromFiles(Path.Combine(Audio, car, name + ".wav"), Path.Combine(Audio, car, name + ".json"));

    private static GranularEngine Load(string car, int cylinders, bool overrun = false) =>
        new(Bank(car, "engine_idle"), Bank(car, "engine_drive"), cylinders, overrun: overrun ? Bank(car, "engine_overrun") : null);

    /// <summary>The overrun recording takes over off the throttle: still at the firing frequency, but another sound than the run-up gives there.</summary>
    [Theory]
    [InlineData("v8", 8, 5600f)]
    [InlineData("flat12", 12, 4800f)]
    [InlineData("hothatch", 4, 5000f)]
    [InlineData("five", 5, 4500f)]
    [InlineData("turbohatch", 4, 4500f)]
    public void Overrun_bank_sounds_off_the_throttle_at_the_firing_frequency(string car, int cylinders, float rpm)
    {
        var with = Load(car, cylinders, overrun: true);
        var without = Load(car, cylinders);
        var closed = Hold(with, rpm, 0f, 2f);
        var plain = Hold(without, rpm, 0f, 2f);
        var firing = rpm * cylinders / 120f;
        Assert.InRange(Peak(closed, with.SampleRate, firing * 0.8f, firing * 1.2f), firing * 0.97f, firing * 1.03f);
        Assert.True(closed.Max(MathF.Abs) > 0.01f, "the overrun is silent");
        Assert.NotEqual(plain.Skip(plain.Length - 4410).ToArray(), closed.Skip(closed.Length - 4410).ToArray());
        // Full throttle is the run-up alone, with or without the overrun bank.
        Assert.Equal(Hold(Load(car, cylinders), rpm, 1f, 1f), Hold(Load(car, cylinders, overrun: true), rpm, 1f, 1f));
    }

    private static float[] Hold(GranularEngine engine, float rpm, float load, float seconds)
    {
        var output = new float[(int)(seconds * engine.SampleRate)];
        for (var at = 0; at < output.Length; at += 512)
        {
            engine.Render(output.AsSpan(at, Math.Min(512, output.Length - at)), rpm, load, true);
        }
        return output;
    }

    /// <summary>The strongest spectral peak between lo and hi Hz, over the last second.</summary>
    private static float Peak(float[] x, int rate, float lo, float hi)
    {
        var n = rate;
        var segment = x.AsSpan(x.Length - n);
        var best = 0.0;
        var bestHz = 0f;
        for (var hz = lo; hz <= hi; hz += 0.5f)
        {
            double re = 0, im = 0;
            for (var i = 0; i < n; i += 2)
            {
                var w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
                var a = 2 * Math.PI * hz * i / rate;
                re += segment[i] * w * Math.Cos(a);
                im += segment[i] * w * Math.Sin(a);
            }
            var power = re * re + im * im;
            if (power > best)
            {
                best = power;
                bestHz = hz;
            }
        }
        return bestHz;
    }

    [Theory]
    [InlineData("v8", 8, 5000f)]
    [InlineData("v8", 8, 7600f)]
    [InlineData("flat12", 12, 4200f)]
    [InlineData("flat12", 12, 6500f)]
    [InlineData("hothatch", 4, 3500f)]
    [InlineData("hothatch", 4, 6500f)]
    [InlineData("five", 5, 3000f)]
    [InlineData("five", 5, 6000f)]
    [InlineData("turbohatch", 4, 3000f)]
    [InlineData("turbohatch", 4, 5800f)]
    public void Sounds_at_the_firing_frequency_of_the_revs(string car, int cylinders, float rpm)
    {
        var engine = Load(car, cylinders);
        var x = Hold(engine, rpm, 1f, 2f);
        var firing = rpm * cylinders / 120f;
        var peak = Peak(x, engine.SampleRate, firing * 0.8f, firing * 1.2f);
        Assert.InRange(peak, firing * 0.97f, firing * 1.03f);
    }

    [Theory]
    [InlineData("v8", 8)]
    [InlineData("flat12", 12)]
    [InlineData("hothatch", 4)]
    [InlineData("five", 5)]
    [InlineData("turbohatch", 4)]
    public void Idles_at_the_idle_firing_frequency(string car, int cylinders)
    {
        var engine = Load(car, cylinders);
        var x = Hold(engine, 950f, 0.1f, 2f);
        var firing = 950f * cylinders / 120f;
        var peak = Peak(x, engine.SampleRate, firing * 0.85f, firing * 1.15f);
        Assert.InRange(peak, firing * 0.96f, firing * 1.04f);
    }

    [Theory]
    [InlineData("v8", 8, 8600f)]
    [InlineData("flat12", 12, 7400f)]
    [InlineData("hothatch", 4, 7000f)]
    [InlineData("five", 5, 6600f)]
    [InlineData("turbohatch", 4, 6500f)]
    public void Sweeps_without_clicks_and_falls_silent_when_stopped(string car, int cylinders, float top)
    {
        var engine = Load(car, cylinders);
        var rate = engine.SampleRate;
        var all = new List<float>();
        var block = new float[441];
        // Idle, run up to the top over four seconds, back down over three, idle, then switch off.
        for (var at = 0.0; at < 10.0; at += 0.01)
        {
            var rpm = at < 1 ? 950f : at < 5 ? 950f + (top - 950f) * (float)(at - 1) / 4f : at < 8 ? top - (top - 950f) * (float)(at - 5) / 3f : 950f;
            var load = at < 5 ? 1f : 0.05f;
            engine.Render(block, rpm, load, at < 9.0);
            all.AddRange(block);
        }
        var x = all.ToArray();
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        Assert.True(x.Max(MathF.Abs) < 1.5f, "the level runs away");
        var loudest = 0f;
        for (var i = 1; i < (int)(9.0 * rate); i++) loudest = MathF.Max(loudest, MathF.Abs(x[i] - x[i - 1]));
        Assert.True(loudest < 0.5f, $"a step of {loudest:0.00} between samples is a click");
        var tail = x.AsSpan((int)(9.8 * rate));
        Assert.True(tail.ToArray().Max(MathF.Abs) < 0.01f, "the engine still sounds after it stopped");

        var demo = Environment.GetEnvironmentVariable("GRUDGELAP_AUDIO_DEMO");
        if (!string.IsNullOrEmpty(demo))
        {
            Directory.CreateDirectory(demo);
            Wav.WritePcm16(Path.Combine(demo, $"{car}_sweep.wav"), x, rate);
            engine.Interior = true;
            Wav.WritePcm16(Path.Combine(demo, $"{car}_cruise_cockpit.wav"), Hold(engine, top * 0.6f, 0.6f, 4f), rate);
            engine.Interior = false;
            Wav.WritePcm16(Path.Combine(demo, $"{car}_cruise_outside.wav"), Hold(engine, top * 0.6f, 0.6f, 4f), rate);
            Wav.WritePcm16(Path.Combine(demo, $"{car}_idle.wav"), Hold(engine, 950f, 0.1f, 4f), rate);
        }
    }
}
