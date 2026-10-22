using System.Numerics;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// The "drone" cars of the original: traffic that keeps to its lane at a steady speed on the start straight
/// and the speed route, in both directions. Kinematic: they ride the road frames and never leave their lane,
/// so avoiding them is entirely the player's problem. They never enter the stunt route.
/// </summary>
public sealed class Traffic
{
    /// <summary>Lane centre offset from the road centre line; the road is two lanes wide.</summary>
    public const float LaneOffset = 2.75f;
    private const float RideHeight = 0.61f;

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
            var car = new TrafficCar { Oncoming = false, LaneOffset = LaneOffset, Speed = 20f + (float)random.NextDouble() * 4f, Colour = i % 3 };
            Place(car, s % loop);
            _cars.Add(car);
        }
        for (var i = 0; i < oncoming; i++)
        {
            var s = 700f + loop * i / MathF.Max(oncoming, 1);
            var car = new TrafficCar { Oncoming = true, LaneOffset = -LaneOffset, Speed = 21f + (float)random.NextDouble() * 5f, Colour = (i + 1) % 3 };
            Place(car, s % loop);
            _cars.Add(car);
        }
    }

    public IReadOnlyList<TrafficCar> Cars => _cars;

    /// <summary>Driving distance of the traffic loop: start straight, speed route, final stretch.</summary>
    public float LoopLength => _track.Common.Length + _track.Speed.Length + _track.Final.Length;

    public void Step(float dt)
    {
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

/// <summary>One drone: where it is on the road graph and its pose for rendering and collision.</summary>
public sealed class TrafficCar
{
    public TrackPath Path = null!;
    public float Distance;
    public float Speed;
    public float LaneOffset;
    public bool Oncoming;
    /// <summary>Paint index for the view (0..2).</summary>
    public int Colour;
    public Vector3 Position;
    public Vector3 Forward;
    public Quaternion Orientation = Quaternion.Identity;
    public RoadFrame Frame;
}
