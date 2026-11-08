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
    /// <summary>
    /// Metres before the fork at which a car without the pull for the loop is told to take the speed route (0.19): about
    /// six seconds at the speed the climb is taken, time to read it and stay left of the stunt route's turn-off.
    /// </summary>
    private const float LoopWarningBeforeFork = 150f;
    /// <summary>The message for a car that cannot make the loop (<see cref="LoopRunUp"/>).</summary>
    public const string LoopWarning = "CAN'T MAKE THE LOOP - TAKE SPEED TRACK";

    private readonly List<RaceEvent> _events = new();
    private readonly List<PoseSample> _replay = new();
    /// <summary>The drones' poses with every replay sample (0.35), so the replay shows the traffic as it was.</summary>
    private readonly List<TrafficPose[]> _replayTraffic = new();
    private bool _replayAnnounced;
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
        Car = new VehicleSim(spec, track) { Weather = Options.Weather };
        Weather = Options.Weather;
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
    /// <summary>
    /// Whether this car has the pull to make the stunt route's loop (<see cref="LoopRunUp"/>); when it has not, the
    /// session says so before the fork and on the stunt route, rather than let the player fall off the loop every lap.
    /// </summary>
    public bool CanMakeTheLoop => LoopRunUp.CanMakeTheLoop(Car.Spec);
    public RaceOptions Options { get; }
    /// <summary>The weather of this credit: the options' at the start, the menu's after <see cref="SetWeather"/>.</summary>
    public Weather Weather { get; private set; }

    /// <summary>Changes the weather during the credit (the menu's choice, 0.15): the car's grip follows at once.</summary>
    public void SetWeather(Weather weather)
    {
        Weather = weather;
        Car.Weather = weather;
    }
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
    /// <summary>The drones at each sample of <see cref="Replay"/> (0.35): empty arrays when there is no traffic.</summary>
    public IReadOnlyList<TrafficPose[]> ReplayTraffic => _replayTraffic;
    /// <summary>The first second after the blow plays at this rate in the replay, so the playback runs <see cref="ReplaySlowExtra"/> longer.</summary>
    public const float ReplaySlowSeconds = 1f;
    public const float ReplaySlowRate = 0.35f;
    public static float ReplaySlowExtra => ReplaySlowSeconds / ReplaySlowRate - ReplaySlowSeconds;
    /// <summary>Seconds the replay phase lasts: the wreck window, then the playback with its slow second.</summary>
    public float ReplayLength => Options.ImpactSeconds + Options.ReplaySeconds + ReplaySlowExtra;
    /// <summary>The playback is on: the wreck window is over and the crane's replay runs (its tune starts with it).</summary>
    public bool ReplayPlaying => Phase == SessionPhase.Replay && ReplayTime >= Options.ImpactSeconds;
    /// <summary>The session clock (<see cref="Elapsed"/>) at the last crash: where the replay's playback reaches the blow.</summary>
    public float CrashTime { get; private set; }
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
    /// <summary>
    /// Whether a lap faster than the best becomes the best lap: the ghost, the NEW RECORD and its fanfare (on by default).
    /// Off while a lap is driven that will not be kept (0.74): the game's attract driver without --save-best, any run
    /// under --no-ghost. Until 0.74 such a lap became the ghost for the rest of the credit unsaved: over a saved lap it
    /// took the car's own ghost and its BEST LAP label, and with none the HUD said GHOST over a ghost that was never drawn.
    /// </summary>
    public bool KeepsBestLaps { get; set; } = true;
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
                if (ReplayTime < Options.ImpactSeconds)
                {
                    // The wreck window (0.35): the simulation carries the wreck on through the crash's own moment (it used
                    // to freeze the car where it struck): the car rebounds, tumbles or slides to rest on its body, the
                    // drones drive on, and it is all recorded, so the replay shows the crash as it happened.
                    Traffic?.Step(dt, PlayerOnRoad());   // the drones brake for the wreck as for the car (without that one ran into it)
                    Track.Train?.Step(dt);
                    Herd.Step(dt, Elapsed, Car.Position, Car.Velocity);
                    for (var left = dt; left > 1e-5f;)
                    {
                        var piece = MathF.Min(left, 1f / 60f);
                        left -= piece;
                        // Out of gear, the brakes on: a wreck's wheels are jammed, it skids to a stop where the blow threw it
                        // (rolling free, it ran back six metres from the tree at a walking pace).
                        Car.Step(new DriverInputs { Gear = 0, Clutch = 1f, Brake = 1f }, piece);
                        Knocks();   // the wreck bounces off what it meets; a second blow is a knock, the car is wrecked already
                    }
                    Elapsed += dt;
                    RecordReplay(dt);
                }
                ReplayTime += dt;
                // The crash itself is shown first, from where the player sits (the explosion, the cracked glass of the
                // cabinet); the replay and its caption follow.
                if (!_replayAnnounced && ReplayTime >= Options.ImpactSeconds)
                {
                    _replayAnnounced = true;
                    SetMessage("INSTANT REPLAY", float.PositiveInfinity);
                }
                if (ReplayTime >= ReplayLength) EndReplay();
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
        // The car is stepped in pieces of at most a sixtieth of a second and the knocks and the crash looked for after
        // each (0.35): looked for once a frame, a long frame (a hitch of a tenth of a second at 100 km/h is 2.8 m) put a
        // tree deep inside the car's plan before the first look, and the car went through it with a sideways shove.
        var crash = CrashReason.None;
        for (var left = dt; left > 1e-5f && crash == CrashReason.None;)
        {
            var piece = MathF.Min(left, 1f / 60f);
            left -= piece;
            Car.Step(input, piece);
            if (_gracePeriod <= 0f) crash = CrashNow();
        }
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
        if (crash != CrashReason.None) Crash(crash);
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
        // The remainder is kept (0.35): zeroed, every long frame lost its surplus and the replay ran the approach fast.
        _replayRecordAccumulator -= 1f / ReplayRecordHz;
        _replay.Add(new PoseSample(Elapsed, Car.Position, Car.Orientation, Car.SteerAngle));
        // The drones with the car (0.35): until then the replay showed them frozen where the crash had left them.
        if (Traffic is null) _replayTraffic.Add(System.Array.Empty<TrafficPose>());
        else
        {
            var poses = new TrafficPose[Traffic.Cars.Count];
            for (var i = 0; i < poses.Length; i++)
            {
                var drone = Traffic.Cars[i];
                poses[i] = new TrafficPose(drone.Position, drone.Orientation, drone.Path, drone.Distance, drone.Speed, drone.Oncoming, drone.LaneOffset);
            }
            _replayTraffic.Add(poses);
        }
        var keep = (int)((Options.ReplaySeconds + 2f) * ReplayRecordHz);
        if (_replay.Count > keep)
        {
            _replayTraffic.RemoveRange(0, _replay.Count - keep);
            _replay.RemoveRange(0, _replay.Count - keep);
        }
    }

    private void RecordLap(float dt)
    {
        if (!_lapOpened) return;
        _lapSampleAccumulator += dt;
        if (_lapSampleAccumulator < 1f / LapRecording.SampleHz) return;
        // The remainder is kept (0.74), as RecordReplay's since 0.35: zeroed, a sample came every fourth frame at 72 fps
        // (0.0556 s apart) and every second one at 30 (0.0667 s), so a lap was recorded at 18 or 15 samples a second
        // and the n-th sample, which the versions before 0.74 read as n / 20 s into the lap, lay further on.
        _lapSampleAccumulator -= 1f / LapRecording.SampleHz;
        AddLapSample();
    }

    private void AddLapSample() => _lapSamples.Add(new PoseSample(LapTime, Car.Position, Car.Orientation, Car.SteerAngle));

    /// <summary>
    /// The lap clock starts again at the line, and the lap's recording with the car there at 0 s (0.74): its first sample
    /// used to come a twentieth of a second or more in, where the ghost then stood at the start.
    /// </summary>
    private void OpenLap()
    {
        LapTime = 0f;
        _lapSamples.Clear();
        _lapSampleAccumulator = 0f;
        AddLapSample();
    }

    /// <summary>
    /// The lap just completed as a recording, closed with the car on the line at the lap's own time (0.74): the last sample
    /// used to fall up to a sampling interval short of it, and the ghost stopped short of the line.
    /// </summary>
    private LapRecording RecordedLap()
    {
        if (_lapSamples.Count == 0 || _lapSamples[^1].Time < LapTime) AddLapSample();
        return new LapRecording(Route, LapTime, new List<PoseSample>(_lapSamples));
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
                SetMessage(route == RouteKind.Speed ? "SPEED TRACK" : CanMakeTheLoop ? "STUNT TRACK" : "STUNT TRACK - TOO SLOW FOR THE LOOP", CanMakeTheLoop ? 3f : 5f);
                _events.Add(RaceEvent.RouteChosen);
            }
        }

        // Climbing to the fork in a car that cannot make the loop: the advice, once a lap, while there is time to act on it.
        var warnAt = path.Length - LoopWarningBeforeFork;
        if (path.Kind == PathKind.Common && !CanMakeTheLoop && !WrongWay && _lastDistance < warnAt && distance >= warnAt)
        {
            SetMessage(LoopWarning, 5f);
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
                        OpenLap();
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
            if (KeepsBestLaps && LapTime < BestLapTime)
            {
                BestLapTime = LapTime;
                BestLap = RecordedLap();
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
        OpenLap();
        _checkpointThisLap = false;
        Route = RouteKind.Undecided;
        ReturnPath = Track.Common;
        ReturnDistance = Track.Common.Markers.FirstOrDefault(m => m.Kind == MarkerKind.StartFinish).Distance;
    }

    /// <summary>m:ss.s, or dashes for no time.</summary>
    public static string FormatLapTime(float seconds)
    {
        if (float.IsInfinity(seconds) || float.IsNaN(seconds)) return "-:--.-";
        var minutes = (int)(seconds / 60f);
        var rest = (seconds - minutes * 60f).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture);
        // The seconds are rounded to the tenth after the minutes are taken, and from 59.95 s into a minute they round up
        // to a whole one: carried into the minutes (0.74), 119.96 s reads 2:00.0 where it read 1:60.0.
        return rest == "60.0" ? $"{minutes + 1}:00.0" : $"{minutes}:{rest}";
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

    /// <summary>The crash the car's state calls for at this moment, or none; the knocks are taken on the way.</summary>
    private CrashReason CrashNow()
    {
        if (Car.LandingImpact > Options.LandingCrashSpeed) return CrashReason.HardLanding;
        // A nose-first touchdown at speed (0.35): a car that comes down pitched more than 20 degrees nose-down at over
        // 60 km/h buries its nose whatever its vertical speed (the overshoot of the drawbridge was a knock one run and a
        // roll-over the next); flat landings keep the vertical-speed rule.
        if (Car.LandingImpact > 0.5f && Car.Forward.Y < -0.34f && MathF.Abs(Car.Speed) > 16f) return CrashReason.HardLanding;
        if (Car.BodyGrounded) return CrashReason.RolledOver;
        // Called a metre below the gap line (2.5 m until 0.35): below 1.5 m the probe hands the car to the ground under the bridge.
        if (Car.BodyHit.Surface == SurfaceType.Chasm && Car.Position.Y < Car.BodyHit.Frame.Origin.Y - 1.0f) return CrashReason.FellOffBridge;
        if (Car.Position.Y < Track.Terrain.Height(Car.Position.X, Car.Position.Z) - 6f) return CrashReason.FellOffBridge;
        if (HitBridgeEdge()) return CrashReason.FellOffBridge;
        if (HitTrain()) return CrashReason.HitTrain;
        if (LandedHard()) return CrashReason.HardLanding;
        return Knocks();
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
                // A sawhorse is knocked flying (0.35; a wall that wrote the car off at 54 km/h before): a scuff of damage.
                if (prop.Kind == PropKind.Barrier) Damage = MathF.Min(Damage + 0.03f, 1f);
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
        CrashTime = Elapsed;
        GhostVisible = false;
        _replayAnnounced = false;
        Message = "";
        // The engine dies in the crash (0.35): the wreck is silent but for the fire, and no note hangs through the replay;
        // the wheels lock (a wrecked car does not roll on: its wheels still spinning at the road's speed drove it on
        // round the tree it had hit).
        Car.Drivetrain.Engine.Stop();
        foreach (var w in Car.Wheels) w.AngularVelocity = 0f;
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
        _replayTraffic.Clear();
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
    /// <summary>
    /// Seconds the crash is shown from where the player sits before the replay (0.35): the wreck window, in which the
    /// simulation carries the car on through the crash (it rebounds, tumbles, slides to rest) with the explosion and the
    /// cracked windscreen in view, as the cabinet showed its crash before its instant replay. The replay phase lasts
    /// this, then <see cref="ReplaySeconds"/> of playback with its slow second (<see cref="RaceSession.ReplayLength"/>).
    /// </summary>
    public float ImpactSeconds { get; init; } = 2.0f;
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
    /// <summary>
    /// The weather of the credit (0.12): the grip of the road and the grass under it (<see cref="WeatherModel"/>) and
    /// what the view draws. Dry by default, so the tests and the feel checklist run on the circuit as it always was;
    /// the game draws one at random for every credit (<see cref="WeatherModel.Draw"/>).
    /// </summary>
    public Weather Weather { get; init; } = Weather.Dry;
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

/// <summary>A drone's pose at a replay sample (0.35): where it was and, for running it on past the recording, its road, its place along it, its speed and its lane.</summary>
public readonly record struct TrafficPose(Vector3 Position, Quaternion Orientation, TrackPath Path, float Distance, float Speed, bool Oncoming, float LaneOffset);
