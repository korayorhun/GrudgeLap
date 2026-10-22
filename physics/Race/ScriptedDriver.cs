using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// A simple automatic driver: pure-pursuit steering along its lane, a speed plan that reads the curvature
/// ahead (with braking distance in mind) and the stunt windows (64 mph for the bridge and the loop), and
/// enough traffic sense to follow a slower drone and overtake it when the other lane is clear. Used by the
/// headless tests and by the game's attract mode / screenshot automation, so both exercise the same code.
/// </summary>
public sealed class ScriptedDriver
{
    private const float RoadFriction = 1.08f;
    private const float CornerMargin = 0.68f;       // fraction of the tyre limit the driver is happy to use
    private const float BrakingDecel = 4.5f;        // m/s^2 assumed when planning where to slow down (leaves grip for turning)
    private const float PlanHorizon = 160f;         // metres of road inspected ahead
    private const float Lane = 2.4f;                // metres right of the centre line on two-way roads
    private const float FollowGap = 24f;            // metres behind a slower drone before matching its speed
    private const float OvertakeGap = 55f;          // metres behind a slower drone at which the pass is set up
    private const float OncomingClearance = 340f;   // metres of empty opposite lane needed to start a pass
    private const float AbortClearance = 150f;      // an oncoming drone closer than this ends a pass in progress
    private int _lastGear;
    private float _lane = Lane;
    private TrafficCar? _passing;

    public bool PreferStunt { get; set; } = true;
    public float CruiseMph { get; set; } = 80f;
    /// <summary>Target speed chosen during the last call, for HUD/debug.</summary>
    public float PlannedMph { get; private set; }
    /// <summary>Lane offset the driver is steering for, for debug.</summary>
    public float LaneOffset => _lane;

    public DriverInputs Next(RaceSession session)
    {
        if (session.Phase == SessionPhase.StartEngine)
        {
            return new DriverInputs { Gear = DriverInputs.NoGearChange, Ignition = true };
        }
        if (session.Phase != SessionPhase.Driving) return DriverInputs.Idle;

        var car = session.Car;
        var path = car.Cursor.Path;
        var distance = car.Cursor.Distance;
        // The two branches share their first metres, so the cursor may sit on the wrong one just after the
        // fork; until the geometry has clearly separated we keep aiming at the preferred branch.
        var branchUndecided = path.Kind == PathKind.Common;
        if (path.Kind is PathKind.Speed or PathKind.Stunt && distance < 45f)
        {
            var preferred = PreferStunt ? PathKind.Stunt : PathKind.Speed;
            if (path.Kind != preferred)
            {
                path = session.Track.Paths.First(p => p.Kind == preferred);
            }
        }

        var trafficLimit = PlanTraffic(session, path, distance, out var laneTarget);
        const float dt = 1f / 60f;
        _lane = MathUtil.MoveToward(_lane, laneTarget, 1.6f * dt);

        var lookAhead = MathF.Max(8f, MathF.Abs(car.Speed) * 0.7f);
        var ahead = FrameAlong(path, distance + lookAhead, branchUndecided);
        var target = ahead.Origin + ahead.Lateral * _lane;
        var toTarget = target - car.Position;
        var angle = MathF.Atan2(Vector3.Dot(toTarget, car.Right), MathF.Max(Vector3.Dot(toTarget, car.Forward), 0.1f));
        angle -= (car.BodyHit.Lateral - _lane) * 0.015f;
        var steer = MathUtil.Clamp(angle / car.MaxSteerAngleAt(car.Speed), -1f, 1f);

        var targetSpeed = MathF.Min(PlanSpeed(car, branchUndecided), trafficLimit);
        PlannedMph = targetSpeed * MathUtil.MphPerMps;
        var error = targetSpeed - car.Speed;
        var gear = car.Drivetrain.Gear;
        var automatic = car.Drivetrain.Mode == TransmissionMode.Automatic;
        if (gear == 0 && !automatic)
        {
            _lastGear = 1;
            return new DriverInputs { Gear = 1, Steer = steer, Throttle = 0.3f };
        }
        var redline = car.Spec.Engine.RedlineRpm;
        var shiftUp = !automatic && car.Drivetrain.Engine.Rpm > redline * 0.9f && car.Drivetrain.ClutchLocked && gear == _lastGear && gear < car.Spec.ForwardGearCount;
        var shiftDown = !automatic && car.Drivetrain.Engine.Rpm < redline * 0.377f && car.Drivetrain.ClutchLocked && gear == _lastGear && gear > 1 && error > 0f;
        _lastGear = gear;

        return new DriverInputs
        {
            Steer = steer,
            Throttle = MathUtil.Clamp01(error * 0.5f + 0.1f),
            // Braking in the air only locks the wheels; wait for the landing.
            Brake = car.Airborne ? 0f : MathUtil.Clamp01(-error * 0.4f - 0.3f),
            Gear = DriverInputs.NoGearChange,
            ShiftUp = shiftUp,
            ShiftDown = shiftDown,
        };
    }

