using System.Numerics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>Drives the rules headless with the scripted driver on the default circuit.</summary>
public sealed class RaceSessionTests
{
    private const float Dt = 1f / 60f;
    private readonly ITestOutputHelper _out;

    public RaceSessionTests(ITestOutputHelper output) => _out = output;

    /// <summary>Runs the scripted driver until the first completed lap or 240 s, tracing events.</summary>
    private List<RaceEvent> DriveLap(RaceSession session, ScriptedDriver driver, string label)
    {
        var events = new List<RaceEvent>();
        var t = 0f;
        var nextTrace = 0f;
        while (t < 240f && session.Phase != SessionPhase.Finished)
        {
            var input = driver.Next(session);
            session.Step(input, Dt);
            events.AddRange(session.Events);
            foreach (var e in session.Events)
            {
                _out.WriteLine($"t={t:F1} {e} | {session.Message} | {session.CurrentPathKind} s={session.PathDistance:F0} v={session.Car.SpeedMph:F0} mph time={session.TimeRemaining:F0}{(e == RaceEvent.Crashed ? $" reason={session.LastCrash}" : "")}");
                if (e is RaceEvent.LeftRoad or RaceEvent.WrongWay or RaceEvent.Crashed)
                {
                    var p = session.Car.Position;
                    _out.WriteLine($"   car at ({p.X:F0}, {p.Z:F0}) heading {MathF.Atan2(session.Car.Forward.X, session.Car.Forward.Z) * MathUtil.Rad2Deg:F0} lat={session.Car.BodyHit.Lateral:F1} kind={session.Car.BodyHit.Frame.Kind} lane={driver.LaneOffset:F1} plan={driver.PlannedMph:F0}");
                }
            }
            if (t >= nextTrace)
            {
                nextTrace += 10f;
                _out.WriteLine($"   t={t:F0} {session.CurrentPathKind} s={session.PathDistance:F0} kind={session.Car.BodyHit.Frame.Kind} v={session.Car.SpeedMph:F0} plan={driver.PlannedMph:F0} lane={driver.LaneOffset:F1} gear={session.Car.Drivetrain.Gear} lat={session.Car.BodyHit.Lateral:F1}");
            }
            if (session.Phase == SessionPhase.Replay) session.Abort();
            if (events.Contains(RaceEvent.LapCompleted)) break;
            t += Dt;
        }
        _out.WriteLine($"{label}: t={t:F1}s phase={session.Phase} laps={session.CompletedLaps} score={session.Score} crashes={session.CrashCount} time left={session.TimeRemaining:F0} best lap={session.BestLapTime:F1}");
        return events;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scripted_driver_completes_a_lap(bool stunt)
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        var driver = new ScriptedDriver { PreferStunt = stunt };
        Assert.Equal(SessionPhase.SelectTransmission, session.Phase);
        session.ChooseTransmission(TransmissionMode.ManualAutoClutch);
        Assert.Equal(SessionPhase.StartEngine, session.Phase);
        Assert.Null(session.Traffic);

        var events = DriveLap(session, driver, stunt ? "stunt route" : "speed route");
        Assert.Contains(RaceEvent.RouteChosen, events);
        Assert.Contains(RaceEvent.CheckpointPassed, events);
        Assert.Contains(RaceEvent.LapCompleted, events);
        Assert.Contains(RaceEvent.NewBestLap, events);
        Assert.Equal(1, session.CompletedLaps);
        Assert.True(session.CrashCount <= 1, $"crashes {session.CrashCount}");
        Assert.True(session.Score > 5000, $"score {session.Score}");
    }

