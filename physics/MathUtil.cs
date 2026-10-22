using System.Numerics;

namespace GrudgeLap.Physics;

/// <summary>
/// Small math helpers shared by the simulation. World convention everywhere in this library:
/// right-handed, +Y up, heading 0 looks along +Z and a positive heading turns to the right (+X).
/// The Godot view flips Z when it copies poses out, so engine conventions never leak in here.
/// </summary>
public static class MathUtil
{
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;
    public const float MphPerMps = 2.2369363f;
    public const float KmhPerMps = 3.6f;
    public const float Gravity = 9.81f;

    public static readonly Vector3 Up = Vector3.UnitY;

    public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    public static float Clamp01(float v) => Clamp(v, 0f, 1f);
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
    public static float MoveToward(float current, float target, float maxDelta)
    {
        var delta = target - current;
        return MathF.Abs(delta) <= maxDelta ? target : current + MathF.Sign(delta) * maxDelta;
    }

    /// <summary>Smooth 0..1 ease used for banking ramps and hill profiles.</summary>
    public static float SmoothStep(float t)
    {
        t = Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    public static Vector3 ProjectOnPlane(Vector3 v, Vector3 unitNormal) => v - unitNormal * Vector3.Dot(v, unitNormal);

    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var len = v.Length();
        return len > 1e-6f ? v / len : fallback;
    }

    /// <summary>Unit direction for a heading (radians) on the ground plane.</summary>
    public static Vector3 HeadingDirection(float heading) => new(MathF.Sin(heading), 0f, MathF.Cos(heading));

    /// <summary>Rotates <paramref name="v"/> around the unit <paramref name="axis"/> by <paramref name="angle"/> radians (Rodrigues).</summary>
    public static Vector3 RotateAround(Vector3 v, Vector3 axis, float angle)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        return v * c + Vector3.Cross(axis, v) * s + axis * Vector3.Dot(axis, v) * (1f - c);
    }

    /// <summary>Wraps an angle to (-pi, pi].</summary>
    public static float WrapAngle(float a)
    {
        a %= MathF.Tau;
        if (a > MathF.PI) a -= MathF.Tau;
        if (a <= -MathF.PI) a += MathF.Tau;
        return a;
    }
}
