using Godot;
using GrudgeLap.Audio;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Engine sound rebuilt from recordings firing pulse by firing pulse (<see cref="GranularEngine"/>): per car an
/// idle recording and a run up through a gear, cut into grains by tools/make-engine-bank.py (sources in
/// assets/audio/SOURCES.md). The voice renders into a stream generator at the recordings' 44.1 kHz.
/// </summary>
/// <remarks>
/// It replaced loops a fraction of a second long pitched to the revs: their repetition buzzed, and the idle was a
/// 3000 rpm loop slowed to a third. The banks' WAVs are imported uncompressed (their .import files say
/// compress/mode=0) because the voice reads the samples themselves.
/// </remarks>
internal sealed partial class SampledEngine : Node
{
    // A game frame (16 ms) and room for a hitch; the voice itself runs about one idle firing period ahead.
    private const float BufferSeconds = 0.05f;
    private const int Chunk = 256;
    private readonly float[] _block = new float[Chunk];
    private readonly Vector2[] _frames = new Vector2[Chunk];
    private AudioStreamGeneratorPlayback? _playback;
    private GranularEngine? _engine;
    private string? _loaded;

    public float Volume { get; set; } = 0.9f;
    /// <summary>The listener sits in the cabin (the cockpit view): the engine is heard through the bulkhead.</summary>
    public bool Interior { get; set; }

    public override void _Ready()
    {
        var player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = 44100, BufferLength = BufferSeconds },
            Autoplay = true,
        };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>Loads the grain banks of an engine (its <see cref="EngineSpec.SoundFolder"/>), replacing the previous ones.</summary>
    public void Load(EngineSpec engine)
    {
        if (_loaded == engine.SoundFolder) return;
        _loaded = engine.SoundFolder;
        var folder = engine.SoundFolder;
        var drive = Bank(folder, "engine_drive", required: true);
        _engine = drive is null ? null : new GranularEngine(Bank(folder, "engine_idle"), drive, engine.Cylinders, overrun: Bank(folder, "engine_overrun"));
    }

    /// <summary>A grain bank of the car's folder; only the run-up is required, a car may have no idle or overrun recording.</summary>
    private static GrainBank? Bank(string folder, string name, bool required = false)
    {
        var wav = $"res://assets/audio/{folder}/{name}.wav";
        var marks = $"res://assets/audio/{folder}/{name}.json";
        var stream = ResourceLoader.Exists(wav) ? GD.Load<AudioStreamWav>(wav) : null;
        if (stream is null || !Godot.FileAccess.FileExists(marks))
        {
            if (required) GD.PrintErr($"engine grain bank missing: {folder}/{name}");
            return null;
        }
        if (stream.Format != AudioStreamWav.FormatEnum.Format16Bits || stream.Stereo)
        {
            GD.PrintErr($"engine grain bank {folder}/{name} is not imported as 16-bit mono PCM (compress/mode=0)");
            return null;
        }
        return GrainBank.FromPcm16(stream.Data, Godot.FileAccess.GetFileAsString(marks));
    }

    /// <summary>Tops up the generator from the smoothed engine state; <paramref name="muted"/> fades the voice out (1989 mode).</summary>
    public void Update(float rpm, float load, bool running, bool muted)
    {
        if (_playback is null || _engine is null) return;
        _engine.Volume = Volume;
        _engine.Interior = Interior;
        // Whole chunks only: the same two arrays every time, and what is left over waits for the next frame.
        while (_playback.GetFramesAvailable() >= Chunk)
        {
            _engine.Render(_block, rpm, load, running && !muted);
            for (var i = 0; i < Chunk; i++) _frames[i] = new Vector2(_block[i], _block[i]);
            _playback.PushBuffer(_frames);
        }
    }
}
