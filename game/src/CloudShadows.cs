using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The volumetric clouds' shadows (0.44): the sun's light through the clouds the sky marches, on the land, the plain's
/// ground and the trees out of a few hundred metres (assets/shaders/cloud_shadow.gdshaderinc), and on everything near
/// the camera through the sun itself, dimmed while the camera is under a cloud's shadow: the car drives into the shade
/// with the road and the cabin round it, and the shadows are seen drifting over the plain and the mountains beyond.
/// </summary>
/// <remarks>
/// The same function runs here and on the GPU: the clouds' shape noise and weather map are read back once generated and
/// sampled as the GPU samples them (wrapped, linear between texel centres). The road, the cars and the props light by
/// Godot's own lighting and take the camera's share alone; a cloud's shadow is a kilometre across, so that is out only
/// near the shadow's edge a few hundred metres off. Off in overcast weather (the grey sky is its own cloud), in the
/// 1989 look and with --cloud-shadows=0.
/// </remarks>
internal static class CloudShadows
{
    // The cloud layer as the sky shader has it: base and top over the plain, coverage, density (extinction a metre).
    private const float Bottom = 1700f;
    private const float Top = 3200f;
    private const float Coverage = 0.41f;
    private const float Extinction = 0.045f;
    private const float ShapeTile = 5200f;
    /// <summary>The shape noise's tile upwards: its texture is half as tall, so its texels are as fine up as across.</summary>
    private const float ShapeTileHigh = 2600f;
    private const float WeatherTile = 26000f;
    /// <summary>The sun's share under a thick cloud: the light the cloud scatters on.</summary>
    private const float Floor = 0.12f;

    private static readonly List<ShaderMaterial> _materials = [];
    private static CloudNoise? _shapeTexture;
    private static NoiseTexture2D? _weatherTexture;
    private static byte[]? _shape;
    private static int _shapeWidth, _shapeHeight, _shapeDepth;
    private static byte[]? _weather;
    private static int _weatherWidth, _weatherHeight;
    private static Vector3 _sun = Vector3.Up;
    private static bool _on;
    private static float _logged = 1f;

    /// <summary>--pose-log: the camera's share of the sun each time it changes by a tenth.</summary>
    public static bool Log { get; set; }

    /// <summary>The sky's noises and the direction to the sun; the materials registered so far and later take them.</summary>
    public static void SetClouds(CloudNoise shape, NoiseTexture2D weather, Vector3 towardsSun)
    {
        _shapeTexture = shape;
        _weatherTexture = weather;
        _sun = towardsSun.Normalized();
        _shape = null;
        _weather = null;
        foreach (var material in _materials) Bind(material);
    }

    /// <summary>A material that includes cloud_shadow.gdshaderinc, to be kept up to date.</summary>
    public static void Register(ShaderMaterial material)
    {
        _materials.Add(material);
        Bind(material);
    }

    private static void Bind(ShaderMaterial material)
    {
        material.SetShaderParameter("cloud_layer", new Vector4(Bottom, Top, Coverage, Extinction));
        material.SetShaderParameter("cloud_sun", _sun);
        material.SetShaderParameter("cloud_shadows", false);
        if (_shapeTexture is null || _weatherTexture is null) return;
        material.SetShaderParameter("cloud_shape", _shapeTexture.Texture);
        material.SetShaderParameter("cloud_weather", _weatherTexture);
    }

    /// <summary>Drops the materials and the noises (the game quits).</summary>
    public static void Clear()
    {
        _materials.Clear();
        _shapeTexture = null;
        _weatherTexture = null;
        _shape = null;
        _weather = null;
        _on = false;
        _logged = 1f;
    }

    /// <summary>
    /// Once a frame: the wind's drift (the sky's own, every frame where the sky takes it every two seconds) and the
    /// camera's share of the sun on every material. Returns that share, for the sun's energy: 1 when off.
    /// </summary>
    public static float Update(bool on, Vector2 wind, Vector3 eye)
    {
        on &= _shape is not null;
        if (!on)
        {
            if (_on)
            {
                foreach (var material in _materials) material.SetShaderParameter("cloud_shadows", false);
                _on = false;
            }
            return 1f;
        }
        var here = Sunlight(eye, wind);
        foreach (var material in _materials)
        {
            if (!_on) material.SetShaderParameter("cloud_shadows", true);
            material.SetShaderParameter("cloud_wind", wind);
            material.SetShaderParameter("cloud_here", here);
        }
        _on = true;
        if (Log && MathF.Abs(here - _logged) >= 0.1f)
        {
            _logged = here;
            GD.Print($"cloud shade: the sun at {here:0.00} at ({eye.X:0}, {eye.Z:0})");
        }
        return here;
    }

