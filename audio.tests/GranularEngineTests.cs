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
    [InlineData("turbohatch", 4, 4500f)]
    [InlineData("v6", 6, 2500f)]
    [InlineData("saloon", 4, 3200f)]
    [InlineData("estate", 4, 4000f)]
    [InlineData("fleet", 4, 5000f)]
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

    /// <summary>
    /// On the overrun a real engine stops firing: what is left at the same revs is the exhaust's rasp, quieter than the
    /// bark under load overall, at the firing frequency and in the top octaves (on every car's recordings: the level of
    /// the run-up and the overrun bank, the throttle's filter and the level that follows the load together). At the
    /// same revs the overrun must never be the louder: in 0.18 the small cars at full throttle played mostly the overrun
    /// bank (their load was the torque over 500 N m), and the sound fell by 11 dB when they were driven flat out.
    /// </summary>
    [Theory]
    [InlineData("v8", 8, 5600f)]
    [InlineData("flat12", 12, 4800f)]
    [InlineData("hothatch", 4, 5000f)]
    [InlineData("turbohatch", 4, 4500f)]
    [InlineData("v6", 6, 2500f)]
    [InlineData("saloon", 4, 3200f)]
    [InlineData("estate", 4, 4000f)]
    [InlineData("fleet", 4, 5000f)]
    public void Overrun_is_quieter_than_the_throttle_at_the_same_revs_in_every_band(string car, int cylinders, float rpm)
    {
        var rate = 44100;
        var open = Hold(Load(car, cylinders, overrun: true), rpm, 1f, 2f).AsSpan(rate);
        var shut = Hold(Load(car, cylinders, overrun: true), rpm, 0f, 2f).AsSpan(rate);
        var firing = rpm * cylinders / 120f;
        var total = Spectrum.BandDb(open, rate, 20, 20000) - Spectrum.BandDb(shut, rate, 20, 20000);
        var fundamental = Spectrum.BandDb(open, rate, firing * 0.8, firing * 1.2) - Spectrum.BandDb(shut, rate, firing * 0.8, firing * 1.2);
        var top = Spectrum.BandDb(open, rate, 2000, 20000) - Spectrum.BandDb(shut, rate, 2000, 20000);
        Assert.True(total > 5, $"overall {total:0.0} dB");
        Assert.True(fundamental > 2, $"at the firing frequency {fundamental:0.0} dB");
        Assert.True(top > 3, $"above 2 kHz {top:0.0} dB");
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
    [InlineData("v6", 6, 2500f)]
    [InlineData("v6", 6, 5500f)]
    [InlineData("turbohatch", 4, 3000f)]
    [InlineData("turbohatch", 4, 5800f)]
    [InlineData("saloon", 4, 2500f)]
    [InlineData("saloon", 4, 4000f)]
    [InlineData("estate", 4, 2200f)]
    [InlineData("estate", 4, 4300f)]
    [InlineData("fleet", 4, 2500f)]
    [InlineData("fleet", 4, 5500f)]
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
    [InlineData("v6", 6)]
    [InlineData("turbohatch", 4)]
    [InlineData("saloon", 4)]
    [InlineData("estate", 4)]
    [InlineData("fleet", 4)]
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
    [InlineData("v6", 6, 6600f)]
    [InlineData("turbohatch", 4, 6800f)]
    [InlineData("saloon", 4, 6000f)]
    [InlineData("estate", 4, 5800f)]
    [InlineData("fleet", 4, 6500f)]
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
        // Stopped, the voice fades with a quarter of a second's time constant: 0.8 s on it is 28 dB under the idle it
        // stopped from (measured 28 to 34 dB). Held to an absolute 0.01, the estate's idle, peaking at 0.26, passed or
        // failed by the grains it happened to pick last (its tail 0.0085 or 0.0102 as the run-up's picks changed).
        var tail = x.AsSpan((int)(9.8 * rate)).ToArray().Max(MathF.Abs);
        var idle = x.AsSpan(8 * rate, rate).ToArray().Max(MathF.Abs);
        Assert.True(tail < idle * 0.056f, $"the engine still sounds after it stopped ({20 * Math.Log10(tail / idle):0.0} dB under its idle)");

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
