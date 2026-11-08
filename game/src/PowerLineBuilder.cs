using Godot;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The power line along the roads (0.50): wooden poles every 55 m on one side of each road, 25 m out, each with a
/// crossarm and three insulators, and the three wires sagging between them (assets/shaders/wire.gdshader), as rural
/// distribution lines run beside Wyoming's roads. The line breaks where the fences do (other roads, buildings, fields,
/// pads, steps in the ground) and starts again past them.
/// </summary>
/// <remarks>
/// The poles are instances of one mesh, tapered 30 to 22 cm, 10 m tall over the ground and 1.5 m in it, in the fences'
/// weathered wood a shade darker (creosote); the wires one mesh a stretch of line, a parabola of 0.9 m sag over a 55 m
/// span drawn as camera-facing ribbons at least a pixel and a half wide (a 2 cm wire is a twentieth of a pixel 300 m
/// off). Visual only; not in the 1989 look nor the export.
/// </remarks>
internal static class PowerLineBuilder
{
    private const float Offset = 25f;
    private const float Span = 55f;
    private const float PoleHeight = 10f;
    private const float ArmHeight = 9.4f;
    private static readonly float[] WireAcross = [-1.05f, 0f, 1.05f];

    private static StandardMaterial3D? _wood;
    private static ShaderMaterial? _wire;

    /// <summary>Drops the shared materials (the game quits).</summary>
    public static void ReleaseShared()
    {
        _wood = null;
        _wire = null;
    }

    private static StandardMaterial3D Wood => _wood ??= new StandardMaterial3D
    {
        AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/fence_color.jpg"),
        NormalEnabled = true,
        NormalTexture = GD.Load<Texture2D>("res://assets/textures/fence_normal.jpg"),
        Roughness = 1f,
        // A pole's creosote, darker and browner than the fences' grey rails.
        AlbedoColor = new Color(0.55f, 0.47f, 0.40f),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    };

    private static ShaderMaterial Wire => _wire ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/wire.gdshader") };

    /// <summary>A pole in its own frame (y up, the crossarm along z, across the line): the tapered post, the arm, the insulators.</summary>
    private static ArrayMesh PoleMesh()
    {
        var st = new MeshBuilder();
        void Cylinder(Vector3 a, Vector3 b, float ra, float rb, int sides, float uScale)
        {
            var axis = (b - a).Normalized();
            var side = Mathf.Abs(axis.Y) < 0.9f ? axis.Cross(Vector3.Up).Normalized() : axis.Cross(Vector3.Right).Normalized();
            var up = side.Cross(axis).Normalized();
            var length = (b - a).Length();
            for (var k = 0; k < sides; k++)
            {
                var a0 = Mathf.Tau * k / sides;
                var a1 = Mathf.Tau * (k + 1) / sides;
                var n0 = side * Mathf.Cos(a0) + up * Mathf.Sin(a0);
                var n1 = side * Mathf.Cos(a1) + up * Mathf.Sin(a1);
                var u0 = 0.02f + uScale * k / sides;
                var u1 = 0.02f + uScale * (k + 1) / sides;
                var v = length / 2f;
                st.SetNormal(n0); st.SetUV(new Vector2(u0, 0f)); st.AddVertex(a + n0 * ra);
                st.SetNormal(n1); st.SetUV(new Vector2(u1, 0f)); st.AddVertex(a + n1 * ra);
                st.SetNormal(n1); st.SetUV(new Vector2(u1, v)); st.AddVertex(b + n1 * rb);
                st.SetNormal(n0); st.SetUV(new Vector2(u0, 0f)); st.AddVertex(a + n0 * ra);
                st.SetNormal(n1); st.SetUV(new Vector2(u1, v)); st.AddVertex(b + n1 * rb);
                st.SetNormal(n0); st.SetUV(new Vector2(u0, v)); st.AddVertex(b + n0 * rb);
            }
        }
        Cylinder(new Vector3(0f, -1.5f, 0f), new Vector3(0f, PoleHeight, 0f), 0.15f, 0.11f, 8, 0.09f);
        // The crossarm: a square timber 2.4 m long across the line, 9.4 m up.
        Cylinder(new Vector3(0f, ArmHeight, -1.2f), new Vector3(0f, ArmHeight, 1.2f), 0.06f, 0.06f, 4, 0.07f);
        foreach (var z in WireAcross)
        {
            Cylinder(new Vector3(0f, ArmHeight + 0.05f, z), new Vector3(0f, ArmHeight + 0.30f, z), 0.045f, 0.03f, 6, 0.04f);
        }
        return st.Commit();
    }

