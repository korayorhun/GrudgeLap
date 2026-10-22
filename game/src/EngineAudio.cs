using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Procedural sound: a flat-twelve style engine built from firing pulses, tyre squeal from filtered noise,
/// crash and bonus bleeps. Deliberately a little raw, in the spirit of the 1989 sample DAC; recorded
/// samples can replace each voice later without touching the rest of the game.
/// </summary>
internal sealed partial class EngineAudio : Node
{
    private const int MixRate = 22050;
    private AudioStreamGeneratorPlayback? _playback;
    private readonly System.Random _random = new(7);

    private double _phase;
    private float _rpm = 950f;
    private float _load;
    private float _squeal;
    private float _lowpass;
    private float _bp1, _bp2;
    private float _crash;
    private float _bleep;
    private float _bleepPhase;
    private float _rumble;
    private bool _running;
    private float _musicLevel;
    private float _moo, _mooPhase, _mooPitch;
    private float _clang, _clangPhase, _clangPhase2;
    private float _trainLevel, _trainClock;
    private double _musicTime;
    private float _musicPhaseLead, _musicPhaseBass;

    private SampledEngine _sampled = null!;

    public float Volume { get; set; } = 0.8f;
    /// <summary>Plays the title/replay tune: a small synth arpeggio, faded in and out.</summary>
    public bool MusicOn { get; set; }
    /// <summary>1989 mode: the synthesised pulse-train engine instead of the recordings.</summary>
    public bool Retro { get; set; }

    /// <summary>Selects the recordings of the chosen car.</summary>
    public void SetCar(CarStyle style) => _sampled.Load(style);

    // A-minor progression on four bars (Am, F, C, G) as MIDI notes: bass root and a four-note arpeggio each.
    private static readonly int[][] Chords =
    [
        [45, 57, 60, 64, 69],
        [41, 53, 57, 60, 65],
        [48, 60, 64, 67, 72],
        [43, 55, 59, 62, 67],
    ];
    private const float Bpm = 118f;

