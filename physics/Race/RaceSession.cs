using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// The rules of a single credit: engine start, countdown, route choice at the fork, checkpoints and
/// lap bonuses, off-road and wrong-way handling, traffic, knocks and damage, crashes with an instant replay,
/// return points, and the grudge match against the ghost of the best lap. Engine-agnostic so the whole rule
/// set can be driven headless in tests. The knocks are in RaceSession.Knocks.cs.
/// </summary>
public sealed partial class RaceSession
{
    private const float ReplayRecordHz = 30f;
    private const float WrongWayGrace = 1.6f;
    private const float RespawnClearance = 45f;
    /// <summary>
    /// Metres into a branch before the route is announced: the stunt route's curve off the fork hill parts from the
    /// speed road by a full road width only about 38 m on, and until then the cursor may sit on either.
    /// </summary>
    private const float RouteChoiceDistance = 45f;

    private readonly List<RaceEvent> _events = new();
    private readonly List<PoseSample> _replay = new();
    private readonly List<PoseSample> _lapSamples = new();
    private float _replayRecordAccumulator;
    private float _lapSampleAccumulator;
    private float _wrongWayTime;
    private float _lastDistance;
    private TrackPath? _lastPath;
    private bool _checkpointThisLap;
    private bool _lapOpened;
    private float _gracePeriod;
    private float _messageExpiry = float.PositiveInfinity;
    private readonly HashSet<int> _knockedSigns = new();
    private readonly Dictionary<int, float> _softHitTimes = new();

    public RaceSession(TrackGeometry track, VehicleSpec spec, RaceOptions? options = null)
    {
        Track = track;
        Options = options ?? new RaceOptions();
        Car = new VehicleSim(spec, track);
        ReturnPath = track.Common;
        ReturnDistance = 0f;
        Phase = SessionPhase.SelectTransmission;
        TimeRemaining = Options.InitialTime;
        _lastPath = track.Common;
        if (Options.TrafficSameDirection + Options.TrafficOncoming > 0)
        {
            Traffic = new Traffic(track, Options.TrafficSameDirection, Options.TrafficOncoming);
        }
        Herd = new Herd(track);
    }

    public TrackGeometry Track { get; }
    public VehicleSim Car { get; }
    public RaceOptions Options { get; }
    /// <summary>Drone cars, or null when the options ask for an empty road.</summary>
    public Traffic? Traffic { get; }
    /// <summary>The cows in the fields.</summary>
    public Herd Herd { get; }

    public SessionPhase Phase { get; private set; }
    public float TimeRemaining { get; private set; }
    public float Elapsed { get; private set; }
    public float LapTime { get; private set; }
    public float BestLapTime { get; private set; } = float.PositiveInfinity;
    /// <summary>The best lap of this credit alone (a loaded ghost's time does not count), +inf before one is done; for the high-score table.</summary>
    public float CreditBestLap { get; private set; } = float.PositiveInfinity;
    public RouteKind CreditBestRoute { get; private set; } = RouteKind.Undecided;
    public int Lap { get; private set; } = 1;
    public int CompletedLaps { get; private set; }
    public long Score { get; private set; }
    public RouteKind Route { get; private set; } = RouteKind.Undecided;
    public float OffRoadTimer { get; private set; }
    public bool OffRoad { get; private set; }
    public bool WrongWay { get; private set; }
    public string Message { get; private set; } = "";
    public CrashReason LastCrash { get; private set; } = CrashReason.None;
    public int CrashCount { get; private set; }
    public TrackPath ReturnPath { get; private set; }
    public float ReturnDistance { get; private set; }
    public float ReplayTime { get; private set; }
    public IReadOnlyList<PoseSample> Replay => _replay;
    /// <summary>Events raised during the last <see cref="Step"/> (audio and UI hooks).</summary>
    public IReadOnlyList<RaceEvent> Events => _events;
    /// <summary>Distance along the current path, for the HUD and tests.</summary>
    public float PathDistance => Car.Cursor.Distance;
    public PathKind CurrentPathKind => Car.Cursor.Path.Kind;