    /// <summary>The line along every road of <paramref name="track"/>; an empty node for the export.</summary>
    public static Node3D Build(TrackGeometry track)
    {
        // Built for the world export too (0.76): WorldExport carries the poles as instances of one template mesh; the wires,
        // ribbons the wire shader turns to the camera, stay out of the export and the other engine strings its own.
        var root = new Node3D { Name = "PowerLines" };
        var terrain = track.Terrain;
        var obstacles = track.Props
            .Where(p => p.Kind is PropKind.Barn or PropKind.House or PropKind.Shop or PropKind.Hut or PropKind.Tower or PropKind.Pier or PropKind.Pump or PropKind.Gate or PropKind.Train or PropKind.Tree)
            .Select(p => (At: new SN.Vector2(p.Position.X, p.Position.Z), Reach: 8f + 0.5f * MathF.Max(p.Size.X, p.Size.Z)))
            .Concat(track.Props.Where(p => p.Kind == PropKind.Cow).Select(p => (At: new SN.Vector2(p.Position.X, p.Position.Z), Reach: 4f)))
            .ToArray();
        var fields = track.Props.Where(p => p.Kind == PropKind.Field).ToArray();
        bool Blocked(SN.Vector3 p)
        {
            var ground = terrain.Height(p.X, p.Z, out var roadWeight);
            if (roadWeight > 0.02f) return true;
            // A deck less than 2.5 m over the ground (0.73: five bays of the cutting's fence crossed the stunt route's low
            // return-leg bridge), and the railway, which a fence leaves a gap for.
            if (terrain.DeckHeight(p.X, p.Z, 1f) - ground < 2.5f || track.Train?.OffsetFromLine(p.X, p.Z) < 2.5f) return true;
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
                if (MathF.Abs(across) < field.Size.X * 0.5f + 2f && MathF.Abs(along) < field.Size.Z * 0.5f + 2f) return true;
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

        var poles = new List<Transform3D>();
        var wires = new MeshBuilder();
        var wireCount = 0;
        void Wires(Transform3D a, Transform3D b)
        {
            // Each wire a parabola of 0.9 m sag between its insulators, 16 segments, two vertices a point (the ribbon's edges).
            foreach (var z in WireAcross)
            {
                var from = a * new Vector3(0f, ArmHeight + 0.32f, z);
                var to = b * new Vector3(0f, ArmHeight + 0.32f, z);
                const int segments = 16;
                Vector3 At(int i)
                {
                    var t = i / (float)segments;
                    return from.Lerp(to, t) + Vector3.Down * (0.9f * 4f * t * (1f - t));
                }
                for (var i = 0; i < segments; i++)
                {
                    var p0 = At(i);
                    var p1 = At(i + 1);
                    var dir = (p1 - p0).Normalized();
                    wires.SetNormal(dir); wires.SetUV(new Vector2(-1f, 0f)); wires.AddVertex(p0);
                    wires.SetNormal(dir); wires.SetUV(new Vector2(1f, 0f)); wires.AddVertex(p0);
                    wires.SetNormal(dir); wires.SetUV(new Vector2(1f, 1f)); wires.AddVertex(p1);
                    wires.SetNormal(dir); wires.SetUV(new Vector2(-1f, 0f)); wires.AddVertex(p0);
                    wires.SetNormal(dir); wires.SetUV(new Vector2(1f, 1f)); wires.AddVertex(p1);
                    wires.SetNormal(dir); wires.SetUV(new Vector2(-1f, 1f)); wires.AddVertex(p1);
                }
            }
            wireCount++;
        }
        for (var pathIndex = 0; pathIndex < track.Paths.Count; pathIndex++)
        {
            var path = track.Paths[pathIndex];
            // One side of each road, alternating from road to road.
            var side = pathIndex % 2 == 0 ? 1f : -1f;
            Transform3D? last = null;
            for (var s = 10f; s < path.Length; s += Span)
            {
                var f = path.FrameAt(s);
                if (!Open(f))
                {
                    last = null;
                    continue;
                }
                var foot = f.Origin + f.Lateral * (side * (f.HalfWidth + Offset - 5.5f));
                if (Blocked(foot))
                {
                    last = null;
                    continue;
                }
                foot.Y = terrain.Height(foot.X, foot.Z);
                var heading = Convert.ToGodot(f.Tangent);
                heading.Y = 0f;
                heading = heading.Normalized();
                // x along the line, y up, z = x cross y across it (the crossarm's way): a right-handed frame.
                var basis = new Basis(heading, Vector3.Up, heading.Cross(Vector3.Up).Normalized());
                var pole = new Transform3D(basis, Convert.ToGodot(foot));
                poles.Add(pole);
                if (last is { } previous && previous.Origin.DistanceTo(pole.Origin) < Span * 1.5f) Wires(previous, pole);
                last = pole;
            }
        }
        if (poles.Count > 0)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = PoleMesh(), InstanceCount = poles.Count };
            for (var i = 0; i < poles.Count; i++) multi.SetInstanceTransform(i, poles[i]);
            root.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = Wood, Name = "Poles" });
        }
        if (wireCount > 0 && !WorldBuilder.ExportMode) root.AddChild(new MeshInstance3D { Mesh = wires.Commit(), MaterialOverride = Wire, Name = "Wires", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (WorldBuilder.PerfLog) GD.Print($"power lines: {poles.Count} poles, {wireCount} spans");
        return root;
    }
}
