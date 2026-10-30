using System.Numerics;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// The "drone" cars of the original: traffic that keeps to its lane on the start straight and the speed route, in
/// both directions. Kinematic: they ride the road frames and never leave their lane, so avoiding them is the
/// player's problem; but since 0.9.1 they keep their distance like drivers do, slowing behind a slower drone or the
/// player's car in their lane (before, a faster drone drove through a slower one, and a car stopped in a lane was
/// run down at full speed), and a knock from the player changes their speed.
/// </summary>
public sealed class Traffic
{
    /// <summary>Lane centre offset from the road centre line; the road is two lanes wide.</summary>
    public const float LaneOffset = 2.75f;
    private const float RideHeight = 0.61f;
    /// <summary>Half a drone's length and width (m), for the gap ahead and the knocks.</summary>
    public const float HalfLength = 2.15f;
    public const float HalfWidth = 0.95f;
    /// <summary>A drone, kg.</summary>
    public const float Mass = 1250f;
    /// <summary>How far ahead a drone looks for something in its lane (m).</summary>
    private const float Lookahead = 70f;
    /// <summary>The gap (m, bumper to bumper) a drone stops at behind a standing car, and its time gap (s) behind a moving one.</summary>
    private const float StandingGap = 3f;
    private const float Headway = 1.2f;
    /// <summary>The deceleration a drone plans its stops with, and the hardest it brakes (m/s^2).</summary>
    private const float ComfortBraking = 4f;
    private const float Braking = 7f;
    private const float Accelerating = 1.6f;

    private readonly TrackGeometry _track;
    private readonly List<TrafficCar> _cars = new();

    public Traffic(TrackGeometry track, int sameDirection, int oncoming, int seed = 11)
    {
        _track = track;
        var random = new Random(seed);
        var loop = LoopLength;
        // Spread each group evenly round the loop, starting well ahead of the start line so the first
        // seconds of a credit are clear.
        for (var i = 0; i < sameDirection; i++)
        {
            var s = 260f + loop * i / MathF.Max(sameDirection, 1);
            var cruise = 20f + (float)random.NextDouble() * 4f;
            var car = new TrafficCar { Oncoming = false, LaneOffset = LaneOffset, Speed = cruise, CruiseSpeed = cruise, Colour = i % 3 };
            Place(car, s % loop);
            _cars.Add(car);
        }
        for (var i = 0; i < oncoming; i++)
        {
            var s = 700f + loop * i / MathF.Max(oncoming, 1);
            var cruise = 21f + (float)random.NextDouble() * 5f;
            var car = new TrafficCar { Oncoming = true, LaneOffset = -LaneOffset, Speed = cruise, CruiseSpeed = cruise, Colour = (i + 1) % 3 };
            Place(car, s % loop);
            _cars.Add(car);
        }
    }

    public IReadOnlyList<TrafficCar> Cars => _cars;

    /// <summary>Driving distance of the traffic loop: start straight, speed route, final stretch.</summary>
    public float LoopLength => _track.Common.Length + _track.Speed.Length + _track.Final.Length;

    public void Step(float dt) => Step(dt, null);

    /// <summary>
    /// Moves the drones on by <paramref name="dt"/>. Each keeps to its cruising speed unless something is in its lane
    /// ahead within <see cref="Lookahead"/> metres of road: the player's car (<paramref name="player"/>) or a slower
    /// drone; then it slows to keep its distance (<see cref="Follow"/>), braking at up to 7 m/s^2 (a driver's firm
    /// stop), and picks up again at 1.6 m/s^2. Distances are measured along the road, so a bend does not hide a car.
    /// </summary>
    public void Step(float dt, RoadUser? player)
    {
        var loop = LoopLength;
        foreach (var car in _cars)
        {
            var target = car.CruiseSpeed;
            var here = LoopPosition(car.Path, car.Distance);
            if (player is { } p && !float.IsNaN(LoopPosition(p.Path, p.Distance)) && MathF.Abs(p.Lateral - car.LaneOffset) < 1.8f)
            {
                var gap = Ahead(car, here, LoopPosition(p.Path, p.Distance), loop);
                if (gap < Lookahead) target = MathF.Min(target, Follow(gap, Vector3.Dot(p.Velocity, car.Forward)));
            }
            foreach (var other in _cars)
            {
                if (ReferenceEquals(other, car) || other.Oncoming != car.Oncoming) continue;
                var gap = Ahead(car, here, LoopPosition(other.Path, other.Distance), loop);
                if (gap < Lookahead) target = MathF.Min(target, Follow(gap, other.Speed));
            }
            car.Speed = MathUtil.MoveToward(car.Speed, target, (target < car.Speed ? Braking : Accelerating) * dt);
        }
        foreach (var car in _cars)
        {
            if (car.Oncoming)
            {
                car.Distance -= car.Speed * dt;
                while (car.Distance < 0f)
                {
                    car.Path = Previous(car.Path);
                    car.Distance += car.Path.Length;
                }
            }
            else
            {
                car.Distance += car.Speed * dt;
                while (car.Distance > car.Path.Length)
                {
                    car.Distance -= car.Path.Length;
                    car.Path = Next(car.Path);
                }
            }
            Pose(car);
        }
    }

