using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Audio.Tests;

/// <summary>
/// The body's synthesised noises (0.19), rendered offline and measured: wind as the square of the speed that takes over
/// from the recorded tyre roar near 110 km/h in the cabin, gear whine at the meshing frequencies and loudest in the low
/// gears, the anti-lock pump only while it works, and nothing that runs away. Set GRUDGELAP_AUDIO_DEMO to a folder to also
/// write listenable renders there.
/// </summary>
public class BodyNoiseTests(ITestOutputHelper output)
{
    private const int Rate = 22050;
    private static readonly string Audio = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "game", "assets", "audio"));

    private static float[] Render(BodyState state, float seconds = 4f)
    {
        var noise = new BodyNoise(Rate);
        var x = new float[(int)(seconds * Rate)];
        for (var at = 0; at < x.Length; at += 256) noise.Render(x.AsSpan(at, Math.Min(256, x.Length - at)), state);
        return x;
    }

    /// <summary>The last <paramref name="seconds"/> of a render, past the glide in.</summary>
    private static ReadOnlySpan<float> Tail(float[] x, float seconds = 3f) => x.AsSpan(x.Length - (int)(seconds * Rate));

    private static BodyState Wind(float speed, bool interior = true) => new(speed, interior, 0f, 0f, 0f, 0f, 0f, 0f);

    [Fact]
    public void Wind_grows_as_the_square_of_the_speed_and_brightens()
    {
        var levels = new[] { 10f, 20f, 40f }.Select(v => Render(Wind(v))).ToArray();
        var rms = levels.Select(x => Spectrum.Rms(Tail(x))).ToArray();
        var centroids = levels.Select(x => Spectrum.Centroid(Tail(x), Rate)).ToArray();
        output.WriteLine($"wind RMS {string.Join(", ", rms.Select(r => r.ToString("0.0000")))}; centroid {string.Join(", ", centroids.Select(c => c.ToString("0")))} Hz");
        // 12 dB for every doubling of the speed, within a dB.
        Assert.InRange(Spectrum.Db(rms[1] / rms[0]), 11, 13);
        Assert.InRange(Spectrum.Db(rms[2] / rms[1]), 11, 13);
        Assert.InRange(rms[1], BodyNoise.WindLevel(Wind(20f)) * 0.9, BodyNoise.WindLevel(Wind(20f)) * 1.1);
        Assert.True(centroids[0] < centroids[1] && centroids[1] < centroids[2], "the wind does not brighten with the speed");
        Assert.True(Spectrum.Rms(Tail(Render(Wind(20f, interior: false)))) < rms[1], "the wind is louder outside than in the cabin");
        Assert.Equal(0f, Render(Wind(0f)).Max(MathF.Abs));
    }

    /// <summary>
    /// In the cabin the tyres' roar dominates in town and the wind on the motorway: the recorded roar at the gain the
    /// tyre mix gives it against the rendered wind, both as they reach the bus (TyreAudio plays them at one volume).
    /// </summary>
    [Fact]
    public void The_wind_takes_over_from_the_tyres_roar_near_110_kmh()
    {
        var road = Loop("tyres/road.wav");
        var roadRms = Spectrum.Rms(road);
        double Roar(float speed)
        {
            var mix = new TyreMix();
            var wheel = TyreContact.Of(TyreGround.Asphalt, 0.1f, 0f, 0f, speed, 1f, 1f);
            var wheels = new[] { wheel, wheel, wheel, wheel };
            for (var t = 0f; t < 4f; t += 1f / 60f) mix.Update(wheels, 0f, 1f / 60f);
            return roadRms * mix.Roll.Level;
        }
        double WindAt(float speed) => Spectrum.Rms(Tail(Render(Wind(speed))));
        foreach (var kmh in new[] { 50f, 90f, 110f, 130f, 160f })
        {
            output.WriteLine($"{kmh} km/h: roar {Spectrum.Db(Roar(kmh / 3.6f)):0.0} dB, wind {Spectrum.Db(WindAt(kmh / 3.6f)):0.0} dB");
        }
        // The two cross between 90 and 130 km/h (the roar grows by 30 dB a decade, the wind by 40, so they part slowly).
        Assert.True(Roar(90f / 3.6f) > WindAt(90f / 3.6f), "the wind is louder than the tyres at 90 km/h");
        Assert.True(WindAt(130f / 3.6f) > Roar(130f / 3.6f), "the tyres still drown the wind at 130 km/h");
        Assert.True(Roar(50f / 3.6f) > WindAt(50f / 3.6f) * 1.4, "the wind is near the tyres in town");
        Assert.True(WindAt(160f / 3.6f) > Roar(160f / 3.6f) * 1.15, "the tyres are near the wind at 160 km/h");
    }

    /// <summary>
    /// First gear (3.14:1, the 1980s sports car's) with its output shaft at 20 rev/s: its pair meshes at 20 x 50 x
    /// 2.09 / 3.09 = 677 Hz, the constant-mesh pair at 20 x 3.14 x 20 = 1256 Hz. The whine falls gear by gear, a direct
    /// top gear barely whines, a straight-cut reverse whines loudest and neutral leaves only the final drive.
    /// </summary>
    [Fact]
    public void Gears_whine_at_their_meshing_frequencies_and_loudest_in_the_low_gears()
    {
        static BodyState Gear(float ratio, float torque = 1f) => new(0f, true, 0f, 0f, 0f, ratio, 20f, torque);
        var first = Render(Gear(3.14f));
        var pair = 20.0 * 50 * (3.14 / 1.5) / (1 + 3.14 / 1.5);
        var peak = Spectrum.PeakHz(Tail(first), Rate, 400, 1000);
        var mesh = Spectrum.PeakHz(Tail(first), Rate, 1100, 1500);
        output.WriteLine($"first gear: pair {peak:0.0} Hz (expected {pair:0.0}), constant mesh {mesh:0.0} Hz (expected 1256)");
        Assert.InRange(peak, pair * 0.995, pair * 1.005);
        Assert.InRange(mesh, 1256 * 0.995, 1256 * 1.005);

        var rms = new[] { 3.14f, 2.11f, 1.46f, 1.0f, -3.0f, 0f }.Select(r => Spectrum.Rms(Tail(Render(Gear(r))))).ToArray();
        output.WriteLine($"whine RMS 1st..4th, reverse, neutral: {string.Join(", ", rms.Select(r => r.ToString("0.00000")))}");
        Assert.True(rms[0] > rms[1] && rms[1] > rms[2], "the whine does not fall gear by gear");
        Assert.True(rms[3] < rms[2] * 0.5, "a direct top gear whines");
        Assert.True(rms[4] > rms[0] * 1.5, "the straight-cut reverse is not the loudest");
        Assert.True(rms[5] < rms[3], "neutral whines more than top gear");
        Assert.True(Spectrum.Rms(Tail(Render(Gear(3.14f, 0f)))) < rms[0] * 0.5, "the teeth whine as loud without torque");
    }

    [Fact]
    public void The_anti_lock_pump_growls_only_while_it_works_and_mostly_in_the_cabin()
    {
        var idle = Render(new BodyState(0f, true, 0f, 0f, 0f, 0f, 0f, 0f));
        var working = Render(new BodyState(0f, true, 0f, 1f, 1f, 0f, 0f, 0f));
        var outside = Render(new BodyState(0f, false, 0f, 1f, 1f, 0f, 0f, 0f));
        var pump = Spectrum.PeakHz(Tail(working), Rate, 30, 60);
        output.WriteLine($"pump RMS cabin {Spectrum.Rms(Tail(working)):0.0000}, outside {Spectrum.Rms(Tail(outside)):0.0000}, fundamental {pump:0.0} Hz");
        Assert.Equal(0f, idle.Max(MathF.Abs));
        Assert.True(Spectrum.Rms(Tail(working)) > 0.01);
        Assert.True(Spectrum.Rms(Tail(outside)) < Spectrum.Rms(Tail(working)) * 0.5);
        Assert.InRange(pump, 44, 46);
    }

    /// <summary>
    /// The roadside going by (0.47): the car's noise thrown back off what it passes; nothing with nothing beside the car,
    /// more for a bigger object, as the square of the speed, and nothing standing still.
    /// </summary>
    [Fact]
    public void The_roadside_echo_follows_the_speed_and_what_is_passed()
    {
        static BodyState Passing(float speed, float strength, float gap) => new(speed, false, 0f, 0f, 0f, 0f, 0f, 0f, strength, gap);
        var open = Spectrum.Rms(Tail(Render(Passing(30f, 0f, 2f))));
        var post = Spectrum.Rms(Tail(Render(Passing(30f, 0.35f, 1.5f))));
        var wall = Spectrum.Rms(Tail(Render(Passing(30f, 1.3f, 1.5f))));
        output.WriteLine($"RMS at 30 m/s: open {open:0.0000}, a post 1.5 m off {post:0.0000}, a wall {wall:0.0000}");
        Assert.True(post > open * 1.03, "a post going by adds nothing");
        Assert.True(wall > post, "a wall throws back no more than a post");
        Assert.InRange(BodyNoise.PassLevel(Passing(30f, 1f, 1.5f)) / BodyNoise.PassLevel(Passing(15f, 1f, 1.5f)), 3.9, 4.1);
        Assert.Equal(0f, Render(Passing(0f, 1f, 1.5f)).Max(MathF.Abs));
        var x = Render(Passing(55f, 1.5f, 0.5f), 2f);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        Assert.True(x.Max(MathF.Abs) < 0.95f, $"peak {x.Max(MathF.Abs):0.00}");
    }

    [Fact]
    public void Nothing_runs_away_at_the_extremes()
    {
        var x = Render(new BodyState(95f, true, 2f, 1f, 1f, -3.1f, 70f, 1f), 3f);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        Assert.True(x.Max(MathF.Abs) < 0.95f, $"peak {x.Max(MathF.Abs):0.00}");
        // A stop from speed to rest in a block glides: no step between samples louder than the noise itself.
        var noise = new BodyNoise(Rate);
        var block = new float[256];
        noise.Render(block, new BodyState(50f, true, 1f, 0f, 0f, 3.14f, 40f, 1f));
        Array.Clear(block);
        noise.Render(block, new BodyState(0f, true, 0f, 0f, 0f, 0f, 0f, 0f));
        Assert.True(MathF.Abs(block[^1]) < 0.05f);

        var demo = Environment.GetEnvironmentVariable("GRUDGELAP_AUDIO_DEMO");
        if (string.IsNullOrEmpty(demo)) return;
        Directory.CreateDirectory(demo);
        Wav.WritePcm16(Path.Combine(demo, "wind_30.wav"), Render(Wind(30f)), Rate);
        Wav.WritePcm16(Path.Combine(demo, "wind_60.wav"), Render(Wind(60f)), Rate);
        Wav.WritePcm16(Path.Combine(demo, "whine_first.wav"), Render(new BodyState(10f, true, 0f, 0f, 0f, 3.14f, 15f, 1f)), Rate);
        Wav.WritePcm16(Path.Combine(demo, "whine_reverse.wav"), Render(new BodyState(3f, true, 0f, 0f, 0f, -3f, 6f, 0.6f)), Rate);
        Wav.WritePcm16(Path.Combine(demo, "abs_pump.wav"), Render(new BodyState(15f, true, 0f, 1f, 0.5f, 0f, 0f, 0f)), Rate);
        Wav.WritePcm16(Path.Combine(demo, "spray_30.wav"), Render(new BodyState(30f, true, 1f, 0f, 0f, 0f, 0f, 0f)), Rate);
    }

    private static float[] Loop(string file)
    {
        var pcm = Wav.ReadPcm16(Path.Combine(Audio, file));
        var x = new float[pcm.Length / 2];
        for (var i = 0; i < x.Length; i++) x[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
        return x;
    }
}