    /// <summary>
    /// Traffic sense on the two-way roads: keep to the right lane, hold station behind a slower drone, and
    /// move to the other lane to pass when nothing is coming. Returns the speed limit the traffic imposes.
    /// </summary>
    private float PlanTraffic(RaceSession session, TrackPath path, float distance, out float laneTarget)
    {
        laneTarget = path.Kind == PathKind.Stunt ? 0f : Lane;
        var traffic = session.Traffic;
        if (traffic is null || path.Kind == PathKind.Stunt) return float.MaxValue;

        var track = session.Track;
        var loop = traffic.LoopLength;
        var here = LoopDistance(track, path, distance);
        var limit = float.MaxValue;

        TrafficCar? slower = null;
        var slowerGap = float.MaxValue;
        var oncomingGap = float.MaxValue;
        foreach (var drone in traffic.Cars)
        {
            var gap = (LoopDistance(track, drone.Path, drone.Distance) - here + loop) % loop;
            if (drone.Oncoming)
            {
                if (gap < oncomingGap) oncomingGap = gap;
            }
            else if (gap < OvertakeGap && gap < slowerGap)
            {
                slower = drone;
                slowerGap = gap;
            }
        }

        if (_passing is not null)
        {
            // Stay in the passing lane until the drone is clearly behind; give up (and drop back) when something
            // comes the other way, or when the drone is nowhere near any more (a respawn, for instance).
            var passedBy = (here - LoopDistance(track, _passing.Path, _passing.Distance) + loop) % loop;
            var stillBehind = passedBy > loop - 80f;
            if (passedBy is > 12f and < 600f || !(stillBehind || passedBy <= 12f))
            {
                _passing = null;
            }
            else if (stillBehind && oncomingGap < AbortClearance)
            {
                _passing = null;
                slower ??= null;
            }
            else
            {
                laneTarget = -Lane;
                return limit;
            }
        }

        if (slower is null) return limit;
        if (oncomingGap > OncomingClearance)
        {
            _passing = slower;
            laneTarget = -Lane;
            return limit;
        }
        // Cannot pass: match the drone's speed before closing in.
        var closing = session.Car.Speed - slower.Speed;
        if (slowerGap < FollowGap + MathF.Max(closing, 0f) * 2.5f) limit = MathF.Max(slower.Speed - 1f, 5f);
        return limit;
    }

    /// <summary>Distance round the traffic loop (start straight, speed route, final stretch).</summary>
    private static float LoopDistance(TrackGeometry track, TrackPath path, float distance) => path.Kind switch
    {
        PathKind.Common => distance,
        PathKind.Speed => track.Common.Length + distance,
        PathKind.Final => track.Common.Length + track.Speed.Length + distance,
        _ => track.Common.Length + distance,
    };

