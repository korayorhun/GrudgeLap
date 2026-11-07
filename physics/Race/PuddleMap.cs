namespace GrudgeLap.Physics;

/// <summary>
/// Where water stands on the road (0.56): a seamless tile of puddle depth, 0 where the road drains to 1 at a puddle's
/// deepest, laid over the asphalt the way the view lays its puddles (the asphalt's roughness map, WorldBuilder.Puddles):
/// two tiles across the road's full width and one every 5.56 m along it, so the water under the tyres is the water the
/// driver sees. The view makes the tile and hands it to the car (<see cref="Vehicle.VehicleSim.Puddles"/>); without one
/// a wet road has no standing water, and so no aquaplaning and no water to push aside.
/// </summary>
public sealed class PuddleMap
{
    /// <summary>The tile's repeats across the road's full width (the asphalt's UV scale across).</summary>
    public const float TilesAcross = 2f;

    /// <summary>The tile's repeats a metre along the road (the asphalt's UV scale along).</summary>
    public const float TilesPerMetre = 0.18f;

    private readonly float[] _depth;
    private readonly int _size;

    /// <param name="depth">The tile, row after row (rows along the road), each sample 0 to 1.</param>
    /// <param name="size">Its side in samples.</param>
    public PuddleMap(float[] depth, int size)
    {
        if (size < 1 || depth.Length != size * size) throw new ArgumentException("the tile is size x size samples", nameof(depth));
        _depth = depth;
        _size = size;
    }

    /// <summary>Water standing all over the road, as deep as a puddle: for the tests.</summary>
    public static PuddleMap Flooded => new([1f], 1);

    /// <summary>
    /// The puddle at a point of the road: <paramref name="lateral"/> metres from its centreline, where the road is
    /// <paramref name="halfWidth"/> wide each side, <paramref name="distance"/> metres along its path.
    /// </summary>
    public float At(float lateral, float halfWidth, float distance)
    {
        var across = (Math.Clamp(lateral / MathF.Max(halfWidth, 0.1f), -1f, 1f) + 1f) * 0.5f * TilesAcross;
        return Sample(across, distance * TilesPerMetre);
    }

    /// <summary>Bilinear between the samples, wrapping round the tile's edges: the tile is seamless.</summary>
    public float Sample(float u, float v)
    {
        var x = (u - MathF.Floor(u)) * _size - 0.5f;
        var y = (v - MathF.Floor(v)) * _size - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var top = Tap(x0, y0) + (Tap(x0 + 1, y0) - Tap(x0, y0)) * fx;
        var bottom = Tap(x0, y0 + 1) + (Tap(x0 + 1, y0 + 1) - Tap(x0, y0 + 1)) * fx;
        return top + (bottom - top) * fy;
    }

    private float Tap(int i, int j) => _depth[Wrap(j) * _size + Wrap(i)];

    private int Wrap(int i) => ((i % _size) + _size) % _size;
}
