using System.Numerics;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// A sequence of road frames the car can follow without branching. The track is a small graph of paths:
/// Common (start line to the fork), Speed and Stunt (fork to the join), Final (join back to the start).
/// </summary>
public sealed class TrackPath
{
    private readonly List<RoadFrame> _frames = new();

    public TrackPath(PathKind kind) => Kind = kind;

    public PathKind Kind { get; }
    public IReadOnlyList<RoadFrame> Frames => _frames;
    public float Length => _frames.Count == 0 ? 0f : _frames[^1].Distance;
    public List<TrackPath> Next { get; } = new();
    public List<TrackMarker> Markers { get; } = new();

    internal void Add(in RoadFrame frame) => _frames.Add(frame);
    internal void Clear() => _frames.Clear();

    public RoadFrame First => _frames[0];
    public RoadFrame Last => _frames[^1];

    public Vector3 Centre
    {
        get
        {
            var sum = Vector3.Zero;
            foreach (var f in _frames) sum += f.Origin;
            return sum / MathF.Max(1, _frames.Count);
        }
    }

    /// <summary>Frame interpolated at a distance along the path.</summary>
    public RoadFrame FrameAt(float distance)
    {
        if (_frames.Count == 1) return _frames[0];
        distance = MathUtil.Clamp(distance, 0f, Length);
        var i = FrameIndexAt(distance);
        if (i >= _frames.Count - 1) return _frames[^1];
        var a = _frames[i];
        var b = _frames[i + 1];
        var span = b.Distance - a.Distance;
        var t = span > 1e-5f ? (distance - a.Distance) / span : 0f;
        return RoadFrame.Lerp(a, b, t);
    }

    public int FrameIndexAt(float distance)
    {
        // Frames are ~1 m apart so a binary search is cheap and exact.
        int lo = 0, hi = _frames.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_frames[mid].Distance <= distance) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}

public enum PathKind
{
    Common,
    Speed,
    Stunt,
    Final,
}

/// <summary>Something that happens at a distance along a path: the start/finish line, a checkpoint, a respawn point...</summary>
public readonly record struct TrackMarker(MarkerKind Kind, float Distance, string Label = "");

public enum MarkerKind
{
    StartFinish,
    Checkpoint,
    /// <summary>Where the car is put back after a crash or a timed-out excursion off the road.</summary>
    ReturnPoint,
    /// <summary>Informational: a speed-limit or warning sign beside the road (value in Label).</summary>
    Sign,
}
