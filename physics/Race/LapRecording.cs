using System.Numerics;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// The best lap, pose by pose, so it can be driven again as the ghost of the "grudge match": every later
/// lap races the recording. Sampled at a fixed rate from the start line; serialisable so the ghost outlives
/// the process.
/// </summary>
public sealed class LapRecording
{
    /// <summary>Samples a second on average (<see cref="RaceSession"/>.RecordLap); each one carries its own lap time, which is what <see cref="TryPose"/> goes by.</summary>
    public const float SampleHz = 20f;
    private const int Magic = 0x31474C47; // "GLG1"

    private readonly List<PoseSample> _samples;

    public LapRecording(RouteKind route, float duration, List<PoseSample> samples)
    {
        Route = route;
        Duration = duration;
        _samples = samples;
    }

    public RouteKind Route { get; }
    public float Duration { get; }
    public IReadOnlyList<PoseSample> Samples => _samples;

    /// <summary>Pose at a time into the lap; false once the recording has ended.</summary>
    /// <remarks>
    /// The samples are looked up by the lap time each one carries (0.74), never by their place in the list: until 0.74 the
    /// n-th sample was taken to be n / <see cref="SampleHz"/> seconds in, but a lap recorded at 72 fps (a 144 Hz screen)
    /// has one every 0.0556 s and at 30 fps one every 0.0667 s, so its ghost ran ahead of the lap it stood for and sat on
    /// the line for the rest of it (a 0.72 lap of 86.8 s reached its last sample 78.0 s in). Looked up by time, the files
    /// saved before replay right as they are.
    /// </remarks>
    public bool TryPose(float lapTime, out Vector3 position, out Quaternion orientation, out float steer)
    {
        position = default;
        orientation = Quaternion.Identity;
        steer = 0f;
        if (_samples.Count == 0 || lapTime < 0f || lapTime > Duration) return false;
        var next = FirstSampleAfter(lapTime);
        if (next == 0 || next == _samples.Count)
        {
            // Before the first sample (a file of 0.73 or before starts a twentieth of a second or so in) or after the last one.
            var held = next == 0 ? _samples[0] : _samples[^1];
            position = held.Position;
            orientation = held.Orientation;
            steer = held.SteerAngle;
            return true;
        }
        var a = _samples[next - 1];
        var b = _samples[next];
        var span = MathF.Max(b.Time - a.Time, 1e-4f);
        var t = MathUtil.Clamp01((lapTime - a.Time) / span);
        position = Vector3.Lerp(a.Position, b.Position, t);
        orientation = Quaternion.Normalize(Quaternion.Slerp(a.Orientation, b.Orientation, t));
        steer = MathUtil.Lerp(a.SteerAngle, b.SteerAngle, t);
        return true;
    }

    /// <summary>Index of the first sample later than <paramref name="lapTime"/>, or the count when none is; the times only grow along a lap.</summary>
    private int FirstSampleAfter(float lapTime)
    {
        var low = 0;
        var high = _samples.Count;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (_samples[middle].Time <= lapTime) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Magic);
        w.Write((int)Route);
        w.Write(Duration);
        w.Write(_samples.Count);
        foreach (var s in _samples)
        {
            w.Write(s.Time);
            w.Write(s.Position.X); w.Write(s.Position.Y); w.Write(s.Position.Z);
            w.Write(s.Orientation.X); w.Write(s.Orientation.Y); w.Write(s.Orientation.Z); w.Write(s.Orientation.W);
            w.Write(s.SteerAngle);
        }
        return stream.ToArray();
    }

    /// <summary>Null for anything that is not a recording this version understands.</summary>
    public static LapRecording? Deserialize(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            using var r = new BinaryReader(stream);
            if (r.ReadInt32() != Magic) return null;
            var route = (RouteKind)r.ReadInt32();
            var duration = r.ReadSingle();
            var count = r.ReadInt32();
            if (count < 2 || count > 200_000 || !(duration > 0f)) return null;
            var samples = new List<PoseSample>(count);
            for (var i = 0; i < count; i++)
            {
                var time = r.ReadSingle();
                var position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var orientation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var steer = r.ReadSingle();
                samples.Add(new PoseSample(time, position, orientation, steer));
            }
            return new LapRecording(route, duration, samples);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
        {
            return null;
        }
    }
}