    [Fact]
    public void Scripted_driver_threads_the_traffic_on_the_speed_route()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.Medium);
        Assert.NotNull(session.Traffic);
        Assert.Equal(8, session.Traffic!.Cars.Count);
        var driver = new ScriptedDriver { PreferStunt = false };
        session.ChooseTransmission(TransmissionMode.Automatic);

        var events = DriveLap(session, driver, "speed route in traffic");
        Assert.Contains(RaceEvent.LapCompleted, events);
        Assert.True(session.CrashCount <= 1, $"crashes {session.CrashCount} (last {session.LastCrash})");
        foreach (var drone in session.Traffic.Cars)
        {
            Assert.NotEqual(PathKind.Stunt, drone.Path.Kind);
        }
    }

    [Fact]
    public void Drones_keep_their_lane_and_never_enter_the_stunt_route()
    {
        var track = OriginalInspiredTrack.Build();
        var traffic = new Traffic(track, 5, 3);
        for (var i = 0; i < 60 * 300; i++)
        {
            traffic.Step(Dt);
            foreach (var car in traffic.Cars)
            {
                Assert.NotEqual(PathKind.Stunt, car.Path.Kind);
                Assert.InRange(car.Distance, 0f, car.Path.Length + 1e-3f);
                // A drone sits in its lane at ride height above the road frame it rides.
                var offset = car.Position - car.Frame.Origin;
                var lateral = Vector3.Dot(offset, car.Frame.Lateral);
                Assert.InRange(lateral, car.LaneOffset - 0.01f, car.LaneOffset + 0.01f);
            }
        }
    }

    [Fact]
    public void Hitting_a_drone_crashes_and_replays()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.Medium);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        Assert.Equal(SessionPhase.Driving, session.Phase);

        // Put the car 40 m behind a drone that drives our way, in its lane, and floor it.
        var drone = session.Traffic!.Cars.First(c => !c.Oncoming && c.Path.Kind == PathKind.Speed && c.Distance > 60f);
        var behind = drone.Distance - 40f;
        session.Car.Reset(drone.Path.FrameAt(behind), new PathCursor(drone.Path, drone.Path.FrameIndexAt(behind)));
        session.Car.LaunchAt(38f, drone.LaneOffset);
        var crashed = false;
        for (var i = 0; i < 60 * 15 && !crashed; i++)
        {
            // Chase the drone itself: aim at its tail.
            var to = drone.Position - session.Car.Position;
            var angle = MathF.Atan2(Vector3.Dot(to, session.Car.Right), MathF.Max(Vector3.Dot(to, session.Car.Forward), 0.1f));
            var steer = MathUtil.Clamp(angle / session.Car.MaxSteerAngleAt(session.Car.Speed), -1f, 1f);
            session.Step(new DriverInputs { Throttle = 1f, Gear = DriverInputs.NoGearChange, Steer = steer }, Dt);
            if (session.Events.Contains(RaceEvent.Crashed)) crashed = true;
        }
        Assert.True(crashed, "expected to run into the drone");
        Assert.Equal(CrashReason.HitTraffic, session.LastCrash);
        Assert.Equal(SessionPhase.Replay, session.Phase);

        for (var i = 0; i < 60 * 11; i++) session.Step(DriverInputs.Idle, Dt);
        Assert.Equal(SessionPhase.StartEngine, session.Phase);
        // Nothing is parked on the return point.
        foreach (var car in session.Traffic.Cars) Assert.True(Vector3.Distance(car.Position, session.Car.Position) > 20f);
    }

    [Fact]
    public void Best_lap_is_recorded_and_its_ghost_drives_the_next_credit()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        DriveLap(session, new ScriptedDriver { PreferStunt = false }, "recording lap");

        var best = session.BestLap;
        Assert.NotNull(best);
        Assert.Equal(RouteKind.Speed, best!.Route);
        Assert.InRange(best.Duration, 60f, 200f);
        Assert.InRange(best.Samples.Count, best.Duration * LapRecording.SampleHz * 0.9f, best.Duration * LapRecording.SampleHz * 1.1f);
        Assert.True(best.TryPose(best.Duration * 0.5f, out var midPosition, out _, out _));
        Assert.True(track.ClearanceFromRoads(midPosition) < 6f, "the ghost should be on the road");
        Assert.False(best.TryPose(best.Duration + 1f, out _, out _, out _));

        var bytes = best.Serialize();
        var copy = LapRecording.Deserialize(bytes);
        Assert.NotNull(copy);
        Assert.Equal(best.Samples.Count, copy!.Samples.Count);
        Assert.Equal(best.Duration, copy.Duration);
        Assert.Null(LapRecording.Deserialize(new byte[] { 1, 2, 3 }));

        // A fresh credit loads the ghost and sees it once the lap opens.
        var next = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        next.LoadBestLap(copy);
        Assert.True(next.GrudgeMatch);
        Assert.Equal(copy.Duration, next.BestLapTime);
        next.ChooseTransmission(TransmissionMode.Automatic);
        var driver = new ScriptedDriver { PreferStunt = false };
        var seen = false;
        for (var i = 0; i < 60 * 30 && !seen; i++)
        {
            next.Step(driver.Next(next), Dt);
            seen = next.GhostVisible;
        }
        Assert.True(seen, "ghost never appeared");
        Assert.Contains("GRUDGE MATCH", next.Message);
    }

    [Fact]
    public void Driving_into_a_barn_crashes_and_returns_to_the_last_point()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        Assert.Equal(SessionPhase.Driving, session.Phase);

        // Park a barn on the road 70 m ahead so the drive to it is deterministic.
        var startFrame = track.StartFrame;
        var barn = new TrackProp(PropKind.Barn, startFrame.Origin + startFrame.Tangent * 70f, 90f, new Vector3(18f, 9f, 12f));
        track.Props.Add(barn);
        session.Car.LaunchAt(20f);
        var crashed = false;
        for (var i = 0; i < 60 * 40 && !crashed; i++)
        {
            var to = barn.Position - session.Car.Position;
            var angle = MathF.Atan2(Vector3.Dot(to, session.Car.Right), MathF.Max(Vector3.Dot(to, session.Car.Forward), 0.1f));
            var steer = MathUtil.Clamp(angle / session.Car.MaxSteerAngleAt(session.Car.Speed), -1f, 1f);
            session.Step(new DriverInputs { Steer = steer, Throttle = 0.5f, Gear = DriverInputs.NoGearChange }, Dt);
            if (session.Events.Contains(RaceEvent.Crashed)) crashed = true;
        }
        Assert.True(crashed, "expected to crash into the barn");
        Assert.Equal(SessionPhase.Replay, session.Phase);
        Assert.Equal(CrashReason.HitBuilding, session.LastCrash);
        Assert.NotEmpty(session.Replay);

        for (var i = 0; i < 60 * 11; i++) session.Step(DriverInputs.Idle, Dt);
        Assert.Equal(SessionPhase.StartEngine, session.Phase);
        Assert.False(session.Car.Drivetrain.Engine.Running);
        _out.WriteLine($"returned to {session.ReturnPath.Kind} {session.ReturnDistance:F0} m");
    }

    [Fact]
    public void Skid_pad_is_asphalt_with_no_off_road_clock_and_no_wrong_way()
    {
        var track = OriginalInspiredTrack.Build();
        var pad = track.Pads[0];
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);

        // Drop the car onto the pad and drive a tight circle for a while.
        var frame = track.StartFrame;
        var onPad = new RoadFrame(pad.Centre + new Vector3(10f, 0f, 0f), frame.Tangent, MathUtil.Up, Vector3.Cross(MathUtil.Up, frame.Tangent), 0f, 5.5f, SurfaceType.Pad, SegmentKind.Straight, 0);
        session.Car.Reset(onPad, track.StartCursor);
        session.Car.Drivetrain.Engine.Start();
        for (var i = 0; i < 60 * 25; i++)
        {
            session.Step(new DriverInputs { Throttle = 0.55f, Steer = 0.8f, Gear = DriverInputs.NoGearChange }, Dt);
            Assert.Equal(SessionPhase.Driving, session.Phase);
            Assert.False(session.OffRoad, $"off-road clock started at step {i}");
            Assert.False(session.WrongWay, $"wrong way at step {i}");
        }
        Assert.Equal(SurfaceType.Pad, session.Car.BodyHit.Surface);
        Assert.True(Vector3.Distance(session.Car.Position, pad.Centre) < pad.Radius, "left the pad");
    }
}
