using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Engine sound from recordings: a few seamless loops cut from real engines at known speeds, each pitched
/// to the simulated rpm and cross-faded by how far the rpm sits from the loop's own speed, through a
/// low-pass filter that opens with the throttle. The loops come from a CC0 collection (see
/// assets/audio/SOURCES.md); which set plays follows the chosen car.
/// </summary>
internal sealed partial class SampledEngine : Node
{
    private const string BusName = "Engine";
    private readonly record struct Loop(string File, float Rpm, bool Coast);

    private static readonly Dictionary<CarStyle, Loop[]> Sets = new()
    {
        // Twelve cylinders of the 1980s: a Countach recording stands in for the flat-twelve (no clean recording exists).
        [CarStyle.FlatTwelve] = [new("flat12/low.wav", 3000f, false), new("flat12/mid.wav", 4750f, false), new("flat12/high.wav", 6650f, false)],
        // A 2010s 4.5-litre V8, with a quieter off-throttle loop for the overrun.
        [CarStyle.V8] = [new("v8/low.wav", 2700f, false), new("v8/mid.wav", 5800f, false), new("v8/high.wav", 8400f, false), new("v8/coast.wav", 6300f, true)],
    };

    private readonly List<(AudioStreamPlayer Player, Loop Loop)> _players = new();
    private AudioEffectLowPassFilter? _filter;
    private CarStyle? _loaded;

    public float Volume { get; set; } = 0.9f;

    public override void _Ready()
    {
        // Own bus with a low-pass filter: closed throttle muffles the exhaust note.
        var index = AudioServer.GetBusIndex(BusName);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, BusName);
            _filter = new AudioEffectLowPassFilter { CutoffHz = 4000f, Resonance = 0.4f };
            AudioServer.AddBusEffect(index, _filter);
        }
        else
        {
            _filter = AudioServer.GetBusEffect(index, 0) as AudioEffectLowPassFilter;
        }
    }

    /// <summary>Loads the loop set of a car, replacing the previous one.</summary>
    public void Load(CarStyle style)
    {
        if (_loaded == style) return;
        foreach (var (player, _) in _players) player.QueueFree();
        _players.Clear();
        _loaded = style;
        foreach (var loop in Sets[style])
        {
            var stream = GD.Load<AudioStreamWav>($"res://assets/audio/{loop.File}");
            if (stream is null)
            {
                GD.PrintErr($"engine loop missing: {loop.File}");
                continue;
            }
            // Mono 16-bit: two bytes per frame; the importer leaves the loop points unset.
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = stream.Data.Length / 2;
            var player = new AudioStreamPlayer { Stream = stream, Bus = BusName, VolumeDb = -80f, Autoplay = false };
            AddChild(player);
            player.Play();
            _players.Add((player, loop));
        }
    }

    /// <summary>Drives the loops from the smoothed engine state; <paramref name="muted"/> silences them (retro mode).</summary>
    public void Update(float rpm, float load, bool running, bool muted)
    {
        if (_players.Count == 0) return;
        if (_filter is not null) _filter.CutoffHz = 900f + 9000f * load;
        var gain = running && !muted ? Volume * (0.5f + 0.5f * load) : 0f;

        var power = _players.Where(p => !p.Loop.Coast).OrderBy(p => p.Loop.Rpm).ToList();
        var coastWeight = 0f;
        foreach (var (player, loop) in _players)
        {
            if (!loop.Coast) continue;
            // The overrun loop fades in off throttle, strongest near its own speed.
            var band = 1f - MathUtil.Clamp01(MathF.Abs(rpm - loop.Rpm) / 2600f);
            coastWeight = (1f - load) * band * (running ? 1f : 0f);
            Apply(player, loop, rpm, (muted ? 0f : Volume) * coastWeight * 0.9f);
        }

        for (var i = 0; i < power.Count; i++)
        {
            var (player, loop) = power[i];
            var weight = Weight(power, i, rpm);
            Apply(player, loop, rpm, gain * weight * (1f - 0.55f * coastWeight));
        }
    }

    /// <summary>Equal-power cross-fade between neighbouring loops on a logarithmic rpm scale.</summary>
    private static float Weight(List<(AudioStreamPlayer Player, Loop Loop)> loops, int i, float rpm)
    {
        var r = loops[i].Loop.Rpm;
        if (loops.Count == 1) return 1f;
        if (i == 0 && rpm <= r) return 1f;
        if (i == loops.Count - 1 && rpm >= r) return 1f;
        if (i > 0 && rpm < r)
        {
            var lower = loops[i - 1].Loop.Rpm;
            if (rpm <= lower) return 0f;
            var t = MathF.Log(rpm / lower) / MathF.Log(r / lower);
            return MathF.Sin(t * MathF.PI / 2f);
        }
        if (i < loops.Count - 1 && rpm >= r)
        {
            var upper = loops[i + 1].Loop.Rpm;
            if (rpm >= upper) return 0f;
            var t = MathF.Log(rpm / r) / MathF.Log(upper / r);
            return MathF.Cos(t * MathF.PI / 2f);
        }
        return 0f;
    }

    private static void Apply(AudioStreamPlayer player, Loop loop, float rpm, float linear)
    {
        player.PitchScale = MathUtil.Clamp(MathF.Max(rpm, 300f) / loop.Rpm, 0.25f, 4f);
        player.VolumeDb = linear <= 0.001f ? -80f : Mathf.LinearToDb(linear);
    }
}
