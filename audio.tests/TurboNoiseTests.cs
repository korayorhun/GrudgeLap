using GrudgeLap.Physics.Vehicle;
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

    /// <summary>A turbo four's figures: 45 % of its torque from the boost (the rally hatch's, EngineSpec.RallyTurboFour, is 48 %).</summary>
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
    /// Every car of the catalogue whose engine has a turbo (the game synthesises one for any engine with boost), heard
    /// outside and in the cabin: found by its boost, not by its place in the catalogue, which the hatchbacks' respec
    /// (cars3) changed under a test that named the turbo four by index.
    /// </summary>
    public static TheoryData<int, bool> BoostedCars()
    {
        var cars = new TheoryData<int, bool>();
        for (var i = 0; i < VehicleSpec.Catalogue.Length; i++)
        {
            if (VehicleSpec.Catalogue[i].Engine.BoostShare <= 0f) continue;
            cars.Add(i, false);
            cars.Add(i, true);
        }
        return cars;
    }

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
        // Both stand out over the steady turbo for a moment in their own bands, away from the whine at 2.5 kHz that rules
        // the steady sound (the vent's rush above 3 kHz, the surge's chuffs and thumps under 2 kHz), and both are over
        // within the 1.2 s.
        double Rise(float[] x, int length, double lo, double hi) =>
            Spectrum.BandDb(x.AsSpan(after, length), Rate, lo, hi, 1024) - Spectrum.BandDb(x.AsSpan(after - length, length), Rate, lo, hi, 1024);
        var vent = Rise(vented, Rate / 10, 3000, 9000);
        var flutter = Rise(surging, Rate / 4, 50, 2000);
        output.WriteLine($"over the steady turbo: the vent's first 100 ms {vent:+0.0;-0.0} dB above 3 kHz, the surge's first 250 ms {flutter:+0.0;-0.0} dB under 2 kHz");
        Assert.True(vent > 6, "the vent is not heard");
        Assert.True(flutter > 6, "the surge is not heard");
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
    /// Subtle: on full boost the whole turbo (whistle, whine and rush) is at least 12 LU under the turbo four's engine
    /// pulling at full throttle (measured: 12.8), at the gains the game mixes them at (EngineAudio's layers at 0.8 through
    /// a player at -6 dB, the engine voice at 0.9 trimmed to <see cref="GranularEngine.ReferenceLufs"/>); the vent's rush
    /// and the surge's flutter at their loudest are at least 8 LU under it. Heard all the same: its whine is level with
    /// the engine in its own band (<see cref="The_turbo_is_heard_on_boost"/>).
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
        Assert.True(whole < engine - 12, $"the turbo is only {engine - whole:0.0} LU under the engine");
        Assert.True(rush < engine - 8, $"the vent is only {engine - rush:0.0} LU under the engine");
        Assert.True(flutter < engine - 8, $"the surge is only {engine - flutter:0.0} LU under the engine");
    }

    /// <summary>
    /// Subtle, but heard (0.20 review, problem 3): the turbo four driven flat out through <see cref="EngineModel"/> at 70 %
    /// of its redline is on full boost within a second, its shaft at full speed and its blade tone (15 kHz) out of the
    /// mix, and the shaft's own whine, at the gains the game mixes the turbo and the engine at, stands at least level with
    /// the engine in its third-octave band (2.5 kHz), outside and in the cabin (both heard through the cabin's low-pass
    /// there): a tone level with the noise of its own band is well over its masked threshold (Zwicker and Fastl,
    /// Psychoacoustics). With the whine at a quarter of its gain the turbo was under the engine in every band on boost
    /// outside (7.5 dB under at best, its rush at 6.4 kHz) and the whine 11 dB under in its own band.
    /// </summary>
    [Theory]
    [MemberData(nameof(BoostedCars))]
    public void The_turbo_is_heard_on_boost(int index, bool interior)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        var engine = new EngineModel(spec);
        engine.Start();
        var rpm = 0.7f * spec.RedlineRpm;
        for (var t = 0f; t < 1f; t += VehicleSim.FixedStep)
        {
            engine.AngularVelocity = rpm / EngineModel.RpmPerRadPerSec;
            engine.Torque(1f);
            engine.Tick(VehicleSim.FixedStep);
        }
        Assert.InRange(engine.Boost, 0.99f, 1f);

        var audio = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "game", "assets", "audio", spec.SoundFolder));
        GrainBank Bank(string name) => GrainBank.FromFiles(Path.Combine(audio, name + ".wav"), Path.Combine(audio, name + ".json"));
        var voicing = new EngineVoicing(EnginePlacement.Front, 2.3f);
        var voice = new GranularEngine(Bank("engine_idle"), Bank("engine_drive"), spec.Cylinders, voicing, spec.RedlineRpm, overrun: Bank("engine_overrun"))
        {
            Interior = interior,
            Volume = 0.9f,
        };
        var pulling = new float[2 * voice.SampleRate];
        for (var at = 0; at < pulling.Length; at += 512) voice.Render(pulling.AsSpan(at, Math.Min(512, pulling.Length - at)), rpm, 1f, true);
        var heard = pulling.AsSpan(voice.SampleRate);

        var gain = 0.8f * MathF.Pow(10f, -6f / 20f);
        var state = new TurboState(engine.Boost, 1f, 0.7f, spec.BoostShare, spec.BlowOffValve, interior, voicing.CabinCutoff);
        var noise = new TurboNoise(Rate);
        var turbo = Render(noise, Hold(state, 3f)).Select(s => s * gain).ToArray().AsSpan(2 * Rate);
        var whineHz = TurboNoise.MaxShaftHz * noise.Shaft;
        var whine = Spectrum.BandDb(turbo, Rate, whineHz * 0.891, whineHz * 1.122) - Spectrum.BandDb(heard, voice.SampleRate, whineHz * 0.891, whineHz * 1.122);
        // Third-octave bands up to 8 kHz (the turbo is mixed at 22.05 kHz); the Welch frames are 4096 samples at either
        // rate, so a band's energy is the signal's mean square in it times the same factor for both.
        var (bestHz, best) = (0.0, double.NegativeInfinity);
        for (var f = 200.0; f < 8000.0; f *= 1.2599)
        {
            var margin = Spectrum.BandDb(turbo, Rate, f * 0.891, f * 1.122) - Spectrum.BandDb(heard, voice.SampleRate, f * 0.891, f * 1.122);
            if (margin > best) (bestHz, best) = (f, margin);
        }
        output.WriteLine($"{spec.SoundFolder} {(interior ? "cabin" : "outside")}: boost {engine.Boost:0.00}, the whine at {whineHz:0} Hz {whine:+0.0;-0.0} dB against the engine in its band; the turbo stands out most at {bestHz:0} Hz by {best:+0.0;-0.0} dB");
        Assert.True(whine >= 0, $"the turbo's whine is {-whine:0.0} dB under the engine in its band on boost");
    }
}
