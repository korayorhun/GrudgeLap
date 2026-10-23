using System.Text.Json;

namespace GrudgeLap.Audio;

/// <summary>
/// Firing pulses cut from an engine recording (tools/make-engine-bank.py): the recording as samples, and for every
/// pulse its position and the firing frequency there. A grain is the stretch from one pulse's predecessor to its
/// successor, so the grains of neighbouring pulses overlap by exactly a period, cut at the same point of a pulse.
/// </summary>
public sealed class GrainBank
{
    private readonly int[] _byFiring;

    public GrainBank(float[] samples, int sampleRate, int[] marks, float[] firing, int[] segment)
    {
        if (marks.Length != firing.Length || marks.Length != segment.Length) throw new ArgumentException("marks, firing and segment differ in length");
        Samples = samples;
        SampleRate = sampleRate;
        Marks = marks;
        Firing = firing;
        // A pulse can be a grain when both neighbours belong to its own segment (the bank joins several recordings).
        Usable = new bool[marks.Length];
        for (var i = 1; i < marks.Length - 1; i++)
        {
            Usable[i] = segment[i - 1] == segment[i] && segment[i + 1] == segment[i] && marks[i - 1] >= 0 && marks[i + 1] < samples.Length;
        }
        _byFiring = Enumerable.Range(0, marks.Length).Where(i => Usable[i]).OrderBy(i => firing[i]).ToArray();
        if (_byFiring.Length == 0) throw new ArgumentException("the bank has no usable grain");
        MinFiring = firing[_byFiring[0]];
        MaxFiring = firing[_byFiring[^1]];
    }

    public float[] Samples { get; }
    public int SampleRate { get; }
    public int[] Marks { get; }
    public float[] Firing { get; }
    public bool[] Usable { get; }
    public float MinFiring { get; }
    public float MaxFiring { get; }
    public int Count => _byFiring.Length;

    /// <summary>The longest grain in samples (the two periods around the slowest pulse).</summary>
    public int LongestGrain
    {
        get
        {
            var longest = 0;
            for (var i = 1; i < Marks.Length - 1; i++)
            {
                if (Usable[i]) longest = Math.Max(longest, Marks[i + 1] - Marks[i - 1]);
            }
            return longest;
        }
    }

    /// <summary>The farthest a grain reaches before its pulse, in samples (the period before the slowest pulse).</summary>
    public int LongestLead
    {
        get
        {
            var longest = 0;
            for (var i = 1; i < Marks.Length - 1; i++)
            {
                if (Usable[i]) longest = Math.Max(longest, Marks[i] - Marks[i - 1]);
            }
            return longest;
        }
    }

    /// <summary>A usable pulse picked at random among the <paramref name="spread"/> whose firing frequency is nearest to <paramref name="firing"/>.</summary>
    internal int Nearest(float firing, int spread, Random random)
    {
        var lo = 0;
        var hi = _byFiring.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Firing[_byFiring[mid]] < firing) lo = mid + 1;
            else hi = mid;
        }
        var first = Math.Clamp(lo - spread / 2, 0, Math.Max(_byFiring.Length - spread, 0));
        var last = Math.Min(first + spread, _byFiring.Length);
        return _byFiring[random.Next(first, last)];
    }

    /// <summary>A bank from the 16-bit mono PCM of its recording and the text of its marks file.</summary>
    public static GrainBank FromPcm16(ReadOnlySpan<byte> pcm, string marksJson)
    {
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
        using var document = JsonDocument.Parse(marksJson);
        var root = document.RootElement;
        var marks = root.GetProperty("marks").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var firing = root.GetProperty("firing").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        var segment = root.TryGetProperty("segment", out var seg) ? seg.EnumerateArray().Select(e => e.GetInt32()).ToArray() : new int[marks.Length];
        return new GrainBank(samples, root.GetProperty("rate").GetInt32(), marks, firing, segment);
    }

    /// <summary>A bank from its files: a 16-bit mono WAV and the marks beside it.</summary>
    public static GrainBank FromFiles(string wavPath, string marksPath) => FromPcm16(Wav.ReadPcm16(wavPath), File.ReadAllText(marksPath));
}

/// <summary>The data chunk of a 16-bit PCM WAV file.</summary>
public static class Wav
{
    public static byte[] ReadPcm16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var size = BitConverter.ToInt32(bytes, at + 4);
            if (bytes[at] == 'd' && bytes[at + 1] == 'a' && bytes[at + 2] == 't' && bytes[at + 3] == 'a')
            {
                return bytes.AsSpan(at + 8, Math.Min(size, bytes.Length - at - 8)).ToArray();
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException($"no data chunk in {path}");
    }

    public static void WritePcm16(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var s in samples) writer.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));
    }
}
