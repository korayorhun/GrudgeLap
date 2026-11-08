using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;

namespace GrudgeLap.Game;

/// <summary>
/// The rain's thunderstorm (0.63): every 18 to 53 seconds a lightning strike somewhere round the plain, from 0.9 to 8 km
/// off and more often far than near: its flash at once, the image brightening in three strokes in a third of a second
/// (<see cref="WorldBuilder.SetFlash"/>), its thunder a second for every 343 metres later, a clap by the distance
/// (assets/audio/thunder: near under 2 km, middling under 4.5, far beyond; tools/make-thunder.py), 6 dB quieter for each
/// doubling of the distance past a kilometre, on the world's bus so that from the seat it comes through the glass. Only
/// while it rains; never in the replay or the 1989 look. A clap under way when the rain stops plays out.
/// </summary>
internal sealed partial class Thunderstorm : Node
{
    private readonly System.Random _random = new(1989);
    private AudioStreamPlayer? _player;
    private AudioStreamWav? _near;
    private AudioStreamWav? _mid;
    private AudioStreamWav? _far;
    private float _clock;
    private float _nextStrike = 20f;
    private float _flashAt = -1f;
    private float _flashStrength;
    private float _thunderAt = -1f;
    private float _distance;
    private Vector3 _strikeTop;
    private bool _boltDrawn;
    private float _boltEnergy;
    private MeshInstance3D? _bolt;
    private ShaderMaterial? _boltMaterial;

    public override void _Ready()
    {
        _near = AudioClips.OneShot("thunder/near.wav");
        _mid = AudioClips.OneShot("thunder/mid.wav");
        _far = AudioClips.OneShot("thunder/far.wav");
        _player = new AudioStreamPlayer { Name = "Thunder", Bus = "World", VolumeDb = -80f };
        AddChild(_player);
    }

    /// <summary>
    /// Once a frame: a strike when one is due in the rain, the flash's strokes, the thunder when its sound arrives;
    /// <paramref name="eye"/> is the camera's place, for the bolt (0.64).
    /// </summary>
    public void Update(Weather weather, SessionPhase phase, bool retro, Vector3 eye, float dt)
    {
        _clock += dt;
        var storm = WeatherModel.Raining(weather) && !retro && phase != SessionPhase.Replay;
        if (storm && _clock >= _nextStrike)
        {
            _distance = 900f + 7100f * MathF.Pow((float)_random.NextDouble(), 1.5f);
            _flashAt = _clock;
            _flashStrength = Math.Clamp(2500f / _distance, 0.25f, 1.6f);
            _thunderAt = _clock + _distance / 343f;
            _nextStrike = _clock + 18f + 35f * (float)_random.NextDouble();
            // The channel from the deck's base (700 m) to the ground at the strike, a little below the plain's level so
            // that it does not stop short over a dip; the deck lit round its top.
            var bearing = (float)(_random.NextDouble() * Math.Tau);
            var ground = eye + new Vector3(MathF.Sin(bearing), 0f, MathF.Cos(bearing)) * _distance;
            ground.Y = -30f;
            _strikeTop = ground + new Vector3((float)(_random.NextDouble() - 0.5) * 300f, 730f, (float)(_random.NextDouble() - 0.5) * 300f);
            // Only this strike's channel: a far one draws none, and the mesh left from the last near one must stay hidden.
            _boltDrawn = _distance < 6000f;
            if (_boltDrawn) DrawBolt(eye, _strikeTop, ground, _distance);
            else if (_bolt is not null) _bolt.Mesh = null;
        }
        var flash = 0f;
        if (_flashAt >= 0f)
        {
            // A strike is a few strokes down the same channel: bright, a dark gap, fainter, again, fainter still.
            var t = _clock - _flashAt;
            flash = t < 0.07f ? 1f : t < 0.12f ? 0.1f : t < 0.17f ? 0.7f : t < 0.24f ? 0.05f : t < 0.29f ? 0.4f : 0f;
            if (t > 0.35f) _flashAt = -1f;
        }
        WorldBuilder.SetFlash(storm ? flash : 0f, _flashStrength, _strikeTop);
        if (_bolt is not null && _boltMaterial is not null)
        {
            // The channel follows the strokes: bright with each, a dim glow in the gaps between them.
            var lit = _boltDrawn && storm && flash > 0f;
            _bolt.Visible = lit;
            if (lit) _boltMaterial.SetShaderParameter("energy", _boltEnergy * flash);
        }
        if (_thunderAt < 0f || _clock < _thunderAt) return;
        _thunderAt = -1f;
        if (!storm || _player is null) return;
        _player.Stream = _distance < 2000f ? _near : _distance < 4500f ? _mid : _far;
        _player.VolumeDb = Mathf.LinearToDb(Math.Clamp(1000f / _distance, 0.1f, 1f)) - 2f;
        _player.Play();
    }

