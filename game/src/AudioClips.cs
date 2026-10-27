using Godot;

namespace GrudgeLap.Game;

/// <summary>Loads the recorded clips of assets/audio: seamless loops and one-shots (sources in assets/audio/SOURCES.md).</summary>
internal static class AudioClips
{
    /// <summary>A loop cut by tools/make-loop.py, set to repeat over its whole length; null (with a message) when the file is missing.</summary>
    public static AudioStreamWav? Loop(string file)
    {
        var stream = OneShot(file);
        if (stream is null) return null;
        // The importer leaves the loop points unset. The loop end counts frames, which the data's length does
        // not give for the compressed (QOA) import: that ended every loop after its first fifth, with a click.
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        stream.LoopBegin = 0;
        stream.LoopEnd = (int)Math.Round(stream.GetLength() * stream.MixRate);
        return stream;
    }

    /// <summary>A clip played once through; null (with a message) when the file is missing.</summary>
    public static AudioStreamWav? OneShot(string file)
    {
        var path = $"res://assets/audio/{file}";
        if (ResourceLoader.Exists(path)) return GD.Load<AudioStreamWav>(path);
        GD.PrintErr($"audio clip missing: {file}");
        return null;
    }

    /// <summary>Several takes of one sound, drawn at random with a spread of pitch and level so that it never repeats itself.</summary>
    public static AudioStreamRandomizer? Takes(float pitchSpread, float levelSpreadDb, params string[] files)
    {
        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitch = pitchSpread,
            RandomVolumeOffsetDb = levelSpreadDb,
        };
        foreach (var file in files)
        {
            var clip = OneShot(file);
            if (clip is not null) randomizer.AddStream(-1, clip);
        }
        return randomizer.StreamsCount == 0 ? null : randomizer;
    }
}
