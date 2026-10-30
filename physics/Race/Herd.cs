using System.Numerics;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// The cows in the fields: each grazes where the track put it, moos now and then while the car is about, shies away
/// from a car coming at it fast, and takes a knock from one that does not stop: it is shoved along the ground,
/// staggers and trots off. A cow is a body of 650 kg to the car (until 0.9.1 the car drove through it).
/// </summary>
public sealed class Herd
{
    /// <summary>A grown dairy cow, kg.</summary>
    public const float CowMass = 650f;
    /// <summary>Plan of a cow, nose to tail and across the barrel (m): a Holstein is about 2.5 m long and 0.75 m wide.</summary>
    public const float Length = 2.45f;
    public const float Width = 0.75f;
    private const float TrotSpeed = 2.6f;
    private const float SkidDeceleration = 4.5f;
    private const float HearingRange = 70f;

    private readonly TerrainField _terrain;
    private readonly Random _random = new(23);
    private readonly List<Cow> _cows = new();

    public Herd(TrackGeometry track)
    {
        _terrain = track.Terrain;
        for (var i = 0; i < track.Props.Count; i++)
        {
            var prop = track.Props[i];
            if (prop.Kind != PropKind.Cow) continue;
            _cows.Add(new Cow
            {
                Prop = i,
                Position = prop.Position,
                YawDeg = prop.YawDeg,
                // The first moos come a few seconds apart, not all at once.
                NextMoo = 4f + 9f * (float)_random.NextDouble(),
            });
        }
    }

    public IReadOnlyList<Cow> Cows => _cows;

    /// <summary>Index into <see cref="Cows"/> of a cow that mooed on its own during the last step, or -1.</summary>
    public int Mooed { get; private set; } = -1;

    /// <summary>The cow's plan on the ground, for the knocks.</summary>
    public static Footprint Plan(Cow cow) => Footprint.OfProp(cow.Position, cow.YawDeg, Length, Width);

    /// <summary>
    /// Moves the herd on by <paramref name="dt"/>: knocked cows skid to a stop, startled ones trot away, the rest graze.
    /// A cow moos on its own every 12 to 35 s while the car is within earshot; one the car bears down on fast shies.
    /// </summary>
    public void Step(float dt, float time, Vector3 carPosition, Vector3 carVelocity)
    {
        Mooed = -1;
        for (var i = 0; i < _cows.Count; i++)
        {
            var cow = _cows[i];
            var toCow = cow.Position - carPosition;
            toCow.Y = 0f;
            var distance = toCow.Length();

            // A car coming at the cow within 14 m at more than 8 m/s (29 km/h) makes it shy to the side, away from the
            // car's line. Cows do not bolt like deer; they turn and trot.
            var closing = distance > 0.1f ? Vector3.Dot(carVelocity, toCow / distance) : 0f;
            if (cow.Startled <= 0f && distance < 14f && closing > 8f)
            {
                var line = Vector3.Normalize(carVelocity with { Y = 0f });
                var side = toCow - line * Vector3.Dot(toCow, line);
                var away = side.LengthSquared() > 0.01f ? Vector3.Normalize(side) : Vector3.Cross(Vector3.UnitY, line);
                Startle(cow, new Vector2(away.X, away.Z), 3f);
            }

            var velocity = cow.Velocity;
            var speed = velocity.Length();
            if (speed > TrotSpeed + 0.1f)
            {
                // Shoved faster than it can trot: it skids to a stop on its hooves first.
                velocity *= MathF.Max(speed - SkidDeceleration * dt, 0f) / speed;
            }
            else if (cow.Startled > 0f)
            {
                cow.Startled -= dt;
                // Turn towards the way out at up to 100 degrees a second and trot that way, picking up speed over a second.
                var turn = MathUtil.Clamp(SignedAngle(Heading(cow), cow.Flee), -100f * dt, 100f * dt);
                cow.YawDeg -= turn;
                velocity = Heading(cow) * MathF.Min(speed + TrotSpeed * dt, TrotSpeed);
            }
            else if (speed > 0f)
            {
                // The trot dies away.
                velocity *= MathF.Max(speed - 2f * dt, 0f) / speed;
            }
            cow.Velocity = velocity;
            cow.Spin *= MathF.Max(0f, 1f - 3f * dt);
            cow.YawDeg += cow.Spin * dt;
            var moved = cow.Position + new Vector3(velocity.X, 0f, velocity.Y) * dt;
            moved.Y = _terrain.Height(moved.X, moved.Z);
            cow.Position = moved;
            cow.Knocked = cow.Knocked > 0f ? cow.Knocked + dt : 0f;
            if (cow.Knocked > 4f) cow.Knocked = 0f;

            if (time >= cow.NextMoo)
            {
                cow.NextMoo = time + 12f + 23f * (float)_random.NextDouble();
                if (distance < HearingRange && Mooed < 0) Mooed = i;
            }
        }
    }