    /// <summary>
    /// The strike's channel (0.64), drawn while the flash's strokes last: a jagged line from <paramref name="top"/> in the
    /// deck to <paramref name="ground"/>, split six times by midpoint displacement (each midpoint pushed sideways by up
    /// to a fifth of its segment, so the line zigzags at every scale), and two or three branches off its upper half
    /// reaching down and out. Ribbons turned to the eye: a flat-topped core some three pixels wide at its distance (a
    /// line under two pixels came out dotted and faint, the temporal anti-aliasing averaging it away), and a halo six
    /// times as wide and faint round it, the glow of a stroke seen through the rain.
    /// </summary>
    private void DrawBolt(Vector3 eye, Vector3 top, Vector3 ground, float distance)
    {
        // The ribbons in pixels of the 3D render at the strike's distance (the active camera's field of view, the render's
        // own height under any scaling, from the window: the stretched view's rect is its 1600 x 900 base): a core three
        // pixels wide, a branch's two and a half. A fixed angle left the
        // branches under two pixels in the cockpit and the whole channel under two at 720 lines or with FSR 2, and under
        // two the temporal anti-aliasing broke a line into dots.
        var viewport = GetViewport();
        var lines = MathF.Max(GetWindow().Size.Y * viewport.Scaling3DScale, 240f);
        var pixel = 2f * MathF.Tan(Mathf.DegToRad(viewport.GetCamera3D()?.Fov ?? 70f) * 0.5f) / lines * distance;
        var width = 3f * pixel;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var channel = Jagged(top, ground, 6, 0.2f);
        Ribbon(st, channel, eye, width, true);
        Ribbon(st, channel, eye, width * 6f, false);
        var branches = 2 + _random.Next(2);
        for (var b = 0; b < branches; b++)
        {
            var from = channel[_random.Next(channel.Count / 6, channel.Count / 2)];
            var drop = (from.Y - ground.Y) * (0.3f + 0.3f * (float)_random.NextDouble());
            var angle = (float)(_random.NextDouble() * Math.Tau);
            var to = from + new Vector3(MathF.Cos(angle) * drop * 0.6f, -drop, MathF.Sin(angle) * drop * 0.6f);
            var branch = Jagged(from, to, 4, 0.25f);
            Ribbon(st, branch, eye, 2.5f * pixel, true);
            Ribbon(st, branch, eye, 10f * pixel, false);
        }
        _boltMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/lightning.gdshader") };
        // A little dimmer through more of the rain's air; the strokes scale it frame by frame (Update).
        _boltEnergy = 14f * MathF.Exp(-distance / 9000f);
        if (_bolt is null)
        {
            _bolt = new MeshInstance3D { Name = "Bolt", MaterialOverride = _boltMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_bolt);
        }
        _bolt.Mesh = st.Commit();
        _bolt.Visible = false;
    }

    /// <summary>The points of a line from <paramref name="a"/> to <paramref name="b"/> split <paramref name="levels"/> times, each midpoint pushed sideways by up to <paramref name="jag"/> of its segment.</summary>
    private List<Vector3> Jagged(Vector3 a, Vector3 b, int levels, float jag)
    {
        var points = new List<Vector3> { a, b };
        for (var level = 0; level < levels; level++)
        {
            var next = new List<Vector3>(points.Count * 2) { points[0] };
            for (var i = 1; i < points.Count; i++)
            {
                var p = points[i - 1];
                var q = points[i];
                var length = p.DistanceTo(q);
                var push = new Vector3((float)(_random.NextDouble() - 0.5), (float)(_random.NextDouble() - 0.5) * 0.3f, (float)(_random.NextDouble() - 0.5)) * (2f * jag * length);
                next.Add((p + q) * 0.5f + push);
                next.Add(q);
            }
            points = next;
        }
        return points;
    }

    /// <summary>A ribbon along the points, turned to the eye, UV.x running across it, its colour's red 1 for a core, 0 for a halo.</summary>
    private static void Ribbon(SurfaceTool st, List<Vector3> points, Vector3 eye, float width, bool core)
    {
        st.SetColor(core ? Colors.White : Colors.Black);
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var side = (b - a).Cross(eye - a).Normalized() * width * 0.5f;
            st.SetUV(new Vector2(0f, 0f)); st.AddVertex(a - side);
            st.SetUV(new Vector2(1f, 0f)); st.AddVertex(a + side);
            st.SetUV(new Vector2(1f, 1f)); st.AddVertex(b + side);
            st.SetUV(new Vector2(0f, 0f)); st.AddVertex(a - side);
            st.SetUV(new Vector2(1f, 1f)); st.AddVertex(b + side);
            st.SetUV(new Vector2(0f, 1f)); st.AddVertex(b - side);
        }
    }
}
