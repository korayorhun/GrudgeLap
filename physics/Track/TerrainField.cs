using System.Numerics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// Ground height everywhere off the road. The undisturbed ground rolls gently (value noise); near every
/// road built on earth the ground is pulled to the road's edge height, so hills get embankments, dips get
/// cuttings and the car rolls over the same surface the view draws. Bridges, ramps and the loop are
/// structures and leave the ground alone. The same field drives the physics probe, the prop placement
/// and the terrain mesh, which is why it lives in the simulation library.
/// </summary>
public sealed class TerrainField
{
    private const float CellSize = 32f;
    /// <summary>Metres beyond the shoulder over which a road at ground level stops shaping the terrain.</summary>
    private const float MinFalloff = 9f;
    /// <summary>Metres of falloff per metre of height difference (an embankment slope of about 1 in 2.5).</summary>
    private const float FalloffPerMetre = 2.5f;
    private const float Shoulder = 1.3f;

    private readonly Dictionary<(int, int), List<int>> _cells = new();
    private readonly List<RoadFrame> _frames = new();
    private readonly List<int> _pathOfFrame = new();
    private readonly List<SkidPad> _pads;
    private readonly int _seed;
    private readonly Vector3 _centre;
    private float _minX = float.MaxValue, _maxX = float.MinValue, _minZ = float.MaxValue, _maxZ = float.MinValue;

    public TerrainField(IReadOnlyList<TrackPath> paths, IReadOnlyList<SkidPad> pads, int seed = 1989)
    {
        _pads = [.. pads];
        _seed = seed;
        var sum = Vector3.Zero;
        var count = 0;
        for (var p = 0; p < paths.Count; p++)
        {
            foreach (var f in paths[p].Frames)
            {
                sum += f.Origin;
                count++;
                _minX = MathF.Min(_minX, f.Origin.X); _maxX = MathF.Max(_maxX, f.Origin.X);
                _minZ = MathF.Min(_minZ, f.Origin.Z); _maxZ = MathF.Max(_maxZ, f.Origin.Z);
                if (!IsEarthwork(f)) continue;
                var index = _frames.Count;
                _frames.Add(f);
                _pathOfFrame.Add(p);
                var key = CellOf(f.Origin.X, f.Origin.Z);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
                list.Add(index);
            }
        }
        _centre = count > 0 ? sum / count : Vector3.Zero;
        PathCount = paths.Count;
    }

    public int PathCount { get; }
    public IReadOnlyList<SkidPad> Pads => _pads;
    /// <summary>Amplitude of the rolling ground, metres. Zero makes the world flat (test tracks).</summary>
    public float RollingAmplitude { get; init; } = 2.2f;
    /// <summary>Height of the hills that ring the circuit beyond the outermost road, metres.</summary>
    public float HillAmplitude { get; init; } = 40f;

    /// <summary>Frames of roads built on earth; the ground is pulled to their edges.</summary>
    public static bool IsEarthwork(in RoadFrame f) =>
        f.Kind is SegmentKind.Straight or SegmentKind.Curve or SegmentKind.Hill or SegmentKind.Dip or SegmentKind.BankedCurve or SegmentKind.Spline
        && f.Surface == SurfaceType.Road;

    /// <summary>Ground height at a point.</summary>
    public float Height(float x, float z) => Height(x, z, out _);

    /// <summary>Ground height at a point; <paramref name="roadWeight"/> is 1 at a road's edge and fades to 0 away from it.</summary>
    public float Height(float x, float z, out float roadWeight)
    {
        roadWeight = 0f;
        var baseHeight = Base(x, z);

        // Nearest earthwork frame of every path within reach; each pulls the ground toward its edge height.
        Span<float> bestDistance = stackalloc float[PathCount];
        Span<int> bestFrame = stackalloc int[PathCount];
        for (var p = 0; p < PathCount; p++)
        {
            bestDistance[p] = float.MaxValue;
            bestFrame[p] = -1;
        }
        var (cx, cz) = CellOf(x, z);
        for (var dz = -2; dz <= 2; dz++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                if (!_cells.TryGetValue((cx + dx, cz + dz), out var list)) continue;
                foreach (var index in list)
                {
                    var o = _frames[index].Origin;
                    var ddx = o.X - x;
                    var ddz = o.Z - z;
                    var d2 = ddx * ddx + ddz * ddz;
                    var p = _pathOfFrame[index];
                    if (d2 < bestDistance[p])
                    {
                        bestDistance[p] = d2;
                        bestFrame[p] = index;
                    }
                }
            }
        }

