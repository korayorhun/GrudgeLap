using Godot;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Jackson Hole's buck-and-rail fences (0.49): lines of weathered lodgepole along both sides of the roads at the edge of
/// the right of way, some 30 m out, each 3 m bay an X of two crossed legs with a rail in its crotch and two more pinned
/// to one leg below, broken where another road, a building, a field, a pad or the earthworks come near, and for a gate now
/// and then. Seen from every road in Wyoming's ranch country; until 0.49 the plain ran unfenced to the woods.
/// </summary>
/// <remarks>
/// The bays are instances of one mesh (six-sided poles 12 cm across, Poly Haven's Brown Planks 03 mapped along each pole
/// within one plank's grain), sheared to the ground's slope between their ends so the legs stay upright; chunks of 200 m
/// of road drawn within 700 m. Visual only: the simulation does not know them (the car crosses them 30 m off the road).
/// Not in the 1989 look nor the export.
/// </remarks>
internal static class FenceBuilder
{
    /// <summary>Metres from the road's centre line to the fence.</summary>
    private const float Offset = 32f;
    private const float Bay = 3f;
    private const float PoleRadius = 0.06f;

    private static StandardMaterial3D? _material;

    /// <summary>Drops the shared material (the game quits).</summary>
    public static void ReleaseShared() => _material = null;

    private static StandardMaterial3D Material => _material ??= new StandardMaterial3D
    {
        AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/fence_color.jpg"),
        NormalEnabled = true,
        NormalTexture = GD.Load<Texture2D>("res://assets/textures/fence_normal.jpg"),
        RoughnessTexture = GD.Load<Texture2D>("res://assets/textures/fence_roughness.jpg"),
        Roughness = 1f,
        // Sun and rain have greyed the poles a shade past the photo's planks.
        AlbedoColor = new Color(0.92f, 0.92f, 0.90f),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    };

    /// <summary>
    /// One bay in its own frame (x along the fence, y up, z across): the buck's two legs at x = 0 splayed across the line
    /// and crossing a metre up, the top rail in their crotch and two rails pinned to the outer face of one leg, each rail
    /// 3.4 m long so it overlaps the next bay's.
    /// </summary>
    private static ArrayMesh BayMesh()
    {
        var st = new MeshBuilder();
        var plank = 0;
        void Pole(Vector3 a, Vector3 b)
        {
            // Within one plank of the photo (nine across it): its grain runs along the pole, the joints never show.
            var u0 = 0.015f + 0.111f * (plank++ % 9);
            var axis = b - a;
            var length = axis.Length();
            var dir = axis / length;
            var side = Mathf.Abs(dir.Y) < 0.9f ? dir.Cross(Vector3.Up).Normalized() : dir.Cross(Vector3.Right).Normalized();
            var up = side.Cross(dir).Normalized();
            const int sides = 6;
            for (var k = 0; k < sides; k++)
            {
                var a0 = Mathf.Tau * k / sides;
                var a1 = Mathf.Tau * (k + 1) / sides;
                var n0 = side * Mathf.Cos(a0) + up * Mathf.Sin(a0);
                var n1 = side * Mathf.Cos(a1) + up * Mathf.Sin(a1);
                var ua = u0 + 0.075f * k / sides;
                var ub = u0 + 0.075f * (k + 1) / sides;
                var vb = length / 2f;
                st.SetNormal(n0); st.SetUV(new Vector2(ua, 0f)); st.AddVertex(a + n0 * PoleRadius);
                st.SetNormal(n1); st.SetUV(new Vector2(ub, 0f)); st.AddVertex(a + n1 * PoleRadius);
                st.SetNormal(n1); st.SetUV(new Vector2(ub, vb)); st.AddVertex(b + n1 * PoleRadius);
                st.SetNormal(n0); st.SetUV(new Vector2(ua, 0f)); st.AddVertex(a + n0 * PoleRadius);
                st.SetNormal(n1); st.SetUV(new Vector2(ub, vb)); st.AddVertex(b + n1 * PoleRadius);
                st.SetNormal(n0); st.SetUV(new Vector2(ua, vb)); st.AddVertex(b + n0 * PoleRadius);
            }
        }
        // The buck: two legs from feet 1.2 m apart across the line, leaning in, crossing at about a metre.
        Pole(new Vector3(0f, -0.1f, -0.62f), new Vector3(0f, 1.55f, 0.36f));
        Pole(new Vector3(0f, -0.1f, 0.62f), new Vector3(0f, 1.55f, -0.36f));
        // The top rail in the crotch; two more on the outer face of the first leg, lower down.
        Pole(new Vector3(-0.2f, 1.02f, 0f), new Vector3(3.2f, 1.02f, 0f));
        Pole(new Vector3(-0.2f, 0.70f, -0.36f), new Vector3(3.2f, 0.70f, -0.36f));
        Pole(new Vector3(-0.2f, 0.38f, -0.55f), new Vector3(3.2f, 0.38f, -0.55f));
        return st.Commit();
    }

