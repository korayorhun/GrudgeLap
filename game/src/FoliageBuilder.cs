using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Vegetation and fields: grass tufts as cross-quads in multimeshes along every road and scattered over the
/// ground, trees as a bark trunk with branches under a canopy of leaf-cluster cards, and fields as patches
/// conforming to the terrain. The tuft and leaf atlases are generated from CC0 material (assets/textures).
/// </summary>
internal static class FoliageBuilder
{
    private const int TuftVariants = 8;
    private static StandardMaterial3D? _tuft;
    private static StandardMaterial3D? _leaf;
    private static StandardMaterial3D? _bark;

    public static void ReleaseShared()
    {
        _tuft = null;
        _leaf = null;
        _bark = null;
    }

    private static StandardMaterial3D Tuft => _tuft ??= new StandardMaterial3D
    {
        AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/grass_blades.png"),
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
        AlphaScissorThreshold = 0.45f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
        Roughness = 0.95f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        // Beyond 150 m the cards would only be blocky mipmap blobs; the ground texture carries the grass there.
        DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither,
        DistanceFadeMinDistance = 110f,
        DistanceFadeMaxDistance = 170f,
    };

    private static StandardMaterial3D Leaf => _leaf ??= new StandardMaterial3D
    {
        AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/leaf_clusters.png"),
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
        AlphaScissorThreshold = 0.5f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
        Roughness = 0.9f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    };

    private static StandardMaterial3D Bark => _bark ??= WorldBuilder.PropMaterial("bark", new Color(0.9f, 0.85f, 0.75f), 0.6f);

    // ---- grass ----------------------------------------------------------------------------------------

