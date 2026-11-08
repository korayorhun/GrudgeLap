using System.IO.Compression;
using Godot;
using GrudgeLap.Physics;

namespace GrudgeLap.Game;

/// <summary>
/// The real land round the plain (0.37): a square of Jackson Hole, Wyoming, 48 km across, with the Teton Range and
/// the Gros Ventre on either side of the valley, made by tools/make-terrain.py from the USGS elevation model and a
/// Sentinel-2 image into assets/terrain/jackson.bin (heights over the valley floor, how much of the game's sun and of
/// the sky each point sees) and jackson_albedo.jpg (the ground's colour).
/// </summary>
/// <remarks>
/// <para>
/// Until 0.37 the horizon was a ring of noise with vertex colours (WorldBuilder.BuildMountains, kept for the 1989
/// look), and under the haze it read as a bank of cloud. The land is a polar grid round the circuit's centre, since
/// it is only ever seen from within a kilometre or so of it: 1024 bearings and rings 0.6 % of their radius apart
/// (12 m at 2 km, 60 m at 10 km, 140 m at 24 km), cut into 32 wedges so the camera leaves out those it is not facing.
/// </para>
/// <para>
/// Inside the game's own terrain (WorldBuilder.TerrainBounds) the land lies flat just under it, at the far ground's
/// old level, and the triangles wholly inside it less <see cref="Overlap"/> are left out: the cuttings go below that
/// level and a sheet of land across them would hide their roads. From the terrain's edge the real heights come in over
/// <see cref="RampMetres"/>. The Earth's curvature, with the air's refraction, drops the land 7 m at 10 km and 39 m
/// at 24 km; past the data a skirt carries the rim out to 60 km, so the land does not end in the haze.
/// </para>
/// </remarks>
internal static class RealLand
{
    private const string DataPath = "res://assets/terrain/jackson.bin";
    public const string AlbedoPath = "res://assets/terrain/jackson_albedo.jpg";
    private const int Bearings = 1024;
    private const int Wedges = 32;
    private const float InnerRadius = 2000f;
    private const float RingGrowth = 0.006f;
    private const float RampMetres = 1200f;
    private const float RimBlendMetres = 900f;
    private const float Overlap = 60f;
    private const float SkirtRadius = 60000f;
    // The Earth's radius stretched by the air's refraction (a coefficient of 0.13, the surveyors' usual figure).
    private const float EarthRadius = 6371000f / (1f - 0.13f);
    private const float PlainLevel = -0.06f;

    /// <summary>The land's grid: heights in metres over the valley floor at the circuit, and the light it sees.</summary>
    private sealed class Grid
    {
        public int N;
        public float Step;
        public float Half;
        public float CentreX;
        public float CentreZ;
        public float[] Height = [];
        public byte[] Sun = [];
        public byte[] Sky = [];
        /// <summary>The sunrise's sun's visibility (version 2, 0.55); the day's in a version 1 file.</summary>
        public byte[] SunriseSun = [];
        /// <summary>The dusk's sun's visibility (version 3, 0.60); the day's in an older file.</summary>
        public byte[] DuskSun = [];

        /// <summary>Bilinear sample at a point relative to the circuit's centre; outside the square, its nearest edge.</summary>
        public float Sample(float[] values, float rx, float rz)
        {
            var u = Math.Clamp((rx + Half) / Step, 0f, N - 1.001f);
            var v = Math.Clamp((rz + Half) / Step, 0f, N - 1.001f);
            var i = (int)u;
            var j = (int)v;
            var fu = u - i;
            var fv = v - j;
            var a = j * N + i;
            var top = values[a] + (values[a + 1] - values[a]) * fu;
            var bottom = values[a + N] + (values[a + N + 1] - values[a + N]) * fu;
            return top + (bottom - top) * fv;
        }

        public float SampleByte(byte[] values, float rx, float rz)
        {
            var u = Math.Clamp((rx + Half) / Step, 0f, N - 1.001f);
            var v = Math.Clamp((rz + Half) / Step, 0f, N - 1.001f);
            var i = (int)u;
            var j = (int)v;
            var fu = u - i;
            var fv = v - j;
            var a = j * N + i;
            var top = values[a] + (values[a + 1] - values[a]) * fu;
            var bottom = values[a + N] + (values[a + N + 1] - values[a + N]) * fu;
            return (top + (bottom - top) * fv) / 255f;
        }
    }