    /// <summary>
    /// Frame at a distance along the path, following the path graph: the preferred branch at a fork, the
    /// only successor elsewhere. Clamps at the end of the graph (never happens on a closed circuit).
    /// </summary>
    private RoadFrame FrameAlong(TrackPath path, float distance, bool chooseBranch)
    {
        for (var hops = 0; hops < 4; hops++)
        {
            if (distance <= path.Length || path.Next.Count == 0) return path.FrameAt(MathF.Min(distance, path.Length));
            var next = chooseBranch && path.Kind == PathKind.Common
                ? path.Next.FirstOrDefault(p => p.Kind == (PreferStunt ? PathKind.Stunt : PathKind.Speed)) ?? path.Next[0]
                : path.Next[0];
            distance -= path.Length;
            path = next;
        }
        return path.FrameAt(MathF.Min(distance, path.Length));
    }

    /// <summary>
    /// Allowed speed now: the lowest of cruise, the stunt windows, and for every point ahead the corner speed
    /// raised by what braking distance still permits.
    /// </summary>
    private float PlanSpeed(VehicleSim car, bool chooseBranch)
    {
        var path = car.Cursor.Path;
        var here = car.Cursor.Distance;
        if (path.Kind is PathKind.Speed or PathKind.Stunt && here < 45f)
        {
            var preferred = PreferStunt ? PathKind.Stunt : PathKind.Speed;
            if (path.Kind != preferred) path = car.Track.Paths.First(p => p.Kind == preferred);
        }
        var allowed = CruiseMph / MathUtil.MphPerMps;
        var currentKind = car.BodyHit.Frame.Kind;
        if (IsStunt(currentKind)) allowed = MathF.Min(allowed, 64f / MathUtil.MphPerMps);

        const float step = 6f;
        var previous = FrameAlong(path, here, chooseBranch);
        for (var d = step; d <= PlanHorizon; d += step)
        {
            var frame = FrameAlong(path, here + d, chooseBranch);
            float limit;
            if (IsStunt(frame.Kind))
            {
                limit = 64f / MathUtil.MphPerMps;
            }
            else if (frame.Kind == SegmentKind.Hill)
            {
                limit = 55f / MathUtil.MphPerMps;
            }
            else if (frame.Kind == SegmentKind.Dip)
            {
                limit = 78f / MathUtil.MphPerMps;
            }
            else
            {
                // Horizontal curvature from the heading change between successive samples.
                var h0 = MathF.Atan2(previous.Tangent.X, previous.Tangent.Z);
                var h1 = MathF.Atan2(frame.Tangent.X, frame.Tangent.Z);
                var curvature = MathF.Abs(MathUtil.WrapAngle(h1 - h0)) / step;
                if (curvature < 1e-4f) limit = float.MaxValue;
                else
                {
                    var radius = 1f / curvature;
                    // Banking raises the limit: v^2 = g R (tan b + mu) / (1 - mu tan b).
                    var bank = MathF.Asin(MathUtil.Clamp(MathF.Abs(frame.Normal.X * frame.Tangent.Z - frame.Normal.Z * frame.Tangent.X), 0f, 1f));
                    var tanB = MathF.Tan(bank);
                    var mu = RoadFriction * car.Spec.TireGrip * CornerMargin;
                    var denominator = MathF.Max(1f - mu * tanB, 0.2f);
                    limit = MathF.Sqrt(MathUtil.Gravity * radius * (tanB + mu) / denominator);
                }
            }
            // What we may do now so that braking at BrakingDecel brings us to `limit` by that point.
            var reachable = MathF.Sqrt(limit * limit + 2f * BrakingDecel * MathF.Max(d - 8f, 0f));
            allowed = MathF.Min(allowed, reachable);
            previous = frame;
        }
        return allowed;

        static bool IsStunt(SegmentKind k) => k is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.Gap or SegmentKind.RampDown;
    }
}
