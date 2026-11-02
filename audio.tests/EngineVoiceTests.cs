using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Audio.Tests;

/// <summary>
/// The engine's voice as the game drives it (0.20): from the simulated engine through <see cref="EngineFollower"/> to
/// <see cref="GranularEngine"/>, voiced per car (<see cref="EngineVoicing"/>), trimmed to one loudness, stuttering on the
/// limiter, its banks evened across their segments, and still heard over the tyres and the wind at top speed.
/// </summary>
public class EngineVoiceTests(ITestOutputHelper output)
{
    private const int Rate = 44100;
    private static readonly string Audio = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "game", "assets", "audio"));

    /// <summary>
    /// The cars as the game voices them (SampledEngine.VoicingOf): sound folder and catalogue index, taken from the
    /// catalogue itself. Written out by index, the table went stale when the hatchbacks were respecified (cars3: the
    /// turbo four moved from index 4 to 3 with the Delta, and index 4 became the V6 Golf).
    /// </summary>
    public static readonly TheoryData<string, int> Cars = CarsOfCatalogue();

    private static TheoryData<string, int> CarsOfCatalogue()
    {
        var cars = new TheoryData<string, int>();
        for (var i = 0; i < VehicleSpec.Catalogue.Length; i++) cars.Add(VehicleSpec.Catalogue[i].Engine.SoundFolder, i);
        return cars;
    }

    /// <summary>The catalogue's index of the car whose engine sounds from <paramref name="folder"/>.</summary>
    private static int IndexOf(string folder)
    {
        var index = Array.FindIndex(VehicleSpec.Catalogue, c => c.Engine.SoundFolder == folder);
        Assert.True(index >= 0, $"no car sounds from {folder}");
        return index;
    }

    private static EngineVoicing VoicingOf(string car) => car switch
    {
        "flat12" or "v8" => new EngineVoicing(EnginePlacement.Mid, 1.3f, TailpipeRadius: 0.035f),
        "saloon" or "fleet" => new EngineVoicing(EnginePlacement.Front, 1.9f),
        "estate" => new EngineVoicing(EnginePlacement.Front, 2.8f),
        _ => new EngineVoicing(EnginePlacement.Front, 2.3f),
    };

    private static GrainBank? Bank(string car, string name, bool even = true)
    {
        var wav = Path.Combine(Audio, car, name + ".wav");
        return File.Exists(wav) ? GrainBank.FromFiles(wav, Path.ChangeExtension(wav, ".json"), even) : null;
    }

    private static GranularEngine Voice(string car, int index, bool interior = false, int seed = 7) =>
        new(Bank(car, "engine_idle"), Bank(car, "engine_drive")!, VehicleSpec.Catalogue[index].Engine.Cylinders, VoicingOf(car), VehicleSpec.Catalogue[index].Engine.RedlineRpm, seed, Bank(car, "engine_overrun"))
        {
            Interior = interior,
        };

    private static float[] Hold(GranularEngine engine, float rpm, float load, float seconds)
    {
        var x = new float[(int)(seconds * engine.SampleRate)];
        for (var at = 0; at < x.Length; at += 512) engine.Render(x.AsSpan(at, Math.Min(512, x.Length - at)), rpm, load, true);
        return x;
    }

    /// <summary>The loudness of the last second of a hold.</summary>
    private static double HeldLufs(GranularEngine engine, float rpm, float load) => Loudness.Lufs(Hold(engine, rpm, load, 2f).AsSpan(Rate), Rate);

    /// <summary>
    /// The engine's load reaches the voice as the engine's throttle (finding 1 of the 0.19 audit, pinned through the
    /// sound's own path): every engine held at full throttle at 60 % of its redline gives the follower a load of 1 and
    /// the voice its full-throttle sound, where the torque over 500 N m that 0.18 used would have played the small cars
    /// several decibels down, mostly on their overrun recording.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Full_throttle_reaches_the_voice_as_full_load_on_every_engine(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        var engine = new EngineModel(spec);
        engine.Start();
        var follower = new EngineFollower(spec.IdleRpm);
        var rpm = spec.RedlineRpm * 0.6f;
        const float dt = 1f / 60f;
        for (var t = 0f; t < 0.6f; t += dt)
        {
            engine.AngularVelocity = rpm / EngineModel.RpmPerRadPerSec;
            engine.Torque(1f);
            engine.Tick(dt);
            follower.Update(engine.Rpm, engine.Throttle, engine.LimiterCut, dt);
        }
        Assert.InRange(follower.Load, 0.99f, 1f);
        Assert.False(follower.Cut);
        // All three at the follower's revs, so that only the load differs: the follower is still a rev or two short of
        // the engine after 0.6 s, and a rev apart the voice strings other grains together, whose level varies by more than
        // the 0.3 LU compared here (the rally hatch's holds a rev apart within 40 rpm of 4,080: over 0.8 LU, 3.3 before
        // its run-up was levelled grain by grain).
        var heard = HeldLufs(Voice(car, index), follower.Rpm, follower.Load);
        var full = HeldLufs(Voice(car, index), follower.Rpm, 1f);
        var byTorque = HeldLufs(Voice(car, index), follower.Rpm, Math.Clamp(engine.LastTorque / 500f, 0f, 1f));
        output.WriteLine($"{car}: {heard:0.0} LUFS at the follower's load, {full:0.0} at full load, {byTorque:0.0} at the torque over 500 N m ({engine.LastTorque / 500f:0.00})");
        Assert.InRange(heard, full - 0.3, full + 0.3);
        // The old load would have played every car under its torque of 500 N m quieter by at least what the voice's level
        // law gives it (CutLevel at a shut throttle, rising with the load), the throttle's filter and below 0.4 the overrun
        // taking more: 7.5 dB at a load of 0, 2.6 at the rally hatch's 0.55. Each car is held to its own figure (a cut-off
        // by torque held the rally hatch, 2.7 dB down, to the 3 dB meant for the loads under a half).
        var oldLoad = Math.Clamp(engine.LastTorque / 500f, 0f, 1f);
        var law = 20 * Math.Log10(GranularEngine.CutLevel + (1f - GranularEngine.CutLevel) * oldLoad);
        Assert.True(byTorque < full + law + 0.3, $"the old load ({oldLoad:0.00}) is {full - byTorque:0.0} dB down, the level law {-law:0.0}");
    }

    /// <summary>
    /// The idle the simulation settles at: the engine alone in neutral, its idle governor opening the throttle against
    /// its friction, gives the voice a load of about a tenth, so the idle is the idle bank at a closed throttle's level:
    /// within 3 dB of the same voice at the 0.18 load (the torque over 500 N m, a few hundredths; it is 1.1 to 1.9 dB
    /// louder now) and at least 6 dB under the engine pulling at 60 % of its redline.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void The_idle_governors_throttle_keeps_a_quiet_idle(string car, int index)
    {
        var car0 = VehicleSpec.Catalogue[index];
        var spec = car0.Engine;
        var engine = new EngineModel(spec);
        engine.Start();
        var follower = new EngineFollower(spec.IdleRpm);
        const float dt = 1f / 240f;
        for (var t = 0f; t < 4f; t += dt)
        {
            engine.AngularVelocity += engine.Torque(0f) / car0.EngineInertia * dt;
            engine.Tick(dt);
            follower.Update(engine.Rpm, engine.Throttle, engine.LimiterCut, dt);
        }
        Assert.True(engine.Running, "the engine stalled at idle");
        Assert.InRange(follower.Rpm, spec.IdleRpm - 150f, spec.IdleRpm + 150f);
        var idle = HeldLufs(Voice(car, index), follower.Rpm, follower.Load);
        var old = HeldLufs(Voice(car, index), follower.Rpm, Math.Clamp(engine.LastTorque / 500f, 0f, 1f));
        var pulling = HeldLufs(Voice(car, index), spec.RedlineRpm * 0.6f, 1f);
        output.WriteLine($"{car}: idles at {follower.Rpm:0} rpm, load {follower.Load:0.000}: {idle:0.0} LUFS (0.18: {old:0.0}), pulling at {spec.RedlineRpm * 0.6f:0} rpm {pulling:0.0}");
        Assert.InRange(follower.Load, 0.02f, 0.3f);
        Assert.InRange(idle - old, -0.5, 3);
        Assert.True(idle < pulling - 6, "the idle is nearly as loud as the engine pulling");
    }

    /// <summary>
    /// Pulling hard the voice is a tone, the firing frequency and its harmonics, and not a noise between them, all the
    /// way to the redline. Past the top of the V6 hatch's re-cut run-up (3,600 rpm, its redline 6,600) every pulse was a
    /// pick at random among 32 from four blips of the throttle and several of them sounded at every sample: pulling at
    /// 90 % of the redline the harmonics held a sixth of the sound (-7.7 dB), where the run-up of 0.20 had held 0.6
    /// (-2.2 dB), and the strongest peak still sat at the firing frequency. Held at full throttle from half the redline
    /// to the redline, outside: within 3 % of the first six harmonics lies at least a third of the sound between half and
    /// six and a half times the firing frequency (measured: at least 0.43, -3.7 dB, the hot hatch at 60 and 80 % of its
    /// redline; the V6 hatch 0.44, -3.6 dB, at 90 and 100 %, and 0.32, -5.0 dB, at 80 % before its fix).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Pulls_as_a_tone_up_to_the_redline(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        var voice = Voice(car, index);
        const int n = 32768;
        var shares = new List<string>();
        foreach (var part in new[] { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f })
        {
            var firing = part * spec.RedlineRpm * spec.Cylinders / 120.0;
            var power = Spectrum.Power(Hold(voice, part * spec.RedlineRpm, 1f, 4f).AsSpan(Rate), n);
            double Band(double lo, double hi)
            {
                var sum = 0.0;
                for (var k = (int)Math.Ceiling(lo * n / Rate); k <= (int)(hi * n / Rate) && k < power.Length; k++) sum += power[k];
                return sum;
            }
            var harmonics = 0.0;
            for (var h = 1; h <= 6; h++) harmonics += Band(h * firing * 0.97, h * firing * 1.03);
            var tone = 10 * Math.Log10(harmonics / Band(0.5 * firing, 6.5 * firing));
            shares.Add($"{part * 100:0} % {tone:0.0} dB");
            Assert.True(tone >= -4.77, $"{car} at {part * spec.RedlineRpm:0} rpm: its harmonics hold {tone:0.0} dB of the sound");
        }
        output.WriteLine($"{car}: the harmonics' share of the sound pulling at " + string.Join(", ", shares));
    }

    /// <summary>
    /// The limiter stutters (audit finding 12): with the cut set for 60 ms in every 100, as the simulation's limiter does
    /// at the redline, the voice's level, averaged over a hundred cycles in step with them, drops at least 6 dB and falls
    /// within a few milliseconds of each cut (a firing period or two and the 2 ms blocks it is measured in); the same cut
    /// followed through the load, as in 0.19, swung by less and took tens of milliseconds to fall, or barely dipped at all
    /// (a quarter of the crisp swing or less, whose fall cannot be timed: the estate's, 0.7 dB). Over twenty cycles the
    /// grains a run happened to pick moved the swing by a decibel or two and the fall by up to 20 ms either way (eight
    /// seeds of the rally hatch: its crisp cut fell in 6 to 26 ms; the V8 of 0.20 swung 6.6 dB over twenty and 5.3 over
    /// a hundred), and any change to the picks passed or failed one of the three cars.
    /// </summary>
    [Theory]
    [InlineData("turbohatch")]
    [InlineData("v8")]
    [InlineData("estate")]
    public void The_limiter_cuts_crisply(string car)
    {
        var index = IndexOf(car);
        var redline = VehicleSpec.Catalogue[index].Engine.RedlineRpm;
        var (depth, edge) = Swing(Stutter(Voice(car, index), redline, crisp: true));
        var (blurredDepth, blurredEdge) = Swing(Stutter(Voice(car, index), redline, crisp: false));
        output.WriteLine($"{car}: the cut swings {depth:0.0} dB and falls in {edge:0} ms; through the load {blurredDepth:0.0} dB in {blurredEdge:0} ms");
        Assert.True(depth > 6, $"the cut swings only {depth:0.0} dB");
        Assert.True(edge <= 10, $"the cut takes {edge:0} ms to fall");
        // A swing of under a decibel has no fall to time: the band between its quarter and three quarters is a third of a
        // decibel, within the cycle's own ripple (the estate's re-cut banks, banks: through the load 0.7 dB, timed at 6 or
        // 24 ms as the grains fell). Such a cut is not crisper either; it hardly dips.
        Assert.True(blurredEdge > 2 * edge || blurredDepth < depth / 4, "the crisp cut falls no faster than the blurred one");
    }

    /// <summary>
    /// The limiter as the simulation runs it (0.20 review, problem 1): the engine held flat out on its limiter, stepped at
    /// the physics' 240 Hz and read at 60 frames a second as the game reads it, free in neutral (its own flywheel) and
    /// with a car's inertia behind it (in gear, about 3 kg m^2 at the crank). The follower's held load stays at 0.9 or
    /// more through every cut: the cut and the shut throttle are reported for the same step, so the step that ends a cut
    /// does not pass for the driver lifting. Read from the cut's countdown, the load sagged to 0.4-0.7 every few frames (a
    /// dip of about 2 dB with the throttle's filter closing from 12 to about 7 kHz: 0.19's blurred dip back again).
    /// </summary>
    [Theory]
    [InlineData("turbohatch", false)]
    [InlineData("turbohatch", true)]
    [InlineData("v6", false)]
    [InlineData("v6", true)]
    [InlineData("v8", false)]
    [InlineData("v8", true)]
    [InlineData("estate", false)]
    [InlineData("estate", true)]
    public void The_simulated_limiter_holds_the_load_through_its_cuts(string folder, bool inGear)
    {
        var car = VehicleSpec.Catalogue[IndexOf(folder)];
        var spec = car.Engine;
        var engine = new EngineModel(spec);
        engine.Start();
        engine.AngularVelocity = 0.95f * spec.RedlineRpm / EngineModel.RpmPerRadPerSec;
        var inertia = car.EngineInertia + (inGear ? 3f : 0f);
        var follower = new EngineFollower(spec.RedlineRpm);
        follower.Update(spec.RedlineRpm, 1f, false, 1f);
        const float step = VehicleSim.FixedStep;
        var lowest = 1f;
        var cuts = 0;
        for (var frame = 0; frame < 180; frame++)
        {
            for (var k = 0; k < 4; k++)
            {
                engine.AngularVelocity += engine.Torque(1f) / inertia * step;
                engine.Tick(step);
            }
            follower.Update(engine.Rpm, engine.Throttle, engine.LimiterCut, 4 * step);
            if (follower.Cut) cuts++;
            if (frame >= 30) lowest = MathF.Min(lowest, follower.Load);
        }
        output.WriteLine($"{spec.SoundFolder} {(inGear ? "in gear" : "in neutral")}: {cuts} of 180 frames cut, the load at least {lowest:0.000}");
        Assert.True(cuts > 10, "the engine never reached its limiter");
        Assert.True(lowest >= 0.9f, $"the held load sags to {lowest:0.00} on the limiter");
    }

    /// <summary>
    /// Ten seconds on the limiter, cut 60 ms in every 100, rendered in 2 ms blocks (the load through an
    /// <see cref="EngineFollower"/> at 60 frames a second, as the game feeds it), and the mean power of each 2 ms of the
    /// cycle over its hundred cycles, in dB.
    /// </summary>
    private static double[] Stutter(GranularEngine voice, float redline, bool crisp)
    {
        Hold(voice, redline, 1f, 1f);
        var follower = new EngineFollower(redline);
        follower.Update(redline, 1f, false, 1f);
        var block = new float[88];
        var power = new double[50];
        var frame = 0f;
        const int cycles = 100;
        for (var step = 0; step < cycles * 50; step++)
        {
            var cut = step % 50 < 30;
            frame += 0.002f;
            if (frame >= 1f / 60f)
            {
                // Crisp: the game's path, the cut passed on and the load held. Blurred: 0.19's, the cut seen only as the throttle shut.
                follower.Update(redline, cut && !crisp ? 0f : 1f, crisp && cut, frame);
                frame = 0f;
            }
            voice.Cut = crisp && cut;
            voice.Render(block, redline, crisp ? 1f : follower.Load, true);
            power[step % 50] += Spectrum.Rms(block) * Spectrum.Rms(block);
        }
        return power.Select(p => 10 * Math.Log10(p / cycles + 1e-20)).ToArray();
    }

    /// <summary>
    /// The swing of a cycle's level (dB) and how long its fall takes from three quarters of the swing to a quarter (ms):
    /// the longest run of the cycle's 2 ms steps between those two levels on the way down.
    /// </summary>
    private static (double Depth, double EdgeMs) Swing(double[] cycle)
    {
        var high = cycle.Max();
        var low = cycle.Min();
        var upper = low + 0.75 * (high - low);
        var lower = low + 0.25 * (high - low);
        var start = Array.IndexOf(cycle, high);
        var fall = 0;
        var falling = false;
        for (var k = 1; k < cycle.Length; k++)
        {
            var v = cycle[(start + k) % cycle.Length];
            if (!falling && v < upper) falling = true;
            if (falling && v < lower) break;
            if (falling) fall++;
        }
        return (high - low, (fall + 1) * 2.0);
    }

    /// <summary>
    /// Every engine is about as loud at comparable driving, outside and in the cabin: run up at full throttle from 40 to
    /// 95 % of its redline over four seconds, the revs gliding as in a gear (not the trim's own steady holds at four revs,
    /// so the test does not just read back the trim; the cap on its peaks may also hold a car under it), every car is
    /// within 1.5 LU of the cars' mean, where in 0.19 the estate was some 5 LU over the flat-twelve. A big engine still
    /// differs, in its sound (lower firing, the mid engine's cabin), not its level.
    /// </summary>
    [Fact]
    public void Every_engine_is_about_as_loud_pulling_hard()
    {
        foreach (var interior in new[] { false, true })
        {
            var levels = new Dictionary<string, double>();
            foreach (var row in Cars)
            {
                var (car, index) = ((string)row[0], (int)row[1]);
                var redline = VehicleSpec.Catalogue[index].Engine.RedlineRpm;
                var voice = Voice(car, index, interior);
                Hold(voice, 0.4f * redline, 1f, 0.5f);
                var x = new float[4 * Rate];
                for (var at = 0; at < x.Length; at += 512)
                {
                    var rpm = redline * (0.4f + 0.55f * (at + 512f) / x.Length);
                    voice.Render(x.AsSpan(at, Math.Min(512, x.Length - at)), rpm, 1f, true);
                }
                levels[car] = Loudness.Lufs(x, Rate);
            }
            var mean = levels.Values.Average();
            output.WriteLine($"{(interior ? "cabin" : "outside")}: {string.Join(", ", levels.Select(l => $"{l.Key} {l.Value:0.0}"))} LUFS");
            Assert.All(levels, l => Assert.InRange(l.Value, mean - 1.5, mean + 1.5));
        }
    }

    /// <summary>
    /// No engine peaks over -1 dBFS at full throttle (0.20 review, problem 2): held from 30 % of its redline to the
    /// redline in steps of 2 %, at the game's volume of 0.9, outside and in the cabin. Trimmed to the reference alone the
    /// fleet saloon peaked at +2.8 dBFS, the V8 at +1.2 and the turbo hatch at +1.1, and the mix's limiter ducked the
    /// whole mix with them. The trims are held under <see cref="GranularEngine.PeakCeiling"/> by their own renders' peaks;
    /// what those miss is rounded off above <see cref="GranularEngine.PeakKnee"/>, which must stay rare: at most one
    /// sample in a thousand (measured: at most 18 in 952,560, the saloon's cabin, and none at all on five of the cars:
    /// the flat-twelve, the V8, the hot hatch, the rally hatch and the V6 hatch).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Full_throttle_peaks_under_minus_one_dbfs(string car, int index)
    {
        var redline = VehicleSpec.Catalogue[index].Engine.RedlineRpm;
        foreach (var interior in new[] { false, true })
        {
            var voice = Voice(car, index, interior);
            voice.Volume = 0.9f;
            var (peak, at) = (0f, 0f);
            long rounded = 0, count = 0;
            for (var share = 0.3f; share <= 1.001f; share += 0.02f)
            {
                var x = Hold(voice, share * redline, 1f, 0.8f).AsSpan(Rate / 5);
                foreach (var s in x)
                {
                    var magnitude = MathF.Abs(s);
                    if (magnitude > peak) (peak, at) = (magnitude, share * redline);
                    if (magnitude > GranularEngine.PeakKnee) rounded++;
                }
                count += x.Length;
            }
            output.WriteLine($"{car} {(interior ? "cabin" : "outside")}: peaks at {20 * Math.Log10(peak):+0.0;-0.0} dBFS ({at:0} rpm), {rounded} of {count} samples rounded off");
            Assert.True(20 * Math.Log10(peak) <= -1.0, $"{car} peaks at {20 * Math.Log10(peak):+0.0;-0.0} dBFS");
            Assert.True(rounded <= count / 1000, $"{car}: {rounded} of {count} samples rounded off");
        }
    }

    /// <summary>
    /// Each bank's segments (recorded apart) are evened onto one trend of brightness and level across the revs (audit
    /// finding 9): after evening no segment's grains sit more than 1.5 dB off the trend unless its correction reached its
    /// limit, none is moved away from it, and the bank peaks no higher than its raw recording but for its rarest samples.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Segments_are_evened_across_the_revs(string car, int index)
    {
        _ = index;
        foreach (var name in new[] { "engine_idle", "engine_drive", "engine_overrun" })
        {
            var raw = Bank(car, name, even: false);
            if (raw is null) continue;
            var even = Bank(car, name)!;
            var brightness = raw.BrightnessTrend();
            foreach (var s in raw.Segment.Distinct())
            {
                if (!even.Evened.TryGetValue(s, out var done)) continue;
                var before = raw.SegmentOffset(s, raw.Brightness, brightness);
                var after = even.SegmentOffset(s, even.Brightness, brightness);
                output.WriteLine($"{car} {name} segment {s}: brightness {before:+0.0;-0.0} -> {after:+0.0;-0.0} dB (tilt {done.Tilt:+0.0;-0.0}, gain {done.Gain:+0.0;-0.0})");
                if (MathF.Abs(done.Tilt) < 5.9f) Assert.InRange(after, -1.5f, 1.5f);
                Assert.True(MathF.Abs(after) <= MathF.Abs(before) + 0.1f, "a segment was moved away from the trend");
            }
            // No segment's peaks are raised past the raw recording's but for the rarest samples, a click (PeakMeter): the
            // fleet saloon's Bravo segment, tilted and raised whole, reached 1.49 against 0.99.
            var rawPeak = raw.Samples.Max(MathF.Abs);
            var evenPeak = PeakMeter.Of(even.Samples);
            output.WriteLine($"{car} {name}: peaks at {rawPeak:0.00} raw, {evenPeak:0.00} evened but for the rarest samples ({even.Samples.Max(MathF.Abs):0.00} with them)");
            Assert.True(evenPeak <= rawPeak + 0.002f, $"{car} {name} peaks at {evenPeak:0.00} evened against {rawPeak:0.00} raw");
        }
    }

    /// <summary>
    /// Rendered across the revs at full throttle, the voice's timbre (its energy above 1 kHz against below) follows the
    /// revs more evenly once its banks are evened, on the two engines whose run-ups join the most different segments:
    /// the spread of the timbre about its trend falls from 2.3 to 1.0 dB on the rally hatch's turbo four (in 0.19 brighter
    /// at 3350 rpm than at 4700, most of that a compressor's flutter in one of its recordings, cut out of the bank since)
    /// and from 3.0 to 2.0 dB on the fleet saloon (the Punto's run-up joined to the Bravo's). On the
    /// others it moves by a tenth of a decibel or less (the V8's spread is its recordings' own, not their joins').
    /// </summary>
    [Theory]
    [InlineData("turbohatch")]
    [InlineData("fleet")]
    public void The_timbre_follows_the_revs_more_evenly(string car)
    {
        double Spread(bool even)
        {
            var spec = VehicleSpec.Catalogue[IndexOf(car)].Engine;
            var voice = new GranularEngine(Bank(car, "engine_idle", even), Bank(car, "engine_drive", even)!, spec.Cylinders, VoicingOf(car));
            var revs = new List<double>();
            var timbre = new List<double>();
            for (var rpm = spec.RedlineRpm * 0.35f; rpm <= spec.RedlineRpm * 0.95f; rpm += spec.RedlineRpm * 0.02f)
            {
                var x = Hold(voice, rpm, 1f, 0.5f).AsSpan(Rate / 10);
                revs.Add(Math.Log2(rpm));
                timbre.Add(Spectrum.BandDb(x, Rate, 1000, 16000, 2048) - Spectrum.BandDb(x, Rate, 20, 1000, 2048));
            }
            var meanRevs = revs.Average();
            var meanTimbre = timbre.Average();
            var slope = revs.Zip(timbre).Sum(p => (p.First - meanRevs) * (p.Second - meanTimbre)) / revs.Sum(r => (r - meanRevs) * (r - meanRevs));
            return Math.Sqrt(revs.Zip(timbre).Average(p => Math.Pow(p.Second - meanTimbre - slope * (p.First - meanRevs), 2)));
        }
        var before = Spread(false);
        var after = Spread(true);
        output.WriteLine($"{car}: the timbre spreads {before:0.00} dB about its trend raw, {after:0.00} dB evened");
        Assert.True(after < before, "evening the banks did not even the timbre");
    }

    /// <summary>
    /// The voicing: a mid engine's cabin passes more of the engine's top and of its intake than a front engine's, every
    /// cabin booms at its own length's mode, and from behind the car the tailpipe's top end is louder than in the cabin.
    /// </summary>
    [Fact]
    public void The_cabin_hears_a_mid_engine_brighter_than_a_front_one()
    {
        double TopShare(EngineVoicing voicing, bool interior)
        {
            var voice = new GranularEngine(Bank("estate", "engine_idle"), Bank("estate", "engine_drive")!, 4, voicing) { Interior = interior };
            var x = Hold(voice, 4000f, 1f, 2f).AsSpan(Rate);
            return Spectrum.BandDb(x, Rate, 2500, 8000) - Spectrum.BandDb(x, Rate, 50, 2500);
        }
        var front = new EngineVoicing(EnginePlacement.Front, 2.3f);
        var mid = new EngineVoicing(EnginePlacement.Mid, 1.3f);
        var frontCabin = TopShare(front, true);
        var midCabin = TopShare(mid, true);
        var outside = TopShare(front, false);
        output.WriteLine($"top against the rest: front cabin {frontCabin:0.0} dB, mid cabin {midCabin:0.0} dB, behind the car {outside:0.0} dB");
        Assert.True(midCabin > frontCabin + 3, "the mid engine's cabin is not brighter");
        Assert.True(outside > frontCabin + 6, "the outside is not brighter than the front engine's cabin");
        Assert.InRange(front.BoomHz, 70f, 80f);
        Assert.InRange(mid.BoomHz, 125f, 140f);
    }

    /// <summary>
    /// At top speed the engine stays audible over the tyres' roar and the wind, at the gains the game plays them at
    /// (SampledEngine and TyreAudio alike at 0.9) and at the speed where both stop growing (250 km/h; the roar's growth
    /// ends at 216 km/h, the wind's at 198). Pulling at 90 % of its redline, every engine is at most 4 LU under the roar
    /// and the wind together (at 250 km/h a cabin is loud with wind, as real ones are), and in the third-octave band
    /// where it stands out most it is at least 4 dB over them: well over the masked threshold, which for a tone in noise
    /// lies a few decibels under the noise's own level in the same critical band (Zwicker and Fastl, Psychoacoustics, the
    /// masking of tones by noise). Measured in 0.20: overall from 2.2 LU under (the fleet saloon's cabin) to 3.7 over
    /// (the flat-twelve from behind), the prominent band from 5.3 dB over (the hot hatch's cabin, the closest) to 16.9.
    /// </summary>
    [Fact]
    public void The_engine_stays_audible_over_the_roar_and_the_wind_at_top_speed()
    {
        const int noiseRate = 22050;
        var road = LoopSamples("tyres/road.wav");
        var mix = new TyreMix();
        var wheel = TyreContact.Of(TyreGround.Asphalt, 0.1f, 0f, 0f, 70f, 1f, 1f);
        var wheels = new[] { wheel, wheel, wheel, wheel };
        for (var t = 0f; t < 4f; t += 1f / 60f) mix.Update(wheels, 0f, 1f / 60f);
        var roar = road.Select(s => s * mix.Roll.Level).ToArray();
        var margins = new List<(string Car, bool Interior, double Over, double Band)>();
        foreach (var interior in new[] { true, false })
        {
            var body = new BodyNoise(noiseRate);
            var wind = new float[3 * noiseRate];
            var state = new BodyState(70f, interior, 0f, 0f, 0f, 1f, 30f, 1f);
            for (var at = 0; at < wind.Length; at += 256) body.Render(wind.AsSpan(at, Math.Min(256, wind.Length - at)), state);
            var noise = 10 * Math.Log10(Math.Pow(10, Loudness.Lufs(roar, noiseRate) / 10) + Math.Pow(10, Loudness.Lufs(wind.AsSpan(noiseRate), noiseRate) / 10));
            var both = new float[2 * noiseRate];
            for (var i = 0; i < both.Length; i++) both[i] = roar[i % roar.Length] + wind[noiseRate + i];
            foreach (var row in Cars)
            {
                var (car, index) = ((string)row[0], (int)row[1]);
                var spec = VehicleSpec.Catalogue[index].Engine;
                var voice = Voice(car, index, interior);
                var held = Hold(voice, spec.RedlineRpm * 0.9f, 1f, 2f).AsSpan(Rate);
                var engine = Loudness.Lufs(held, Rate);
                var firing = spec.RedlineRpm * 0.9f * spec.Cylinders / 120f;
                // Third-octave bands from 100 Hz to 5 kHz; the Welch frames are 4096 samples at either rate, so a band's
                // energy is the signal's mean square in it times the same factor for both.
                var (bestHz, bestMargin) = (0.0, double.NegativeInfinity);
                for (var f = 100.0; f < 5000.0; f *= 1.2599)
                {
                    var margin = Spectrum.BandDb(held, Rate, f * 0.891, f * 1.122) - Spectrum.BandDb(both, noiseRate, f * 0.891, f * 1.122);
                    if (margin > bestMargin) (bestHz, bestMargin) = (f, margin);
                }
                output.WriteLine($"{(interior ? "cabin" : "outside")} {car}: engine {engine:0.0}, roar and wind {noise:0.0} LUFS; firing {firing:0} Hz; stands out most at {bestHz:0} Hz by {bestMargin:+0.0;-0.0} dB");
                margins.Add((car, interior, engine - noise, bestMargin));
            }
        }
        Assert.All(margins, m =>
        {
            Assert.True(m.Over > -4, $"{m.Car}'s engine is {-m.Over:0.0} LU under the roar and the wind");
            Assert.True(m.Band > 4, $"{m.Car}'s engine stands out by only {m.Band:0.0} dB in its loudest band");
        });
    }

    /// <summary>
    /// Every bank is cut clean (banks): no grain is a drop-out, more than 12 dB under the median of the dozen pulses either
    /// side of it in its segment, and none holds a crackle, a step between samples more than four times as sharp against
    /// its level as its segment's grains have (the warning of tools/make-engine-bank.py). Cut with the tool seeking before
    /// its input, the banks began their segments with the MP3 decoder's silence: 50 grains 12 dB or more under their
    /// neighbours (the V8's run-up 15 and its overrun 7, the flat-twelve's run-up 8 and its overrun 7, the rally hatch's
    /// overrun 5, the hot hatch's run-up 4 and its overrun 3, the estate's overrun 1), most of them 40 to 160 dB under,
    /// which the voice played as a drop-out of a firing period; and the rally hatch's idle and overrun, the hot hatch's
    /// overrun, the saloon's idle and the fleet saloon's Bravo segments held crackles. Measured on the re-cut banks: the
    /// deepest grain 9.8 dB under its neighbours (the V6 hatch's run-up, a blip's last pulse).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Every_bank_is_cut_without_drop_outs_or_crackles(string car, int index)
    {
        _ = index;
        foreach (var name in new[] { "engine_idle", "engine_drive", "engine_overrun" })
        {
            var bank = Bank(car, name, even: false);
            if (bank is null) continue;
            var (deepest, deepestAt) = (0.0, 0);
            var (sharpest, sharpestAt) = (0.0, 0);
            foreach (var segment in bank.Segment.Distinct())
            {
                var pulses = Enumerable.Range(0, bank.Marks.Length).Where(i => bank.Usable[i] && bank.Segment[i] == segment).ToArray();
                if (pulses.Length == 0) continue;
                var level = pulses.Select(i => (double)bank.Level(i)).ToArray();
                var sharpness = pulses.Select(i => Sharpness(bank, i)).ToArray();
                var typical = Median(sharpness);
                for (var k = 0; k < pulses.Length; k++)
                {
                    var from = Math.Max(k - 12, 0);
                    var dip = Median(level.Skip(from).Take(Math.Min(k + 13, level.Length) - from)) - level[k];
                    if (dip > deepest) (deepest, deepestAt) = (dip, pulses[k]);
                    if (sharpness[k] / typical > sharpest) (sharpest, sharpestAt) = (sharpness[k] / typical, pulses[k]);
                }
            }
            output.WriteLine($"{car} {name}: deepest grain {deepest:0.0} dB under its neighbours (pulse {deepestAt}), sharpest {sharpest:0.0} times its segment's median (pulse {sharpestAt})");
            Assert.True(deepest <= 12, $"{car} {name}: pulse {deepestAt} is {deepest:0.0} dB under its neighbours");
            Assert.True(sharpest <= 4, $"{car} {name}: pulse {sharpestAt} is {sharpest:0.0} times as sharp as its segment's grains (a crackle)");
        }
    }

    /// <summary>
    /// Every grain reaches at least 0.6 of its firing period either side of its pulse (banks review). The cutting tool kept
    /// two peaks half a period apart as two pulses, and filled a gap of 1.5 periods (rounded to two) with a pulse half a
    /// period from either neighbour: the hot hatch's idle had 12 grains reaching under 0.6 of the period on a side, those
    /// it filled in 585 to 656 samples against a period of 1,160, and laid at its 900 rpm idle, under the 1,140 rpm its
    /// tracker read, two such sides no longer met and left a gap no grain covered (6 ms of digital silence, 28 dB under the
    /// idle around it). The tool now keeps the stronger of
    /// two peaks closer than 0.6 of a period and fills only gaps of 1.75 periods, three quarters of a period from either
    /// side. Measured: at least 0.60 (the V8's run-up and the flat-twelve's idle), 0.69 on the hot hatch's idle.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Every_grain_reaches_most_of_a_period_either_side(string car, int index)
    {
        _ = index;
        foreach (var name in new[] { "engine_idle", "engine_drive", "engine_overrun" })
        {
            var bank = Bank(car, name, even: false);
            if (bank is null) continue;
            var (shortest, shortestAt) = (double.MaxValue, 0);
            for (var i = 1; i < bank.Marks.Length - 1; i++)
            {
                if (!bank.Usable[i]) continue;
                var side = Math.Min(bank.Marks[i] - bank.Marks[i - 1], bank.Marks[i + 1] - bank.Marks[i]) * bank.Firing[i] / (double)bank.SampleRate;
                if (side < shortest) (shortest, shortestAt) = (side, i);
            }
            output.WriteLine($"{car} {name}: the shortest side {shortest:0.000} of its period (pulse {shortestAt})");
            Assert.True(shortest >= 0.6, $"{car} {name}: pulse {shortestAt} reaches only {shortest:0.00} of its period on one side");
        }
    }

    /// <summary>The sharpest step between samples of a pulse's grain against the grain's RMS (as the cutting tool measures a crackle).</summary>
    private static double Sharpness(GrainBank bank, int pulse)
    {
        double step = 0, energy = 0;
        for (var j = bank.Marks[pulse - 1]; j < bank.Marks[pulse + 1]; j++)
        {
            energy += bank.Samples[j] * bank.Samples[j];
            if (j > bank.Marks[pulse - 1]) step = Math.Max(step, Math.Abs(bank.Samples[j] - bank.Samples[j - 1]));
        }
        return step / Math.Sqrt(energy / (bank.Marks[pulse + 1] - bank.Marks[pulse - 1]) + 1e-20);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : 0.5 * (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]);
    }

    /// <summary>
    /// The seeds the voice's random picks are tried with where one seed's picks could pass by luck: the sweep run on seed 7
    /// alone passed while seed 4 left the rally hatch's idle with a drop-out of 20.3 dB (banks review).
    /// </summary>
    private static readonly int[] Seeds = [1, 2, 3, 4, 7];

    /// <summary>
    /// Driven as the game drives it through every rev it has, each car's voice is free of clicks and drop-outs (banks):
    /// idling at the governor's throttle for a second, then flat out from its idle to its redline over eight seconds and
    /// off the throttle down 40 % of its redline in two, at the game's volume of 0.9, outside and in the cabin, with five
    /// seeds of the random picks (<see cref="Seeds"/>). No step between samples is over 0.45, nor three times the RMS of the
    /// 5 ms around it (a click), and no 10 ms of it falls 20 dB under the 210 ms around it (a drop-out, as the near-silent
    /// grains of the banks cut before played). Measured over the five seeds: steps at most 2.2 times the RMS around them (the
    /// estate from behind) and 0.38 between samples (the hot hatch), drop-outs at most 16.9 dB (the hot hatch in its cabin).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Sweeps_from_idle_to_the_redline_and_off_without_clicks_or_drop_outs(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        foreach (var seed in Seeds)
        {
            foreach (var interior in new[] { false, true })
            {
                var voice = Voice(car, index, interior, seed);
                voice.Volume = 0.9f;
                var x = new float[11 * Rate];
                const int block = 441;
                for (var at = 0; at < x.Length; at += block)
                {
                    var t = at / (double)Rate;
                    var (rpm, load) = t < 1 ? (spec.IdleRpm, 0.12f)
                        : t < 9 ? (spec.IdleRpm + (spec.RedlineRpm - spec.IdleRpm) * (float)(t - 1) / 8f, 1f)
                        : (spec.RedlineRpm * (1f - 0.2f * (float)(t - 9)), 0.05f);
                    voice.Render(x.AsSpan(at, Math.Min(block, x.Length - at)), rpm, load, true);
                }
                AssertClean(x, $"{car} seed {seed} {(interior ? "cabin" : "outside")}");
            }
        }
    }

    /// <summary>
    /// Idling, the voice holds no drop-out (banks review): at its idle revs, at the governor's throttle and at full
    /// throttle, ten seconds for each of five seeds, outside. The idle is where the voice lays an idle recording under the
    /// revs it was recorded at, and where a grain too short on a side leaves a gap: the hot hatch's idle as first re-cut,
    /// idling at 900 rpm for two minutes over six seeds, had five frames 20 dB or more under the idle around them and 6 ms
    /// of digital silence, which no sweep met (the game's attract laps never idle). Measured: drop-outs at most 17.4 dB (the
    /// hot hatch at the governor's throttle), steps at most 2.3 times the RMS around them (the estate at full throttle) and
    /// 0.28 between samples (the saloon at full throttle).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Idles_without_drop_outs(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        foreach (var seed in Seeds)
        {
            foreach (var load in new[] { 0.12f, 1f })
            {
                var voice = Voice(car, index, seed: seed);
                voice.Volume = 0.9f;
                AssertClean(Hold(voice, spec.IdleRpm, load, 10f), $"{car} seed {seed} idling at load {load:0.00}");
            }
        }
    }

    /// <summary>
    /// No step between samples of <paramref name="x"/> over 0.45 nor three times the RMS of the 5 ms around it (a click),
    /// and no 10 ms 20 dB under the 210 ms around it (a drop-out).
    /// </summary>
    private void AssertClean(float[] x, string what)
    {
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        // Steps against the RMS of the 5 ms around them, from a running sum of squares.
        const int half = Rate / 400;
        var squares = new double[x.Length + 1];
        for (var i = 0; i < x.Length; i++) squares[i + 1] = squares[i] + x[i] * x[i];
        var (step, ratio, ratioAt) = (0.0, 0.0, 0);
        for (var i = 1; i < x.Length; i++)
        {
            var d = Math.Abs(x[i] - x[i - 1]);
            var from = Math.Max(i - half, 0);
            var to = Math.Min(i + half, x.Length);
            var rms = Math.Sqrt((squares[to] - squares[from]) / (to - from));
            step = Math.Max(step, d);
            if (d / (rms + 1e-4) > ratio) (ratio, ratioAt) = (d / (rms + 1e-4), i);
        }
        // Drop-outs: 10 ms frames against the 210 ms around them.
        const int frame = Rate / 100;
        var frames = Enumerable.Range(0, x.Length / frame).Select(k => (squares[(k + 1) * frame] - squares[k * frame]) / frame + 1e-12).ToArray();
        var (drop, dropAt) = (0.0, 0);
        for (var k = 10; k < frames.Length - 10; k++)
        {
            var under = 10 * Math.Log10(frames.Skip(k - 10).Take(21).Average() / frames[k]);
            if (under > drop) (drop, dropAt) = (under, k);
        }
        output.WriteLine($"{what}: largest step {step:0.000}, {ratio:0.0} times the RMS around it at {ratioAt / (double)Rate:0.00} s; deepest 10 ms {drop:0.0} dB under the 210 ms around it at {dropAt / 100.0:0.00} s");
        Assert.True(step <= 0.45, $"{what}: a step of {step:0.00} between samples");
        Assert.True(ratio <= 3, $"{what}: a step {ratio:0.0} times the RMS around it at {ratioAt / (double)Rate:0.00} s (a click)");
        Assert.True(drop <= 20, $"{what}: 10 ms {drop:0.0} dB under the 210 ms around it at {dropAt / 100.0:0.00} s (a drop-out)");
    }

    /// <summary>
    /// Lifting off from the redline the voice holds no run of ticks (banks review): the throttle shut and the revs falling
    /// from the redline to half of it in five seconds, at the game's volume of 0.9, outside and in the cabin, five seeds.
    /// A tick is a millisecond whose high end (the RMS of the second difference) stands 12 dB over the median of the 50 ms
    /// around it, counted once within 5 ms. In 0.22 the V6 hatch's overrun held the ignition's last bangs after each limiter
    /// (28 to 44 ticks in the five seconds) and the hot hatch's an overloaded blip (35 to 76); both overruns now start past
    /// them (SOURCES.md), and a bank cut through either again fails here, where the crackle test of the banks, against its
    /// limit of four times a segment's median sharpness, passed the V6's at 3.3. Measured: at most 14 (the V6 hatch in its
    /// cabin; 4 outside), at most 1 on the others.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Lifts_off_from_the_redline_without_a_run_of_ticks(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        foreach (var interior in new[] { false, true })
        {
            var counts = new List<int>();
            foreach (var seed in Seeds)
            {
                var voice = Voice(car, index, interior, seed);
                voice.Volume = 0.9f;
                var x = new float[5 * Rate];
                const int block = 441;
                for (var at = 0; at < x.Length; at += block)
                {
                    var t = at / (double)Rate;
                    voice.Render(x.AsSpan(at, Math.Min(block, x.Length - at)), spec.RedlineRpm * (1f - 0.1f * (float)t), 0f, true);
                }
                counts.Add(Ticks(x));
            }
            output.WriteLine($"{car} {(interior ? "cabin" : "outside")}: {string.Join(", ", counts)} ticks in five seconds lifting off");
            Assert.True(counts.Max() <= 20, $"{car} {(interior ? "cabin" : "outside")}: {counts.Max()} ticks in five seconds lifting off from the redline");
        }
    }

    /// <summary>
    /// The ticks in <paramref name="x"/>: milliseconds whose RMS of the second difference stands 12 dB over the median of
    /// the 25 ms either side (and over -70 dB), counted once within 5 ms.
    /// </summary>
    private static int Ticks(float[] x)
    {
        const int frame = Rate / 1000;
        var n = (x.Length - 2) / frame;
        var high = new double[n];
        for (var k = 0; k < n; k++)
        {
            var sum = 0.0;
            for (var j = k * frame; j < (k + 1) * frame; j++)
            {
                var d = x[j + 2] - 2.0 * x[j + 1] + x[j];
                sum += d * d;
            }
            high[k] = 10.0 * Math.Log10(sum / frame + 1e-24);
        }
        var (count, last) = (0, -100);
        for (var k = 0; k < n; k++)
        {
            var from = Math.Max(0, k - 25);
            var median = Median(high.Skip(from).Take(Math.Min(n - 1, k + 25) - from + 1));
            if (high[k] - median > 12 && high[k] > -70 && k - last > 5) (count, last) = (count + 1, k);
        }
        return count;
    }

    /// <summary>
    /// Pulling at full throttle the voice's level follows the revs evenly from the idle to the redline (banks), outside and
    /// in the cabin: held every 100 rpm (its loudness the mean over five holds of two seconds, 25 and 50 rpm either side,
    /// as one rev to the next strings other grains together and moves by a few tenths), no step from one to the next is
    /// over 1.2 LU, and no point sags more than 2.1 LU under the lower of the loudest points within 600 rpm on either side
    /// (a dip taken a step at a time passes the first and not the second). Between the idle's recording and the run-up's
    /// the voice sagged by 3 to 8 LU before (the rally hatch at 30 % of its redline 4 LU under its run-up 400 rpm up; the
    /// V8 at 1,900 rpm 7 LU under both ends), the V8's run-up began with 25 pulses of its recording fading in (14 dB under
    /// the rest after levelling) and stepped 2.7 LU at 2,300 rpm, and a gap of 9 dB in its run-up at 6,700 rpm stepped it
    /// 1.9 LU. Measured: steps at most 1.1 LU (the V8 in its cabin at 6,900 rpm); sags at most 1.5 LU (the V8 in its cabin
    /// at 6,600 rpm) but on two cars: the fleet saloon in its cabin 2.0 LU at 1,700 rpm, where its run-up is laid under the
    /// 1,820 rpm its recording starts at (the run-up alone sags 1.4 LU there), and the estate 1.7 LU at 1,200 rpm, where the
    /// idle recorded at 1,100 rpm plays, quieter pulling than the one recorded at 810.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void The_level_follows_the_revs_evenly_from_the_idle_to_the_redline(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        foreach (var interior in new[] { false, true })
        {
            var voice = Voice(car, index, interior);
            var levels = new List<(float Rpm, double Lufs)>();
            for (var rpm = MathF.Ceiling(spec.IdleRpm / 100f) * 100f; rpm <= spec.RedlineRpm + 1f; rpm += 100f)
            {
                var energy = 0.0;
                foreach (var offset in new[] { -50f, -25f, 0f, 25f, 50f })
                {
                    energy += Math.Pow(10.0, Loudness.Lufs(Hold(voice, rpm + offset, 1f, 2.5f).AsSpan(Rate / 2), Rate) / 10.0);
                }
                levels.Add((rpm, 10.0 * Math.Log10(energy / 5.0)));
            }
            var steps = levels.Zip(levels.Skip(1), (a, b) => (b.Rpm, Step: b.Lufs - a.Lufs)).ToArray();
            var worst = steps.MaxBy(s => Math.Abs(s.Step));
            var (sag, sagAt) = (0.0, 0f);
            for (var k = 0; k < levels.Count; k++)
            {
                var at = levels[k];
                var left = levels.Take(k).Where(p => p.Rpm >= at.Rpm - 600f).Select(p => p.Lufs).DefaultIfEmpty(double.NegativeInfinity).Max();
                var right = levels.Skip(k + 1).Where(p => p.Rpm <= at.Rpm + 600f).Select(p => p.Lufs).DefaultIfEmpty(double.NegativeInfinity).Max();
                if (Math.Min(left, right) - at.Lufs > sag) (sag, sagAt) = (Math.Min(left, right) - at.Lufs, at.Rpm);
            }
            output.WriteLine($"{car} {(interior ? "cabin" : "outside")}: {levels[0].Lufs:0.0} LUFS at {levels[0].Rpm:0} rpm to {levels[^1].Lufs:0.0} at {levels[^1].Rpm:0}, the largest step {worst.Step:+0.0;-0.0} LU at {worst.Rpm:0} rpm, the deepest sag {sag:0.00} LU at {sagAt:0} rpm");
            Assert.True(Math.Abs(worst.Step) <= 1.2, $"{car}: the level steps {worst.Step:+0.0;-0.0} LU at {worst.Rpm:0} rpm");
            Assert.True(sag <= 2.1, $"{car}: the level sags {sag:0.0} LU at {sagAt:0} rpm");
        }
    }

    /// <summary>
    /// Opening the throttle at the idle's revs raises the level step by step (banks review): held at the idle revs at 0.12,
    /// 0.3, 0.45, 0.6 and full throttle (two seconds heard after half a second, the power mean over three seeds, outside),
    /// each step is louder than the last by 0.2 LU or more, and full throttle at least 2.5 LU louder than the governor's
    /// throttle. With the idle slid by its gain from its own level onto the run-up's trend between 0.2 and 0.6 of the
    /// throttle, the V8 at 900 rpm was 1.7 LU louder at full throttle than at 0.12 and 1.9 LU quieter at 0.6 than at 0.3.
    /// Measured: steps of 0.4 LU or more (the estate from 0.45 to 0.6), full throttle 3.0 LU (the estate) to 12.2 LU (the
    /// saloon) louder than the governor's throttle.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cars))]
    public void Opening_the_throttle_at_the_idle_revs_raises_the_level(string car, int index)
    {
        var spec = VehicleSpec.Catalogue[index].Engine;
        var loads = new[] { 0.12f, 0.3f, 0.45f, 0.6f, 1f };
        var levels = loads.Select(load =>
        {
            var energy = Seeds.Take(3).Sum(seed => Math.Pow(10.0, Loudness.Lufs(Hold(Voice(car, index, seed: seed), spec.IdleRpm, load, 2.5f).AsSpan(Rate / 2), Rate) / 10.0));
            return 10.0 * Math.Log10(energy / 3.0);
        }).ToArray();
        output.WriteLine($"{car} at {spec.IdleRpm:0} rpm: " + string.Join(", ", loads.Zip(levels, (l, v) => $"{l:0.00}: {v:0.0}")) + " LUFS");
        for (var k = 1; k < levels.Length; k++)
        {
            Assert.True(levels[k] >= levels[k - 1] + 0.2, $"{car}: {levels[k] - levels[k - 1]:+0.0;-0.0} LU from a load of {loads[k - 1]:0.00} to {loads[k]:0.00}");
        }
        Assert.True(levels[^1] - levels[0] >= 2.5, $"{car}: full throttle only {levels[^1] - levels[0]:0.0} LU over the governor's");
    }

    private static float[] LoopSamples(string file)
    {
        var pcm = Wav.ReadPcm16(Path.Combine(Audio, file));
        var x = new float[pcm.Length / 2];
        for (var i = 0; i < x.Length; i++) x[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
        return x;
    }
}
