using System.Numerics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

public sealed class TrackTests
{
    private readonly ITestOutputHelper _out;

    public TrackTests(ITestOutputHelper output) => _out = output;

    /// <summary>Folder for human-inspectable artefacts (maps); git-ignored.</summary>
    internal static string ArtifactDir => Path.Combine(FindRepoRoot(), "artifacts");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrudgeLap.sln"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    [Fact]
    public void Default_track_is_closed_and_sane()
    {
        var track = OriginalInspiredTrack.Build();

        // The final spline must land exactly on the start frame and the branches on the join.
        Assert.True(Vector3.Distance(track.Final.Last.Origin, track.Common.First.Origin) < 0.6f);
        Assert.True(Vector3.Distance(track.Stunt.Last.Origin, track.Final.First.Origin) < 0.6f);
        Assert.True(Vector3.Dot(track.Final.Last.Tangent, track.Common.First.Tangent) > 0.95f);

        foreach (var path in track.Paths)
        {
            // Frame spacing stays near one metre so the probe window assumptions hold.
            for (var i = 1; i < path.Frames.Count; i++)
            {
                var d = Vector3.Distance(path.Frames[i].Origin, path.Frames[i - 1].Origin);
                Assert.InRange(d, 0.2f, 2.5f);
                Assert.InRange(path.Frames[i].Normal.Length(), 0.99f, 1.01f);
            }
        }

        _out.WriteLine($"common {track.Common.Length:F0} m, speed {track.Speed.Length:F0} m, stunt {track.Stunt.Length:F0} m, final {track.Final.Length:F0} m");
        _out.WriteLine($"speed lap {track.SpeedLapLength:F0} m, stunt lap {track.StuntLapLength:F0} m");
        static string Describe(in RoadFrame f) => $"({f.Origin.X:F0}, {f.Origin.Z:F0}) heading {MathF.Atan2(f.Tangent.X, f.Tangent.Z) * MathUtil.Rad2Deg:F0}";
        var stuntBeforeSpline = track.Stunt.Frames.Last(f => f.Kind != SegmentKind.Spline);
        var finalBeforeSpline = track.Final.Frames.Last(f => f.Kind != SegmentKind.Spline);
        _out.WriteLine($"fork {Describe(track.Common.Last)}; speed end/join {Describe(track.Speed.Last)}");
        _out.WriteLine($"stunt before spline {Describe(stuntBeforeSpline)} spline {track.Stunt.Length - stuntBeforeSpline.Distance:F0} m");
        _out.WriteLine($"final before spline {Describe(finalBeforeSpline)} spline {track.Final.Length - finalBeforeSpline.Distance:F0} m");
        Assert.InRange(track.SpeedLapLength, 2000f, 4500f);
        Assert.InRange(track.StuntLapLength, 1500f, 4000f);
    }

    [Fact]
    public void Solid_props_stay_clear_of_every_road()
    {
        var track = OriginalInspiredTrack.Build();
        var offenders = new List<string>();
        foreach (var prop in track.Props.Where(p => p.Solid))
        {
            var radius = MathF.Max(prop.Size.X, prop.Size.Z) * 0.5f;
            foreach (var path in track.Paths)
            {
                foreach (var f in path.Frames)
                {
                    // A pier stands under the deck it holds up and a drawbridge tower beside its lip: a road raised over
                    // the ground there is that structure. Every road on the ground keeps clear of them, as of the rest.
                    if (prop.Kind == PropKind.Pier && f.Origin.Y > prop.Position.Y + 0.5f) continue;
                    var flat = new Vector3(prop.Position.X - f.Origin.X, 0f, prop.Position.Z - f.Origin.Z);
                    if (flat.Length() < f.HalfWidth + 2f + radius)
                    {
                        offenders.Add($"{prop.Kind} '{prop.Label}' at ({prop.Position.X:F0}, {prop.Position.Z:F0}) touches {path.Kind} at s={f.Distance:F0} ({f.Origin.X:F0}, {f.Origin.Z:F0})");
                        break;
                    }
                }
            }
        }
        foreach (var o in offenders) _out.WriteLine(o);
        Assert.Empty(offenders);
    }

