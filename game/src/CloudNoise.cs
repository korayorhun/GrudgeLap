using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// A tiling 3D noise texture for the clouds with its whole mip chain (0.71), generated on a worker thread.
/// </summary>
/// <remarks>
/// NoiseTexture3D, which the sky used until 0.71, makes only the full-size level, so the cloud march read the finest
/// noise at every distance: towards the horizon a quarter-resolution pixel spans some 150 m and a step of the march up
/// to 230 m, against the noises' 54 m and 23 m texels, and as the clouds drifted their billows slipped in and out of the
/// samples, so the far clouds flickered (the owner, 8 November 2026). With the chain the march reads each noise at the
/// level its footprint calls for (volumetric_sky.gdshader, textureLod). Each level is the box average of the one above,
/// down to a single texel, in the order ImageTexture3D.Create takes them: every slice of a level, then the next level's.
/// The texture is handed out at once and filled when the worker is done (Create replaces its placeholder), as
/// NoiseTexture3D's own thread did; <see cref="Voxels"/> keeps the full-size level for the cloud shade's lookups on the
/// CPU (CloudShadows), which read it back from the texture before.
/// </remarks>
internal sealed class CloudNoise
{
    public ImageTexture3D Texture { get; } = new();
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    /// <summary>The full-size level, one byte a texel, x fastest, then y, then z; null until the worker is done.</summary>
    public byte[]? Voxels { get; private set; }

    public CloudNoise(FastNoiseLite noise, int width, int height, int depth)
    {
        Width = width;
        Height = height;
        Depth = depth;
        // The worker has a copy of its own: a noise resource is not safe to read from two threads at once.
        var own = (FastNoiseLite)noise.Duplicate();
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var levels = Levels(own);
                Callable.From(() => Fill(levels)).CallDeferred();
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"cloud noise {width} x {height} x {depth}: {ex.Message}");
            }
        });
    }

    /// <summary>The seamless noise at full size (as NoiseTexture3D made it: a 0.1 blend skirt, normalised) and every smaller level.</summary>
    private (byte[] Data, int W, int H, int D)[] Levels(FastNoiseLite noise)
    {
        var slices = noise.GetSeamlessImage3D(Width, Height, Depth, false, 0.1f, true);
        var full = new byte[Width * Height * Depth];
        for (var z = 0; z < Depth; z++)
        {
            var slice = slices[z];
            if (slice.GetFormat() != Image.Format.L8) slice.Convert(Image.Format.L8);
            System.Buffer.BlockCopy(slice.GetData(), 0, full, z * Width * Height, Width * Height);
        }
        var levels = new List<(byte[] Data, int W, int H, int D)> { (full, Width, Height, Depth) };
        var (data, w, h, d) = levels[0];
        while (w > 1 || h > 1 || d > 1)
        {
            var nw = System.Math.Max(1, w >> 1);
            var nh = System.Math.Max(1, h >> 1);
            var nd = System.Math.Max(1, d >> 1);
            var next = new byte[nw * nh * nd];
            for (var z = 0; z < nd; z++)
            {
                int z0 = z * d / nd, z1 = (z + 1) * d / nd;
                for (var y = 0; y < nh; y++)
                {
                    int y0 = y * h / nh, y1 = (y + 1) * h / nh;
                    for (var x = 0; x < nw; x++)
                    {
                        int x0 = x * w / nw, x1 = (x + 1) * w / nw;
                        var sum = 0;
                        var count = 0;
                        for (var zz = z0; zz < z1; zz++)
                        {
                            for (var yy = y0; yy < y1; yy++)
                            {
                                var row = (zz * h + yy) * w;
                                for (var xx = x0; xx < x1; xx++) sum += data[row + xx];
                                count += x1 - x0;
                            }
                        }
                        next[(z * nh + y) * nw + x] = (byte)((sum + count / 2) / count);
                    }
                }
            }
            levels.Add((next, nw, nh, nd));
            (data, w, h, d) = (next, nw, nh, nd);
        }
        return [.. levels];
    }

    private void Fill((byte[] Data, int W, int H, int D)[] levels)
    {
        var images = new Godot.Collections.Array<Image>();
        foreach (var (data, w, h, d) in levels)
        {
            for (var z = 0; z < d; z++)
            {
                var slice = new byte[w * h];
                System.Buffer.BlockCopy(data, z * w * h, slice, 0, w * h);
                images.Add(Image.CreateFromData(w, h, false, Image.Format.L8, slice));
            }
        }
        var error = Texture.Create(Image.Format.L8, Width, Height, Depth, true, images);
        if (error != Error.Ok)
        {
            GD.PrintErr($"cloud noise {Width} x {Height} x {Depth}: the texture was not made ({error})");
            return;
        }
        Voxels = levels[0].Data;
    }
}
