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

    /// <summary>
    /// A control for the knock (diesel6 review): the bank's recording replaced by white noise, its marks, firing and
    /// segments kept. The grains' windows impose the firing's rhythm on anything they are cut from, so whatever the
    /// voice makes of this is what a steady hiss would score.
    /// </summary>
    private static GrainBank? Hiss(GrainBank? bank, int seed)
    {
        if (bank is null) return null;
        var random = new Random(seed);
        var x = new float[bank.Samples.Length];
        for (var i = 0; i < x.Length; i++) x[i] = (float)(0.2 * (random.NextDouble() * 2 - 1));
        return new GrainBank(x, bank.SampleRate, bank.Marks, bank.Firing, bank.Segment, evenTimbre: true);
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

    /// <summary>The 1-3 kHz band's envelope (as <see cref="Knock"/> follows it) at every sample of a recording.</summary>
    private static double[] KnockEnvelope(ReadOnlySpan<float> x)
    {
        Biquad h1 = default, h2 = default, l1 = default, l2 = default;
        h1.SetHighPass(1000f, 0.707f, Rate);
        h2.SetHighPass(1000f, 0.707f, Rate);
        l1.SetLowPass(3000f, 0.707f, Rate);
        l2.SetLowPass(3000f, 0.707f, Rate);
        var envelope = new double[x.Length];
        var follow = Math.Exp(-2 * Math.PI * 400 / Rate);
        double level = 0;
        for (var i = 0; i < x.Length; i++)
        {
            level = follow * level + (1 - follow) * Math.Abs(l2.Process(l1.Process(h2.Process(h1.Process(x[i])))));
            envelope[i] = level;
        }
        return envelope;
    }

    /// <summary>
    /// How strongly a bank's own recording knocks at each firing, before any grain is cut from it (diesel6 review): the
    /// 1-3 kHz envelope folded on the bank's marks, each pulse's interval mapped onto one period, its swing against its
    /// mean. Only intervals of 0.75 to 1.33 periods within one segment are folded (a missed pulse would fold two firings
    /// into one), and the first quarter of a second is left to the filters to settle.
    /// </summary>
    private static double KnockOnMarks(GrainBank bank)
    {
        var envelope = KnockEnvelope(bank.Samples);
        var bins = new double[32];
        var counts = new int[32];
        for (var k = 0; k + 1 < bank.Marks.Length; k++)
        {
            int from = bank.Marks[k], to = bank.Marks[k + 1];
            var periods = (to - from) * bank.Firing[k] / bank.SampleRate;
            if (bank.Segment[k] != bank.Segment[k + 1] || from < Rate / 4 || periods < 0.75f || periods > 1.33f) continue;
            for (var i = from; i < to; i++)
            {
                var bin = (int)((long)(i - from) * bins.Length / (to - from));
                bins[bin] += envelope[i];
                counts[bin]++;
            }
        }
        var folded = bins.Select((v, k) => v / Math.Max(counts[k], 1)).ToArray();
        return (folded.Max() - folded.Min()) / folded.Average();
    }

    /// <summary>
    /// The diesel's knock (diesel6): idling at its governor's throttle and at 1,500 rpm on a light throttle (0.3), where a
    /// diesel's rattle is plainest, the fleet saloon's 1-3 kHz carries a larger share of its sound than any petrol four's
    /// at the same throttle (the hot hatch, the rally hatch, the saloon and the estate, each at its own idle), by 6 dB idling
    /// and 3.5 dB at 1,500 rpm, and the band follows its firing (its envelope folded on the firing period swings by half its
    /// mean or more): it is the knock of each firing, not the recording's hiss. Measured: the diesel's share -9.3 dB idling
    /// and -10.8 at 1,500 rpm against the petrols' -17.5 (the saloon) to -29.3 and -15.1 (the estate) to -27.6; its band
    /// swings 0.97 and 0.60 (the petrols' 0.41 to 0.83: every engine's 1-3 kHz follows its firing). The fleet bank it
    /// replaced, the Punto's and the Bravo's petrol 1.4, gave -29.6 and -26.5. (Before the review's re-cut of the idle,
    /// -10.5 dB and 0.65 idling.)
    /// <para>
    /// Neither figure alone tells a knock from hiss (diesel6 review): white noise cut on the diesel's marks and played the
    /// same way scores -5.6 and -6.3 dB, a larger share than the recording's, and swings 0.40 and 0.46, as the grains'
    /// windows lay the firing's rhythm on anything. So the voice must also swing at least 0.1 more than that control, and
    /// the knock is measured where no grain window can make it, on the banks' own recordings folded on their marks: the
    /// diesel's idle and run-up must swing at least 0.15 more than white noise on the same marks and more than every
    /// petrol four's bank of the same kind. Measured there: the diesel's idle 0.67 and run-up 0.24, white noise 0.04 and
    /// 0.03, the petrol fours' idles 0.19 to 0.41 and run-ups 0.04 to 0.09.
    /// </para>
    /// <para>
    /// The petrol fours are the four-cylinder cars whose banks were recorded from a four (cars13): a car that plays another
    /// car's banks until its own land is a four's voice only if the banks' owner, the first car of the folder, is one. The
    /// Bravo 1.6 and the Leon Cupra, on the hot hatch's and the rally hatch's banks, are compared; the BRZ, on the V6
    /// hatch's, is not: the VR6's recording, which no four of the catalogue played before, carries -13.7 dB of its sound
    /// between 1 and 3 kHz idling at the BRZ's 700 rpm, a six's exhaust and not a knock.
    /// </para>
    /// </summary>
    [Fact]
    public void The_diesel_knocks_where_the_petrol_fours_do_not()
    {
        var diesel = VehicleSpec.Catalogue.Single(c => c.Engine.Governor);
        var petrols = VehicleSpec.Catalogue
            .Where(c => c.Engine.Cylinders == 4 && !c.Engine.Governor
                && VehicleSpec.Catalogue.First(owner => owner.Engine.SoundFolder == c.Engine.SoundFolder).Engine.Cylinders == 4)
            .ToArray();
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
            var folder = diesel.Engine.SoundFolder;
            var (rpm, load) = light ? (1500f, 0.3f) : (diesel.Engine.IdleRpm, 0.12f);
            var hissVoice = new GranularEngine(Hiss(Bank(folder, "engine_idle"), 1), Hiss(Bank(folder, "engine_drive"), 2)!, diesel.Engine.Cylinders,
                new EngineVoicing(EnginePlacement.Front, 1.9f), diesel.Engine.RedlineRpm, overrun: Hiss(Bank(folder, "engine_overrun"), 3));
            var hiss = Knock(Hold(hissVoice, rpm, load, 4f).AsSpan(Rate), rpm * diesel.Engine.Cylinders / 120f);
            output.WriteLine($"{folder} {what}: 1-3 kHz {knock.Share:0.0} dB of the sound, swinging {knock.Firing:0.00} with the firing (hiss cut on its marks {hiss.Share:0.0} dB, {hiss.Firing:0.00})");
            Assert.True(knock.Firing >= 0.5, $"the diesel's 1-3 kHz {what} does not follow its firing ({knock.Firing:0.00})");
            Assert.True(knock.Firing >= hiss.Firing + 0.1, $"the diesel's 1-3 kHz {what} swings {knock.Firing:0.00}, hardly more than hiss cut on its marks ({hiss.Firing:0.00})");
            foreach (var petrol in petrols)
            {
                var other = Heard(petrol);
                output.WriteLine($"  {petrol.Engine.SoundFolder}: {other.Share:0.0} dB, swinging {other.Firing:0.00}");
                Assert.True(knock.Share >= other.Share + margin, $"{what} the diesel's 1-3 kHz is {knock.Share - other.Share:0.0} dB over the {petrol.Engine.SoundFolder}'s");
            }
        }

        foreach (var name in new[] { "engine_idle", "engine_drive" })
        {
            var bank = Bank(diesel.Engine.SoundFolder, name)!;
            var swing = KnockOnMarks(bank);
            var hiss = KnockOnMarks(Hiss(bank, 4)!);
            output.WriteLine($"{diesel.Engine.SoundFolder}/{name} on its marks: swinging {swing:0.00}, hiss on the same marks {hiss:0.00}");
            Assert.True(swing >= hiss + 0.15, $"the diesel's {name} knocks on its marks hardly more than hiss ({swing:0.00} against {hiss:0.00})");
            foreach (var petrol in petrols)
            {
                if (Bank(petrol.Engine.SoundFolder, name) is not { } other) continue;
                var theirs = KnockOnMarks(other);
                output.WriteLine($"  {petrol.Engine.SoundFolder}/{name}: {theirs:0.00}");
                Assert.True(swing > theirs, $"the diesel's {name} knocks on its marks no more than the {petrol.Engine.SoundFolder}'s ({swing:0.00} against {theirs:0.00})");
            }
        }
    }

    /// <summary>
    /// The diesel idles without a steady hum (diesel6 review): both drives its banks are cut from carry a tone at 161-163 Hz
    /// that does not follow the revs (an auxiliary of the car's), the strongest line of their idles; cut into grains it
    /// played 7.1 dB over the voice's firing line outside and 8.9 in the cabin, chopped at the firing rate into a buzz. The
    /// idle is cut from copies notched at 159-166 Hz (tools/clean-recording.py), and idling at its governor's throttle the
    /// voice's 160-167 Hz is at least 6 dB under its firing line, outside and in the cabin. Measured: 10.4 and 8.6 dB under.
    /// </summary>
    [Fact]
    public void The_diesel_idles_without_a_steady_hum()
    {
        var diesel = VehicleSpec.Catalogue.Single(c => c.Engine.Governor);
        var fire = diesel.Engine.IdleRpm * diesel.Engine.Cylinders / 120f;
        foreach (var interior in new[] { false, true })
        {
            var voice = Voice(diesel);
            voice.Interior = interior;
            var x = Hold(voice, diesel.Engine.IdleRpm, 0.12f, 5f).AsSpan(Rate);
            var hum = Spectrum.BandDb(x, Rate, 160, 167, 32768) - Spectrum.BandDb(x, Rate, 0.95 * fire, 1.05 * fire, 32768);
            output.WriteLine($"{(interior ? "cabin" : "outside")}: 160-167 Hz {hum:+0.0;-0.0} dB against the firing line at {fire:0.0} Hz");
            Assert.True(hum <= -6, $"{(interior ? "in the cabin" : "outside")} the diesel's idle hums at 160-167 Hz, {hum:+0.0;-0.0} dB against its firing line");
        }
    }
}
