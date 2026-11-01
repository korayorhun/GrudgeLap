using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Audio.Tests;

/// <summary>
/// The turbocharger's sounds (0.20, audit finding 11), rendered at the rate the game mixes them (EngineAudio, 22.05 kHz)
/// and measured: a whistle at the blade-passing frequency of a shaft that spools with the boost, a vent on a car with a
/// blow-off valve, the flutter of a surging compressor on one without, and all of it well under the engine. Set
/// GRUDGELAP_AUDIO_DEMO to a folder to also write listenable renders there.
/// </summary>
public class TurboNoiseTests(ITestOutputHelper output)
{
    private const int Rate = 22050;
    private const float Dt = 1f / 60f;

    /// <summary>The turbo four's figures (EngineSpec.TurboFour: 45 % of its torque from the boost).</summary>
    private static TurboState State(float boost, float throttle, float revShare = 0.7f, bool valve = true) => new(boost, throttle, revShare, 0.45f, valve, Interior: false);

    /// <summary>Renders a sequence of frame states, one 1/60 s frame each.</summary>
    private static float[] Render(TurboNoise turbo, IEnumerable<TurboState> frames)
    {
        var x = new List<float>();
        var block = new float[Rate / 60];
        foreach (var state in frames)
        {
            Array.Clear(block);
            turbo.Render(block, state, Dt);
            x.AddRange(block);
        }
        return [.. x];
    }

    private static IEnumerable<TurboState> Hold(TurboState state, float seconds) => Enumerable.Repeat(state, (int)(seconds * 60));

    /// <summary>
    /// The shaft follows the compressor's work: at no boost the exhaust alone turns it (a third of its speed at most),
    /// and on boost it turns as the square root of the work, so the first half of the boost spins it most of the way.
    /// The blade tone sits at six times the shaft's revolutions a second, climbing with the boost.
    /// </summary>
    [Fact]
    public void The_whistle_follows_the_shaft_and_the_shaft_the_boost()
    {
        Assert.InRange(TurboNoise.ShaftTarget(State(0f, 0f, 0f)), 0f, 0.01f);
        Assert.InRange(TurboNoise.ShaftTarget(State(0f, 1f, 1f)), 0.34f, 0.36f);
        Assert.InRange(TurboNoise.ShaftTarget(State(1f, 1f)), 0.999f, 1.001f);
        var half = TurboNoise.ShaftTarget(State(0.5f, 1f));
        Assert.InRange(half, 0.68f, 0.76f);
        var previous = 0.0;
        foreach (var boost in new[] { 0.1f, 0.2f, 0.3f })
        {
            var turbo = new TurboNoise(Rate);
            var x = Render(turbo, Hold(State(boost, 1f), 3f));
            var expected = TurboNoise.MaxShaftHz * TurboNoise.ShaftTarget(State(boost, 1f)) * TurboNoise.Blades;
            var tail = x.AsSpan(x.Length - Rate);
            var peak = Spectrum.PeakHz(tail, Rate, 3000, 10000);
            output.WriteLine($"boost {boost:0.0}: shaft {turbo.Shaft:0.00}, blade tone {peak:0} Hz (expected {expected:0})");
            Assert.InRange(peak, expected * 0.97, expected * 1.03);
            Assert.True(peak > previous, "the whistle does not climb with the boost");
            previous = peak;
        }
    }

    /// <summary>
    /// Shutting the throttle on boost: with a valve the charge is vented in one rush that is gone within about a second;
    /// without one the compressor surges, its level beating at 6 to 15 cycles a second (the flutter) while the stored air
    /// bleeds away, and an open throttle ends it.
    /// </summary>
    [Fact]
    public void A_shut_throttle_on_boost_vents_or_flutters()
    {
        IEnumerable<TurboState> Lift(bool valve) => Hold(State(0.9f, 1f, valve: valve), 1.5f).Concat(Hold(State(0.85f, 0f, valve: valve), 1.2f));
        var vented = Render(new TurboNoise(Rate), Lift(valve: true));
        var surging = Render(new TurboNoise(Rate), Lift(valve: false));
        var after = (int)(1.5f * Rate);
        var (ventRate, ventDepth) = Beat(vented.AsSpan(after, (int)(0.6f * Rate)));
        var (surgeRate, surgeDepth) = Beat(surging.AsSpan(after, (int)(0.6f * Rate)));
        output.WriteLine($"vent: beats {ventDepth:0.0} dB at {ventRate:0.0} Hz; surge: beats {surgeDepth:0.0} dB at {surgeRate:0.0} Hz");
        Assert.InRange(surgeRate, 6, 15);
        Assert.True(surgeDepth > 10, $"the surge beats by only {surgeDepth:0.0} dB");
        Assert.True(ventDepth < surgeDepth - 6, "the vent flutters like a surge");
        // Both are louder than the steady whistle for a moment, and both are over within the 1.2 s.
        var steady = Spectrum.Rms(vented.AsSpan(after - Rate / 2, Rate / 2));
        output.WriteLine($"steady {Spectrum.Db(steady):0.0} dB, vent's first 100 ms {Spectrum.Db(Spectrum.Rms(vented.AsSpan(after, Rate / 10))):0.0}, surge's first 250 ms {Spectrum.Db(Spectrum.Rms(surging.AsSpan(after, Rate / 4))):0.0}");
        Assert.True(Spectrum.Rms(vented.AsSpan(after, Rate / 10)) > 2 * steady, "the vent is not heard");
        Assert.True(Spectrum.Rms(surging.AsSpan(after, Rate / 4)) > 2 * steady, "the surge is not heard");
        var turbo = new TurboNoise(Rate);
        Render(turbo, Hold(State(0.9f, 1f, valve: false), 1.5f).Concat(Hold(State(0.85f, 0f, valve: false), 0.2f)));
        Assert.True(turbo.Surging);
        Render(turbo, Hold(State(0.85f, 1f, valve: false), 0.05f));
        Assert.False(turbo.Surging, "an open throttle does not end the surge");

        var demo = Environment.GetEnvironmentVariable("GRUDGELAP_AUDIO_DEMO");
        if (string.IsNullOrEmpty(demo)) return;
        Directory.CreateDirectory(demo);
        Wav.WritePcm16(Path.Combine(demo, "turbo_vent.wav"), vented, Rate);
        Wav.WritePcm16(Path.Combine(demo, "turbo_surge.wav"), surging, Rate);
    }