    /// <summary>The fences along every road of <paramref name="track"/>; an empty node for the export.</summary>
    public static Node3D Build(TrackGeometry track)
    {
        var root = new Node3D { Name = "Fences" };
        if (WorldBuilder.ExportMode) return root;
        var terrain = track.Terrain;
        var obstacles = track.Props
            .Where(p => p.Kind is PropKind.Barn or PropKind.House or PropKind.Shop or PropKind.Hut or PropKind.Tower or PropKind.Pier or PropKind.Pump or PropKind.Gate or PropKind.Train)
            .Select(p => (At: new SN.Vector2(p.Position.X, p.Position.Z), Reach: 12f + 0.5f * MathF.Max(p.Size.X, p.Size.Z)))
            // A cow standing where the line would run (some graze 30 m off the road): the fence opens round it (0.50).
            .Concat(track.Props.Where(p => p.Kind == PropKind.Cow).Select(p => (At: new SN.Vector2(p.Position.X, p.Position.Z), Reach: 5f)))
            .ToArray();
        var fields = track.Props.Where(p => p.Kind == PropKind.Field).ToArray();
        bool Blocked(SN.Vector3 p)
        {
            var ground = terrain.Height(p.X, p.Z, out var roadWeight);
            if (roadWeight > 0.02f) return true;
            // A deck less than 2.5 m over the ground (0.73: five bays of the cutting's fence crossed the stunt route's low
            // return-leg bridge), and the railway, which a fence leaves a gap for.
            if (terrain.DeckHeight(p.X, p.Z, 2f) - ground < 2.5f || track.Train?.OffsetFromLine(p.X, p.Z) < 2.5f) return true;
            var at = new SN.Vector2(p.X, p.Z);
            foreach (var (centre, reach) in obstacles)
            {
                if (SN.Vector2.DistanceSquared(at, centre) < reach * reach) return true;
            }
            foreach (var field in fields)
            {
                var dx = p.X - field.Position.X;
                var dz = p.Z - field.Position.Z;
                var yaw = field.YawDeg * MathF.PI / 180f;
                var along = dx * MathF.Sin(yaw) + dz * MathF.Cos(yaw);
                var across = dx * MathF.Cos(yaw) - dz * MathF.Sin(yaw);
                if (MathF.Abs(across) < field.Size.X * 0.5f + 3f && MathF.Abs(along) < field.Size.Z * 0.5f + 3f) return true;
            }
            foreach (var pad in track.Pads)
            {
                var dx = p.X - pad.Centre.X;
                var dz = p.Z - pad.Centre.Z;
                if (dx * dx + dz * dz < (pad.Radius + 6f) * (pad.Radius + 6f)) return true;
            }
            return false;
        }
        static bool Open(in RoadFrame f) =>
            f.Surface == SurfaceType.Road && f.Kind is SegmentKind.Straight or SegmentKind.Curve or SegmentKind.Hill or SegmentKind.Dip or SegmentKind.Spline or SegmentKind.BankedCurve;

        var mesh = BayMesh();
        var material = Material;
        var rng = new Random(1890);
        var total = 0;
        for (var pathIndex = 0; pathIndex < track.Paths.Count; pathIndex++)
        {
            var path = track.Paths[pathIndex];
            foreach (var side in new[] { -1f, 1f })
            {
                var chunk = new List<Transform3D>();
                var chunkStart = 0f;
                var nextGate = 150f + 350f * (float)rng.NextDouble();
                void Flush()
                {
                    if (chunk.Count == 0) return;
                    var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = chunk.Count };
                    for (var i = 0; i < chunk.Count; i++) multi.SetInstanceTransform(i, chunk[i]);
                    root.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = material, VisibilityRangeEnd = 700f, VisibilityRangeEndMargin = 40f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
                    total += chunk.Count;
                    chunk = new List<Transform3D>();
                }
                for (var s = 0f; s + Bay < path.Length; s += Bay)
                {
                    if (s - chunkStart > 200f)
                    {
                        Flush();
                        chunkStart = s;
                    }
                    var f0 = path.FrameAt(s);
                    var f1 = path.FrameAt(s + Bay);
                    if (!Open(f0) || !Open(f1)) continue;
                    // A gate every few hundred metres: two bays left out.
                    if (s >= nextGate)
                    {
                        if (s >= nextGate + 2f * Bay) nextGate = s + 250f + 400f * (float)rng.NextDouble();
                        continue;
                    }
                    var a = f0.Origin + f0.Lateral * (side * (f0.HalfWidth + Offset - 5.5f));
                    var b = f1.Origin + f1.Lateral * (side * (f1.HalfWidth + Offset - 5.5f));
                    if (Blocked(a) || Blocked(b)) continue;
                    a.Y = terrain.Height(a.X, a.Z);
                    b.Y = terrain.Height(b.X, b.Z);
                    var run = b - a;
                    var flat = MathF.Sqrt(run.X * run.X + run.Z * run.Z);
                    // Not over a step in the ground (the cuttings' faces) nor round a corner sharper than a bay can bend.
                    if (flat < 1.5f || flat > 4.5f || MathF.Abs(run.Y) > 1.2f) continue;
                    var from = Convert.ToGodot(a);
                    var to = Convert.ToGodot(b);
                    var along = (to - from) / Bay;
                    // x along, y up, z = x cross y: a right-handed frame, or the mesh would be mirrored and drawn inside out.
                    var across = new Vector3(-along.Z, 0f, along.X).Normalized();
                    chunk.Add(new Transform3D(new Basis(along, Vector3.Up, across), from));
                }
                Flush();
            }
        }
        if (WorldBuilder.PerfLog) GD.Print($"fences: {total} bays in {root.GetChildCount()} chunks");
        return root;
    }
}
