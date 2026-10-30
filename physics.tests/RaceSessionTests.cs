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

    /// <summary>Runs the scripted driver until the first completed lap or 240 s, tracing events; one call per <paramref name="dt"/>, as one per frame in the game.</summary>
    private List<RaceEvent> DriveLap(RaceSession session, ScriptedDriver driver, string label, float dt = Dt)
    {
        var events = new List<RaceEvent>();
        var t = 0f;
        var nextTrace = 0f;
        while (t < 240f && session.Phase != SessionPhase.Finished)
        {
            var input = driver.Next(session);
            session.Step(input, dt);
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
            t += dt;
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

    /// <summary>
    /// The game steps the rules once a frame at whatever rate its frame cap allows: up to 90 fps in front, 30 in the
    /// background and in recordings, 20 in 1989 mode (144 checks that nothing in the driver counts frames). The
    /// attract lap has to hold at all of them, and the fork must not count a lap: a cursor that hopped back to the
    /// start road there replayed its start line.
    /// </summary>
    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 20)]
    [InlineData(true, 30)]
    [InlineData(false, 30)]
    [InlineData(true, 90)]
    [InlineData(false, 90)]
    [InlineData(true, 144)]
    [InlineData(false, 144)]
    public void Scripted_driver_completes_a_lap_at_any_frame_rate(bool stunt, int fps)
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        var driver = new ScriptedDriver { PreferStunt = stunt };
        session.ChooseTransmission(TransmissionMode.ManualAutoClutch);

        var events = DriveLap(session, driver, $"{(stunt ? "stunt" : "speed")} route at {fps} fps", 1f / fps);
        Assert.Contains(RaceEvent.CheckpointPassed, events);
        Assert.Contains(RaceEvent.LapCompleted, events);
        Assert.Equal(1, session.CompletedLaps);
        Assert.Equal(2, session.Lap);
        Assert.True(session.CrashCount == 0, $"crashes {session.CrashCount} (last {session.LastCrash})");
        Assert.DoesNotContain(RaceEvent.LeftRoad, events);
    }

    /// <summary>
    /// The hatchbacks round both routes: 147 hp and front-wheel drive still make the drawbridge's 60-80 mph and the
    /// loop's 60 at the speeds the scripted driver plans.
    /// </summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public void Every_hatchback_laps_both_routes(int car, bool stunt)
    {
        var spec = VehicleSpec.Catalogue[car];
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, spec, RaceOptions.MediumNoTraffic);
        var driver = new ScriptedDriver { PreferStunt = stunt };
        session.ChooseTransmission(TransmissionMode.ManualAutoClutch);

        var events = DriveLap(session, driver, $"{spec.Name} {(stunt ? "stunt" : "speed")} route");
        Assert.Contains(RaceEvent.CheckpointPassed, events);
        Assert.Contains(RaceEvent.LapCompleted, events);
        Assert.True(session.CrashCount == 0, $"crashes {session.CrashCount} (last {session.LastCrash})");
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
            if (i % 15 == 0) _out.WriteLine($"t={i * Dt:F2} gap {Vector3.Distance(drone.Position, session.Car.Position):F1} side {Vector3.Dot(drone.Position - session.Car.Position, session.Car.Right):F2} up {Vector3.Dot(drone.Position - session.Car.Position, session.Car.Up):F2} car {session.Car.Speed:F1} drone {drone.Speed:F1} events {string.Join(',', session.Events)}");
        }
        _out.WriteLine($"crashed {crashed}: {session.LastCrash}, damage {session.Damage:F2}, knock {session.LastKnock}");
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

    /// <summary>
    /// A pier under a bridge stops the car like a building (until 0.9 the car drove through them): driven at from the
    /// field beside the bridge, under the deck's edge, it knocks the car back.
    /// </summary>
    [Fact]
    public void Running_into_a_bridge_pier_knocks_the_car_back()
    {
        var track = OriginalInspiredTrack.Build();
        var pier = track.Props.Where(p => p.Kind == PropKind.Pier && p.Label == "pier").MaxBy(p => p.Size.Y)!;
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);

        // Out from the bridge's centre line through the pier, and 18 m on: the car comes back in along that line.
        var deck = track.Paths.SelectMany(p => p.Frames).OrderBy(f => Vector3.DistanceSquared(f.Origin with { Y = 0f }, pier.Position with { Y = 0f })).First();
        var outward = Vector3.Normalize((pier.Position - deck.Origin) with { Y = 0f });
        var from = pier.Position + outward * 18f;
        from.Y = track.Terrain.Height(from.X, from.Z);
        var start = new RoadFrame(from, -outward, MathUtil.Up, Vector3.Cross(MathUtil.Up, -outward), 0f, 5.5f, SurfaceType.Road, SegmentKind.Straight, 0);
        session.Car.Reset(start, track.StartCursor);
        session.Car.LaunchAt(9f);
        var knocked = false;
        var events = new List<RaceEvent>();
        for (var i = 0; i < 60 * 6 && !knocked && session.Phase == SessionPhase.Driving; i++)
        {
            session.Step(new DriverInputs { Throttle = 0.25f, Gear = DriverInputs.NoGearChange }, Dt);
            events.AddRange(session.Events);
            knocked = session.Events.Contains(RaceEvent.Knocked) && session.LastKnock.Kind == KnockKind.Solid;
        }
        _out.WriteLine($"pier {pier.Size.Y:F1} m tall: phase {session.Phase} ({session.LastCrash}), knock {session.LastKnock}, damage {session.Damage:F2}");
        Assert.True(knocked, "expected to knock into the pier");
        Assert.DoesNotContain(RaceEvent.Crashed, events);
        Assert.Equal(PropKind.Pier, track.Props[session.LastKnock.Index].Kind);
        for (var i = 0; i < 30; i++) session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, Dt);
        Assert.True(Vector3.Dot(session.Car.Velocity, -outward) < 1f, "the car should have been stopped by the pier");
    }

    /// <summary>Over the crash speed a knock is a crash: a tree at 65 km/h.</summary>
    [Fact]
    public void A_knock_over_the_crash_speed_crashes()
    {
        var (_, session, _) = TreeAhead(15f);
        var events = new List<RaceEvent>();
        RollInto(session, 18f, events);
        Assert.Contains(RaceEvent.Crashed, events);
        Assert.DoesNotContain(RaceEvent.Knocked, events);
        Assert.Equal(CrashReason.HitTree, session.LastCrash);
        Assert.Equal(SessionPhase.Replay, session.Phase);
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
    public void Train_at_the_crossing_is_a_crash_and_signs_are_not()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);

        // Park on the crossing and wait for the train.
        var crossing = track.Speed.FrameAt(OriginalInspiredTrack.CrossingDistance);
        session.Car.Reset(crossing, new PathCursor(track.Speed, track.Speed.FrameIndexAt(OriginalInspiredTrack.CrossingDistance)));
        session.Car.Drivetrain.Engine.Start();
        var crashed = false;
        var waited = 0f;
        var budget = track.Train!.Cycle + 5f;
        for (var t = 0f; t < budget && !crashed; t += Dt)
        {
            session.Step(DriverInputs.Idle, Dt);
            waited = t;
            if (session.Events.Contains(RaceEvent.Crashed)) crashed = true;
        }
        Assert.True(crashed, "the train never hit the parked car");
        Assert.Equal(CrashReason.HitTrain, session.LastCrash);
        _out.WriteLine($"train arrived after {waited:F1} s");

        // Soft props: drive through a sign, nothing but an event (cows are bodies since 0.9.1: see the cow's test).
        var fresh = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        fresh.ChooseTransmission(TransmissionMode.Automatic);
        fresh.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        var events = new List<RaceEvent>();
        foreach (var kind in new[] { PropKind.Sign })
        {
            // A specimen with nothing solid within 25 m, so the run-through ends on open ground.
            var prop = track.Props.First(p => p.Kind == kind && !track.Props.Any(o => o.Solid && Vector3.Distance(o.Position, p.Position) < 25f));
            var frame = track.StartFrame;
            var start = new RoadFrame(prop.Position - frame.Tangent * 12f, frame.Tangent, MathUtil.Up, Vector3.Cross(MathUtil.Up, frame.Tangent), 0f, 5.5f, SurfaceType.Road, SegmentKind.Straight, 0);
            fresh.Car.Reset(start, track.StartCursor);
            fresh.Car.LaunchAt(12f);
            for (var i = 0; i < 60 * 3; i++)
            {
                fresh.Step(new DriverInputs { Throttle = 0.3f, Gear = DriverInputs.NoGearChange }, Dt);
                events.AddRange(fresh.Events);
                if (fresh.Phase != SessionPhase.Driving || fresh.Events.Contains(RaceEvent.SignHit) || fresh.Events.Contains(RaceEvent.CowHit)) break;
            }
            Assert.True(fresh.Phase == SessionPhase.Driving, $"driving through the {kind} '{prop.Label}' at ({prop.Position.X:F0}, {prop.Position.Z:F0}) ended in {fresh.Phase} ({fresh.LastCrash}); car at ({fresh.Car.Position.X:F0}, {fresh.Car.Position.Y:F1}, {fresh.Car.Position.Z:F0})");
        }
        Assert.Contains(RaceEvent.SignHit, events);
        Assert.DoesNotContain(RaceEvent.Crashed, events);
        Assert.Single(fresh.KnockedSigns);
    }

    /// <summary>A tree planted on the start straight, <paramref name="ahead"/> metres on, for the knock tests.</summary>
    private static (TrackGeometry Track, RaceSession Session, TrackProp Tree) TreeAhead(float ahead)
    {
        var track = OriginalInspiredTrack.Build();
        var spot = track.StartCursor.Path.FrameAt(track.StartFrame.Distance + ahead);
        var tree = new TrackProp(PropKind.Tree, spot.Origin, 0f, new Vector3(1.0f, 7f, 1.0f), "knock-test");
        track.Props.Add(tree);
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        Assert.Equal(SessionPhase.Driving, session.Phase);
        return (track, session, tree);
    }

    /// <summary>Rolls the car at the tree at <paramref name="speed"/> with the throttle shut until it knocks or crashes; returns the speed along the road after.</summary>
    private float RollInto(RaceSession session, float speed, List<RaceEvent> events)
    {
        session.Car.Reset(session.Track.StartFrame, session.Track.StartCursor);
        session.Car.LaunchAt(speed);
        for (var i = 0; i < 60 * 12; i++)
        {
            session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, Dt);
            events.AddRange(session.Events);
            if (session.Events.Contains(RaceEvent.Knocked) || session.Phase != SessionPhase.Driving) break;
        }
        // A few frames on for the bounce.
        for (var i = 0; i < 20 && session.Phase == SessionPhase.Driving; i++) session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange }, Dt);
        return session.Car.Speed;
    }

    /// <summary>Under the crash speed a tree bounces the car off with a dent and no replay (0.9.1; before, any touch was a crash).</summary>
    [Fact]
    public void A_knock_under_the_crash_speed_bounces_the_car_off_and_dents_it()
    {
        var (_, session, tree) = TreeAhead(15f);
        var events = new List<RaceEvent>();
        var after = RollInto(session, 10f, events);
        _out.WriteLine($"knock at {session.LastKnock.Speed:F1} m/s: damage {session.Damage:F2}, speed after {after:F1} m/s, power {session.Car.Drivetrain.Engine.PowerFactor:F2}");
        Assert.Equal(SessionPhase.Driving, session.Phase);
        Assert.Contains(RaceEvent.Knocked, events);
        Assert.DoesNotContain(RaceEvent.Crashed, events);
        Assert.Equal(KnockKind.Solid, session.LastKnock.Kind);
        Assert.True(session.LastKnock.Point.Z > 1f, $"struck at {session.LastKnock.Point}: the nose should take it");
        Assert.InRange(session.Damage, 0.1f, 0.4f);
        Assert.True(after < 1f, $"the car should bounce back off the tree, not run on at {after:F1} m/s");
        // Clear of the trunk: the car's nose is not inside the tree's box.
        var nose = session.Car.Position + session.Car.Forward * session.Car.Spec.BodyFront;
        Assert.True(Vector3.Dot(tree.Position - nose, session.Car.Forward) > 0.4f, "the nose sank into the tree");
    }

    /// <summary>Knock after knock the damage adds up to a write-off: the third knock just under the crash speed is the crash.</summary>
    [Fact]
    public void Repeated_knocks_wreck_the_car_and_the_crash_repairs_it()
    {
        var (_, session, _) = TreeAhead(15f);
        var events = new List<RaceEvent>();
        var knocks = 0;
        for (var i = 0; i < 6 && session.Phase == SessionPhase.Driving; i++)
        {
            RollInto(session, 13.5f, events);
            if (session.Phase == SessionPhase.Driving) knocks++;
            _out.WriteLine($"run {i + 1}: phase {session.Phase}, damage {session.Damage:F2}");
        }
        Assert.Equal(SessionPhase.Replay, session.Phase);
        Assert.Equal(CrashReason.HitTree, session.LastCrash);
        Assert.InRange(knocks, 2, 4);
        for (var i = 0; i < 60 * 11; i++) session.Step(DriverInputs.Idle, Dt);
        Assert.Equal(SessionPhase.StartEngine, session.Phase);
        Assert.Equal(0f, session.Damage);
        Assert.Equal(1f, session.Car.Drivetrain.Engine.PowerFactor);
    }

    /// <summary>A cow is a body: the car shoves it aside and does not pass through it, and it moos (0.9.1).</summary>
    [Fact]
    public void A_cow_is_shoved_aside_not_driven_through()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.MediumNoTraffic);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        var cow = session.Herd.Cows.First(c => !track.Props.Any(o => o.Solid && Vector3.Distance(o.Position, c.Position) < 25f));
        var home = cow.Position;
        // Come at its side from 15 m at 7 m/s (25 km/h): under the speed that makes it shy.
        var heading = Herd.Heading(cow);
        var across = Vector3.Normalize(new Vector3(heading.Y, 0f, -heading.X));
        var start = new RoadFrame(cow.Position - across * 15f, across, MathUtil.Up, Vector3.Cross(MathUtil.Up, across), 0f, 5.5f, SurfaceType.Road, SegmentKind.Straight, 0);
        session.Car.Reset(start, track.StartCursor);
        session.Car.LaunchAt(7f);
        var events = new List<RaceEvent>();
        var deepest = 0f;
        for (var i = 0; i < 60 * 5; i++)
        {
            session.Step(new DriverInputs { Throttle = 0.2f, Gear = DriverInputs.NoGearChange }, Dt);
            events.AddRange(session.Events);
            var car = Footprint.Of(session.Car.Position, session.Car.Forward, session.Car.Spec.BodyFront, session.Car.Spec.BodyRear, session.Car.Spec.BodyHalfWidth);
            if (car.Overlaps(Herd.Plan(cow), out _, out var depth, out _)) deepest = MathF.Max(deepest, depth);
        }
        _out.WriteLine($"cow moved {Vector3.Distance(home, cow.Position):F1} m; deepest overlap {deepest:F2} m; damage {session.Damage:F2}");
        Assert.Equal(SessionPhase.Driving, session.Phase);
        Assert.Contains(RaceEvent.CowHit, events);
        Assert.True(Vector3.Distance(home, cow.Position) > 1f, "the cow should have been shoved");
        Assert.True(deepest < 0.35f, $"the car sank {deepest:F2} m into the cow");
    }

    /// <summary>Drones keep their distance: one coming up behind a car stopped in its lane stops behind it (0.9.1).</summary>
    [Fact]
    public void A_drone_stops_behind_a_car_stopped_in_its_lane()
    {
        var track = OriginalInspiredTrack.Build();
        var session = new RaceSession(track, new VehicleSpec(), RaceOptions.Medium);
        session.ChooseTransmission(TransmissionMode.Automatic);
        session.Step(new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true }, Dt);
        var drone = session.Traffic!.Cars.First(c => !c.Oncoming && c.Path.Kind == PathKind.Speed && c.Distance > 60f && c.Distance < c.Path.Length - 400f);
        var at = drone.Distance + 120f;
        session.Car.Reset(drone.Path.FrameAt(at), new PathCursor(drone.Path, drone.Path.FrameIndexAt(at)));
        session.Car.Displace(drone.Path.FrameAt(at).Lateral * drone.LaneOffset);
        session.Car.Drivetrain.Engine.Start();
        var events = new List<RaceEvent>();
        for (var i = 0; i < 60 * 15; i++)
        {
            session.Step(new DriverInputs { Brake = 1f, Gear = DriverInputs.NoGearChange }, Dt);
            events.AddRange(session.Events);
            if (i % 30 == 0) _out.WriteLine($"t={i * Dt:F1} gap {Vector3.Distance(drone.Position, session.Car.Position) - Traffic.HalfLength - session.Car.Spec.BodyRear:F1} drone {drone.Speed:F1} car {session.Car.Speed:F1}");
        }
        var gap = Vector3.Distance(drone.Position, session.Car.Position) - Traffic.HalfLength - session.Car.Spec.BodyRear;
        _out.WriteLine($"drone at {drone.Speed:F1} m/s, gap {gap:F1} m");
        Assert.DoesNotContain(RaceEvent.Crashed, events);
        Assert.DoesNotContain(RaceEvent.Knocked, events);
        Assert.True(drone.Speed < 0.5f, $"the drone still rolls at {drone.Speed:F1} m/s");
        Assert.InRange(gap, 2f, 12f);
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
        // Start 22 m out so the tight circle never reaches the column in the middle.
        var onPad = new RoadFrame(pad.Centre + new Vector3(22f, 0f, 0f), frame.Tangent, MathUtil.Up, Vector3.Cross(MathUtil.Up, frame.Tangent), 0f, 5.5f, SurfaceType.Pad, SegmentKind.Straight, 0);
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
