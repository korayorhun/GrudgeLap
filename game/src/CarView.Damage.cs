using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// Crash damage on the car as drawn: dents pressed into the skin, the glass and the lining, parts that break off,
/// cracked glass; and the repair that puts it all back when the car is set down again. The simulation knows
/// nothing of it: it stops the car at the crash and the instant replay shows the damage (<see cref="CrashScene"/>).
/// </summary>
internal sealed partial class CarView
{
    private readonly record struct DentSpec(Vector3 Centre, Vector3 Push, float Depth, float Radius, float Seed);

    /// <summary>A mesh that dents, with its arrays as built kept for repeated dents and for the repair.</summary>
    private sealed class DeformableMesh(MeshInstance3D node)
    {
        public readonly MeshInstance3D Node = node;
        public readonly ArrayMesh Original = (ArrayMesh)node.Mesh;
        public Godot.Collections.Array? Arrays;
        public Vector3[]? Vertices;
        public Vector3[]? Normals;
    }

    private readonly List<DeformableMesh> _deformables = [];
    private readonly List<DentSpec> _dents = [];
    private readonly List<Node3D> _parts = [];
    private readonly HashSet<Node> _wheelWells = [];
    private readonly List<Node3D> _mirrorParts = [];
    private readonly List<(Node3D Part, Node3D Parent, Transform3D Local)> _brokenOff = [];
    private readonly List<Node3D> _cracks = [];
    private static StandardMaterial3D? _crackedGlass;

    /// <summary>The paint the car was built in, for the bits of it a crash throws.</summary>
    public Color Paint => _paint;

    /// <summary>The body's reach in this node's frame (origin at the centre of gravity): nose and tail z, half width, and the road's height.</summary>
    public (float Nose, float Tail, float HalfWidth, float Road) Extent => (_style.Keys[0], _style.Keys[^1], _style.HalfWidth.Max(), -_cgHeight);

    private void AddDeformable(MeshInstance3D node) => _deformables.Add(new DeformableMesh(node));

    /// <summary>The parts that can break off: what is on the outside, but the wheel wells; and a hatchback's mirrors.</summary>
    private void CollectParts(Node3D body)
    {
        if (body.GetNodeOrNull<Node3D>("Outside") is { } outside)
        {
            foreach (var child in outside.GetChildren())
            {
                if (child is Node3D part && !_wheelWells.Contains(part)) _parts.Add(part);
            }
        }
        _parts.AddRange(_mirrorParts);
    }

    /// <summary>
    /// Presses a dent into the body: every point of the skin, the glass and the lining within
    /// <paramref name="radius"/> of <paramref name="point"/> (this node's frame) moves along <paramref name="push"/>,
    /// by <paramref name="depth"/> at the centre and less towards the rim, wavering so the panel creases instead of
    /// sinking smoothly. Returns the dent's index, so a dent can grow over a few frames (<see cref="SetDentDepths"/>).
    /// </summary>
    public int Dent(Vector3 point, Vector3 push, float depth, float radius)
    {
        _dents.Add(new DentSpec(point + new Vector3(0f, _cgHeight, 0f), push.Normalized(), depth, radius, 1.3f + 2.1f * _dents.Count));
        ApplyDents();
        return _dents.Count - 1;
    }

    /// <summary>Sets the depths of several dents at once and rebuilds the meshes once (a negative index is skipped).</summary>
    public void SetDentDepths(params (int Dent, float Depth)[] depths)
    {
        foreach (var (dent, depth) in depths)
        {
            if (dent >= 0 && dent < _dents.Count) _dents[dent] = _dents[dent] with { Depth = depth };
        }
        ApplyDents();
    }

