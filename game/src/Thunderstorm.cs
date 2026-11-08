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

    public override void _Ready()
    {
        _near = AudioClips.OneShot("thunder/near.wav");
        _mid = AudioClips.OneShot("thunder/mid.wav");
        _far = AudioClips.OneShot("thunder/far.wav");
        _player = new AudioStreamPlayer { Name = "Thunder", Bus = "World", VolumeDb = -80f };
        AddChild(_player);
    }

    /// <summary>Once a frame: a strike when one is due in the rain, the flash's strokes, the thunder when its sound arrives.</summary>
    public void Update(Weather weather, SessionPhase phase, bool retro, float dt)
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
        }
        var flash = 0f;
        if (_flashAt >= 0f)
        {
            // A strike is a few strokes down the same channel: bright, a dark gap, fainter, again, fainter still.
            var t = _clock - _flashAt;
            flash = t < 0.07f ? 1f : t < 0.12f ? 0.1f : t < 0.17f ? 0.7f : t < 0.24f ? 0.05f : t < 0.29f ? 0.4f : 0f;
            if (t > 0.35f) _flashAt = -1f;
        }
        WorldBuilder.SetFlash(storm ? flash * _flashStrength : 0f);
        if (_thunderAt < 0f || _clock < _thunderAt) return;
        _thunderAt = -1f;
        if (!storm || _player is null) return;
        _player.Stream = _distance < 2000f ? _near : _distance < 4500f ? _mid : _far;
        _player.VolumeDb = Mathf.LinearToDb(Math.Clamp(1000f / _distance, 0.1f, 1f)) - 2f;
        _player.Play();
    }
}