    /// <summary>
    /// The level's strongest beat between 3 and 25 Hz, in 5 ms frames: its rate (Hz) and how far it swings about the
    /// level's straight decay in dB (the 90th against the 10th percentile of the frames' departures from it, dB), so an
    /// exponential fade does not count as a beat.
    /// </summary>
    private static (double Rate, double Depth) Beat(ReadOnlySpan<float> x)
    {
        var frame = Rate / 200;
        var levels = new double[x.Length / frame];
        for (var i = 0; i < levels.Length; i++) levels[i] = Spectrum.Db(Spectrum.Rms(x.Slice(i * frame, frame)));
        var meanIndex = (levels.Length - 1) / 2.0;
        var mean = levels.Average();
        var slope = levels.Select((v, i) => (i - meanIndex) * (v - mean)).Sum() / levels.Select((_, i) => (i - meanIndex) * (i - meanIndex)).Sum();
        levels = levels.Select((v, i) => v - slope * (i - meanIndex)).ToArray();
        var sorted = levels.Order().ToArray();
        var depth = sorted[sorted.Length * 9 / 10] - sorted[sorted.Length / 10];
        var (best, bestPower) = (0.0, 0.0);
        for (var hz = 3.0; hz <= 25.0; hz += 0.25)
        {
            double re = 0, im = 0;
            for (var i = 0; i < levels.Length; i++)
            {
                var a = 2 * Math.PI * hz * i / 200.0;
                re += (levels[i] - mean) * Math.Cos(a);
                im += (levels[i] - mean) * Math.Sin(a);
            }
            if (re * re + im * im > bestPower) (best, bestPower) = (hz, re * re + im * im);
        }
        return (best, depth);
    }

    /// <summary>
    /// Subtle: on full boost the whole turbo (whistle, shaft and rush) is at least 20 LU under the turbo four's engine
    /// pulling at full throttle, at the gains the game mixes them at (EngineAudio's layers at 0.8 through a player at
    /// -6 dB, the engine voice at 0.9 trimmed to <see cref="GranularEngine.ReferenceLufs"/>); the vent's rush and the
    /// surge's flutter at their loudest are at least 8 LU under it.
    /// </summary>
    [Fact]
    public void The_turbo_stays_well_under_the_engine()
    {
        var gain = 0.8f * MathF.Pow(10f, -6f / 20f);
        var engine = GranularEngine.ReferenceLufs + 20 * Math.Log10(0.9);
        var spooled = Render(new TurboNoise(Rate), Hold(State(1f, 1f), 3f)).Select(s => s * gain).ToArray();
        var whole = Loudness.Lufs(spooled.AsSpan(spooled.Length - Rate), Rate);
        var vent = Render(new TurboNoise(Rate), Hold(State(1f, 1f), 1.5f).Concat(Hold(State(1f, 0f), 0.2f))).Select(s => s * gain).ToArray();
        var rush = Loudness.Lufs(vent.AsSpan((int)(1.5f * Rate), Rate / 10), Rate);
        var surge = Render(new TurboNoise(Rate), Hold(State(1f, 1f, valve: false), 1.5f).Concat(Hold(State(1f, 0f, valve: false), 0.3f))).Select(s => s * gain).ToArray();
        var flutter = Loudness.Lufs(surge.AsSpan((int)(1.5f * Rate), Rate / 4), Rate);
        output.WriteLine($"engine {engine:0.0} LUFS, turbo on full boost {whole:0.0}, the vent's first 100 ms {rush:0.0}, the surge's first 250 ms {flutter:0.0}");
        Assert.True(whole < engine - 20, $"the turbo is only {engine - whole:0.0} LU under the engine");
        Assert.True(rush < engine - 8, $"the vent is only {engine - rush:0.0} LU under the engine");
        Assert.True(flutter < engine - 8, $"the surge is only {engine - flutter:0.0} LU under the engine");
    }
}
