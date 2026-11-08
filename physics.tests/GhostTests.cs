using System.Globalization;
using System.Numerics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The ghost of the best lap against the lap it was recorded from (0.74): at every time on the lap clock the recording
/// gives the pose the car had at that time, whatever the frame rate it was recorded at, and so do the files of the
/// versions before; the laps the game will not keep make no ghost; and the lap times read right at the whole minute.
/// </summary>
public sealed class GhostTests
{
    /// <summary>
    /// How far the ghost may stand from where the car was: a straight line between samples a twentieth of a second apart,
    /// against a car that moves on in the simulation's fixed steps of 1/240 s (VehicleSim.FixedStep), so at a frame rate
    /// that does not divide 240 it stands up to a step behind its frame's time (15 cm at 130 km/h; 10 cm was the most at
    /// 72 fps). The 0.73 lookup was hundreds of metres out.
    /// </summary>
    private const float PoseTolerance = 0.25f;
    private const float AngleTolerance = 3f;

    private readonly ITestOutputHelper _out;

    public GhostTests(ITestOutputHelper output) => _out = output;

    /// <summary>A frame of the recorded lap: the lap clock after it, the car then, and the frame's length.</summary>
    private readonly record struct Frame(float Time, Vector3 Position, Quaternion Orientation, float Steer, float Dt);

    /// <summary>
    /// The game's frame caps (Bootstrap.ApplyFrameCap: 20 in 1989 mode, 30 in the background, an even share of the
    /// screen's refresh up to 90, so 72 on 144 Hz and 75 on 75 Hz) and the rates a busy GPU holds under them (36, 45),
    /// with 144 for the uncapped. Until 0.74 only 20 and 60 recorded a ghost that kept to its lap: at 72 the ghost of a
    /// 90 s lap was 3.4 s ahead 30 s in and sat on the line from 81 s.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(36)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(72)]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(144)]
    public void The_ghost_drives_its_lap_on_the_lap_clock_at_every_frame_cap(int fps)
    {
        var dt = 1f / fps;
        CheckGhost($"{fps} fps", _ => dt);
    }

    /// <summary>
    /// Frames of uneven length: Godot's smoothed 60 Hz frame of 16 666 microseconds (three of them fall short of a twentieth
    /// of a second, so 0.73 sampled every fourth frame, 0.0667 s apart, and its ghost ran a third fast), and an uncapped
    /// game whose frames wander between 4 and 25 ms with a hitch of up to a tenth of a second now and then.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_ghost_drives_its_lap_on_the_lap_clock_with_frames_of_uneven_length(int seed)
    {
        if (seed == 0)
        {
            CheckGhost("smoothed 60 Hz (16666 us)", _ => 0.016666f);
            return;
        }
        var random = new Random(seed);
        CheckGhost($"uncapped, seed {seed}", frame => frame % 211 == 0 ? 0.05f + 0.05f * random.NextSingle() : 0.004f + 0.021f * random.NextSingle());
    }

    /// <summary>
    /// Drives the lap and checks its recording, and the same lap as 0.73 recorded it written as 0.73 wrote the file, against
    /// the car at every frame on the lap clock.
    /// </summary>
    private void CheckGhost(string label, Func<int, float> frames)
    {
        var (lap, truth, session) = DriveFirstLap(frames);
        Assert.Equal(0, session.CrashCount);
        Assert.Equal(RouteKind.Speed, lap.Route);
        var expectedSamples = lap.Duration * LapRecording.SampleHz;
        _out.WriteLine($"{label}: lap {lap.Duration:F3} s, {lap.Samples.Count} samples ({expectedSamples:F0} at 20 a second), {truth.Count} frames");

        // The recording made now: the car at 0 s and at the lap's time are its first and last samples, and 20 a second
        // between them whatever the frame rate (the remainder of a sampling interval is carried, not dropped).
        Assert.Equal(0f, lap.Samples[0].Time);
        Assert.Equal(lap.Duration, lap.Samples[^1].Time);
        Assert.InRange(lap.Samples.Count, expectedSamples - 1f, expectedSamples + 4f);
        for (var i = 1; i < lap.Samples.Count; i++) Assert.True(lap.Samples[i].Time > lap.Samples[i - 1].Time, $"sample {i} not later than the one before");
        var (position, degrees, steer) = WorstError(lap, truth);
        _out.WriteLine($"   recorded now: worst {position * 100f:F1} cm, {degrees:F2} deg, steer {steer:F3} rad from the car at the same lap time");
        Assert.True(position < PoseTolerance, $"{label}: the ghost stood {position:F2} m from where the car was");
        Assert.True(degrees < AngleTolerance, $"{label}: the ghost was turned {degrees:F1} degrees from the car");
        Assert.True(lap.TryPose(lap.Duration, out var finish, out _, out _) && Vector3.Distance(finish, truth[^1].Position) < 1e-4f, "the ghost should be on the line at the lap's time");
        Assert.False(lap.TryPose(lap.Duration + 0.01f, out _, out _, out _));

        // The same lap as 0.73 recorded and saved it: read from the bytes of its file, its ghost keeps to the lap too.
        var old = RecordedAs073(truth);
        var bytes = FileAs073(lap.Route, lap.Duration, old);
        var loaded = LapRecording.Deserialize(bytes);
        Assert.NotNull(loaded);
        Assert.Equal(bytes, loaded!.Serialize());
        var (oldPosition, oldDegrees, _) = WorstError(loaded, truth.Where(f => f.Time >= old[0].Time && f.Time <= old[^1].Time).ToList());
        var asBefore = truth.Max(f => Vector3.Distance(PoseAs073(old, f.Time), f.Position));
        var arrived = (old.Count - 1) / LapRecording.SampleHz;
        _out.WriteLine($"   0.73 file: {old.Count} samples, mean {(old[^1].Time - old[0].Time) / (old.Count - 1):F4} s apart; read now worst {oldPosition * 100f:F1} cm, {oldDegrees:F2} deg; read by 0.73 worst {asBefore:F1} m, on the line from {arrived:F1} s");
        Assert.True(oldPosition < PoseTolerance, $"{label}: a 0.73 file's ghost stood {oldPosition:F2} m from where the car was");
        Assert.True(oldDegrees < AngleTolerance, $"{label}: a 0.73 file's ghost was turned {oldDegrees:F1} degrees from the car");
    }