    /// <summary>The lap the ghost drives: the best lap so far, this credit or a loaded one.</summary>
    public LapRecording? BestLap { get; private set; }
    public bool GhostVisible { get; private set; }
    public Vector3 GhostPosition { get; private set; }
    public Quaternion GhostOrientation { get; private set; } = Quaternion.Identity;
    public float GhostSteer { get; private set; }
    /// <summary>True once the lap in progress has a ghost to race.</summary>
    public bool GrudgeMatch => BestLap is not null;
    /// <summary>Indices into <c>Track.Props</c> of the signs the car has knocked over this credit.</summary>
    public IReadOnlySet<int> KnockedSigns => _knockedSigns;
    /// <summary>Index of the soft prop hit during the last step (cow or sign), or -1.</summary>
    public int LastSoftHit { get; private set; } = -1;

    public void ChooseTransmission(TransmissionMode mode)
    {
        if (Phase != SessionPhase.SelectTransmission) return;
        Car.Drivetrain.Mode = mode;
        Phase = SessionPhase.StartEngine;
        SetMessage("TURN KEY TO START ENGINE", float.PositiveInfinity);
    }

    /// <summary>Installs a previously recorded best lap as the ghost to beat (from a save file).</summary>
    public void LoadBestLap(LapRecording recording)
    {
        BestLap = recording;
        BestLapTime = recording.Duration;
    }

    /// <summary>Skips the instant replay; a no-op in other phases.</summary>
    public void Abort()
    {
        if (Phase == SessionPhase.Replay) EndReplay();
        else if (Phase == SessionPhase.Driving && OffRoad) ReturnToLastPoint(resetTimer: false);
    }