    /// <summary>The share of the sun's light that reaches <paramref name="p"/> through the clouds (the shader's cloud_sunlight).</summary>
    public static float Sunlight(Vector3 p, Vector2 wind)
    {
        if (_shape is null || _weather is null) return 1f;
        var t0 = MathF.Max((Bottom - p.Y) / _sun.Y, 0f);
        var t1 = (Top - p.Y) / _sun.Y;
        if (t1 <= 0f) return 1f;
        var step = (t1 - t0) * 0.25f;
        var depth = 0f;
        for (var i = 0; i < 4; i++) depth += Density(p + _sun * (t0 + (i + 0.5f) * step), wind);
        return Floor + (1f - Floor) * MathF.Exp(-depth * step);
    }

    private static float Density(Vector3 p, Vector2 wind)
    {
        var h = (p.Y - Bottom) / (Top - Bottom);
        var q = p + new Vector3(wind.X, 0f, wind.Y);
        var cover = Math.Clamp(Coverage + (Weather(q.X / WeatherTile, q.Z / WeatherTile) - 0.5f) * 0.9f, 0f, 1f);
        var profile = SmoothStep(0f, 0.08f, h) * (1f - SmoothStep(0.35f + 0.45f * cover, 1f, h));
        return MathF.Max(Shape(q.X / ShapeTile, q.Y / ShapeTileHigh, q.Z / ShapeTile) * profile - (1f - cover), 0f) * Extinction;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    /// <summary>The shape noise at texture coordinates (u, v, w), linear between texel centres, wrapped.</summary>
    private static float Shape(float u, float v, float w)
    {
        var data = _shape!;
        var x = u * _shapeWidth - 0.5f;
        var y = v * _shapeHeight - 0.5f;
        var z = w * _shapeDepth - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var z0 = (int)MathF.Floor(z);
        var fx = x - x0;
        var fy = y - y0;
        var fz = z - z0;
        float Texel(int i, int j, int k) => data[(Wrap(k, _shapeDepth) * _shapeHeight + Wrap(j, _shapeHeight)) * _shapeWidth + Wrap(i, _shapeWidth)] / 255f;
        float Row(int j, int k) => Texel(x0, j, k) + (Texel(x0 + 1, j, k) - Texel(x0, j, k)) * fx;
        float Layer(int k) => Row(y0, k) + (Row(y0 + 1, k) - Row(y0, k)) * fy;
        return Layer(z0) + (Layer(z0 + 1) - Layer(z0)) * fz;
    }

    /// <summary>The weather map at texture coordinates (u, v), linear between texel centres, wrapped.</summary>
    private static float Weather(float u, float v)
    {
        var data = _weather!;
        var x = u * _weatherWidth - 0.5f;
        var y = v * _weatherHeight - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        float Texel(int i, int j) => data[Wrap(j, _weatherHeight) * _weatherWidth + Wrap(i, _weatherWidth)] / 255f;
        float Row(int j) => Texel(x0, j) + (Texel(x0 + 1, j) - Texel(x0, j)) * fx;
        return Row(y0) + (Row(y0 + 1) - Row(y0)) * fy;
    }

    /// <summary>Whether the noises have been read back (<see cref="Capture"/>).</summary>
    public static bool Ready => _shape is not null;

    /// <summary>
    /// The extra wind that clears the sun at <paramref name="eye"/>: the clouds moved on along the drift 400 m at a
    /// time until the sun there is full, so the game opens in the sun (the start line lay under a cloud).
    /// </summary>
    public static Vector2 Clearing(Vector3 eye, Vector2 wind, Vector2 drift)
    {
        var along = drift.Normalized() * 400f;
        for (var k = 0; k < 40; k++)
        {
            if (Sunlight(eye, wind + along * k) >= 0.99f) return along * k;
        }
        return Vector2.Zero;
    }

    /// <summary>
    /// Takes the noises once they have been generated (on threads, after the sky is made); true once they are. The shape
    /// comes as CloudNoise keeps its full-size level (0.71): read back from the texture it would now carry the whole mip
    /// chain, every level's slices one after another.
    /// </summary>
    public static bool Capture()
    {
        if (_shape is not null) return true;
        if (_shapeTexture?.Voxels is not { } voxels || _weatherTexture is null) return false;
        var map = _weatherTexture.GetImage();
        if (map is null || map.IsEmpty()) return false;
        _weather = Luminance(map);
        _weatherWidth = map.GetWidth();
        _weatherHeight = map.GetHeight();
        _shapeWidth = _shapeTexture.Width;
        _shapeHeight = _shapeTexture.Height;
        _shapeDepth = _shapeTexture.Depth;
        _shape = voxels;
        if (Log) GD.Print($"cloud shade: noises read back, {_shapeWidth} x {_shapeHeight} x {_shapeDepth} and {_weatherWidth} x {_weatherHeight}");
        return true;
    }

    private static byte[] Luminance(Image image)
    {
        if (image.GetFormat() == Image.Format.L8) return image.GetData();
        var copy = (Image)image.Duplicate();
        copy.Convert(Image.Format.L8);
        return copy.GetData();
    }
}