    /// <summary>
    /// A knock from the car: <paramref name="impulse"/> (N s, level) at <paramref name="point"/> shoves the cow along
    /// the ground and turns it, and it trots off once it has its legs back.
    /// </summary>
    public void Knock(Cow cow, Vector3 impulse, Vector3 point, float time)
    {
        var push = new Vector2(impulse.X, impulse.Z) / CowMass;
        cow.Velocity += push;
        // Struck off its middle, the cow swings round (about a vertical axis; its moment about it ~ m L^2 / 12).
        var arm = new Vector2(point.X - cow.Position.X, point.Z - cow.Position.Z);
        var torque = arm.X * impulse.Z - arm.Y * impulse.X;
        var inertia = CowMass * Length * Length / 12f;
        cow.Spin += MathUtil.Clamp(-torque / inertia * MathUtil.Rad2Deg, -240f, 240f);
        cow.Knocked = 1e-3f;
        var away = push.LengthSquared() > 1e-4f ? Vector2.Normalize(push) : Heading(cow);
        Startle(cow, away, 3.5f);
        cow.NextMoo = MathF.Max(cow.NextMoo, time + 6f);
    }

    /// <summary>Moves a cow out of the car's way by <paramref name="offset"/> (the share of an overlap that is the cow's).</summary>
    public void Displace(Cow cow, Vector3 offset)
    {
        var moved = cow.Position + offset with { Y = 0f };
        moved.Y = _terrain.Height(moved.X, moved.Z);
        cow.Position = moved;
    }

    private static void Startle(Cow cow, Vector2 away, float seconds)
    {
        cow.Startled = seconds;
        cow.Flee = away;
    }

    /// <summary>Where the cow's head points on the ground (its local +x, as the track yaws a prop).</summary>
    public static Vector2 Heading(Cow cow)
    {
        var yaw = cow.YawDeg * MathUtil.Deg2Rad;
        return new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
    }

    /// <summary>Signed angle in degrees from <paramref name="from"/> to <paramref name="to"/>, positive clockwise seen from above (towards -yaw).</summary>
    private static float SignedAngle(Vector2 from, Vector2 to) =>
        MathF.Atan2(from.X * to.Y - from.Y * to.X, Vector2.Dot(from, to)) * MathUtil.Rad2Deg;
}

/// <summary>One cow: where it stands, which way it faces, and what it is doing.</summary>
public sealed class Cow
{
    /// <summary>Index into <c>Track.Props</c> of the cow as placed.</summary>
    public int Prop;
    /// <summary>On the ground under the middle of the cow.</summary>
    public Vector3 Position;
    /// <summary>Heading in TrackProp's convention: the head along the local +x.</summary>
    public float YawDeg;
    /// <summary>Over the ground (x, z), m/s.</summary>
    public Vector2 Velocity;
    /// <summary>Turning, degrees a second.</summary>
    public float Spin;
    /// <summary>Seconds of trotting away left.</summary>
    public float Startled;
    public Vector2 Flee;
    /// <summary>Seconds since the car knocked it (0 when it has not lately), for the view's stagger.</summary>
    public float Knocked;
    /// <summary>Session time of its next moo.</summary>
    public float NextMoo;
}