    /// <summary>
    /// The piers under the jump's ramps and the drawbridge towers are solid props, and none stands on a road on the
    /// ground: the speed route runs under the jump, and until 0.9 a tower stood with a leg on its asphalt.
    /// </summary>
    [Fact]
    public void Bridge_piers_and_towers_are_solid_and_off_the_roads_below()
    {
        var track = OriginalInspiredTrack.Build();
        var piers = track.Props.Where(p => p.Kind == PropKind.Pier).ToList();
        Assert.Contains(piers, p => p.Label == "pier");
        Assert.Contains(piers, p => p.Label == "tower");
        foreach (var pier in piers)
        {
            Assert.True(pier.Solid);
            // The footprint's corners, in the pier's yawed frame.
            var yaw = pier.YawDeg * MathUtil.Deg2Rad;
            var axisX = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            var axisZ = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
            foreach (var (sx, sz) in new[] { (-1f, -1f), (-1f, 1f), (1f, -1f), (1f, 1f) })
            {
                var corner = pier.Position + axisX * (sx * pier.Size.X * 0.5f) + axisZ * (sz * pier.Size.Z * 0.5f);
                foreach (var path in track.Paths)
                {
                    foreach (var f in path.Frames)
                    {
                        // Its own deck runs over it; a road on the ground must keep a metre clear of it.
                        if (f.Origin.Y > pier.Position.Y + 0.5f) continue;
                        var d = corner - f.Origin;
                        d.Y = 0f;
                        if (MathF.Abs(Vector3.Dot(d, Vector3.Normalize(f.Tangent with { Y = 0f }))) > 1.2f) continue;
                        var lateral = MathF.Abs(Vector3.Dot(d, Vector3.Normalize(f.Lateral with { Y = 0f })));
                        Assert.True(lateral > f.HalfWidth + 1f, $"{pier.Label} at ({pier.Position.X:F0}, {pier.Position.Z:F0}) reaches {path.Kind} s={f.Distance:F0}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Fields_stay_clear_of_every_road()
    {
        var track = OriginalInspiredTrack.Build();
        var fields = track.Props.Where(p => p.Kind == PropKind.Field).ToList();
        Assert.True(fields.Count >= 5, $"only {fields.Count} fields");
        var offenders = new List<string>();
        foreach (var field in fields)
        {
            var yaw = field.YawDeg * MathUtil.Deg2Rad;
            var axisX = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            var axisZ = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
            for (var u = -0.5f; u <= 0.5f; u += 0.1f)
            {
                for (var v = -0.5f; v <= 0.5f; v += 0.1f)
                {
                    var p = field.Position + axisX * (u * field.Size.X) + axisZ * (v * field.Size.Z);
                    var clearance = track.ClearanceFromRoads(p);
                    if (clearance < TrackBuilder.DefaultHalfWidth + 4f)
                    {
                        offenders.Add($"field '{field.Label}' at ({field.Position.X:F0}, {field.Position.Z:F0}) reaches within {clearance:F0} m of a road");
                        u = 1f;
                        break;
                    }
                }
            }
        }
        foreach (var o in offenders) _out.WriteLine(o);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Train_runs_across_the_speed_route_on_a_schedule()
    {
        var track = OriginalInspiredTrack.Build();
        var train = Assert.IsType<Train>(track.Train);
        var crossing = track.Speed.FrameAt(OriginalInspiredTrack.CrossingDistance);
        Assert.True(Vector3.Distance(new Vector3(train.Crossing.X, 0f, train.Crossing.Z), new Vector3(crossing.Origin.X, 0f, crossing.Origin.Z)) < 1f);
        Assert.InRange(MathF.Abs(Vector3.Dot(train.Direction, crossing.Tangent)), 0f, 0.05f);
        // Over one cycle the locomotive passes the crossing once and the cars stay on the line.
        var passes = 0;
        var wasBefore = train.HeadOffset < 0f;
        for (var t = 0f; t < train.Cycle; t += 1f / 60f)
        {
            train.Step(1f / 60f);
            var before = train.HeadOffset < 0f;
            if (wasBefore && !before && train.Running) passes++;
            wasBefore = before;
            foreach (var car in train.Cars)
            {
                var offLine = Vector3.Dot(car - train.Crossing, new Vector3(-train.Direction.Z, 0f, train.Direction.X));
                Assert.InRange(offLine, -0.01f, 0.01f);
            }
        }
        Assert.Equal(1, passes);
        _out.WriteLine($"train cycle {train.Cycle:F0} s, next crossing in {train.SecondsToCrossing():F0} s");
    }

    [Fact]
    public void Probe_finds_road_under_start_and_grass_beside_it()
    {
        var track = OriginalInspiredTrack.Build();
        var cursor = track.StartCursor;
        var start = track.StartFrame;

        var onRoad = track.Probe(start.Origin + start.Normal * 0.5f, ref cursor, true);
        Assert.Equal(SurfaceType.Road, onRoad.Surface);
        Assert.InRange(onRoad.Height, 0.45f, 0.55f);

        var beside = track.Probe(start.Origin + start.Lateral * 9f + Vector3.UnitY * 0.5f, ref cursor, false);
        Assert.Equal(SurfaceType.Grass, beside.Surface);
    }

    [Fact]
    public void Loop_frames_point_down_at_the_top()
    {
        var track = OriginalInspiredTrack.Build();
        var loopFrames = track.Stunt.Frames.Where(f => f.Kind == SegmentKind.Loop).ToList();
        Assert.NotEmpty(loopFrames);
        var top = loopFrames.OrderByDescending(f => f.Origin.Y).First();
        Assert.True(top.Normal.Y < -0.95f, $"top normal {top.Normal}");
        var bottom = loopFrames.OrderBy(f => f.Origin.Y).First();
        Assert.True(bottom.Normal.Y > 0.9f);
        _out.WriteLine($"loop height {top.Origin.Y - bottom.Origin.Y:F1} m");
    }

    [Fact]
    public void Skid_pad_sits_clear_of_roads_and_props()
    {
        var track = OriginalInspiredTrack.Build();
        var pad = Assert.Single(track.Pads);
        var clearance = track.ClearanceFromRoads(pad.Centre);
        _out.WriteLine($"pad at ({pad.Centre.X:F0}, {pad.Centre.Y:F1}, {pad.Centre.Z:F0}) r={pad.Radius:F0}, nearest road {clearance:F0} m");
        Assert.True(clearance > pad.Radius + 15f, $"road within {clearance:F0} m of the pad centre");
        foreach (var prop in track.Props.Where(p => p.Solid && p.Label != "pad-column"))
        {
            var d = new Vector3(prop.Position.X - pad.Centre.X, 0f, prop.Position.Z - pad.Centre.Z).Length();
            Assert.True(d > pad.Radius + 4f, $"{prop.Kind} '{prop.Label}' is {d:F0} m from the pad centre");
        }
        Assert.Contains(track.Props, p => p.Label == "pad-column" && Vector3.Distance(p.Position, pad.Centre) < 0.5f);
        // The pad is flat asphalt at its own height.
        var cursor = track.StartCursor;
        var hit = track.Probe(pad.Centre + Vector3.UnitY * 0.5f, ref cursor, false);
        Assert.Equal(SurfaceType.Pad, hit.Surface);
        Assert.InRange(hit.Height, 0.45f, 0.55f);
    }

    [Fact]
    public void Ground_meets_the_road_edges_and_rolls_elsewhere()
    {
        var track = OriginalInspiredTrack.Build();
        var terrain = track.Terrain;
        var crest = track.Common.Last;    // top of the hill, 10 m up
        Assert.True(crest.Origin.Y > 9f);
        foreach (var side in new[] { -1f, 1f })
        {
            var edge = crest.Origin + crest.Lateral * (side * (crest.HalfWidth + 1.3f));
            var ground = terrain.Height(edge.X, edge.Z);
            Assert.InRange(ground, edge.Y - 0.35f, edge.Y + 0.35f);
            var toe = crest.Origin + crest.Lateral * (side * 60f);
            Assert.True(terrain.Height(toe.X, toe.Z) < edge.Y - 4f, "the embankment should have come down by 60 m out");
        }
        // Bridges are structures: under the low bridge before the line the ground is the speed route's cutting, well
        // below the deck, not an embankment pulled up to it.
        var deck = track.Final.Frames.Where(f => f.Kind == SegmentKind.Overpass).OrderByDescending(f => f.Origin.Y).First();
        Assert.True(terrain.Height(deck.Origin.X, deck.Origin.Z) < deck.Origin.Y - 3f);
        // Dips cut into the ground.
        var dip = track.Speed.Frames.Where(f => f.Kind == SegmentKind.Dip).OrderBy(f => f.Origin.Y).First();
        Assert.True(dip.Origin.Y < -2f);
        Assert.InRange(terrain.Height(dip.Origin.X + dip.Lateral.X * 7f, dip.Origin.Z + dip.Lateral.Z * 7f), dip.Origin.Y - 0.6f, dip.Origin.Y + 0.6f);
        // Somewhere far from every road the ground is not quite flat: a gentle roll, the original's plain.
        var rolling = false;
        for (var x = -600f; x <= 600f && !rolling; x += 50f)
        {
            for (var z = -600f; z <= 600f; z += 50f)
            {
                if (track.ClearanceFromRoads(new Vector3(x, 0f, z)) > 80f && MathF.Abs(terrain.Height(x, z)) > 0.2f) { rolling = true; break; }
            }
        }
        Assert.True(rolling);
        // Every prop stands on the ground.
        foreach (var prop in track.Props)
        {
            Assert.InRange(prop.Position.Y, terrain.Height(prop.Position.X, prop.Position.Z) - 0.05f, terrain.Height(prop.Position.X, prop.Position.Z) + 0.05f);
        }
    }

    [Fact]
    public void Render_top_down_map()
    {
        var track = OriginalInspiredTrack.Build();
        var path = Path.Combine(ArtifactDir, "track-map.png");
        TrackMapRenderer.Render(track, path);
        _out.WriteLine(path);
        Assert.True(File.Exists(path));
    }
}

internal static class TrackMapRenderer
{
    public static void Render(TrackGeometry track, string file, int size = 1400)
    {
        var all = track.Paths.SelectMany(p => p.Frames.Select(f => f.Origin)).ToList();
        var minX = all.Min(p => p.X) - 60f;
        var maxX = all.Max(p => p.X) + 60f;
        var minZ = all.Min(p => p.Z) - 60f;
        var maxZ = all.Max(p => p.Z) + 60f;
        var scale = MathF.Min(size / (maxX - minX), size / (maxZ - minZ));
        var width = (int)((maxX - minX) * scale) + 1;
        var height = (int)((maxZ - minZ) * scale) + 1;
        var png = new PngWriter(width, height, (46, 110, 40));

        (int X, int Y) ToPixel(Vector3 p) => ((int)((p.X - minX) * scale), height - 1 - (int)((p.Z - minZ) * scale));

        foreach (var path in track.Paths)
        {
            var colour = path.Kind switch
            {
                PathKind.Common => ((byte)230, (byte)230, (byte)230),
                PathKind.Speed => ((byte)60, (byte)60, (byte)60),
                PathKind.Stunt => ((byte)90, (byte)70, (byte)40),
                _ => ((byte)150, (byte)150, (byte)150),
            };
            foreach (var f in path.Frames)
            {
                var c = f.Kind switch
                {
                    SegmentKind.Loop => ((byte)240, (byte)200, (byte)40),
                    SegmentKind.Gap => ((byte)30, (byte)120, (byte)220),
                    SegmentKind.RampUp or SegmentKind.RampDown => ((byte)200, (byte)200, (byte)200),
                    SegmentKind.BankedCurve => ((byte)220, (byte)90, (byte)60),
                    SegmentKind.Hill or SegmentKind.Dip => ((byte)120, (byte)170, (byte)90),
                    SegmentKind.Overpass => ((byte)230, (byte)120, (byte)40),
                    SegmentKind.Spline => ((byte)180, (byte)120, (byte)200),
                    _ => colour,
                };
                var (x, y) = ToPixel(f.Origin + f.Lateral * -f.HalfWidth);
                var (x2, y2) = ToPixel(f.Origin + f.Lateral * f.HalfWidth);
                var n = Math.Max(Math.Abs(x2 - x), Math.Abs(y2 - y)) + 1;
                for (var i = 0; i <= n; i++) png.Set(x + (x2 - x) * i / n, y + (y2 - y) * i / n, c);
            }
            foreach (var m in path.Markers)
            {
                var (x, y) = ToPixel(path.FrameAt(m.Distance).Origin);
                var c = m.Kind switch
                {
                    MarkerKind.StartFinish => ((byte)255, (byte)40, (byte)40),
                    MarkerKind.Checkpoint => ((byte)80, (byte)200, (byte)255),
                    MarkerKind.ReturnPoint => ((byte)255, (byte)255, (byte)255),
                    _ => ((byte)255, (byte)230, (byte)0),
                };
                png.Disc(x, y, m.Kind == MarkerKind.Sign ? 3 : 5, c);
            }
        }

        foreach (var pad in track.Pads)
        {
            var (x, y) = ToPixel(pad.Centre);
            png.Disc(x, y, (int)(pad.Radius * scale), ((byte)70, (byte)70, (byte)75));
        }

        // Fields as their outlines, the railway as a thin grey line through the crossing.
        foreach (var field in track.Props.Where(p => p.Kind == PropKind.Field))
        {
            var yaw = field.YawDeg * MathUtil.Deg2Rad;
            var ax = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw)) * (field.Size.X * 0.5f);
            var az = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw)) * (field.Size.Z * 0.5f);
            var corners = new[] { field.Position - ax - az, field.Position + ax - az, field.Position + ax + az, field.Position - ax + az };
            var colour = field.Label == "plough" ? ((byte)120, (byte)80, (byte)40) : field.Label == "hay" ? ((byte)190, (byte)170, (byte)70) : ((byte)30, (byte)90, (byte)30);
            for (var i = 0; i < 4; i++)
            {
                var (x1, y1) = ToPixel(corners[i]);
                var (x2, y2) = ToPixel(corners[(i + 1) % 4]);
                var n = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1;
                for (var k = 0; k <= n; k++) png.Set(x1 + (x2 - x1) * k / n, y1 + (y2 - y1) * k / n, colour);
            }
        }
        if (track.Train is { } train)
        {
            var (x1, y1) = ToPixel(train.Crossing - train.Direction * 320f);
            var (x2, y2) = ToPixel(train.Crossing + train.Direction * 320f);
            var n = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1;
            for (var k = 0; k <= n; k++) png.Set(x1 + (x2 - x1) * k / n, y1 + (y2 - y1) * k / n, ((byte)200, (byte)200, (byte)210));
        }

        foreach (var prop in track.Props)
        {
            var (x, y) = ToPixel(prop.Position);
            var c = prop.Kind switch
            {
                PropKind.Tree => ((byte)20, (byte)70, (byte)20),
                PropKind.Cow => ((byte)250, (byte)250, (byte)250),
                PropKind.Barn => ((byte)170, (byte)40, (byte)30),
                PropKind.House or PropKind.Shop or PropKind.Hut => ((byte)230, (byte)160, (byte)60),
                _ => ((byte)255, (byte)255, (byte)0),
            };
            png.Disc(x, y, Math.Max(2, (int)(prop.Size.X * scale * 0.5f)), c);
        }

        png.Save(file);
    }
}