    /// <summary>Grass tufts along the roadsides (dense) and over the open ground (sparse), skipping roads, pads and fields.</summary>
    public static Node3D BuildGrass(TrackGeometry track)
    {
        var rng = new Random(29);
        var terrain = track.Terrain;
        var root = new Node3D { Name = "Grass" };
        var transforms = new List<Transform3D>[TuftVariants];
        var colours = new List<Color>[TuftVariants];
        for (var v = 0; v < TuftVariants; v++)
        {
            transforms[v] = new List<Transform3D>();
            colours[v] = new List<Color>();
        }

        // Fields are bare or cropped ground: no tufts on them.
        var fields = track.Props.Where(p => p.Kind == PropKind.Field).ToArray();
        bool InField(float x, float z)
        {
            foreach (var field in fields)
            {
                var dx = x - field.Position.X;
                var dz = z - field.Position.Z;
                var yaw = field.YawDeg * MathF.PI / 180f;
                var along = dx * MathF.Sin(yaw) + dz * MathF.Cos(yaw);
                var across = dx * MathF.Cos(yaw) - dz * MathF.Sin(yaw);
                if (MathF.Abs(across) < field.Size.X * 0.5f + 1f && MathF.Abs(along) < field.Size.Z * 0.5f + 1f) return true;
            }
            return false;
        }

        void Place(float x, float z, float scale)
        {
            var y = terrain.Height(x, z, out var roadWeight);
            if (roadWeight > 0.97f) return;   // on the deck itself
            if (InField(x, z)) return;
            foreach (var pad in track.Pads)
            {
                var dx = x - pad.Centre.X;
                var dz = z - pad.Centre.Z;
                if (dx * dx + dz * dz < (pad.Radius + 2f) * (pad.Radius + 2f)) return;
            }
            var v = rng.Next(TuftVariants);
            var yaw = (float)(rng.NextDouble() * Mathf.Tau);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale * (0.85f + 0.3f * (float)rng.NextDouble()), scale));
            transforms[v].Add(new Transform3D(basis, Convert.ToGodot(new SN.Vector3(x, y, z))));
            // Olive, close to the ground texture's tint, so the tufts read as part of the grass rather than as sprites.
            var tone = 0.42f + 0.22f * (float)rng.NextDouble();
            colours[v].Add(new Color(tone * (0.85f + 0.2f * (float)rng.NextDouble()), tone, tone * 0.62f));
        }

        // Dense along every road, thinning out with distance from the shoulder.
        foreach (var path in track.Paths)
        {
            var frames = path.Frames;
            for (var i = 0; i < frames.Count; i += 2)
            {
                var f = frames[i];
                if (f.Kind is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.Gap or SegmentKind.RampDown or SegmentKind.Overpass) continue;
                if (f.Surface != SurfaceType.Road) continue;
                for (var k = 0; k < 5; k++)
                {
                    var side = rng.Next(2) == 0 ? -1f : 1f;
                    var lateral = f.HalfWidth + 2.2f + (float)rng.NextDouble() * 26f;
                    if (rng.NextDouble() > 1.1f - (lateral - f.HalfWidth) / 30f) continue;
                    var p = f.Origin + f.Lateral * (side * lateral);
                    Place(p.X, p.Z, 0.55f + 0.45f * (float)rng.NextDouble());
                }
            }
        }

        // Sparse scatter over the ground around the whole circuit.
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var path in track.Paths)
        {
            foreach (var f in path.Frames)
            {
                minX = MathF.Min(minX, f.Origin.X); maxX = MathF.Max(maxX, f.Origin.X);
                minZ = MathF.Min(minZ, f.Origin.Z); maxZ = MathF.Max(maxZ, f.Origin.Z);
            }
        }
        for (var x = minX - 300f; x <= maxX + 300f; x += 9f)
        {
            for (var z = minZ - 300f; z <= maxZ + 300f; z += 9f)
            {
                if (rng.NextDouble() > 0.4) continue;
                Place(x + (float)rng.NextDouble() * 8f, z + (float)rng.NextDouble() * 8f, 0.7f + 0.6f * (float)rng.NextDouble());
            }
        }

        var total = 0;
        for (var v = 0; v < TuftVariants; v++)
        {
            if (transforms[v].Count == 0) continue;
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = TuftMesh(v),
                InstanceCount = transforms[v].Count,
            };
            for (var i = 0; i < transforms[v].Count; i++)
            {
                multi.SetInstanceTransform(i, transforms[v][i]);
                multi.SetInstanceColor(i, colours[v][i]);
            }
            root.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = Tuft, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            total += transforms[v].Count;
        }
        GD.Print($"grass tufts: {total}");
        return root;
    }

    /// <summary>Two crossed quads, 1.2 m wide and 0.75 m tall, mapped to one cell of the tuft atlas (4 x 2).</summary>
    private static ArrayMesh TuftMesh(int variant)
    {
        var st = new MeshBuilder();
        var u0 = (variant % 4) / 4f;
        var v0 = (variant / 4) / 2f;
        var u1 = u0 + 0.25f;
        var v1 = v0 + 0.5f;
        const float w = 0.6f, h = 0.75f;
        foreach (var yaw in new[] { 0f, Mathf.Pi / 2f })
        {
            var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var a = -right * w; var b = right * w;
            var c = right * w + Vector3.Up * h; var d = -right * w + Vector3.Up * h;
            st.SetNormal(Vector3.Up);
            st.SetUV(new Vector2(u0, v1)); st.AddVertex(a);
            st.SetUV(new Vector2(u1, v1)); st.AddVertex(b);
            st.SetUV(new Vector2(u1, v0)); st.AddVertex(c);
            st.SetUV(new Vector2(u0, v1)); st.AddVertex(a);
            st.SetUV(new Vector2(u1, v0)); st.AddVertex(c);
            st.SetUV(new Vector2(u0, v0)); st.AddVertex(d);
        }
        return st.Commit();
    }

    // ---- trees ----------------------------------------------------------------------------------------

    /// <summary>A tree: tapered trunk and three branches in bark, a canopy of leaf-cluster cards in one mesh.</summary>
    public static Node3D BuildTree(TrackProp prop, Random rng)
    {
        var node = new Node3D { Name = $"Tree-{prop.Label}" };
        var height = prop.Size.Y;
        var spread = prop.Size.X * 0.62f;
        var trunkHeight = height * 0.48f;
        var trunk = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.16f + height * 0.012f, BottomRadius = 0.28f + height * 0.02f, Height = trunkHeight, RadialSegments = 10, Material = Bark },
            Position = new Vector3(0f, trunkHeight * 0.5f, 0f),
        };
        node.AddChild(trunk);
        for (var b = 0; b < 3; b++)
        {
            var yaw = b * 2.1f + (float)rng.NextDouble();
            var branch = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.14f, Height = spread * 0.9f, RadialSegments = 6, Material = Bark },
                Position = new Vector3(Mathf.Cos(yaw) * spread * 0.3f, trunkHeight * 0.95f + 0.4f, Mathf.Sin(yaw) * spread * 0.3f),
                Rotation = new Vector3(Mathf.Sin(yaw) * 0.9f, 0f, -Mathf.Cos(yaw) * 0.9f),
            };
            node.AddChild(branch);
        }

        // Canopy: cards scattered through an ellipsoid above the trunk, each rotated at random and tinted a little.
        var st = new MeshBuilder();
        var centre = new Vector3(0f, trunkHeight + height * 0.28f, 0f);
        var cards = 18 + (int)(height * 1.6f);
        var ry = height * 0.36f;
        for (var i = 0; i < cards; i++)
        {
            var u = (float)rng.NextDouble() * 2f - 1f;
            var theta = (float)rng.NextDouble() * Mathf.Tau;
            var r = MathF.Sqrt(1f - u * u) * spread * (0.3f + 0.7f * (float)rng.NextDouble());
            var p = centre + new Vector3(Mathf.Cos(theta) * r, u * ry * (0.4f + 0.6f * (float)rng.NextDouble()), Mathf.Sin(theta) * r);
            var size = spread * (0.55f + 0.5f * (float)rng.NextDouble());
            var tone = 0.72f + 0.38f * (float)rng.NextDouble();
            var colour = new Color(tone * (0.85f + 0.2f * (float)rng.NextDouble()), tone, tone * 0.85f);
            var cell = rng.Next(4);
            var basis = new Basis(Quaternion.FromEuler(new Vector3((float)rng.NextDouble() * 1.2f - 0.6f, (float)rng.NextDouble() * Mathf.Tau, (float)rng.NextDouble() * 1.2f - 0.6f)));
            Card(st, p, basis, size, cell, colour);
        }
        var canopy = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Leaf };
        node.AddChild(canopy);
        return node;
    }

    /// <summary>A square card centred at <paramref name="centre"/>, mapped to one of the four leaf clusters (2 x 2 atlas).</summary>
    private static void Card(MeshBuilder st, Vector3 centre, Basis basis, float size, int cell, Color colour)
    {
        var u0 = (cell % 2) * 0.5f;
        var v0 = (cell / 2) * 0.5f;
        var right = basis.X * (size * 0.5f);
        var up = basis.Y * (size * 0.5f);
        var normal = basis.Z;
        st.SetColor(colour);
        st.SetNormal(normal);
        st.SetUV(new Vector2(u0, v0 + 0.5f)); st.AddVertex(centre - right - up);
        st.SetUV(new Vector2(u0 + 0.5f, v0 + 0.5f)); st.AddVertex(centre + right - up);
        st.SetUV(new Vector2(u0 + 0.5f, v0)); st.AddVertex(centre + right + up);
        st.SetUV(new Vector2(u0, v0 + 0.5f)); st.AddVertex(centre - right - up);
        st.SetUV(new Vector2(u0 + 0.5f, v0)); st.AddVertex(centre + right + up);
        st.SetUV(new Vector2(u0, v0)); st.AddVertex(centre - right + up);
    }

    // ---- fields ---------------------------------------------------------------------------------------

    /// <summary>A field patch: a grid that hugs the terrain, ploughed earth, crop rows or cut hay with bales.</summary>
    public static Node3D BuildField(TrackProp prop, TerrainField terrain)
    {
        var node = new Node3D { Name = $"Field-{prop.Label}" };
        var yaw = prop.YawDeg * MathUtil.Deg2Rad;
        var axisX = new SN.Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var axisZ = new SN.Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        const float cell = 5f;
        var nx = (int)MathF.Ceiling(prop.Size.X / cell);
        var nz = (int)MathF.Ceiling(prop.Size.Z / cell);
        var st = new MeshBuilder();
        var rows = prop.Label == "crop";
        for (var j = 0; j <= nz; j++)
        {
            for (var i = 0; i <= nx; i++)
            {
                var u = i / (float)nx - 0.5f;
                var v = j / (float)nz - 0.5f;
                var p = prop.Position + axisX * (u * prop.Size.X) + axisZ * (v * prop.Size.Z);
                var y = terrain.Height(p.X, p.Z) + 0.06f;
                var shade = rows ? (i % 2 == 0 ? 0.78f : 1f) : 0.92f + 0.08f * MathF.Sin(i * 1.7f + j * 2.3f);
                st.SetColor(new Color(shade, shade, shade));
                st.SetNormal(Convert.ToGodot(terrain.Normal(p.X, p.Z)));
                st.SetUV(new Vector2(u * prop.Size.X / 4f, v * prop.Size.Z / 4f));
                st.AddVertex(Convert.ToGodot(new SN.Vector3(p.X, y, p.Z)));
            }
        }
        for (var j = 0; j < nz; j++)
        {
            for (var i = 0; i < nx; i++)
            {
                var a = j * (nx + 1) + i;
                var b = a + 1;
                var c = a + nx + 1;
                var d = c + 1;
                st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                st.AddIndex(b); st.AddIndex(d); st.AddIndex(c);
            }
        }
        st.GenerateTangents();
        var material = prop.Label switch
        {
            "plough" => WorldBuilder.PropMaterial("dirt", new Color(0.62f, 0.52f, 0.40f), 1f, triplanar: false),
            "hay" => WorldBuilder.PropMaterial("dirt", new Color(0.95f, 0.85f, 0.50f), 1f, triplanar: false),
            _ => WorldBuilder.PropMaterial("grass", new Color(0.55f, 0.66f, 0.40f), 1f, triplanar: false),
        };
        material = (StandardMaterial3D)material.Duplicate();
        material.VertexColorUseAsAlbedo = true;
        node.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material });

        if (prop.Label == "hay")
        {
            var rng = new Random(prop.Label.GetHashCode() ^ (int)prop.Position.X);
            var bale = WorldBuilder.PropMaterial("dirt", new Color(0.92f, 0.78f, 0.42f), 2f);
            for (var k = 0; k < 9; k++)
            {
                var u = (float)rng.NextDouble() - 0.5f;
                var v = (float)rng.NextDouble() - 0.5f;
                var p = prop.Position + axisX * (u * prop.Size.X * 0.85f) + axisZ * (v * prop.Size.Z * 0.85f);
                var y = terrain.Height(p.X, p.Z);
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.8f, Height = 1.5f, RadialSegments = 14, Material = bale },
                    Position = Convert.ToGodot(new SN.Vector3(p.X, y + 0.8f, p.Z)),
                    Rotation = new Vector3(0f, (float)rng.NextDouble() * Mathf.Tau, Mathf.Pi / 2f),
                });
            }
        }
        return node;
    }
}