    public void Step(DriverInputs input, float dt)
    {
        _events.Clear();
        switch (Phase)
        {
            case SessionPhase.SelectTransmission:
            case SessionPhase.Finished:
                return;

            case SessionPhase.Replay:
                ReplayTime += dt;
                if (ReplayTime >= Options.ReplaySeconds) EndReplay();
                return;

            case SessionPhase.StartEngine:
                Traffic?.Step(dt, PlayerOnRoad());
                Track.Train?.Step(dt);
                Herd.Step(dt, Elapsed, Car.Position, Car.Velocity);
                if (input.Ignition)
                {
                    var starting = new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true, Clutch = 1f };
                    Car.Step(starting, dt);
                    if (Car.Drivetrain.Engine.Running)
                    {
                        Phase = SessionPhase.Driving;
                        Message = "";
                        _gracePeriod = 0.5f;
                        _events.Add(RaceEvent.EngineStarted);
                    }
                }
                TickClock(dt);
                return;

            case SessionPhase.Driving:
                StepDriving(input, dt);
                return;
        }
    }

    private void StepDriving(DriverInputs input, float dt)
    {
        Traffic?.Step(dt, PlayerOnRoad());
        Track.Train?.Step(dt);
        Herd.Step(dt, Elapsed, Car.Position, Car.Velocity);
        if (Herd.Mooed >= 0) _events.Add(RaceEvent.CowMooed);
        LastSoftHit = -1;
        var wasRunning = Car.Drivetrain.Engine.Running;
        Car.Step(input, dt);
        if (wasRunning && !Car.Drivetrain.Engine.Running)
        {
            _events.Add(RaceEvent.EngineStalled);
            SetMessage("STALLED - TURN KEY", float.PositiveInfinity);
        }
        else if (!wasRunning && Car.Drivetrain.Engine.Running)
        {
            _events.Add(RaceEvent.EngineStarted);
            Message = "";
        }

        if (_gracePeriod > 0f) _gracePeriod -= dt;
        TickClock(dt);
        LapTime += dt;
        RecordReplay(dt);
        RecordLap(dt);
        TrackProgress();
        UpdateGhost();
        UpdateOffRoad(dt);
        UpdateWrongWay(dt);
        CheckSoftProps();
        DetectCrash();
        if (Phase != SessionPhase.Driving) return;

        if (TimeRemaining <= 0f)
        {
            Phase = SessionPhase.Finished;
            SetMessage("OUT OF TIME", float.PositiveInfinity);
            _events.Add(RaceEvent.TimeOut);
        }
    }

    /// <summary>The car as the drones see it (<see cref="RoadUser"/>), or null when it is off every road.</summary>
    private RoadUser? PlayerOnRoad()
    {
        var hit = Car.BodyHit;
        return hit.HasSurface && hit.Path is not null ? new RoadUser(hit.Path, hit.Frame.Distance, hit.Lateral, Car.Velocity) : null;
    }

    private void TickClock(float dt)
    {
        Elapsed += dt;
        TimeRemaining = MathF.Max(0f, TimeRemaining - dt);
        if (Elapsed >= _messageExpiry)
        {
            Message = "";
            _messageExpiry = float.PositiveInfinity;
        }
    }

    /// <summary>Shows a message; it clears itself after <paramref name="seconds"/> unless infinite.</summary>
    private void SetMessage(string text, float seconds = 3f)
    {
        Message = text;
        _messageExpiry = float.IsPositiveInfinity(seconds) ? float.PositiveInfinity : Elapsed + seconds;
    }

    private void RecordReplay(float dt)
    {
        _replayRecordAccumulator += dt;
        if (_replayRecordAccumulator < 1f / ReplayRecordHz) return;
        _replayRecordAccumulator = 0f;
        _replay.Add(new PoseSample(Elapsed, Car.Position, Car.Orientation, Car.SteerAngle));
        var keep = (int)((Options.ReplaySeconds + 2f) * ReplayRecordHz);
        if (_replay.Count > keep) _replay.RemoveRange(0, _replay.Count - keep);
    }

    private void RecordLap(float dt)
    {
        if (!_lapOpened) return;
        _lapSampleAccumulator += dt;
        if (_lapSampleAccumulator < 1f / LapRecording.SampleHz) return;
        _lapSampleAccumulator = 0f;
        _lapSamples.Add(new PoseSample(LapTime, Car.Position, Car.Orientation, Car.SteerAngle));
    }

    private void UpdateGhost()
    {
        if (BestLap is not null && _lapOpened && BestLap.TryPose(LapTime, out var position, out var orientation, out var steer))
        {
            GhostVisible = true;
            GhostPosition = position;
            GhostOrientation = orientation;
            GhostSteer = steer;
        }
        else
        {
            GhostVisible = false;
        }
    }

    private void TrackProgress()
    {
        var cursor = Car.Cursor;
        var path = cursor.Path;
        var distance = cursor.Distance;

        if (!ReferenceEquals(path, _lastPath))
        {
            // Onto the road that follows the last one, counting starts at its beginning. A hop between the branches,
            // which share their first metres, or back across the fork or the merge carries the distance over: the
            // cursor follows the road surface on top, which just past the fork can be the other branch for a few
            // metres, and starting from zero there scored the same road twice and replayed the start line.
            var followsOn = _lastPath is null || _lastPath.Next.Contains(path);
            _lastPath = path;
            _lastDistance = followsOn ? 0f : distance;
        }

        if (distance > _lastDistance && Car.OnRoad && !WrongWay)
        {
            Score += (long)((distance - _lastDistance) * Options.PointsPerMetre);
        }

        // The branches share their first metres and the cursor may hop between them; the route is announced
        // once the car is clearly on one of them, past the last metre where the two roads overlap.
        if (path.Kind is PathKind.Speed or PathKind.Stunt && distance > RouteChoiceDistance)
        {
            var route = path.Kind == PathKind.Speed ? RouteKind.Speed : RouteKind.Stunt;
            if (Route != route)
            {
                Route = route;
                SetMessage(route == RouteKind.Speed ? "SPEED TRACK" : "STUNT TRACK");
                _events.Add(RaceEvent.RouteChosen);
            }
        }

        foreach (var marker in path.Markers)
        {
            if (marker.Distance <= _lastDistance || marker.Distance > distance) continue;
            switch (marker.Kind)
            {
                case MarkerKind.StartFinish:
                    // The car starts a little behind the line: the first crossing opens the lap, later ones close it.
                    if (!_lapOpened)
                    {
                        _lapOpened = true;
                        LapTime = 0f;
                        _lapSamples.Clear();
                        _lapSampleAccumulator = 0f;
                        if (BestLap is not null) SetMessage($"GRUDGE MATCH - BEAT {FormatLapTime(BestLapTime)}", 4f);
                    }
                    else if (!WrongWay)
                    {
                        CompleteLap();
                    }
                    break;
                case MarkerKind.Checkpoint:
                    if (!_checkpointThisLap && !WrongWay)
                    {
                        _checkpointThisLap = true;
                        TimeRemaining += Options.CheckpointBonus;
                        _events.Add(RaceEvent.CheckpointPassed);
                        SetMessage($"CHECKPOINT +{Options.CheckpointBonus:F0}");
                    }
                    break;
                case MarkerKind.ReturnPoint:
                    if (!WrongWay)
                    {
                        ReturnPath = path;
                        ReturnDistance = marker.Distance;
                    }
                    break;
            }
        }
        _lastDistance = distance;
    }

    private void CompleteLap()
    {
        if (_checkpointThisLap && !WrongWay)
        {
            CompletedLaps++;
            TimeRemaining += Options.LapBonus;
            if (LapTime < CreditBestLap)
            {
                CreditBestLap = LapTime;
                CreditBestRoute = Route;
            }
            if (LapTime < BestLapTime)
            {
                BestLapTime = LapTime;
                BestLap = new LapRecording(Route, LapTime, new List<PoseSample>(_lapSamples));
                _events.Add(RaceEvent.NewBestLap);
                SetMessage($"NEW RECORD {FormatLapTime(LapTime)}  +{Options.LapBonus:F0}", 4f);
            }
            else
            {
                SetMessage($"LAP {CompletedLaps} {FormatLapTime(LapTime)}  +{Options.LapBonus:F0}", 4f);
            }
            _events.Add(RaceEvent.LapCompleted);
        }
        else
        {
            SetMessage("LAP NOT COUNTED");
        }
        Lap++;
        LapTime = 0f;
        _lapSamples.Clear();
        _lapSampleAccumulator = 0f;
        _checkpointThisLap = false;
        Route = RouteKind.Undecided;
        ReturnPath = Track.Common;
        ReturnDistance = Track.Common.Markers.FirstOrDefault(m => m.Kind == MarkerKind.StartFinish).Distance;
    }

    public static string FormatLapTime(float seconds)
    {
        if (float.IsInfinity(seconds) || float.IsNaN(seconds)) return "-:--.-";
        var minutes = (int)(seconds / 60f);
        return $"{minutes}:{seconds - minutes * 60f:00.0}";
    }

    private void UpdateOffRoad(float dt)
    {
        var offRoadNow = !Car.OnRoad && !Car.Airborne && Car.BodyHit.Surface == SurfaceType.Grass;
        if (offRoadNow)
        {
            if (!OffRoad) _events.Add(RaceEvent.LeftRoad);
            OffRoad = true;
            OffRoadTimer += dt;
            SetMessage($"OFF ROAD {MathF.Ceiling(Options.OffRoadSeconds - OffRoadTimer):F0}", float.PositiveInfinity);
            if (OffRoadTimer >= Options.OffRoadSeconds)
            {
                _events.Add(RaceEvent.OffRoadTimeout);
                ReturnToLastPoint(resetTimer: false);
            }
        }
        else if (OffRoad && Car.OnRoad)
        {
            OffRoad = false;
            OffRoadTimer = 0f;
            if (Message.StartsWith("OFF ROAD", StringComparison.Ordinal)) Message = "";
        }
    }

    private void UpdateWrongWay(float dt)
    {
        if (Car.BodyHit.Surface == SurfaceType.Pad)
        {
            // Donuts on the skid pad face every way; the rule only applies on the road.
            _wrongWayTime = 0f;
            if (WrongWay) Message = "";
            WrongWay = false;
            return;
        }
        var frame = Car.BodyHit.Frame;
        var heading = Vector3.Dot(Car.Forward, frame.Tangent);
        var reversing = heading < -0.3f && MathF.Abs(Car.Speed) > 2f;
        _wrongWayTime = reversing ? _wrongWayTime + dt : 0f;
        var wrong = _wrongWayTime > WrongWayGrace;
        if (wrong && !WrongWay) _events.Add(RaceEvent.WrongWay);
        WrongWay = wrong;
        if (WrongWay) SetMessage("WRONG WAY", float.PositiveInfinity);
        else if (Message == "WRONG WAY") Message = "";
    }

    private void DetectCrash()
    {
        if (_gracePeriod > 0f) return;

        var reason = CrashReason.None;
        if (Car.LandingImpact > Options.LandingCrashSpeed) reason = CrashReason.HardLanding;
        else if (Car.BodyGrounded) reason = CrashReason.RolledOver;
        else if (Car.BodyHit.Surface == SurfaceType.Chasm && Car.Position.Y < Car.BodyHit.Frame.Origin.Y - 2.5f) reason = CrashReason.FellOffBridge;
        else if (Car.Position.Y < Track.Terrain.Height(Car.Position.X, Car.Position.Z) - 6f) reason = CrashReason.FellOffBridge;
        else if (HitBridgeEdge()) reason = CrashReason.FellOffBridge;
        else if (HitTrain()) reason = CrashReason.HitTrain;
        else if (LandedHard()) reason = CrashReason.HardLanding;
        else reason = Knocks();

        if (reason == CrashReason.None) return;
        Crash(reason);
    }

    /// <summary>
    /// A car that reaches the landing ramp with its wheels below the deck has hit the edge of the open bridge.
    /// The raycast wheels would otherwise pass under the deck and "land" a metre later.
    /// </summary>
    private bool HitBridgeEdge()
    {
        var hit = Car.BodyHit;
        if (hit.Frame.Kind != SegmentKind.RampDown) return false;
        var frames = Car.Cursor.Path.Frames;
        var start = Car.Cursor.FrameIndex;
        while (start > 0 && frames[start - 1].Kind == SegmentKind.RampDown) start--;
        var intoRamp = hit.Frame.Distance - frames[start].Distance;
        return intoRamp < 3f && hit.Height < RaceOptions.BridgeEdgeClearance;
    }

    private bool HitTrain()
    {
        var train = Track.Train;
        if (train is null || !train.Running) return false;
        var ground = Track.Terrain.Height(Car.Position.X, Car.Position.Z);
        var forward = Car.Forward;
        return train.Hits(Car.Position, ground) || train.Hits(Car.Position + forward * 2.2f, ground) || train.Hits(Car.Position - forward * 2.2f, ground);
    }

    /// <summary>Signs and crossbucks tip over when the car runs into them; nothing else happens. (Cows are bodies: see <see cref="Knocks"/>.)</summary>
    private void CheckSoftProps()
    {
        var p = Car.Position;
        var props = Track.Props;
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            if (!prop.Soft || prop.Kind == PropKind.Cow) continue;
            var dx = prop.Position.X - p.X;
            var dz = prop.Position.Z - p.Z;
            if (dx * dx + dz * dz > 1.5f * 1.5f || MathF.Abs(prop.Position.Y - p.Y) > 2.5f) continue;
            if (_knockedSigns.Add(i))
            {
                LastSoftHit = i;
                _events.Add(RaceEvent.SignHit);
            }
        }
    }

    private void Crash(CrashReason reason)
    {
        // A crash wrecks the car whatever it had taken before; it is repaired when it is set down again.
        Damage = 1f;
        LastCrash = reason;
        CrashCount++;
        Phase = SessionPhase.Replay;
        ReplayTime = 0f;
        GhostVisible = false;
        SetMessage("INSTANT REPLAY", float.PositiveInfinity);
        _events.Add(RaceEvent.Crashed);
    }

    private void EndReplay()
    {
        ReturnToLastPoint(resetTimer: false);
        Repair();
        Phase = SessionPhase.StartEngine;
        SetMessage("TURN KEY TO START ENGINE", float.PositiveInfinity);
    }

    private void ReturnToLastPoint(bool resetTimer)
    {
        var frame = ReturnPath.FrameAt(ReturnDistance);
        var cursor = new PathCursor(ReturnPath, ReturnPath.FrameIndexAt(ReturnDistance));
        Car.Reset(frame, cursor);
        Traffic?.ClearAround(Car.Position, RespawnClearance);
        _lastPath = ReturnPath;
        _lastDistance = ReturnDistance;
        OffRoad = false;
        OffRoadTimer = 0f;
        WrongWay = false;
        _wrongWayTime = 0f;
        _gracePeriod = 1.0f;
        _replay.Clear();
        if (resetTimer) TimeRemaining = Options.InitialTime;
        if (Phase == SessionPhase.Driving)
        {
            // Returned without a crash (off-road timeout): the engine is left running, like a re-spot on the cabinet.
            Car.Drivetrain.Engine.Start();
            Message = "";
        }
    }
}

