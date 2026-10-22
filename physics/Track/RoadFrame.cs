using System.Numerics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// One sample of the road centre line, roughly one metre apart. The three axes form a right-handed
/// frame: <see cref="Tangent"/> points along the driving direction, <see cref="Normal"/> away from the
/// surface toward the car (so it points *down* at the top of the loop), <see cref="Lateral"/> to the
/// driver's right.
/// </summary>
public readonly record struct RoadFrame(
    Vector3 Origin,
    Vector3 Tangent,
    Vector3 Normal,
    Vector3 Lateral,
    float Distance,
    float HalfWidth,
    SurfaceType Surface,
    SegmentKind Kind,
    int SegmentIndex)
{
    public static RoadFrame Lerp(in RoadFrame a, in RoadFrame b, float t)
    {
        var tangent = MathUtil.SafeNormalize(Vector3.Lerp(a.Tangent, b.Tangent, t), a.Tangent);
        var normal = MathUtil.SafeNormalize(Vector3.Lerp(a.Normal, b.Normal, t), a.Normal);
        normal = MathUtil.SafeNormalize(MathUtil.ProjectOnPlane(normal, tangent), a.Normal);
        var lateral = Vector3.Cross(normal, tangent);
        return new RoadFrame(
            Vector3.Lerp(a.Origin, b.Origin, t), tangent, normal, lateral,
            MathUtil.Lerp(a.Distance, b.Distance, t), MathUtil.Lerp(a.HalfWidth, b.HalfWidth, t),
            t < 0.5f ? a.Surface : b.Surface, t < 0.5f ? a.Kind : b.Kind, t < 0.5f ? a.SegmentIndex : b.SegmentIndex);
    }
}

public enum SegmentKind
{
    Straight,
    Curve,
    Hill,
    Dip,
    Loop,
    RampUp,
    Gap,
    RampDown,
    BankedCurve,
    Spline,
    /// <summary>Bridge deck carrying this road over another one; drawn with pillars and railings, no embankment.</summary>
    Overpass,
}
