using System.Numerics;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// A rectangle on the ground plane (x and z of the simulation; y is ignored): the plan of a car, a building, a tree
/// trunk or a cow, for the knocks between them.
/// </summary>
/// <param name="Centre">Centre of the rectangle (x, z).</param>
/// <param name="Axis">Unit direction of its length (x, z); the width runs across it.</param>
/// <param name="HalfLength">Half the length, along <paramref name="Axis"/>.</param>
/// <param name="HalfWidth">Half the width, across it.</param>
public readonly record struct Footprint(Vector2 Centre, Vector2 Axis, float HalfLength, float HalfWidth)
{
    /// <summary>The unit direction across: for a car facing +z, the right is +x.</summary>
    public Vector2 Across => new(Axis.Y, -Axis.X);

    /// <summary>
    /// A body's plan from its forward direction and its reach to the front and the rear of a reference point (a car's
    /// centre of gravity sits nearer one end).
    /// </summary>
    public static Footprint Of(Vector3 position, Vector3 forward, float front, float rear, float halfWidth)
    {
        var axis = Flat(forward);
        var centre = new Vector2(position.X, position.Z) + axis * ((front - rear) * 0.5f);
        return new Footprint(centre, axis, (front + rear) * 0.5f, halfWidth);
    }

    /// <summary>A prop's plan: its size along its own x (the length) and z, turned by its yaw as the track places it.</summary>
    public static Footprint OfProp(Vector3 position, float yawDeg, float sizeX, float sizeZ)
    {
        var yaw = yawDeg * MathUtil.Deg2Rad;
        // The prop's local x axis in the world (TrackProp's convention).
        var axis = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
        return new Footprint(new Vector2(position.X, position.Z), axis, sizeX * 0.5f, sizeZ * 0.5f);
    }

    /// <summary>The horizontal part of a direction, normalised (straight ahead when the direction is vertical).</summary>
    public static Vector2 Flat(Vector3 direction)
    {
        var flat = new Vector2(direction.X, direction.Z);
        var length = flat.Length();
        return length > 1e-4f ? flat / length : Vector2.UnitY;
    }

    /// <summary>
    /// Separating-axis test against another rectangle. On overlap, <paramref name="normal"/> points from this one into
    /// the other, <paramref name="depth"/> is how far they overlap along it, and <paramref name="point"/> is where they
    /// touch: this one's deepest corner when the other's face is the shallowest way out (a car's corner into a wall),
    /// the other's deepest corner when this one's face is (a tree trunk into the car's bumper).
    /// </summary>
    public bool Overlaps(in Footprint other, out Vector2 normal, out float depth, out Vector2 point)
    {
        normal = Vector2.Zero;
        point = Vector2.Zero;
        depth = float.MaxValue;
        var fromOther = false;
        var offset = other.Centre - Centre;
        Span<Vector2> axes = [Axis, Across, other.Axis, other.Across];
        for (var i = 0; i < axes.Length; i++)
        {
            var u = axes[i];
            var reach = Reach(u) + other.Reach(u);
            var distance = Vector2.Dot(offset, u);
            var overlap = reach - MathF.Abs(distance);
            if (overlap <= 0f) return false;
            if (overlap < depth)
            {
                depth = overlap;
                normal = distance >= 0f ? u : -u;
                fromOther = i >= 2;
            }
        }
        point = fromOther ? Deepest(normal) : other.Deepest(-normal);
        return true;
    }

    /// <summary>Half the extent along a unit direction.</summary>
    private float Reach(Vector2 u) => HalfLength * MathF.Abs(Vector2.Dot(Axis, u)) + HalfWidth * MathF.Abs(Vector2.Dot(Across, u));

    /// <summary>The corner furthest along a direction; two corners level with each other (a face square to it) give their middle.</summary>
    private Vector2 Deepest(Vector2 direction)
    {
        var along = Axis * HalfLength;
        var across = Across * HalfWidth;
        Span<Vector2> corners = [Centre + along + across, Centre + along - across, Centre - along + across, Centre - along - across];
        var best = float.MinValue;
        foreach (var c in corners) best = MathF.Max(best, Vector2.Dot(c, direction));
        var sum = Vector2.Zero;
        var count = 0;
        foreach (var c in corners)
        {
            if (Vector2.Dot(c, direction) < best - 0.02f) continue;
            sum += c;
            count++;
        }
        return sum / count;
    }
}
