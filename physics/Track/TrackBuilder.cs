using System.Numerics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// Turtle-style road author: keeps a cursor (position, heading, pitch, bank) and appends segments,
/// sampling a <see cref="RoadFrame"/> about every metre. Lengths are usually given in <em>slabs</em>:
/// the alternating road blocks of the 1989 game measured 88 ft, which is the <see cref="Slab"/> constant.
/// </summary>
public sealed class TrackBuilder
{
    /// <summary>Length of one original road slab in metres (88 ft; 60 slabs at 100 mph take 36 s).</summary>
    public const float Slab = 26.8224f;
    public const float Step = 1.0f;
    public const float DefaultHalfWidth = 5.5f;

    private readonly TrackPath _path;
    private Vector3 _pos;
    private float _heading;      // radians, 0 = +Z, positive turns right
    private float _pitch;        // radians, positive = climbing
    private float _bank;         // radians, positive tilts the surface toward the driver's right
    private float _distance;
    private int _segmentIndex;

    public TrackBuilder(PathKind kind, Vector3 start, float headingDeg, float halfWidth = DefaultHalfWidth)
    {
        _path = new TrackPath(kind);
        _pos = start;
        _heading = headingDeg * MathUtil.Deg2Rad;
        HalfWidth = halfWidth;
        Emit(SegmentKind.Straight, SurfaceType.Road);
    }

    /// <summary>Continues from the end of an existing path (used for the fork and the final stretch).</summary>
    public static TrackBuilder ContinueFrom(TrackPath previous, PathKind kind)
    {
        var last = previous.Last;
        var b = new TrackBuilder(kind, last.Origin, 0f, last.HalfWidth);
        b._path.Clear();
        b._heading = MathF.Atan2(last.Tangent.X, last.Tangent.Z);
        b._pitch = MathF.Asin(MathUtil.Clamp(last.Tangent.Y, -1f, 1f));
        b._bank = 0f;
        b._distance = 0f;
        b.Emit(SegmentKind.Straight, SurfaceType.Road);
        previous.Next.Add(b._path);
        return b;
    }

    public float HalfWidth { get; set; }
    public TrackPath Path => _path;
    public Vector3 Position => _pos;
    public float HeadingDeg => _heading * MathUtil.Rad2Deg;
    public float Distance => _distance;

    public TrackBuilder Mark(MarkerKind kind, string label = "")
    {
        _path.Markers.Add(new TrackMarker(kind, _distance, label));
        return this;
    }

    public TrackBuilder StraightSlabs(float slabs) => Straight(slabs * Slab);

    /// <summary>Straight stretch; an optional height change is applied with zero slope at both ends.</summary>
    public TrackBuilder Straight(float length, float heightDelta = 0f)
    {
        _segmentIndex++;
        var n = Math.Max(1, (int)MathF.Round(length / Step));
        var start = _pos;
        var dir = MathUtil.HeadingDirection(_heading);
        for (var i = 1; i <= n; i++)
        {
            var s = length * i / n;
            var next = start + dir * s;
            next.Y = start.Y + heightDelta * EaseInOut(s / length);
            MoveTo(next);
            Emit(SegmentKind.Straight, SurfaceType.Road);
        }
        _pitch = 0f;
        return this;
    }

    /// <summary>
    /// Arc. Positive angle turns right. Banking, if given, is applied toward the inside of the turn with eased
    /// ends; an optional height change is applied with zero slope at both ends.
    /// </summary>
    public TrackBuilder Curve(float angleDeg, float radius, float bankDeg = 0f, float heightDelta = 0f)
    {
        _segmentIndex++;
        var kind = bankDeg > 0f ? SegmentKind.BankedCurve : SegmentKind.Curve;
        var length = MathF.Abs(angleDeg) * MathUtil.Deg2Rad * radius;
        var n = Math.Max(2, (int)MathF.Round(length / Step));
        var step = length / n;
        var dHeading = angleDeg * MathUtil.Deg2Rad / n;
        var turnSign = MathF.Sign(angleDeg);
        var startBank = _bank;
        var startY = _pos.Y;
        var ease = MathF.Min(25f, length * 0.3f);
        for (var i = 0; i < n; i++)
        {
            var s = (i + 1) * step;
            // Ease the bank in and out so the car is not kicked by a sudden tilt.
            var w = MathF.Min(MathUtil.SmoothStep(s / ease), MathUtil.SmoothStep((length - s) / ease));
            _bank = MathUtil.Lerp(startBank, turnSign * bankDeg * MathUtil.Deg2Rad, w);
            _heading += dHeading * 0.5f;
            var next = _pos + MathUtil.HeadingDirection(_heading) * step;
            next.Y = startY + heightDelta * EaseInOut(s / length);
            _heading += dHeading * 0.5f;
            MoveTo(next);
            Emit(kind, SurfaceType.Road);
        }
        _bank = 0f;
        _pitch = 0f;
        return this;
    }

