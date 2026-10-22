using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Tyre sounds from recordings (CC0, see assets/audio/SOURCES.md): a cornering squeal loop, a harder howl
/// that joins it at big slip angles, a screech for wheelspin and locked brakes, and a gravel loop for the
/// wheels off the asphalt. Each loop is faded and pitched from the simulated slip of the four wheels; the
/// synthesised squeal of 1989 mode lives in <see cref="EngineAudio"/>.
/// </summary>
internal sealed partial class TyreAudio : Node
{
    private const string BusName = "Tyres";
    private AudioStreamPlayer _squeal = null!;
    private AudioStreamPlayer _howl = null!;
    private AudioStreamPlayer _skid = null!;
    private AudioStreamPlayer _gravel = null!;
    private float _lateral;
    private float _longitudinal;
    private float _grass;

    public float Volume { get; set; } = 0.9f;

    public override void _Ready()
    {
        if (AudioServer.GetBusIndex(BusName) < 0)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, BusName);
        }
        _squeal = Loop("tyres/squeal.wav");
        _howl = Loop("tyres/squeal_hard.wav");
        _skid = Loop("tyres/skid.wav");
        _gravel = Loop("tyres/gravel.wav");
    }

    private AudioStreamPlayer Loop(string file)
    {
        var path = $"res://assets/audio/{file}";
        var stream = ResourceLoader.Exists(path) ? GD.Load<AudioStreamWav>(path) : null;
        if (stream is null)
        {
            GD.PrintErr($"tyre loop missing: {file}");
        }
        else
        {
            // Mono 16-bit: two bytes per frame; the importer leaves the loop points unset.
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = stream.Data.Length / 2;
        }
        var player = new AudioStreamPlayer { Stream = stream, Bus = BusName, VolumeDb = -80f, Autoplay = false };
        AddChild(player);
        if (stream is not null) player.Play();
        return player;
    }

    /// <summary>Reads the four wheels and fades the loops; <paramref name="muted"/> silences them (1989 mode uses the synthesised squeal).</summary>
    public void Update(RaceSession session, float dt, bool muted)
    {
        var car = session.Car;
        var lateral = 0f;
        var longitudinal = 0f;
        var offRoad = 0f;
        foreach (var w in car.Wheels)
        {
            if (!w.InContact) continue;
            if (w.Surface == SurfaceType.Road)
            {
                // Slip angles beyond about four degrees and slip ratios beyond fifteen percent are audible.
                lateral = MathF.Max(lateral, MathUtil.Clamp01((MathF.Abs(w.SlipAngle) - 0.07f) / 0.22f));
                longitudinal = MathF.Max(longitudinal, MathUtil.Clamp01((MathF.Abs(w.SlipRatio) - 0.15f) / 0.6f));
            }
            else
            {
                offRoad = 1f;
            }
        }
        var speed = MathF.Abs(car.Speed);
        var speedFactor = MathUtil.Clamp01((speed - 2.5f) / 8f);
        lateral *= speedFactor;
        longitudinal *= speedFactor;

        // Fast attack, slower release: a squeal starts at once and trails off.
        Follow(ref _lateral, lateral, dt, 14f, 5f);
        Follow(ref _longitudinal, longitudinal, dt, 20f, 6f);
        Follow(ref _grass, offRoad * MathUtil.Clamp01(speed / 6f), dt, 6f, 4f);

        var gain = muted ? 0f : Volume;
        // The cornering loop, pitched up as the slide deepens; the howl joins at big angles.
        Set(_squeal, gain * _lateral * (1f - 0.5f * _longitudinal), 0.88f + 0.30f * _lateral);
        Set(_howl, gain * MathUtil.Clamp01((_lateral - 0.45f) / 0.55f) * 0.9f, 0.9f + 0.2f * _lateral);
        // Wheelspin or a locked wheel: the screech, its pitch following the speed of the rubber over the road.
        Set(_skid, gain * _longitudinal * 0.9f, 0.8f + 0.5f * MathUtil.Clamp01(speed / 40f));
        Set(_gravel, gain * _grass * (0.5f + 0.5f * MathUtil.Clamp01(speed / 30f)), 0.7f + 0.6f * MathUtil.Clamp01(speed / 30f));
    }

    private static void Follow(ref float value, float target, float dt, float attack, float release)
    {
        var rate = target > value ? attack : release;
        value += (target - value) * MathUtil.Clamp01(dt * rate);
    }

    private static void Set(AudioStreamPlayer player, float linear, float pitch)
    {
        player.PitchScale = pitch;
        player.VolumeDb = linear <= 0.002f ? -80f : Mathf.LinearToDb(linear);
    }
}