    /// <summary>
    /// A knock from the player's car: <paramref name="impulse"/> (N s) changes the drone's speed by its share along the
    /// drone's way (it cannot leave its lane, so the rest is lost), never below a standstill.
    /// </summary>
    public static void Knock(TrafficCar car, Vector3 impulse)
    {
        car.Speed = MathF.Max(car.Speed + Vector3.Dot(impulse, car.Forward) / Mass, 0f);
    }

    /// <summary>Distance along the traffic loop (start straight, speed route, final stretch), NaN off it.</summary>
    private float LoopPosition(TrackPath path, float distance) => path.Kind switch
    {
        PathKind.Common => distance,
        PathKind.Speed => _track.Common.Length + distance,
        PathKind.Final => _track.Common.Length + _track.Speed.Length + distance,
        _ => float.NaN,
    };

    /// <summary>The gap (m of road, bumper to bumper) from a drone at loop position <paramref name="here"/> to something at <paramref name="there"/> ahead of it, its way round.</summary>
    private static float Ahead(TrafficCar car, float here, float there, float loop)
    {
        var along = car.Oncoming ? here - there : there - here;
        along %= loop;
        if (along < 0f) along += loop;
        return along - 2f * HalfLength;
    }

    /// <summary>
    /// The speed to drive at behind something moving at <paramref name="speedAhead"/> (negative when it comes this way)
    /// <paramref name="gap"/> metres ahead: no faster than one can still stop from, braking gently, before the standing
    /// gap; and settling over a second and a half to the time gap behind it.
    /// </summary>
    private static float Follow(float gap, float speedAhead)
    {
        var room = MathF.Max(gap - StandingGap, 0f);
        var stopping = speedAhead + MathF.Sqrt(2f * ComfortBraking * room);
        var settling = speedAhead + (gap - StandingGap - Headway * MathF.Max(speedAhead, 0f)) / 1.5f;
        return MathF.Max(MathF.Min(stopping, settling), 0f);
    }

    /// <summary>Moves every car that sits within <paramref name="radius"/> metres of a point further down its road, so a respawn is never into a bonnet.</summary>
    public void ClearAround(Vector3 point, float radius)
    {
        foreach (var car in _cars)
        {
            if (Vector3.Distance(car.Position, point) > radius) continue;
            var shift = radius * 1.5f;
            car.Distance += car.Oncoming ? -shift : shift;
            if (car.Oncoming)
            {
                while (car.Distance < 0f)
                {
                    car.Path = Previous(car.Path);
                    car.Distance += car.Path.Length;
                }
            }
            else
            {
                while (car.Distance > car.Path.Length)
                {
                    car.Distance -= car.Path.Length;
                    car.Path = Next(car.Path);
                }
            }
            Pose(car);
        }
    }

    private void Place(TrafficCar car, float loopDistance)
    {
        var s = loopDistance;
        if (s < _track.Common.Length)
        {
            car.Path = _track.Common;
            car.Distance = s;
        }
        else if (s < _track.Common.Length + _track.Speed.Length)
        {
            car.Path = _track.Speed;
            car.Distance = s - _track.Common.Length;
        }
        else
        {
            car.Path = _track.Final;
            car.Distance = MathUtil.Clamp(s - _track.Common.Length - _track.Speed.Length, 0f, _track.Final.Length);
        }
        Pose(car);
    }

    private TrackPath Next(TrackPath path) => path.Kind switch
    {
        PathKind.Common => _track.Speed,
        PathKind.Speed => _track.Final,
        _ => _track.Common,
    };

    private TrackPath Previous(TrackPath path) => path.Kind switch
    {
        PathKind.Final => _track.Speed,
        PathKind.Speed => _track.Common,
        _ => _track.Final,
    };

    private static void Pose(TrafficCar car)
    {
        var frame = car.Path.FrameAt(car.Distance);
        car.Frame = frame;
        car.Position = frame.Origin + frame.Lateral * car.LaneOffset + frame.Normal * RideHeight;
        var forward = car.Oncoming ? -frame.Tangent : frame.Tangent;
        car.Forward = forward;
        car.Orientation = LookRotation(forward, frame.Normal);
    }

    /// <summary>Orientation with the given forward (+Z) and up (+Y) axes, as the simulation's car uses them.</summary>
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        var m = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}

/// <summary>
/// The player's car as the drones see it: the road it is on, how far along it (m), how far right of the centre line
/// (m, as the drones' lane offsets), and its velocity.
/// </summary>
public readonly record struct RoadUser(TrackPath Path, float Distance, float Lateral, Vector3 Velocity);

/// <summary>One drone: where it is on the road graph and its pose for rendering and collision.</summary>
public sealed class TrafficCar
{
    public TrackPath Path = null!;
    public float Distance;
    /// <summary>Speed along its way now, m/s.</summary>
    public float Speed;
    /// <summary>The speed it keeps on an empty road.</summary>
    public float CruiseSpeed;
    public float LaneOffset;
    public bool Oncoming;
    /// <summary>Paint index for the view (0..2).</summary>
    public int Colour;
    public Vector3 Position;
    public Vector3 Forward;
    public Quaternion Orientation = Quaternion.Identity;
    public RoadFrame Frame;
}