    /// <summary>Bridge deck over another road: eased rise, flat deck for the middle third, eased descent.</summary>
    public TrackBuilder Overpass(float length, float height)
    {
        _segmentIndex++;
        var n = Math.Max(3, (int)MathF.Round(length / Step));
        var start = _pos;
        var dir = MathUtil.HeadingDirection(_heading);
        for (var i = 1; i <= n; i++)
        {
            var t = (float)i / n;
            var profile = t < 0.34f ? EaseInOut(t / 0.34f) : t < 0.66f ? 1f : EaseInOut((1f - t) / 0.34f);
            var next = start + dir * (length * t);
            next.Y = start.Y + height * profile;
            MoveTo(next);
            Emit(SegmentKind.Overpass, SurfaceType.Road);
        }
        _pitch = 0f;
        return this;
    }

    /// <summary>0..1 profile with zero slope at both ends (a cosine ramp).</summary>
    private static float EaseInOut(float t)
    {
        t = MathUtil.Clamp01(t);
        return 0.5f - 0.5f * MathF.Cos(MathF.PI * t);
    }

    /// <summary>Advances the cursor to an explicit point, deriving pitch and distance from the move.</summary>
    private void MoveTo(Vector3 next)
    {
        var delta = next - _pos;
        _distance += delta.Length();
        var horizontal = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (horizontal > 1e-5f) _pitch = MathF.Atan2(delta.Y, horizontal);
        _pos = next;
    }