    public override void _Ready()
    {
        var generator = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.12f };
        var player = new AudioStreamPlayer { Stream = generator, VolumeDb = -6f, Autoplay = true };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _sampled = new SampledEngine();
        AddChild(_sampled);
    }

    public void Update(RaceSession session, float dt)
    {
        var car = session.Car;
        var engine = car.Drivetrain.Engine;
        _running = engine.Running;
        _rpm += (MathF.Max(engine.Rpm, 0f) - _rpm) * MathUtil.Clamp01(dt * 12f);
        var load = MathUtil.Clamp01(engine.LastTorque / 500f);
        _load += (load - _load) * MathUtil.Clamp01(dt * 8f);

        var squeal = 0f;
        foreach (var w in car.Wheels)
        {
            if (w.InContact && w.Surface == SurfaceType.Road) squeal = MathF.Max(squeal, w.SlipIntensity);
        }
        var speedFactor = MathUtil.Clamp01((MathF.Abs(car.Speed) - 3f) / 10f);
        _squeal += (squeal * speedFactor - _squeal) * MathUtil.Clamp01(dt * 10f);

        foreach (var e in session.Events)
        {
            switch (e)
            {
                case RaceEvent.Crashed: _crash = 1f; break;
                case RaceEvent.CheckpointPassed:
                case RaceEvent.LapCompleted: _bleep = 0.35f; break;
                case RaceEvent.EngineStarted: _rumble = 0.6f; break;
                case RaceEvent.CowHit: _moo = 1f; _mooPhase = 0f; _mooPitch = 0.9f + 0.2f * (float)_random.NextDouble(); break;
                case RaceEvent.SignHit: _clang = 1f; break;
            }
        }
        // The train is heard when it runs within a couple of hundred metres.
        var trainTarget = 0f;
        if (session.Track.Train is { Running: true } train)
        {
            var nearest = float.MaxValue;
            foreach (var c in train.Cars) nearest = MathF.Min(nearest, SN.Vector3.Distance(c, car.Position));
            trainTarget = MathUtil.Clamp01(1f - nearest / 260f);
        }
        _trainLevel += (trainTarget - _trainLevel) * MathUtil.Clamp01(dt * 3f);
        _sampled.Update(_rpm, _load, _running, muted: Retro);
        Fill();
    }

    private void Fill()
    {
        if (_playback is null) return;
        var frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        // Twelve cylinders fire six times per revolution; the fundamental is rpm / 60 * 6.
        var frequency = _running ? _rpm / 60.0 * 6.0 : 0.0;
        var step = frequency / MixRate;
        // The synthesised engine only plays in 1989 mode; otherwise the recordings carry the engine note.
        var engineGain = _running && Retro ? 0.22f + 0.28f * _load : 0f;
        var cutoff = 0.08f + 0.5f * _load; // one-pole low-pass opens with the throttle: the "wide open" growl
        var buffer = new Vector2[frames];

        for (var i = 0; i < frames; i++)
        {
            _phase += step;
            if (_phase >= 1.0) _phase -= 1.0;
            var p = (float)_phase;
            // Pulse train with a bit of second-harmonic rasp; soft-clipped so it never spikes.
            var pulse = MathF.Exp(-p * 9f) * 2f - 0.25f + 0.35f * MathF.Sin(p * MathF.Tau * 2f);
            _lowpass += (pulse - _lowpass) * cutoff;
            var engine = MathF.Tanh(_lowpass * 1.6f) * engineGain;

            var noise = (float)(_random.NextDouble() * 2.0 - 1.0);
            // Resonant band-pass around 1.6 kHz for tyre squeal.
            const float f = 0.35f, q = 0.12f;
            _bp1 += f * (noise - _bp1 - q * _bp2);
            _bp2 += f * _bp1;
            var squeal = _bp2 * _squeal * 0.5f;

            var crash = 0f;
            if (_crash > 0f)
            {
                crash = noise * _crash * 0.9f;
                _crash -= 1.4f / MixRate;
            }
            var rumble = 0f;
            if (_rumble > 0f)
            {
                rumble = noise * _rumble * 0.25f;
                _rumble -= 1.2f / MixRate;
            }
            var bleep = 0f;
            if (_bleep > 0f)
            {
                _bleepPhase += 880f / MixRate;
                bleep = MathF.Sin(_bleepPhase * MathF.Tau) * 0.25f * MathF.Min(1f, _bleep * 6f);
                _bleep -= 1f / MixRate;
            }

            var music = Music();
            var extras = Moo() + Clang(noise) + TrainClack(noise);
            var sample = MathF.Tanh((engine + squeal + crash + rumble + bleep + music + extras) * Volume);
            buffer[i] = new Vector2(sample, sample);
        }
        _playback.PushBuffer(buffer);
    }

    /// <summary>A cow: two formants on a pitch that sags, over about a second.</summary>
    private float Moo()
    {
        if (_moo <= 0f) return 0f;
        var t = 1f - _moo;
        var pitch = (140f - 50f * t) * _mooPitch;
        _mooPhase += pitch / MixRate;
        if (_mooPhase >= 1f) _mooPhase -= 1f;
        var p = _mooPhase;
        var env = MathF.Min(t * 8f, 1f) * MathF.Min(_moo * 3f, 1f);
        var tone = MathF.Sin(p * MathF.Tau) * 0.6f + MathF.Sin(p * MathF.Tau * 2f) * 0.35f + MathF.Sin(p * MathF.Tau * 3f) * 0.2f + MathF.Sin(p * MathF.Tau * 5f) * 0.1f;
        _moo -= 1f / (MixRate * 1.1f);
        return MathF.Tanh(tone * 1.4f) * 0.35f * env;
    }

    /// <summary>A road sign going over: a short bright ring from two detuned partials.</summary>
    private float Clang(float noise)
    {
        if (_clang <= 0f) return 0f;
        _clangPhase += 2350f / MixRate;
        _clangPhase2 += 3170f / MixRate;
        if (_clangPhase >= 1f) _clangPhase -= 1f;
        if (_clangPhase2 >= 1f) _clangPhase2 -= 1f;
        var ring = MathF.Sin(_clangPhase * MathF.Tau) * 0.6f + MathF.Sin(_clangPhase2 * MathF.Tau) * 0.4f + noise * 0.3f * _clang;
        var env = _clang * _clang;
        _clang -= 1f / (MixRate * 0.7f);
        return ring * 0.35f * env;
    }

    /// <summary>Wheels over rail joints: noise bursts twice a second, as loud as the train is near.</summary>
    private float TrainClack(float noise)
    {
        if (_trainLevel < 0.01f) return 0f;
        _trainClock += 1f / MixRate;
        var phase = _trainClock % 0.42f;
        var burst = MathF.Exp(-phase * 60f) + 0.5f * MathF.Exp(-MathF.Max(phase - 0.11f, 0f) * 60f) * (phase > 0.11f ? 1f : 0f);
        return noise * burst * 0.5f * _trainLevel + noise * 0.04f * _trainLevel;
    }

    /// <summary>One sample of the tune: a 16th-note arpeggio on a square wave over a sine bass, 118 bpm, four bars.</summary>
    private float Music()
    {
        _musicLevel += ((MusicOn ? 1f : 0f) - _musicLevel) * (2.5f / MixRate);
        if (_musicLevel < 0.002f) return 0f;
        var beat = _musicTime * Bpm / 60.0;          // quarter notes elapsed
        var bar = (int)(beat / 4.0) % Chords.Length;
        var sixteenth = (int)(beat * 4.0);
        var chord = Chords[bar];
        // Arpeggio pattern up and down the chord; the last step of every bar rests.
        int[] pattern = [1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 3, 4, 3, 2, 1, 0];
        var step = pattern[sixteenth % 16];
        var leadNote = step == 0 ? -1 : chord[step];
        var leadFreq = leadNote < 0 ? 0f : 440f * MathF.Pow(2f, (leadNote - 69) / 12f);
        var bassFreq = 440f * MathF.Pow(2f, (chord[0] - 69) / 12f);
        _musicPhaseLead += leadFreq / MixRate;
        _musicPhaseBass += bassFreq / MixRate;
        if (_musicPhaseLead >= 1f) _musicPhaseLead -= 1f;
        if (_musicPhaseBass >= 1f) _musicPhaseBass -= 1f;
        // Short percussive envelope per sixteenth for the lead; the bass holds for the bar.
        var within = (float)(beat * 4.0 - sixteenth);
        var leadEnv = leadFreq > 0f ? MathF.Exp(-within * 5f) : 0f;
        var lead = (_musicPhaseLead < 0.5f ? 1f : -1f) * 0.08f * leadEnv;
        var bass = MathF.Sin(_musicPhaseBass * MathF.Tau) * 0.10f;
        _musicTime += 1.0 / MixRate;
        return (lead + bass) * _musicLevel;
    }
}
