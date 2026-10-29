using System.Numerics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// The whole circuit: four linked paths, the ground between them, skid pads and roadside props. Answers the
/// one question the vehicle keeps asking: "what surface is under this point, and where am I along the road?"
/// </summary>
public sealed class TrackGeometry
{
    /// <summary>How many frames either side of the cursor are searched per probe (frames are ~1 m apart).</summary>
    private const int SearchWindow = 12;
    /// <summary>Frames after a fork within which sibling branches are still searched (they diverge within ~50 m).</summary>
    private const int BranchOverlap = 90;

    public TrackGeometry(TrackPath common, TrackPath speed, TrackPath stunt, TrackPath final, IReadOnlyList<SkidPad>? pads = null, float rollingGround = 2.2f)
    {
        Common = common;
        Speed = speed;
        Stunt = stunt;
        Final = final;
        Paths = [common, speed, stunt, final];
        Pads = pads ?? [];
        Terrain = new TerrainField(Paths, Pads) { RollingAmplitude = rollingGround };
    }

    public TrackPath Common { get; }
    public TrackPath Speed { get; }
    public TrackPath Stunt { get; }
    public TrackPath Final { get; }
    public IReadOnlyList<TrackPath> Paths { get; }
    public IReadOnlyList<SkidPad> Pads { get; }
    public TerrainField Terrain { get; }
    public List<TrackProp> Props { get; } = new();
    /// <summary>The train at the level crossing, if the circuit has one.</summary>
    public Train? Train { get; set; }

    /// <summary>
    /// The piers under every bridge and ramp, and the legs of the drawbridge towers, as solid props: the car crashes
    /// into them as into a building. Until 0.9 the view drew them alone and the car drove through them.
    /// </summary>
    /// <remarks>
    /// A pier every 14 m where a deck stands more than 1.6 m high, under each edge of the deck (1.2 m inboard),
    /// from the ground to a quarter of a metre under the deck. None is set within 2.5 m of another road, where a
    /// road passes under the bridge. The towers are where <see cref="DrawbridgeTowers"/> stands them.
    /// </remarks>
    public void AddBridgePiers()
    {
        foreach (var path in Paths)
        {
            var frames = path.Frames;
            var lastPier = -100f;
            for (var i = 0; i < frames.Count - 1; i++)
            {
                var a = frames[i];
                var b = frames[i + 1];
                var bridge = a.Kind is SegmentKind.Overpass or SegmentKind.RampUp or SegmentKind.RampDown && a.Origin.Y > 0.4f;
                if (!bridge) continue;
                var yaw = MathF.Atan2(a.Tangent.X, a.Tangent.Z) * MathUtil.Rad2Deg;
                if (a.Distance - lastPier >= 14f && a.Origin.Y > 1.6f)
                {
                    lastPier = a.Distance;
                    foreach (var side in new[] { -1f, 1f })
                    {
                        var foot = a.Origin + a.Lateral * (side * (a.HalfWidth - 1.2f));
                        var ground = Terrain.Height(foot.X, foot.Z);
                        var height = foot.Y - 0.25f - ground;
                        if (height < 0.8f || NearOtherRoad(foot, path, a.Distance, 2.5f)) continue;
                        Props.Add(new TrackProp(PropKind.Pier, new Vector3(foot.X, ground, foot.Z), yaw, new Vector3(1.2f, height, 1.6f), "pier"));
                    }
                }
            }
        }
        foreach (var tower in DrawbridgeTowers())
        {
            var edge = tower.Edge;
            var yaw = MathF.Atan2(edge.Tangent.X, edge.Tangent.Z) * MathUtil.Rad2Deg;
            var ground = Terrain.Height(tower.Foot.X, tower.Foot.Z);
            Props.Add(new TrackProp(PropKind.Pier, tower.Foot with { Y = ground }, yaw, new Vector3(0.6f, tower.Apex.Y - ground, 2f * TowerLegSpread + 0.6f), "tower"));
        }
    }

    /// <summary>Metres the legs of a drawbridge tower spread along the road either side of its foot.</summary>
    public const float TowerLegSpread = 2.4f;