    /// <summary>The land as a node of wedges; null when the data is not there (the old far ground and mountains then stay).</summary>
    /// <param name="hole">The game's terrain in Godot x and z (min x, max x, min z, max z).</param>
    /// <param name="stage">Called after each step with its name, for the start-up timings (--perf-log).</param>
    public static Node3D? Build((float X0, float X1, float Z0, float Z1) hole, Material material, Action<string>? stage = null)
    {
        if (Load(stage) is not { } grid) return null;
        stage?.Invoke("  land: the grid read");
        var rings = new List<float> { InnerRadius };
        var dataRadius = grid.Half - 2f * grid.Step;
        while (rings[^1] < dataRadius)
        {
            rings.Add(MathF.Min(rings[^1] + MathF.Max(12f, rings[^1] * RingGrowth), dataRadius));
        }
        var lastData = rings.Count - 1;
        rings.Add(SkirtRadius);
        var cosines = new float[Bearings + 1];
        var sines = new float[Bearings + 1];
        for (var b = 0; b <= Bearings; b++)
        {
            var angle = MathF.Tau * b / Bearings;
            cosines[b] = MathF.Cos(angle);
            sines[b] = MathF.Sin(angle);
        }
        // The terrain shrunk by the overlap: a triangle wholly inside it is under the game's own ground.
        var (ix0, ix1, iz0, iz1) = (hole.X0 + Overlap, hole.X1 - Overlap, hole.Z0 + Overlap, hole.Z1 - Overlap);
        bool Hidden(float x, float z) => x > ix0 && x < ix1 && z > iz0 && z < iz1;

        float Outside(float x, float z)
        {
            var dx = MathF.Max(MathF.Max(hole.X0 - x, x - hole.X1), 0f);
            var dz = MathF.Max(MathF.Max(hole.Z0 - z, z - hole.Z1), 0f);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        float Height(float x, float z)
        {
            var rx = x - grid.CentreX;
            var rz = z - grid.CentreZ;
            var w = MathUtil.SmoothStep(Outside(x, z) / RampMetres);
            if (w <= 0f) return PlainLevel;
            var real = grid.Sample(grid.Height, rx, rz) - (rx * rx + rz * rz) / (2f * EarthRadius);
            return PlainLevel + (real - PlainLevel) * w;
        }

        var root = new Node3D { Name = "Land" };
        var perWedge = Bearings / Wedges;
        var side = 2f * grid.Half;
        // The wedges' vertices are worked out side by side (the grid and the functions above only read); the meshes
        // go to the engine one after another on this thread. In series the land took a second of the start-up.
        var builders = new MeshBuilder[Wedges];
        Parallel.For(0, Wedges, wedge =>
        {
            var st = new MeshBuilder();
            var columns = perWedge + 1;
            for (var k = 0; k < rings.Count; k++)
            {
                // The skirt takes the data's rim: its height, its light and its colour, carried out flat.
                var skirt = k > lastData;
                var r = rings[k];
                for (var c = 0; c < columns; c++)
                {
                    var b = wedge * perWedge + c;
                    var rimX = grid.CentreX + rings[lastData] * cosines[b];
                    var rimZ = grid.CentreZ + rings[lastData] * sines[b];
                    var x = grid.CentreX + r * cosines[b];
                    var z = grid.CentreZ + r * sines[b];
                    var (sx, sz) = skirt ? (rimX, rimZ) : (x, z);
                    var y = Height(sx, sz);
                    if (skirt) y -= (r * r - rings[lastData] * rings[lastData]) / (2f * EarthRadius);
                    // The normal from the heights a few metres either side, as the light will see the slope.
                    var e = MathF.Max(6f, r * 0.002f);
                    var normal = skirt ? Vector3.Up : new Vector3(Height(x - e, z) - Height(x + e, z), 2f * e, Height(x, z - e) - Height(x, z + e)).Normalized();
                    var rx = sx - grid.CentreX;
                    var rz = sz - grid.CentreZ;
                    var rim = MathUtil.SmoothStep(Outside(sx, sz) / RimBlendMetres);
                    // r the day's sun, g the sky, b the image's weight, a the sunrise's sun (land.gdshader picks the hour's).
                    st.SetColor(new Color(grid.SampleByte(grid.Sun, rx, rz), grid.SampleByte(grid.Sky, rx, rz), rim, grid.SampleByte(grid.SunriseSun, rx, rz)));
                    // The dusk's sun in the second UV's x (0.60).
                    st.SetUV2(new Vector2(grid.SampleByte(grid.DuskSun, rx, rz), 0f));
                    st.SetNormal(normal);
                    st.SetUV(new Vector2((rx + grid.Half) / side, (rz + grid.Half) / side));
                    st.AddVertex(new Vector3(x, y, z));
                }
            }
            for (var k = 0; k < rings.Count - 1; k++)
            {
                for (var c = 0; c < perWedge; c++)
                {
                    var a = k * columns + c;
                    var bIndex = a + 1;
                    var cIndex = a + columns;
                    var d = cIndex + 1;
                    var b0 = wedge * perWedge + c;
                    if (Hidden(grid.CentreX + rings[k] * cosines[b0], grid.CentreZ + rings[k] * sines[b0])
                        && Hidden(grid.CentreX + rings[k] * cosines[b0 + 1], grid.CentreZ + rings[k] * sines[b0 + 1])
                        && Hidden(grid.CentreX + rings[k + 1] * cosines[b0], grid.CentreZ + rings[k + 1] * sines[b0])
                        && Hidden(grid.CentreX + rings[k + 1] * cosines[b0 + 1], grid.CentreZ + rings[k + 1] * sines[b0 + 1]))
                    {
                        continue;
                    }
                    // Seen from above, bearings turn clockwise on Godot's x-z (z runs down the page): this order is
                    // clockwise, Godot's front face, facing up.
                    st.AddIndex(a); st.AddIndex(cIndex); st.AddIndex(bIndex);
                    st.AddIndex(bIndex); st.AddIndex(cIndex); st.AddIndex(d);
                }
            }
            builders[wedge] = st;
        });
        stage?.Invoke("  land: the wedges worked out");
        for (var wedge = 0; wedge < Wedges; wedge++)
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = builders[wedge].Commit(),
                MaterialOverride = material,
                Name = $"Wedge{wedge:00}",
                // Too far for the shadow maps (they end at 350 m); the mountains' own shadows are in the data.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        stage?.Invoke("  land: the meshes committed");
        return root;
    }

    /// <summary>Reads assets/terrain/jackson.bin, or null when it is missing or not what this code reads.</summary>
    private static Grid? Load(Action<string>? stage)
    {
        if (!Godot.FileAccess.FileExists(DataPath)) return null;
        var packed = Godot.FileAccess.GetFileAsBytes(DataPath);
        stage?.Invoke("  land: the file read");
        using var input = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        input.CopyTo(raw);
        var bytes = raw.GetBuffer();
        stage?.Invoke("  land: the file inflated");
        var version = raw.Length < 36 ? 0u : BitConverter.ToUInt32(bytes, 4);
        if (raw.Length < 36 || bytes[0] != 'G' || bytes[1] != 'L' || bytes[2] != 'P' || bytes[3] != 'T' || version is not (1 or 2 or 3))
        {
            GD.PushWarning("assets/terrain/jackson.bin is not a version 1, 2 or 3 land grid; the old horizon stays");
            return null;
        }
        var grid = new Grid
        {
            N = (int)BitConverter.ToUInt32(bytes, 8),
            Step = BitConverter.ToSingle(bytes, 12),
            Half = BitConverter.ToSingle(bytes, 16),
            CentreX = BitConverter.ToSingle(bytes, 24),
            CentreZ = BitConverter.ToSingle(bytes, 28),
        };
        var count = grid.N * grid.N;
        if (raw.Length < 36 + 4L * count) return null;
        grid.Height = new float[count];
        for (var i = 0; i < count; i++) grid.Height[i] = BitConverter.ToInt16(bytes, 36 + 2 * i) * 0.1f;
        grid.Sun = new byte[count];
        Buffer.BlockCopy(bytes, 36 + 2 * count, grid.Sun, 0, count);
        grid.Sky = new byte[count];
        Buffer.BlockCopy(bytes, 36 + 3 * count, grid.Sky, 0, count);
        // Version 2 (0.55) adds the sunrise's shadows; a version 1 grid lights the sunrise with the day's.
        if (version >= 2 && raw.Length >= 36 + 5L * count)
        {
            grid.SunriseSun = new byte[count];
            Buffer.BlockCopy(bytes, 36 + 4 * count, grid.SunriseSun, 0, count);
        }
        else
        {
            grid.SunriseSun = grid.Sun;
        }
        // Version 3 (0.60) adds the dusk's.
        if (version >= 3 && raw.Length >= 36 + 6L * count)
        {
            grid.DuskSun = new byte[count];
            Buffer.BlockCopy(bytes, 36 + 5 * count, grid.DuskSun, 0, count);
        }
        else
        {
            grid.DuskSun = grid.Sun;
        }
        return grid;
    }
}
