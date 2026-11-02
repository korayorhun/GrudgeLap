using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Audio.Tests;

/// <summary>
/// The fleet saloon's diesel sounds like one (diesel6): its banks are a TDI's engine bay (SOURCES.md, "diesel4"), and the
/// combustion's knock, the hard rattle of a compression-ignition pressure rise, is in them where the petrol fours have
/// none. Measured on the voice as the game plays it.
/// </summary>
public class DieselCharacterTests(ITestOutputHelper output)
{
    private const int Rate = 44100;
    private static readonly string Audio = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "game", "assets", "audio"));

    private static GrainBank? Bank(string car, string name)
    {
        var wav = Path.Combine(Audio, car, name + ".wav");
        return File.Exists(wav) ? GrainBank.FromFiles(wav, Path.ChangeExtension(wav, ".json")) : null;
    }

    /// <summary>The voice of a car of the catalogue, outside, voiced as the game voices it (SampledEngine.VoicingOf).</summary>
    private static GranularEngine Voice(VehicleSpec car)
    {
        var folder = car.Engine.SoundFolder;
        var voicing = car.Style switch
        {
            CarStyle.Saloon or CarStyle.Fleet => new EngineVoicing(EnginePlacement.Front, 1.9f),
            CarStyle.Estate => new EngineVoicing(EnginePlacement.Front, 2.8f),
            _ => new EngineVoicing(EnginePlacement.Front, 2.3f),
        };
        return new GranularEngine(Bank(folder, "engine_idle"), Bank(folder, "engine_drive")!, car.Engine.Cylinders, voicing, car.Engine.RedlineRpm, overrun: Bank(folder, "engine_overrun"));
    }

    private static float[] Hold(GranularEngine engine, float rpm, float load, float seconds)
    {
        var x = new float[(int)(seconds * engine.SampleRate)];
        for (var at = 0; at < x.Length; at += 512) engine.Render(x.AsSpan(at, Math.Min(512, x.Length - at)), rpm, load, true);
        return x;
    }

    /// <summary>
    /// The 1-3 kHz band, where a diesel's knock is loudest: its share of the sound (dB) and how strongly its envelope
    /// follows the firing (the envelope folded on the firing period, its swing against its mean): a knock at each firing
    /// swings it, a steady hiss does not.
    /// </summary>
    private static (double Share, double Firing) Knock(ReadOnlySpan<float> x, float firingHz)
    {
        Biquad h1 = default, h2 = default, l1 = default, l2 = default;
        h1.SetHighPass(1000f, 0.707f, Rate);
        h2.SetHighPass(1000f, 0.707f, Rate);
        l1.SetLowPass(3000f, 0.707f, Rate);
        l2.SetLowPass(3000f, 0.707f, Rate);
        var bins = new double[64];
        var counts = new int[64];
        double envelope = 0, band = 0, all = 0;
        var follow = Math.Exp(-2 * Math.PI * 400 / Rate);
        for (var i = 0; i < x.Length; i++)
        {
            var y = l2.Process(l1.Process(h2.Process(h1.Process(x[i]))));
            envelope = follow * envelope + (1 - follow) * Math.Abs(y);
            band += y * y;
            all += x[i] * x[i];
            // The filters settle over the first quarter of a second.
            if (i < Rate / 4) continue;
            var phase = (double)i * firingHz / Rate;
            var bin = (int)((phase - Math.Floor(phase)) * bins.Length);
            bins[bin] += envelope;
            counts[bin]++;
        }
        var folded = bins.Select((v, k) => v / Math.Max(counts[k], 1)).ToArray();
        return (10 * Math.Log10(band / all), (folded.Max() - folded.Min()) / folded.Average());
    }

    /// <summary>
    /// The diesel's knock (diesel6): idling at its governor's throttle and at 1,500 rpm on a light throttle (0.3), where a
    /// diesel's rattle is plainest, the fleet saloon's 1-3 kHz carries a larger share of its sound than any petrol four's
    /// at the same throttle (the hot hatch, the rally hatch, the saloon and the estate, each at its own idle), by 6 dB idling
    /// and 3.5 dB at 1,500 rpm, and the band follows its firing (its envelope folded on the firing period swings by half its
    /// mean or more): it is the knock of each firing, not the recording's hiss. Measured: the diesel's share -10.5 dB idling
    /// and -10.8 at 1,500 rpm against the petrols' -17.5 (the saloon) to -29.3 and -15.1 (the estate) to -27.6; its band
    /// swings 0.65 and 0.58 (the petrols' 0.41 to 0.83: every engine's 1-3 kHz follows its firing). The fleet bank it replaced, the Punto's and the Bravo's petrol 1.4, gave -29.6 and -26.5.
    /// </summary>
    [Fact]
    public void The_diesel_knocks_where_the_petrol_fours_do_not()
    {
        var diesel = VehicleSpec.Catalogue.Single(c => c.Engine.Governor);
        var petrols = VehicleSpec.Catalogue.Where(c => c.Engine.Cylinders == 4 && !c.Engine.Governor).ToArray();
        Assert.NotEmpty(petrols);
        foreach (var (light, margin) in new[] { (false, 6.0), (true, 3.5) })
        {
            (double Share, double Firing) Heard(VehicleSpec car)
            {
                var (rpm, load) = light ? (1500f, 0.3f) : (car.Engine.IdleRpm, 0.12f);
                return Knock(Hold(Voice(car), rpm, load, 4f).AsSpan(Rate), rpm * car.Engine.Cylinders / 120f);
            }
            var knock = Heard(diesel);
            var what = light ? "at 1500 rpm on a light throttle" : "idling";
            output.WriteLine($"{diesel.Engine.SoundFolder} {what}: 1-3 kHz {knock.Share:0.0} dB of the sound, swinging {knock.Firing:0.00} with the firing");
            Assert.True(knock.Firing >= 0.5, $"the diesel's 1-3 kHz {what} does not follow its firing ({knock.Firing:0.00})");
            foreach (var petrol in petrols)
            {
                var other = Heard(petrol);
                output.WriteLine($"  {petrol.Engine.SoundFolder}: {other.Share:0.0} dB, swinging {other.Firing:0.00}");
                Assert.True(knock.Share >= other.Share + margin, $"{what} the diesel's 1-3 kHz is {knock.Share - other.Share:0.0} dB over the {petrol.Engine.SoundFolder}'s");
            }
        }
    }
}
