namespace GrudgeLap.Physics.Track;

/// <summary>
/// The wear of a real road laid over the ideal ribbon of the track: a gentle undulation of the surface, a fine
/// roughness, potholes scattered in the wheel tracks and patches of other asphalt. The same function shapes the
/// surface the wheels run on (<see cref="TrackGeometry.Probe"/>) and the mesh the view draws, so a dip that is
/// felt is also seen. Everything is a deterministic function of the place on the road: the same circuit wears
/// the same way every time, on both engines and in the tests.
/// </summary>
/// <remarks>
/// Depths are small on purpose: a pothole is at most five centimetres deep and under a metre across, which a
/// wheel of a third of a metre radius thumps over without upsetting the car. Structures (the loop, the ramps,
/// the bridge decks) carry only the slight unevenness of their joints.
/// </remarks>
public static class RoadWear
{
    /// <summary>The deepest pothole, metres.</summary>
    public const float MaxDepth = 0.05f;
    private const float PotholeCell = 7f;
    private const float PatchCell = 9f;

    /// <summary>
    /// How far the worn surface lies below the ideal one (negative where it bulges), metres, at a distance along
    /// a path and a lateral offset (to the right positive) across it.
    /// </summary>
    public static float Depth(PathKind path, SegmentKind kind, float distance, float lateral, float halfWidth)
    {
        var seed = Seed(path);
        if (IsStructure(kind))
        {
            // Steel and concrete: only the joints and the plates, a couple of millimetres.
            return 0.002f * Value1(distance * 0.9f, seed + 5);
        }
        // Settled earthworks undulate over a dozen metres. No finer roughness: a tyre's contact patch irons that
        // out on a real road, while the rigid probe wheel here would hammer the suspension with it at speed and
        // bleed twenty miles an hour off the top speed (measured on the drag strip with a 1.1 m ripple of 4 mm).
        var depth = 0.010f * Value2(distance / 12f, lateral / 4f, seed);
        return depth + Pothole(distance, lateral, halfWidth, seed);
    }

    /// <summary>
    /// The shade of a patch of other asphalt at a point, 1 where the road is its own: resurfaced squares are
    /// darker, old patches paler. Only the view uses it.
    /// </summary>
    public static float PatchTone(PathKind path, SegmentKind kind, float distance, float lateral, float halfWidth)
    {
        if (IsStructure(kind)) return 1f;
        var seed = Seed(path);
        var cell = (int)MathF.Floor(distance / PatchCell);
        foreach (var lane in new[] { -1, 1 })
        {
            if (Hash(cell, lane, seed + 11) > 0.14f) continue;
            var start = cell * PatchCell + 0.5f + 4f * Hash(cell, lane, seed + 12);
            var length = 1.6f + 3.2f * Hash(cell, lane, seed + 13);
            var centre = lane * (0.2f + 0.5f * Hash(cell, lane, seed + 14)) * halfWidth;
            var width = 0.9f + 1.5f * Hash(cell, lane, seed + 15);
            // A ragged edge: the rectangle breathes by a few centimetres with a fine noise.
            var ragged = 0.08f * Value2(distance * 2.3f, lateral * 2.3f, seed + 16);
            if (distance < start - ragged || distance > start + length + ragged) continue;
            if (MathF.Abs(lateral - centre) > width * 0.5f + ragged) continue;
            return Hash(cell, lane, seed + 17) < 0.6f ? 0.82f : 1.1f;
        }
        return 1f;
    }

    /// <summary>Potholes: at most one per seven-metre cell and lane, bowl-shaped, in the band the wheels run in.</summary>
    private static float Pothole(float distance, float lateral, float halfWidth, int seed)
    {
        var cell = (int)MathF.Floor(distance / PotholeCell);
        var depth = 0f;
        foreach (var lane in new[] { -1, 1 })
        {
            if (Hash(cell, lane, seed + 2) > 0.16f) continue;
            var centre = cell * PotholeCell + 1f + 5f * Hash(cell, lane, seed + 3);
            var side = lane * (0.25f + 0.45f * Hash(cell, lane, seed + 4)) * halfWidth;
            var along = 0.3f + 0.45f * Hash(cell, lane, seed + 6);
            var across = 0.22f + 0.3f * Hash(cell, lane, seed + 7);
            var ds = (distance - centre) / along;
            var dl = (lateral - side) / across;
            var q = ds * ds + dl * dl;
            if (q >= 1f) continue;
            var bowl = (1f - q) * (1f - q);
            depth += (0.018f + (MaxDepth - 0.018f) * Hash(cell, lane, seed + 8)) * bowl;
        }
        return depth;
    }

    private static bool IsStructure(SegmentKind kind) => kind is SegmentKind.Loop or SegmentKind.RampUp or SegmentKind.Gap or SegmentKind.RampDown or SegmentKind.Overpass;

    private static int Seed(PathKind path) => 1989 + 7919 * (int)path;

    /// <summary>Smooth value noise in [-1, 1] along one axis.</summary>
    private static float Value1(float x, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var t = Fade(x - x0);
        return MathUtil.Lerp(Signed(x0, 0, seed), Signed(x0 + 1, 0, seed), t);
    }

    /// <summary>Smooth value noise in [-1, 1] on a lattice.</summary>
    private static float Value2(float x, float z, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var tx = Fade(x - x0);
        var tz = Fade(z - z0);
        var a = Signed(x0, z0, seed);
        var b = Signed(x0 + 1, z0, seed);
        var c = Signed(x0, z0 + 1, seed);
        var d = Signed(x0 + 1, z0 + 1, seed);
        return MathUtil.Lerp(MathUtil.Lerp(a, b, tx), MathUtil.Lerp(c, d, tx), tz);
    }

    /// <summary>Quintic fade: continuous second derivative, so the surface never kinks.</summary>
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Signed(int x, int z, int seed) => Hash(x, z, seed) * 2f - 1f;

    /// <summary>A hash of a lattice point in [0, 1), the same on every machine; the view uses it to vary what it paints.</summary>
    public static float Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + z * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