    /// <summary>Rebuilds every dented mesh from its arrays as built, with all the dents so far.</summary>
    private void ApplyDents()
    {
        foreach (var d in _deformables)
        {
            if (d.Vertices is null)
            {
                if (d.Original.GetSurfaceCount() == 0) continue;
                d.Arrays = d.Original.SurfaceGetArrays(0);
                d.Vertices = d.Arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                d.Normals = d.Arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            }
            var source = d.Vertices;
            var vertices = (Vector3[])source.Clone();
            var normals = (Vector3[])d.Normals!.Clone();
            var moved = new bool[source.Length];
            var any = false;
            for (var i = 0; i < source.Length; i++)
            {
                var v = source[i];
                var offset = Vector3.Zero;
                foreach (var dent in _dents)
                {
                    var distanceSq = v.DistanceSquaredTo(dent.Centre);
                    if (distanceSq >= dent.Radius * dent.Radius) continue;
                    var f = 1f - distanceSq / (dent.Radius * dent.Radius);
                    var crease = 0.7f + 0.3f * MathF.Sin(v.X * 21f + dent.Seed) * MathF.Sin(v.Y * 17f + v.Z * 13f + 2f * dent.Seed);
                    offset += dent.Push * (dent.Depth * f * f * crease);
                }
                if (offset.LengthSquared() < 1e-10f) continue;
                vertices[i] = v + offset;
                moved[i] = true;
                any = true;
            }
            if (!any)
            {
                d.Node.Mesh = d.Original;
                continue;
            }
            // Every moved face takes its own normal: crumpled sheet metal catches the light in facets. The meshes are
            // plain triangle lists (MeshBuilder adds no index), three vertices to a face.
            for (var i = 0; i + 2 < vertices.Length; i += 3)
            {
                if (!moved[i] && !moved[i + 1] && !moved[i + 2]) continue;
                var n = (vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]);
                if (n.LengthSquared() < 1e-14f) continue;
                n = n.Normalized();
                if (n.Dot(normals[i] + normals[i + 1] + normals[i + 2]) < 0f) n = -n;
                normals[i] = n;
                normals[i + 1] = n;
                normals[i + 2] = n;
            }
            var arrays = new Godot.Collections.Array(d.Arrays!);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            d.Node.Mesh = mesh;
        }
    }

    /// <summary>
    /// The outside parts within <paramref name="reach"/> of <paramref name="point"/> (this node's frame) come off:
    /// each leaves the car for <paramref name="debris"/>, wrapped in a node at its own centre so it tumbles about
    /// its middle, and is returned for the caller to throw. <see cref="Repair"/> puts them back.
    /// </summary>
    public List<Node3D> BreakOff(Vector3 point, float reach, Node3D debris)
    {
        var centre = point + new Vector3(0f, _cgHeight, 0f);
        var holders = new List<Node3D>();
        foreach (var part in _parts)
        {
            if (part.GetParent() is not Node3D parent || _brokenOff.Any(b => b.Part == part)) continue;
            var middle = part is VisualInstance3D visual ? visual.GetAabb().GetCenter() : Vector3.Zero;
            // The part's middle in the body's frame: its parent is the body or the outside node, which sits on the body.
            var inBody = parent == _body ? part.Transform * middle : parent.Transform * (part.Transform * middle);
            if (inBody.DistanceTo(centre) > reach) continue;
            var world = part.GlobalTransform;
            _brokenOff.Add((part, parent, part.Transform));
            parent.RemoveChild(part);
            var holder = new Node3D { Name = "Part" };
            debris.AddChild(holder);
            holder.GlobalTransform = new Transform3D(world.Basis, world * middle);
            holder.AddChild(part);
            part.Transform = new Transform3D(Basis.Identity, -middle);
            holders.Add(holder);
        }
        return holders;
    }

    /// <summary>
    /// Cracks the glass within <paramref name="radius"/> of <paramref name="point"/> (this node's frame): its faces
    /// there are laid again just outside it with a pattern of white cracks, the glass under them left as it was.
    /// </summary>
    public void CrackGlass(Vector3 point, float radius)
    {
        if (_glass?.Mesh is not ArrayMesh glass || glass.GetSurfaceCount() == 0 || _body is null) return;
        var centre = point + new Vector3(0f, _cgHeight, 0f);
        var arrays = glass.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var st = new MeshBuilder();
        var faces = 0;
        for (var i = 0; i + 2 < vertices.Length; i += 3)
        {
            var middle = (vertices[i] + vertices[i + 1] + vertices[i + 2]) / 3f;
            if (middle.DistanceTo(centre) > radius) continue;
            var n = normals[i] + normals[i + 1] + normals[i + 2];
            if (n.LengthSquared() < 1e-12f) continue;
            n = n.Normalized();
            var lift = n * 0.0015f;
            Tri(st, vertices[i] + lift, normals[i], vertices[i + 1] + lift, normals[i + 1], vertices[i + 2] + lift, normals[i + 2], Colors.White, n);
            faces++;
        }
        if (faces == 0) return;
        var cracks = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = CrackedGlass(), Name = "Cracks", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _body.AddChild(cracks);
        _cracks.Add(cracks);
    }

    /// <summary>
    /// White cracks on clear ground, laid over the tinted glass: the edges of cellular noise cells (where the
    /// nearest two cell centres are about as near) turned into thin lines by a colour ramp, mapped across the
    /// panes from all three axes since the loft has no texture coordinates.
    /// </summary>
    private static StandardMaterial3D CrackedGlass()
    {
        if (_crackedGlass is not null) return _crackedGlass;
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.92f, 0.95f, 1f, 0.95f));
        ramp.SetOffset(0, 0f);
        ramp.SetColor(1, new Color(0.85f, 0.9f, 0.95f, 0f));
        ramp.SetOffset(1, 0.12f);
        ramp.AddPoint(0.05f, new Color(0.9f, 0.93f, 0.97f, 0.55f));
        ramp.AddPoint(1f, new Color(0.85f, 0.9f, 0.95f, 0.08f));
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
            CellularDistanceFunction = FastNoiseLite.CellularDistanceFunctionEnum.Euclidean,
            Frequency = 0.035f,
            Seed = 5,
        };
        _crackedGlass = new StandardMaterial3D
        {
            AlbedoTexture = new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true, ColorRamp = ramp },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(2.5f, 2.5f, 2.5f),
            Metallic = 0.3f,
            Roughness = 0.15f,
        };
        return _crackedGlass;
    }

    /// <summary>The car as built again: no dents, the parts back where they were, the glass whole.</summary>
    public void Repair()
    {
        _dents.Clear();
        foreach (var d in _deformables) d.Node.Mesh = d.Original;
        foreach (var (part, parent, local) in _brokenOff)
        {
            part.GetParent()?.RemoveChild(part);
            parent.AddChild(part);
            part.Transform = local;
        }
        _brokenOff.Clear();
        foreach (var crack in _cracks) crack.QueueFree();
        _cracks.Clear();
    }
}