    /// <summary>
    /// The white A-frame towers of the drawbridge, one each side of the road at both lips of the gap: the foot on
    /// the ground (0.3 m into it), the apex 11 m over the deck's edge. A tower stands 2.6 m past the road's edge,
    /// further out where its legs would land on another road, and not at all where that road runs within 14 m: the
    /// speed route runs under the jump, and until 0.9 a tower stood with a leg on its asphalt. On the 1989 course one
    /// tower stands at each lip, on opposite sides.
    /// </summary>
    public IEnumerable<(TrackPath Path, RoadFrame Edge, float Side, Vector3 Foot, Vector3 Apex)> DrawbridgeTowers()
    {
        foreach (var path in Paths)
        {
            var frames = path.Frames;
            for (var i = 0; i < frames.Count - 1; i++)
            {
                var a = frames[i];
                var b = frames[i + 1];
                if (!(a.Kind == SegmentKind.RampUp && b.Kind == SegmentKind.Gap || a.Kind == SegmentKind.Gap && b.Kind == SegmentKind.RampDown)) continue;
                var edge = a.Kind == SegmentKind.Gap ? b : a;
                var along = Vector3.Normalize(edge.Tangent with { Y = 0f });
                foreach (var side in new[] { -1f, 1f })
                {
                    for (var offset = 2.6f; offset <= 14f; offset += 0.5f)
                    {
                        var top = edge.Origin + edge.Lateral * (side * (edge.HalfWidth + offset));
                        var foot = new Vector3(top.X, Terrain.Height(top.X, top.Z) - 0.3f, top.Z);
                        // The legs' feet and the ends of the footprint, half a metre past each, clear of every other road.
                        var reach = along * (TowerLegSpread + 0.5f);
                        if (NearOtherRoad(foot, path, edge.Distance, 2f)
                            || NearOtherRoad(foot + reach, path, edge.Distance, 2f)
                            || NearOtherRoad(foot - reach, path, edge.Distance, 2f))
                        {
                            continue;
                        }
                        yield return (path, edge, side, foot, new Vector3(top.X, top.Y + 11f, top.Z));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>True when a road other than this stretch of <paramref name="own"/> passes within <paramref name="clearance"/> of its edge.</summary>
    private bool NearOtherRoad(Vector3 point, TrackPath own, float ownDistance, float clearance)
    {
        foreach (var path in Paths)
        {
            foreach (var f in path.Frames)
            {
                if (path == own && MathF.Abs(f.Distance - ownDistance) < 40f) continue;
                var d = point - f.Origin;
                d.Y = 0f;
                if (d.LengthSquared() > 900f) continue;
                var tangent = Vector3.Normalize(f.Tangent with { Y = 0f });
                if (MathF.Abs(Vector3.Dot(d, tangent)) > 1.5f) continue;
                var lateral = Vector3.Normalize(f.Lateral with { Y = 0f });
                if (MathF.Abs(Vector3.Dot(d, lateral)) < f.HalfWidth + clearance) return true;
            }
        }
        return false;
    }
    public string Name { get; init; } = "Track";

    public RoadFrame StartFrame => Common.First;
    public PathCursor StartCursor => new(Common, 0);

    /// <summary>Total driving distance for the speed route and the stunt route.</summary>
    public float SpeedLapLength => Common.Length + Speed.Length + Final.Length;
    public float StuntLapLength => Common.Length + Stunt.Length + Final.Length;

    /// <summary>
    /// Finds the surface under <paramref name="point"/>. The cursor makes the search local; pass
    /// <paramref name="updateCursor"/> = true for the car body so the cursor follows it, false for wheel probes.
    /// </summary>
    public SurfaceHit Probe(Vector3 point, ref PathCursor cursor, bool updateCursor)
    {
        // The nearest frame of every path within reach: the cursor's own window, the heads of the paths that follow
        // it, and near the start of a path its parent's tail and, at a fork, the shared first metres of its siblings.
        Span<int> nearest = stackalloc int[Paths.Count];
        Span<float> nearestScore = stackalloc float[Paths.Count];
        nearest.Fill(-1);
        nearestScore.Fill(float.MaxValue);

        Consider(cursor.Path, cursor.FrameIndex - SearchWindow, cursor.FrameIndex + SearchWindow, point, nearest, nearestScore);
        if (cursor.FrameIndex + SearchWindow >= cursor.Path.Frames.Count - 1)
        {
            foreach (var next in cursor.Path.Next) Consider(next, 0, SearchWindow, point, nearest, nearestScore);
        }
        if (cursor.FrameIndex < BranchOverlap)
        {
            foreach (var parent in Paths)
            {
                if (!parent.Next.Contains(cursor.Path)) continue;
                Consider(parent, parent.Frames.Count - 1 - SearchWindow, parent.Frames.Count - 1, point, nearest, nearestScore);
                foreach (var sibling in parent.Next)
                {
                    if (!ReferenceEquals(sibling, cursor.Path)) Consider(sibling, 0, BranchOverlap, point, nearest, nearestScore);
                }
            }
        }

        var bestPath = cursor.Path;
        var bestIndex = cursor.FrameIndex;
        var bestScore = float.MaxValue;
        var inReach = 0;
        for (var p = 0; p < Paths.Count; p++)
        {
            if (nearest[p] < 0) continue;
            inReach++;
            if (nearestScore[p] < bestScore)
            {
                bestScore = nearestScore[p];
                bestPath = Paths[p];
                bestIndex = nearest[p];
            }
        }
        if (inReach > 1)
        {
            // Two roads in reach (a fork or a join): where the point is over both, it rests on the road surface nearest
            // below it, the one drawn on top. By the nearest origin alone, a car in the right lane just past the fork
            // followed the stunt branch curving away under it, sank with its steeper descent below the speed road and
            // fell through it once the cursor caught up. Only roads the point is beside count, so a path's last frame
            // metres behind cannot claim it. The path the cursor has just left is not a candidate: back on the crest
            // a few metres past the fork, its last frame was the higher surface and the cursor hopped back onto it,
            // which replayed that path's markers from the start (a lap counted at the fork). A car that really
            // reverses past the fork goes back by the nearest origin above.
            var bestHeight = float.MaxValue;
            for (var p = 0; p < Paths.Count; p++)
            {
                if (nearest[p] < 0 || Paths[p].Next.Contains(cursor.Path)) continue;
                var near = Paths[p].Frames[nearest[p]];
                var rel = point - near.Origin;
                if (MathF.Abs(Vector3.Dot(rel, near.Tangent)) > 3f) continue;
                var above = Vector3.Dot(rel, near.Normal);
                if (MathF.Abs(Vector3.Dot(rel, near.Lateral)) > near.HalfWidth || above < -0.6f || above >= bestHeight) continue;
                bestHeight = above;
                bestPath = Paths[p];
                bestIndex = nearest[p];
            }
        }

        if (updateCursor)
        {
            cursor.Path = bestPath;
            cursor.FrameIndex = bestIndex;
        }

        // Interpolate toward the neighbour the point leans to, so lateral/height are measured from a frame
        // that actually sits beside the point rather than up to half a metre away.
        var frames2 = bestPath.Frames;
        var f = frames2[bestIndex];
        var along = Vector3.Dot(point - f.Origin, f.Tangent);
        RoadFrame frame;
        if (along > 0f && bestIndex < frames2.Count - 1)
        {
            var n = frames2[bestIndex + 1];
            var span = MathF.Max(n.Distance - f.Distance, 1e-3f);
            frame = RoadFrame.Lerp(f, n, MathUtil.Clamp01(along / span));
        }
        else if (along < 0f && bestIndex > 0)
        {
            var p = frames2[bestIndex - 1];
            var span = MathF.Max(f.Distance - p.Distance, 1e-3f);
            frame = RoadFrame.Lerp(p, f, 1f - MathUtil.Clamp01(-along / span));
        }
        else
        {
            frame = f;
        }

        var offset = point - frame.Origin;
        var lateral = Vector3.Dot(offset, frame.Lateral);
        var height = Vector3.Dot(offset, frame.Normal);
        var onRoad = MathF.Abs(lateral) <= frame.HalfWidth;

        if (frame.Surface == SurfaceType.Chasm && MathF.Abs(lateral) <= frame.HalfWidth + 2f)
        {
            // Over the open drawbridge gap: nothing to land on until the far ramp.
            return new SurfaceHit(false, SurfaceType.Chasm, frame, lateral, height, frame.Origin + frame.Lateral * lateral, frame.Normal, bestPath, bestIndex);
        }

        if (onRoad)
        {
            // The worn surface lies a little under (or over) the ideal ribbon; the wheels feel every pothole the view draws.
            var dip = RoadWear.Depth(bestPath.Kind, frame.Kind, frame.Distance, lateral, frame.HalfWidth);
            var surfacePoint = frame.Origin + frame.Lateral * lateral - frame.Normal * dip;
            return new SurfaceHit(true, SurfaceType.Road, frame, lateral, height + dip, surfacePoint, frame.Normal, bestPath, bestIndex);
        }

        foreach (var pad in Pads)
        {
            var dx = point.X - pad.Centre.X;
            var dz = point.Z - pad.Centre.Z;
            if (dx * dx + dz * dz <= pad.Radius * pad.Radius)
            {
                var padPoint = new Vector3(point.X, pad.Centre.Y, point.Z);
                return new SurfaceHit(true, SurfaceType.Pad, frame, lateral, point.Y - pad.Centre.Y, padPoint, MathUtil.Up, bestPath, bestIndex);
            }
        }

        // Off the road: the rolling ground of the terrain field.
        var groundY = Terrain.Height(point.X, point.Z);
        var grassPoint = new Vector3(point.X, groundY, point.Z);
        return new SurfaceHit(true, SurfaceType.Grass, frame, lateral, point.Y - groundY, grassPoint, Terrain.Normal(point.X, point.Z), bestPath, bestIndex);
    }

    /// <summary>Records the frame of <paramref name="path"/> between two indices nearest to <paramref name="point"/>.</summary>
    private void Consider(TrackPath path, int from, int to, Vector3 point, Span<int> nearest, Span<float> nearestScore)
    {
        var p = 0;
        while (p < Paths.Count && !ReferenceEquals(Paths[p], path)) p++;
        if (p == Paths.Count) return;
        var frames = path.Frames;
        from = Math.Max(0, from);
        to = Math.Min(frames.Count - 1, to);
        for (var i = from; i <= to; i++)
        {
            var score = Vector3.DistanceSquared(frames[i].Origin, point);
            if (score < nearestScore[p])
            {
                nearestScore[p] = score;
                nearest[p] = i;
            }
        }
    }

    /// <summary>Resolves the path and distance a cursor points at, for rules and HUD use.</summary>
    public static float DistanceOf(in PathCursor cursor) => cursor.Path.Frames[cursor.FrameIndex].Distance;

    /// <summary>Smallest horizontal distance from a point to any road centre line.</summary>
    public float ClearanceFromRoads(Vector3 point)
    {
        var best = float.MaxValue;
        foreach (var path in Paths)
        {
            foreach (var f in path.Frames)
            {
                var dx = f.Origin.X - point.X;
                var dz = f.Origin.Z - point.Z;
                best = MathF.Min(best, dx * dx + dz * dz);
            }
        }
        return MathF.Sqrt(best);
    }
}

/// <summary>Where along the track graph a probe last found itself.</summary>
public struct PathCursor
{
    public TrackPath Path;
    public int FrameIndex;

    public PathCursor(TrackPath path, int frameIndex)
    {
        Path = path;
        FrameIndex = frameIndex;
    }

    public readonly RoadFrame Frame => Path.Frames[FrameIndex];
    public readonly float Distance => Path.Frames[FrameIndex].Distance;
}

/// <summary>Result of a surface probe.</summary>
public readonly record struct SurfaceHit(
    bool HasSurface,
    SurfaceType Surface,
    RoadFrame Frame,
    float Lateral,
    /// <summary>Signed distance of the probed point above the surface, along the surface normal.</summary>
    float Height,
    Vector3 SurfacePoint,
    Vector3 SurfaceNormal,
    TrackPath Path,
    int FrameIndex);

/// <summary>A roadside object. Solid props crash the car; soft ones (signs, cows) just react.</summary>
public sealed record TrackProp(PropKind Kind, Vector3 Position, float YawDeg, Vector3 Size, string Label = "")
{
    public bool Solid => Kind is PropKind.Barn or PropKind.House or PropKind.Shop or PropKind.Hut or PropKind.Tree or PropKind.Tower or PropKind.Barrier or PropKind.Train or PropKind.Pump or PropKind.Pier;
    /// <summary>Soft props the car drives through: they react (a sign tips, a cow moos) but never stop it.</summary>
    public bool Soft => Kind is PropKind.Cow or PropKind.Sign or PropKind.Crossbuck;
}

public enum PropKind
{
    Barn,
    House,
    Shop,
    Hut,
    Tree,
    Cow,
    Sign,
    Signpost,
    Tower,
    Barrier,
    Train,
    Flowers,
    /// <summary>A crop, ploughed or hay field: a ground patch conforming to the terrain (Size.X by Size.Z).</summary>
    Field,
    /// <summary>Banner gantry across the road (the green start sign).</summary>
    Gate,
    /// <summary>Level-crossing sign.</summary>
    Crossbuck,
    /// <summary>Fuel pump at the shop.</summary>
    Pump,
    /// <summary>
    /// A concrete pier under a bridge or a ramp (<see cref="TrackGeometry.AddBridgePiers"/>), or with the label
    /// "tower" the footprint of a drawbridge tower's legs, which the view draws with the tower.
    /// </summary>
    Pier,
}
