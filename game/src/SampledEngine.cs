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
/// compress/mode=0) because the voice reads the samples themselves. A voice is made on a worker thread (see
/// <see cref="Load"/>) and swapped in by <see cref="Update"/> when it is ready.
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
    private (string Folder, EngineVoicing Voicing, float Redline)? _loaded;
    /// <summary>The voice being made for the car last loaded; the one it replaces plays until it is ready.</summary>
    private Task<GranularEngine?>? _making;

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

    /// <summary>
    /// Loads the grain banks of a car's engine (its <see cref="EngineSpec.SoundFolder"/>), replacing the previous ones,
    /// voiced for its body (<see cref="VoicingOf"/>) and trimmed to the common loudness over its own revs.
    /// </summary>
    /// <remarks>
    /// The recordings are read here, through Godot's loader on the main thread; the banks are evened and the voice is
    /// trimmed on a worker. Made here, that took 40-130 ms (100-290 in a debug build), longer than the generator's
    /// 50 ms of audio, so a change of car in a new session ran it dry (0.20 review); until the new voice is ready the
    /// previous one plays on.
    /// </remarks>
    public void Load(VehicleSpec spec)
    {
        var engine = spec.Engine;
        var voicing = VoicingOf(spec);
        if (_loaded == (engine.SoundFolder, voicing, engine.RedlineRpm)) return;
        _loaded = (engine.SoundFolder, voicing, engine.RedlineRpm);
        var folder = engine.SoundFolder;
        var drive = Recording(folder, "engine_drive", required: true);
        if (drive is null)
        {
            _engine = null;
            _making = null;
            return;
        }
        var idle = Recording(folder, "engine_idle");
        var overrun = Recording(folder, "engine_overrun");
        var cylinders = engine.Cylinders;
        var redline = engine.RedlineRpm;
        _making = Task.Run(() => (GranularEngine?)new GranularEngine(
            Bank(idle), Bank(drive)!, cylinders, voicing, redline, overrun: Bank(overrun)));
    }

    /// <summary>
    /// How a car's body passes its engine's sound (<see cref="EngineVoicing"/>, 0.20). The flat-twelve and the V8 are
    /// mid-engined, the engine behind the seats of a two-seat cell about 1.3 m long, with big twin tailpipes; every other
    /// car has its engine in front. The cabin is measured from the dash to where its air ends: a hatchback's and an
    /// estate's load space is one space with the cabin (about 2.3 m and 2.8 m), a saloon's boot is shut off behind the
    /// rear seats (about 1.9 m). A car of a style not named here is taken for a front-engined hatchback.
    /// </summary>
    internal static EngineVoicing VoicingOf(VehicleSpec spec) => spec.Style switch
    {
        CarStyle.FlatTwelve or CarStyle.V8 => new EngineVoicing(EnginePlacement.Mid, 1.3f, TailpipeRadius: 0.035f),
        CarStyle.Saloon or CarStyle.Fleet => new EngineVoicing(EnginePlacement.Front, 1.9f),
        CarStyle.Estate => new EngineVoicing(EnginePlacement.Front, 2.8f),
        _ => new EngineVoicing(EnginePlacement.Front, 2.3f),
    };

    /// <summary>A bank made from a recording read by <see cref="Recording"/> (its timbre evened: the costly part).</summary>
    private static GrainBank? Bank((byte[] Pcm, string Marks)? recording) =>
        recording is { } r ? GrainBank.FromPcm16(r.Pcm, r.Marks) : null;

    /// <summary>
    /// The 16-bit PCM and the marks of a grain bank of the car's folder; only the run-up is required, a car may have no
    /// idle or overrun recording.
    /// </summary>
    private static (byte[] Pcm, string Marks)? Recording(string folder, string name, bool required = false)
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
        return (stream.Data, Godot.FileAccess.GetFileAsString(marks));
    }

    /// <summary>
    /// Tops up the generator from the smoothed engine state (<paramref name="cut"/>: the rev limiter cuts the ignition);
    /// <paramref name="muted"/> fades the voice out (1989 mode).
    /// </summary>
    public void Update(float rpm, float load, bool cut, bool running, bool muted)
    {
        if (_making is { IsCompleted: true } made)
        {
            _making = null;
            if (made.IsCompletedSuccessfully) _engine = made.Result;
            else GD.PrintErr($"engine voice {_loaded?.Folder} could not be made: {made.Exception?.GetBaseException().Message}");
        }
        if (_playback is null || _engine is null) return;
        _engine.Volume = Volume;
        _engine.Interior = Interior;
        _engine.Cut = cut;
        // Whole chunks only: the same two arrays every time, and what is left over waits for the next frame.
        while (_playback.GetFramesAvailable() >= Chunk)
        {
            _engine.Render(_block, rpm, load, running && !muted);
            for (var i = 0; i < Chunk; i++) _frames[i] = new Vector2(_block[i], _block[i]);
            _playback.PushBuffer(_frames);
        }
    }
}
