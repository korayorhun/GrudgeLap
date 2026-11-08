using Godot;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The meadow by the roads (0.48; first tried in 0.41): the scanned grass clumps of the tufts' atlas on crossed cards
/// from the verge's fray out to 24 m on either side of every road, densest by the verge, about 28 cards a metre of road
/// (assets/shaders/meadow.gdshader). Cut into chunks of 60 m of road, each a multimesh drawn only within 70 m of the
/// camera (and dithered out from 45 m by its shader), so of the 200 000 cards a few thousand are drawn. Until 0.48 the
/// ground by the road was the lawn's photo with a scatter of tufts: from the seat it read as a mown lawn.
/// </summary>
/// <remarks>
/// 0.41 took it out again for the dark specks it scattered from the seat; they were the cards drawn unlit from behind
/// (Godot turns a double-sided card's up normal down on its back), which the shader now lights as the ground. Drawn at
/// medium and high quality, not in the 1989 look nor the export; --meadow=0 leaves it out.
/// </remarks>
internal static class MeadowBuilder
{
    /// <summary>The cells of the tufts' atlas (4 x 2, tools/bake-grass.py).</summary>
    private const int Cells = 8;

    private static ShaderMaterial? _material;

    private static ShaderMaterial Material => _material ??= MakeMaterial();

    private static ShaderMaterial MakeMaterial()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/meadow.gdshader") };
        material.SetShaderParameter("blades", GD.Load<Texture2D>("res://assets/textures/grass_blades.png"));
        material.SetShaderParameter("stand", _stand);
        return material;
    }

    private static float _stand;

    /// <summary>How far the cards' normal leans towards the eye (FoliageBuilder.SetStand).</summary>
    public static void SetStand(float stand)
    {
        _stand = stand;
        _material?.SetShaderParameter("stand", stand);
    }

    /// <summary>The weather's grass tint and wetness (FoliageBuilder.SetWeather).</summary>
    public static void SetWeather(Color tint, float wetness)
    {
        _material?.SetShaderParameter("tint", tint);
        _material?.SetShaderParameter("wetness", wetness);
    }

    /// <summary>Drops the shared material (the game quits).</summary>
    public static void ReleaseShared() => _material = null;

    /// <summary>A card of the meadow: two crossed quads 0.9 m wide and 0.4 m tall, UV over the whole of one atlas cell.</summary>
    private static ArrayMesh Card()
    {
        var st = new MeshBuilder();
        const float w = 0.45f, h = 0.4f;
        foreach (var yaw in new[] { 0f, Mathf.Pi / 2f })
        {
            var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var a = -right * w;
            var b = right * w;
            var c = right * w + Vector3.Up * h;
            var d = -right * w + Vector3.Up * h;
            st.SetNormal(Vector3.Up);
            st.SetUV(new Vector2(0f, 1f)); st.AddVertex(a);
            st.SetUV(new Vector2(1f, 1f)); st.AddVertex(b);
            st.SetUV(new Vector2(1f, 0f)); st.AddVertex(c);
            st.SetUV(new Vector2(0f, 1f)); st.AddVertex(a);
            st.SetUV(new Vector2(1f, 0f)); st.AddVertex(c);
            st.SetUV(new Vector2(0f, 0f)); st.AddVertex(d);
        }
        return st.Commit();
    }

    /// <summary>The meadow along every road of <paramref name="track"/>; an empty node for the export.</summary>
    public static Node3D Build(TrackGeometry track)
    {
        var root = new Node3D { Name = "Meadow" };
        if (WorldBuilder.ExportMode) return root;
        var terrain = track.Terrain;
        var card = Card();
        var material = Material;
        var fields = track.Props.Where(p => p.Kind == PropKind.Field).ToArray();
        bool Blocked(float x, float z)
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
            foreach (var pad in track.Pads)
            {
                var dx = x - pad.Centre.X;
                var dz = z - pad.Centre.Z;
                if (dx * dx + dz * dz < (pad.Radius + 2f) * (pad.Radius + 2f)) return true;
            }
            return false;
        }
        // Worked out path by path side by side (the terrain only reads); the multimeshes made here afterwards.
        var chunks = new List<(List<Transform3D> Transforms, List<Color> Custom)>[track.Paths.Count];
        Parallel.For(0, track.Paths.Count, pathIndex =>
        {
            var path = track.Paths[pathIndex];
            var rng = new Random(4100 + pathIndex);
            var list = new List<(List<Transform3D>, List<Color>)>();
            var transforms = new List<Transform3D>();
            var custom = new List<Color>();
            var chunkStart = 0f;
            var frames = path.Frames;
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                var road = f.Surface == SurfaceType.Road && f.Kind is not (SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.Gap or SegmentKind.RampDown or SegmentKind.Overpass);
                if (road)
                {
                    foreach (var side in new[] { -1f, 1f })
                    {
                        for (var k = 0; k < 14; k++)
                        {
                            // Densest by the verge and thinning out over 24 m (the lateral distance drawn towards the verge): at
                            // 14 m with a gentler draw the meadow ended in a visible edge.
                            var lateral = f.HalfWidth + 2.2f + 22f * MathF.Pow((float)rng.NextDouble(), 2.2f);
                            var along = (float)rng.NextDouble() - 0.5f;
                            var p = f.Origin + f.Lateral * (side * lateral) + f.Tangent * along;
                            var y = terrain.Height(p.X, p.Z, out var roadWeight);
                            // Not on a road (this one's or another's) nor its stones, nor in a field or on a pad.
                            if (roadWeight > 0.9f || Blocked(p.X, p.Z)) continue;
                            // Nor on a deck less than 2.5 m over the ground, nor on the railway's ballast (0.73: the stunt
                            // route's low return-leg bridge carried meadow cards on its asphalt). NaN, no deck, compares false.
                            if (terrain.DeckHeight(p.X, p.Z, 1f) - y < 2.5f || track.Train?.OffsetFromLine(p.X, p.Z) < 2.6f) continue;
                            var scale = 0.55f + 0.6f * (float)rng.NextDouble();
                            var basis = new Basis(Vector3.Up, (float)rng.NextDouble() * MathF.Tau).Scaled(new Vector3(scale, scale * (0.75f + 0.5f * (float)rng.NextDouble()), scale));
                            transforms.Add(new Transform3D(basis, Convert.ToGodot(new SN.Vector3(p.X, y - 0.02f, p.Z))));
                            custom.Add(new Color(rng.Next(Cells), (float)rng.NextDouble(), 0f, 0f));
                        }
                    }
                }
                if (transforms.Count > 0 && (f.Distance - chunkStart > 60f || i == frames.Count - 1))
                {
                    list.Add((transforms, custom));
                    transforms = new List<Transform3D>();
                    custom = new List<Color>();
                    chunkStart = f.Distance;
                }
            }
            chunks[pathIndex] = list;
        });
        var total = 0;
        foreach (var list in chunks)
        {
            foreach (var (transforms, custom) in list)
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = card, InstanceCount = transforms.Count };
                for (var i = 0; i < transforms.Count; i++)
                {
                    multi.SetInstanceTransform(i, transforms[i]);
                    multi.SetInstanceCustomData(i, custom[i]);
                }
                root.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = multi,
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = 70f,
                    VisibilityRangeEndMargin = 5f,
                });
                total += transforms.Count;
            }
        }
        if (WorldBuilder.PerfLog) GD.Print($"meadow: {total} cards in {root.GetChildCount()} chunks");
        return root;
    }
}