        var weightSum = 0f;
        var weightMax = 0f;
        var offsetSum = 0f;
        for (var p = 0; p < PathCount; p++)
        {
            if (bestFrame[p] < 0) continue;
            var f = _frames[bestFrame[p]];
            var delta = new Vector3(x - f.Origin.X, 0f, z - f.Origin.Z);
            var flatLateral = MathUtil.SafeNormalize(new Vector3(f.Lateral.X, 0f, f.Lateral.Z), Vector3.UnitX);
            var lateral = Vector3.Dot(delta, flatLateral);
            var clamped = MathUtil.Clamp(lateral, -f.HalfWidth, f.HalfWidth);
            var edgeHeight = f.Origin.Y + clamped * f.Lateral.Y;
            var beyond = MathF.Max(MathF.Abs(lateral) - f.HalfWidth - Shoulder, 0f);
            var falloff = MathF.Max(MinFalloff, FalloffPerMetre * MathF.Abs(edgeHeight - baseHeight));
            var w = 1f - MathUtil.SmoothStep(beyond / falloff);
            if (w <= 0f) continue;
            weightSum += w;
            weightMax = MathF.Max(weightMax, w);
            offsetSum += w * (edgeHeight - baseHeight);
        }

        // Skid pads are flat discs that fade into the ground over a few metres.
        foreach (var pad in _pads)
        {
            var dx = x - pad.Centre.X;
            var dz = z - pad.Centre.Z;
            var beyond = MathF.Max(MathF.Sqrt(dx * dx + dz * dz) - pad.Radius - 2f, 0f);
            var w = 1f - MathUtil.SmoothStep(beyond / 14f);
            if (w <= 0f) continue;
            weightSum += w;
            weightMax = MathF.Max(weightMax, w);
            offsetSum += w * (pad.Centre.Y - baseHeight);
        }

        roadWeight = weightMax;
        if (weightSum <= 0f) return baseHeight;
        return baseHeight + weightMax * (offsetSum / weightSum);
    }

    /// <summary>Unit normal of the ground at a point, from central differences.</summary>
    public Vector3 Normal(float x, float z)
    {
        const float h = 0.5f;
        var dx = Height(x + h, z) - Height(x - h, z);
        var dz = Height(x, z + h) - Height(x, z - h);
        return Vector3.Normalize(new Vector3(-dx / (2f * h), 1f, -dz / (2f * h)));
    }

    /// <summary>
    /// The ground with no roads: three octaves of rolling value noise around the circuit, and beyond the
    /// outermost road a ring of hills that rises over half a kilometre and fades again before the far plane.
    /// </summary>
    public float Base(float x, float z)
    {
        if (RollingAmplitude <= 0f) return 0f;
        var dx = x - _centre.X;
        var dz = z - _centre.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        var envelope = 1f - MathUtil.SmoothStep((distance - 900f) / 600f);
        var rolling = 0f;
        if (envelope > 0f)
        {
            var n = ValueNoise(x / 230f, z / 230f, _seed) * 0.70f
                  + ValueNoise(x / 95f, z / 95f, _seed + 7) * 0.22f
                  + ValueNoise(x / 38f, z / 38f, _seed + 13) * 0.08f;
            rolling = n * RollingAmplitude * envelope;
        }
        var outside = MathF.Max(MathF.Max(_minX - 140f - x, x - _maxX - 140f), MathF.Max(_minZ - 140f - z, z - _maxZ - 140f));
        if (outside <= 0f || HillAmplitude <= 0f) return rolling;
        var ramp = MathUtil.SmoothStep(outside / 550f);
        var fade = 1f - MathUtil.SmoothStep((outside - 1500f) / 700f);
        var h = ValueNoise(x / 520f, z / 520f, _seed + 31) * 0.6f + ValueNoise(x / 180f, z / 180f, _seed + 37) * 0.25f + 0.55f;
        return rolling + MathF.Max(h, 0f) * HillAmplitude * ramp * fade;
    }

    private static (int, int) CellOf(float x, float z) => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize));

    /// <summary>Smooth value noise in [-1, 1] on an integer lattice, deterministic for a seed.</summary>
    private static float ValueNoise(float x, float z, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var tx = x - x0;
        var tz = z - z0;
        // Quintic fade keeps the second derivative continuous, so the normals stay smooth.
        tx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);
        tz = tz * tz * tz * (tz * (tz * 6f - 15f) + 10f);
        var a = Hash(x0, z0, seed);
        var b = Hash(x0 + 1, z0, seed);
        var c = Hash(x0, z0 + 1, seed);
        var d = Hash(x0 + 1, z0 + 1, seed);
        return MathUtil.Lerp(MathUtil.Lerp(a, b, tx), MathUtil.Lerp(c, d, tx), tz);
    }

    private static float Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + z * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x7FFFFF - 1f;
        }
    }
}

/// <summary>A flat asphalt disc off the road: the hidden practice area of the original, for donuts.</summary>
public readonly record struct SkidPad(Vector3 Centre, float Radius);
