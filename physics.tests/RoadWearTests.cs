using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;

namespace GrudgeLap.Physics.Tests;

/// <summary>The worn road: small, deterministic, with potholes on the earthworks and next to nothing on the structures, and felt by the probe.</summary>
public sealed class RoadWearTests
{
    [Fact]
    public void Wear_is_small_deterministic_and_has_potholes_on_the_earthworks()
    {
        var track = OriginalInspiredTrack.Build();
        var deepest = 0f;
        var onStructures = 0f;
        foreach (var path in track.Paths)
        {
            foreach (var f in path.Frames)
            {
                for (var lateral = -f.HalfWidth; lateral <= f.HalfWidth; lateral += 0.25f)
                {
                    var depth = RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth);
                    Assert.Equal(depth, RoadWear.Depth(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth));
                    Assert.InRange(depth, -0.02f, RoadWear.MaxDepth + 0.02f);
                    var tone = RoadWear.PatchTone(path.Kind, f.Kind, f.Distance, lateral, f.HalfWidth);
                    Assert.True(tone is 1f or 0.82f or 1.1f, $"patch tone {tone}");
                    if (f.Kind is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.RampDown or SegmentKind.Overpass) onStructures = MathF.Max(onStructures, MathF.Abs(depth));
                    else deepest = MathF.Max(deepest, depth);
                }
            }
        }
        Assert.True(deepest > 0.025f, $"no pothole deeper than {deepest * 100:F1} cm");
        Assert.True(onStructures < 0.003f, $"a structure is worn {onStructures * 100:F2} cm");
    }

    [Fact]
    public void Probe_feels_the_wear_under_the_wheel()
    {
        var track = OriginalInspiredTrack.Build();
        var path = track.Speed;
        // A point a metre over the ideal road is a metre plus the dip over the worn one.
        var f = path.FrameAt(400f);
        var lateral = 0.4f * f.HalfWidth;
        var point = f.Origin + f.Lateral * lateral + f.Normal * 1f;
        var cursor = new PathCursor(path, path.FrameIndexAt(400f));
        var hit = track.Probe(point, ref cursor, false);
        Assert.Equal(SurfaceType.Road, hit.Surface);
        var dip = RoadWear.Depth(path.Kind, f.Kind, hit.Frame.Distance, hit.Lateral, hit.Frame.HalfWidth);
        Assert.InRange(hit.Height, 1f + dip - 0.002f, 1f + dip + 0.002f);
        Assert.InRange(Vector3.Distance(hit.SurfacePoint, point - f.Normal * (1f + dip)), 0f, 0.01f);
    }
}