    /// <summary>
    /// A best-lap file of 0.73 or before recorded at 72 fps (a 144 Hz screen): a sample every fourth frame, 0.0556 s apart,
    /// the first 0.0556 s in, none at the line. It loads, writes back byte for byte the same, and its ghost is where the car
    /// was at every lap time between its first and last samples; 0.73 showed the pose from 33.4 s at 30 s and stood on the
    /// line from 78 s of the 86.8 s lap (the owner's own stunt lap of 31 October 2026 was such a file).
    /// </summary>
    [Fact]
    public void A_best_lap_file_from_before_0_74_loads_and_replays_at_its_own_times()
    {
        const float duration = 86.779f;
        const float interval = 4f / 72f;
        // A car going round a 200 m circle at 40 m/s.
        static Vector3 Where(float t) => new(200f * MathF.Cos(0.2f * t), 1f, 200f * MathF.Sin(0.2f * t));
        static Quaternion Facing(float t) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.2f * t);
        var samples = new List<PoseSample>();
        for (var k = 1; k * interval <= duration; k++) samples.Add(new PoseSample(k * interval, Where(k * interval), Facing(k * interval), 0.05f));
        var bytes = FileAs073(RouteKind.Stunt, duration, samples);

        var lap = LapRecording.Deserialize(bytes);
        Assert.NotNull(lap);
        Assert.Equal(RouteKind.Stunt, lap!.Route);
        Assert.Equal(duration, lap.Duration);
        Assert.Equal(samples.Count, lap.Samples.Count);
        Assert.Equal(bytes, lap.Serialize());
        for (var t = samples[0].Time; t <= samples[^1].Time; t += 0.0137f)
        {
            Assert.True(lap.TryPose(t, out var position, out var orientation, out var steer));
            Assert.True(Vector3.Distance(position, Where(t)) < 0.02f, $"at {t:F3} s the ghost stood {Vector3.Distance(position, Where(t)):F2} m off");
            Assert.True(AngleDegrees(orientation, Facing(t)) < 0.1f);
            Assert.Equal(0.05f, steer, 4);
        }
        // Before the first sample and from the last to the lap's end the ghost holds them, as it did.
        Assert.True(lap.TryPose(0f, out var start, out _, out _) && start == samples[0].Position);
        Assert.True(lap.TryPose(duration, out var end, out _, out _) && end == samples[^1].Position);
        _out.WriteLine($"30 s in: 0.73 showed the pose {Vector3.Distance(PoseAs073(samples, 30f), Where(30f)):F0} m on; on the line from {(samples.Count - 1) / LapRecording.SampleHz:F1} s of {duration:F1}");
    }

    /// <summary>
    /// A lap the game will not keep (0.74, RaceSession.KeepsBestLaps off: the attract driver's without --save-best, any
    /// under --no-ghost) is no record and no ghost: no NEW RECORD, nothing for the HUD's GHOST to stand for; a ghost the
    /// credit already races stays the one it races; the credit's own best, for the high-score table, counts as before;
    /// and the switch is read when the lap ends, so the next lap kept is a record again.
    /// </summary>
    [Fact]
    public void A_lap_the_game_will_not_keep_is_no_record_and_no_ghost()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic) { KeepsBestLaps = false };
        session.ChooseTransmission(TransmissionMode.ManualAutoClutch);
        var driver = new ScriptedDriver { PreferStunt = false };
        var events = DriveToLapEnd(session, driver);
        Assert.Contains(RaceEvent.LapCompleted, events);
        Assert.DoesNotContain(RaceEvent.NewBestLap, events);
        Assert.Null(session.BestLap);
        Assert.False(session.GrudgeMatch);
        Assert.True(float.IsPositiveInfinity(session.BestLapTime));
        Assert.StartsWith("LAP 1 ", session.Message);
        Assert.True(session.CreditBestLap < 200f, $"credit's best lap {session.CreditBestLap}");
        for (var i = 0; i < 60 * 5; i++)
        {
            session.Step(driver.Next(session), 1f / 60f);
            Assert.False(session.GhostVisible, "no ghost to race");
        }

        // A credit racing a (slow) saved lap: the unkept lap leaves it the ghost; the next lap, kept, takes its place.
        var saved = new LapRecording(RouteKind.Speed, 999f, [new PoseSample(0f, Vector3.Zero, Quaternion.Identity, 0f), new PoseSample(999f, Vector3.One, Quaternion.Identity, 0f)]);
        var next = new RaceSession(OriginalInspiredTrack.Build(), new VehicleSpec(), RaceOptions.MediumNoTraffic) { KeepsBestLaps = false };
        next.LoadBestLap(saved);
        next.ChooseTransmission(TransmissionMode.ManualAutoClutch);
        var nextDriver = new ScriptedDriver { PreferStunt = false };
        events = DriveToLapEnd(next, nextDriver);
        Assert.Contains(RaceEvent.LapCompleted, events);
        Assert.DoesNotContain(RaceEvent.NewBestLap, events);
        Assert.Same(saved, next.BestLap);
        Assert.Equal(999f, next.BestLapTime);
        next.KeepsBestLaps = true;
        events = DriveToLapEnd(next, nextDriver);
        Assert.Contains(RaceEvent.NewBestLap, events);
        Assert.NotSame(saved, next.BestLap);
        Assert.StartsWith("NEW RECORD ", next.Message);
    }

    /// <summary>
    /// Minutes, seconds and the tenth (0.74): the seconds are rounded after the minutes are taken, so from 59.95 s into a
    /// minute they make a whole one, carried; 119.96 s read 1:60.0 until then. A float such as 81.35f, a hair under 81.35,
    /// still reads 1:21.4 as it did.
    /// </summary>
    [Theory]
    [InlineData(0f, "0:00.0")]
    [InlineData(59.94f, "0:59.9")]
    [InlineData(59.95f, "1:00.0")]
    [InlineData(59.97f, "1:00.0")]
    [InlineData(59.99f, "1:00.0")]
    [InlineData(60f, "1:00.0")]
    [InlineData(61.2f, "1:01.2")]
    [InlineData(81.35f, "1:21.4")]
    [InlineData(119.96f, "2:00.0")]
    [InlineData(120f, "2:00.0")]
    [InlineData(179.97f, "3:00.0")]
    [InlineData(float.PositiveInfinity, "-:--.-")]
    [InlineData(float.NaN, "-:--.-")]
    public void Lap_times_read_in_minutes_and_seconds_to_the_tenth(float seconds, string text) => Assert.Equal(text, RaceSession.FormatLapTime(seconds));

    /// <summary>Every lap time to ten minutes, and the floats either side of each millisecond, reads as before 0.74 but for the carried minute.</summary>
    [Fact]
    public void Lap_times_read_as_before_but_for_the_carried_minute()
    {
        var carried = 0;
        for (var ms = 0; ms <= 600_000; ms++)
        {
            var exact = ms / 1000f;
            foreach (var seconds in new[] { MathF.BitDecrement(exact), exact, MathF.BitIncrement(exact) })
            {
                if (seconds < 0f) continue;
                var minutes = (int)(seconds / 60f);
                var before = $"{minutes}:{(seconds - minutes * 60f).ToString("00.0", CultureInfo.InvariantCulture)}";
                var now = RaceSession.FormatLapTime(seconds);
                if (before.EndsWith(":60.0", StringComparison.Ordinal))
                {
                    carried++;
                    Assert.Equal($"{minutes + 1}:00.0", now);
                }
                else Assert.Equal(before, now);
            }
        }
        _out.WriteLine($"{carried} of the times read m:60.0 before 0.74");
        Assert.True(carried > 0);
    }

    /// <summary>
    /// The scripted driver's first lap on the speed route, one step a frame of the lengths <paramref name="frames"/> gives:
    /// the session's recording of it, and the car at every frame on the lap clock from the line to the line.
    /// </summary>
    private static (LapRecording Lap, List<Frame> Truth, RaceSession Session) DriveFirstLap(Func<int, float> frames)
    {
        var session = new RaceSession(OriginalInspiredTrack.Build(), new VehicleSpec(), RaceOptions.MediumNoTraffic);
        var driver = new ScriptedDriver { PreferStunt = false };
        session.ChooseTransmission(TransmissionMode.ManualAutoClutch);
        var truth = new List<Frame>();
        var clock = 0f;
        var open = false;
        for (var frame = 0; frame < 240 * 240; frame++)
        {
            var dt = frames(frame);
            session.Step(driver.Next(session), dt);
            var car = session.Car;
            if (session.Events.Contains(RaceEvent.LapCompleted))
            {
                var lap = session.BestLap ?? throw new InvalidOperationException("a first lap is a best lap");
                truth.Add(new Frame(lap.Duration, car.Position, car.Orientation, car.SteerAngle, dt));
                return (lap, truth, session);
            }
            // The car starts short of the line: the lap clock runs from the engine's start and starts again at 0 there.
            open |= session.LapTime < clock;
            clock = session.LapTime;
            if (open) truth.Add(new Frame(session.LapTime, car.Position, car.Orientation, car.SteerAngle, dt));
            if (session.Phase == SessionPhase.Replay) session.Abort();
        }
        throw new InvalidOperationException("no lap in four minutes");
    }

    private static List<RaceEvent> DriveToLapEnd(RaceSession session, ScriptedDriver driver)
    {
        var events = new List<RaceEvent>();
        for (var i = 0; i < 60 * 240 && !events.Contains(RaceEvent.LapCompleted); i++)
        {
            session.Step(driver.Next(session), 1f / 60f);
            events.AddRange(session.Events);
            if (session.Phase == SessionPhase.Replay) session.Abort();
        }
        return events;
    }

    private static (float Position, float Degrees, float Steer) WorstError(LapRecording lap, List<Frame> truth)
    {
        var worst = (Position: 0f, Degrees: 0f, Steer: 0f);
        foreach (var frame in truth)
        {
            Assert.True(lap.TryPose(frame.Time, out var position, out var orientation, out var steer), $"no pose at {frame.Time:F3} s of {lap.Duration:F3}");
            worst.Position = MathF.Max(worst.Position, Vector3.Distance(position, frame.Position));
            worst.Degrees = MathF.Max(worst.Degrees, AngleDegrees(orientation, frame.Orientation));
            worst.Steer = MathF.Max(worst.Steer, MathF.Abs(steer - frame.Steer));
        }
        return worst;
    }

    private static float AngleDegrees(Quaternion a, Quaternion b)
    {
        var dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))));
        return 2f * MathF.Acos(dot) * 180f / MathF.PI;
    }

    /// <summary>The lap as 0.73 recorded it: a sample each time a twentieth of a second had gathered, the remainder dropped, none at either line.</summary>
    private static List<PoseSample> RecordedAs073(List<Frame> truth)
    {
        var samples = new List<PoseSample>();
        var gathered = 0f;
        for (var i = 1; i < truth.Count; i++)
        {
            gathered += truth[i].Dt;
            if (gathered < 1f / LapRecording.SampleHz) continue;
            gathered = 0f;
            samples.Add(new PoseSample(truth[i].Time, truth[i].Position, truth[i].Orientation, truth[i].Steer));
        }
        return samples;
    }

    /// <summary>A best-lap file byte for byte as 0.73 wrote it: "GLG1", the route, the duration, the count, and each sample's time, position, orientation and steer.</summary>
    private static byte[] FileAs073(RouteKind route, float duration, IReadOnlyList<PoseSample> samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x31474C47);
        writer.Write((int)route);
        writer.Write(duration);
        writer.Write(samples.Count);
        foreach (var s in samples)
        {
            writer.Write(s.Time);
            writer.Write(s.Position.X); writer.Write(s.Position.Y); writer.Write(s.Position.Z);
            writer.Write(s.Orientation.X); writer.Write(s.Orientation.Y); writer.Write(s.Orientation.Z); writer.Write(s.Orientation.W);
            writer.Write(s.SteerAngle);
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Where 0.73 put the ghost: the sample at index lap time times 20, as if the samples came exactly a twentieth of a second apart.</summary>
    private static Vector3 PoseAs073(IReadOnlyList<PoseSample> samples, float lapTime)
    {
        var index = (int)(lapTime * LapRecording.SampleHz);
        if (index >= samples.Count - 1) return samples[^1].Position;
        var a = samples[index];
        var b = samples[index + 1];
        return Vector3.Lerp(a.Position, b.Position, Math.Clamp((lapTime - a.Time) / MathF.Max(b.Time - a.Time, 1e-4f), 0f, 1f));
    }
}