public enum SessionPhase
{
    SelectTransmission,
    StartEngine,
    Driving,
    Replay,
    Finished,
}

public enum RouteKind
{
    Undecided,
    Speed,
    Stunt,
}

public enum CrashReason
{
    None,
    HardLanding,
    RolledOver,
    FellOffBridge,
    HitTree,
    HitBuilding,
    HitTrain,
    HitTraffic,
    /// <summary>Ran into a cow too fast for it to be shoved aside.</summary>
    HitCow,
}

public enum RaceEvent
{
    EngineStarted,
    EngineStalled,
    RouteChosen,
    CheckpointPassed,
    LapCompleted,
    NewBestLap,
    LeftRoad,
    OffRoadTimeout,
    WrongWay,
    Crashed,
    TimeOut,
    /// <summary>The car ran into a cow (it moos; no crash).</summary>
    CowHit,
    /// <summary>The car knocked a road sign over (a clang; no crash).</summary>
    SignHit,
    /// <summary>The car took a knock it survived (<see cref="RaceSession.LastKnock"/>): a crunch, a dent.</summary>
    Knocked,
    /// <summary>A cow mooed on its own (<see cref="Herd.Mooed"/>).</summary>
    CowMooed,
}

/// <summary>Difficulty table from the cabinet's custom game options (seconds). Medium is the factory setting.</summary>
public sealed record RaceOptions
{
    public float InitialTime { get; init; } = 90f;
    public float LapBonus { get; init; } = 45f;
    public float CheckpointBonus { get; init; } = 45f;
    public float ReplaySeconds { get; init; } = 10f;
    public float OffRoadSeconds { get; init; } = 10f;
    public float PointsPerMetre { get; init; } = 10f;
    /// <summary>
    /// Normal approach speed (m/s) above which a landing wrecks the car: a drop of 5 m. Until 0.9.1 it was 7.5 m/s and
    /// every landing past it a crash; now the landings between <see cref="LandingDamageSpeed"/> and it bottom the car
    /// out and dent it instead.
    /// </summary>
    public float LandingCrashSpeed { get; init; } = 10f;
    /// <summary>Normal approach speed (m/s) above which a landing damages the car: a drop of a little over 2 m.</summary>
    public float LandingDamageSpeed { get; init; } = 6.5f;
    /// <summary>
    /// Closing speed (m/s) into a solid obstacle (a tree, a wall, a pier, a drone) above which the car is wrecked and
    /// the replay runs: 54 km/h, about where a real car's front end is crushed past driving home. Slower knocks bounce
    /// the car off with a dent (<see cref="RaceSession.Damage"/>); until 0.9.1 any touch was a crash.
    /// </summary>
    public float ImpactCrashSpeed { get; init; } = 15f;
    /// <summary>The same for a cow (72 km/h): it gives way, and below this the car shoves it aside.</summary>
    public float CowCrashSpeed { get; init; } = 20f;
    /// <summary>Drone cars driving the player's way on the start straight and the speed route.</summary>
    public int TrafficSameDirection { get; init; } = 5;
    /// <summary>Drone cars coming the other way in the other lane.</summary>
    public int TrafficOncoming { get; init; } = 3;
    /// <summary>Minimum height of the car's centre above the landing deck at its leading edge; lower means the wheels hit the edge.</summary>
    public const float BridgeEdgeClearance = 0.42f;

    public static RaceOptions Easy => new() { InitialTime = 96f, LapBonus = 48f, CheckpointBonus = 48f };
    public static RaceOptions Medium => new();
    public static RaceOptions Hard => new() { InitialTime = 84f, LapBonus = 42f, CheckpointBonus = 42f };
    public static RaceOptions VeryHard => new() { InitialTime = 78f, LapBonus = 39f, CheckpointBonus = 39f };
    /// <summary>Medium rules on an empty road, for deterministic lap tests.</summary>
    public static RaceOptions MediumNoTraffic => new() { TrafficSameDirection = 0, TrafficOncoming = 0 };
}

public readonly record struct PoseSample(float Time, Vector3 Position, Quaternion Orientation, float SteerAngle);