    /// <summary>Cosine-profile hill (or dip for negative height) along a straight stretch.</summary>
    public TrackBuilder Hill(float length, float height)
    {
        _segmentIndex++;
        var kind = height >= 0 ? SegmentKind.Hill : SegmentKind.Dip;
        var n = Math.Max(2, (int)MathF.Round(length / Step));
        var baseY = _pos.Y;
        var dir = MathUtil.HeadingDirection(_heading);
        var startPos = _pos;
        for (var i = 1; i <= n; i++)
        {
            var s = length * i / n;
            var y = baseY + height * 0.5f * (1f - MathF.Cos(MathF.Tau * s / length));
            var next = startPos + dir * s;
            next.Y = y;
            var delta = next - _pos;
            _distance += delta.Length();
            _pitch = MathF.Atan2(delta.Y, MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z));
            _pos = next;
            Emit(kind, SurfaceType.Road);
        }
        _pitch = 0f;
        return this;
    }

    /// <summary>
    /// Vertical loop. The road climbs the inside of a circle of <paramref name="radius"/> and shifts sideways by
    /// <paramref name="lateralShift"/> over the revolution so the exit clears the entry (a helix, like the real stunt).
    /// </summary>
    public TrackBuilder Loop(float radius, float lateralShift)
    {
        _segmentIndex++;
        var forward = MathUtil.HeadingDirection(_heading);
        var right = Vector3.Cross(MathUtil.Up, forward);
        var centre = _pos + MathUtil.Up * radius;
        var circumference = MathF.Tau * radius;
        var n = Math.Max(8, (int)MathF.Round(circumference / Step));
        for (var i = 1; i <= n; i++)
        {
            var phi = MathF.Tau * i / n;
            var radial = -MathUtil.Up * MathF.Cos(phi) + forward * MathF.Sin(phi);
            var next = centre + radial * radius + right * (lateralShift * i / n);
            var tangent = MathUtil.SafeNormalize(MathUtil.Up * MathF.Sin(phi) + forward * MathF.Cos(phi) + right * (lateralShift / circumference), forward);
            var normal = -radial; // toward the loop centre: up at the bottom, down at the top
            _distance += (next - _pos).Length();
            _pos = next;
            var lateral = Vector3.Cross(normal, tangent);
            _path.Add(new RoadFrame(_pos, tangent, normal, lateral, _distance, HalfWidth, SurfaceType.Road, SegmentKind.Loop, _segmentIndex));
        }
        return this;
    }

    /// <summary>
    /// Open drawbridge: climb a ramp, fly a gap, land on a descending ramp. The landing ramp is longer and
    /// shallower so the 60-80 mph window of the original can be reproduced: too slow drops into the gap,
    /// too fast overshoots the ramp and slams the flat road. The gap frames have no surface.
    /// </summary>
    public TrackBuilder Drawbridge(float rampLength, float rampAngleDeg, float gapLength, float landingLength)
    {
        var angle = rampAngleDeg * MathUtil.Deg2Rad;

        _segmentIndex++;
        _pitch = angle;
        AdvanceStraight(rampLength, SegmentKind.RampUp, SurfaceType.Road);

        // The gap keeps the launch height so the path tracking stays continuous for the airborne car.
        _segmentIndex++;
        _pitch = 0f;
        AdvanceStraight(gapLength, SegmentKind.Gap, SurfaceType.Chasm);

        // The landing ramp starts at the launch height and comes back down to the original ground level.
        _segmentIndex++;
        var drop = rampLength * MathF.Sin(angle);
        _pitch = -MathF.Asin(MathUtil.Clamp(drop / landingLength, -1f, 1f));
        AdvanceStraight(landingLength, SegmentKind.RampDown, SurfaceType.Road);
        _pitch = 0f;
        return this;
    }

    /// <summary>
    /// Cubic Hermite connection to another frame; used to close the circuit without hand-solving geometry.
    /// The tangent weights scale with distance so the curve stays gentle.
    /// </summary>
    public TrackBuilder SplineTo(in RoadFrame target, float tension = 1.0f)
    {
        _segmentIndex++;
        var p0 = _pos;
        var p1 = target.Origin;
        var dist = (p1 - p0).Length();
        var t0 = MathUtil.SafeNormalize(Direction(), Vector3.UnitZ) * dist * tension;
        var t1 = target.Tangent * dist * tension;

        // Sample finely, then resample by arc length so frame spacing stays close to Step.
        const int fine = 600;
        var points = new Vector3[fine + 1];
        for (var i = 0; i <= fine; i++)
        {
            var u = i / (float)fine;
            var u2 = u * u;
            var u3 = u2 * u;
            points[i] = (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * t0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * t1;
        }
        var cumulative = new float[fine + 1];
        for (var i = 1; i <= fine; i++) cumulative[i] = cumulative[i - 1] + (points[i] - points[i - 1]).Length();
        var total = cumulative[fine];
        var n = Math.Max(2, (int)MathF.Round(total / Step));
        var startBank = _bank;
        var k = 1;
        for (var i = 1; i <= n; i++)
        {
            var s = total * i / n;
            while (k < fine && cumulative[k] < s) k++;
            var segLen = cumulative[k] - cumulative[k - 1];
            var w = segLen > 1e-6f ? (s - cumulative[k - 1]) / segLen : 0f;
            var next = Vector3.Lerp(points[k - 1], points[k], w);
            var tangent = MathUtil.SafeNormalize(points[k] - points[k - 1], t1);
            _distance += (next - _pos).Length();
            _pos = next;
            _heading = MathF.Atan2(tangent.X, tangent.Z);
            _pitch = MathF.Asin(MathUtil.Clamp(tangent.Y, -1f, 1f));
            _bank = MathUtil.Lerp(startBank, 0f, MathUtil.SmoothStep(i / (float)n));
            Emit(SegmentKind.Spline, SurfaceType.Road);
        }
        _pitch = 0f;
        _bank = 0f;
        return this;
    }

    /// <summary>Links this path to the start of <paramref name="next"/> (geometry must already meet).</summary>
    public TrackBuilder JoinTo(TrackPath next)
    {
        _path.Next.Add(next);
        return this;
    }

    private Vector3 Direction()
    {
        var flat = MathUtil.HeadingDirection(_heading);
        return new Vector3(flat.X * MathF.Cos(_pitch), MathF.Sin(_pitch), flat.Z * MathF.Cos(_pitch));
    }

    private void Advance(float step)
    {
        _pos += Direction() * step;
        _distance += step;
    }

    private void AdvanceStraight(float length, SegmentKind kind, SurfaceType surface)
    {
        var n = Math.Max(1, (int)MathF.Round(length / Step));
        var step = length / n;
        for (var i = 0; i < n; i++)
        {
            Advance(step);
            Emit(kind, surface);
        }
    }

    private void Emit(SegmentKind kind, SurfaceType surface)
    {
        var tangent = Direction();
        var flatRight = Vector3.Cross(MathUtil.Up, MathUtil.HeadingDirection(_heading));
        var normal = MathUtil.SafeNormalize(Vector3.Cross(tangent, flatRight), MathUtil.Up);
        // Bank: rotate the surface normal about the tangent. Positive bank lowers the right edge.
        normal = MathUtil.RotateAround(normal, tangent, -_bank);
        var lateral = Vector3.Cross(normal, tangent);
        _path.Add(new RoadFrame(_pos, tangent, normal, lateral, _distance, HalfWidth, surface, kind, _segmentIndex));
    }
}
