using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Game;

/// <summary>
/// The sounds of the world around the car, placed in it: every drone's engine on the drone and the train's
/// locomotive on the locomotive, each attenuated by its distance and shifted by the Doppler effect between it and
/// the listener (the camera), so that a drone coming the other way drops its note as it passes, and the train is
/// heard before it is seen and whistles for the crossing; and the cows, each mooing from where it stands, now and
/// then while the car is about and loudly when it is shoved (recordings of one cow, assets/audio/SOURCES.md). The
/// shift is computed here from the simulation's
/// velocities rather than left to the engine's tracking, which measures motion against the wall clock and so gives
/// nothing in a fixed-rate (recorded) run and the wrong amount whenever the frame rate drifts. From the driver's seat they come through the glass: a low-pass on their
/// bus closes and the level drops (<see cref="Interior"/>). 1989 mode mutes them; the cabinet had only the
/// synthesised clack of <see cref="EngineAudio"/>.
/// </summary>
internal sealed partial class WorldAudio : Node
{
    private const string BusName = "World";
    private AudioEffectLowPassFilter? _glass;
    private int _bus = -1;
    private readonly List<(AudioStreamPlayer3D Player, TrafficCar Car, float Tune)> _drones = new();
    private AudioStreamWav? _droneLoop;
    private AudioStreamWav? _steamLoop;
    private AudioStreamWav? _whistleClip;
    private AudioStreamPlayer3D? _steam;
    private AudioStreamPlayer3D? _whistle;
    private AudioStreamRandomizer? _moos;
    private readonly Dictionary<int, AudioStreamPlayer3D> _cows = new();
    private Train? _train;
    private int _blasts;
    private float _glassMix;   // 0 in the open, 1 from the driver's seat, smoothed
    private Vector3 _listener;
    private bool _listenerKnown;
    private readonly System.Random _random = new(5);

    public float Volume { get; set; } = 0.9f;
    /// <summary>Heard from the driver's seat: through the glass.</summary>
    public bool Interior { get; set; }
    /// <summary>1989 mode: the placed sounds are muted.</summary>
    public bool Retro { get; set; }

    public override void _Ready()
    {
        _bus = AudioServer.GetBusIndex(BusName);
        if (_bus < 0)
        {
            AudioServer.AddBus();
            _bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(_bus, BusName);
        }
        _glass = new AudioEffectLowPassFilter { CutoffHz = 20500f, Resonance = 0.4f };
        AudioServer.AddBusEffect(_bus, _glass);
        _droneLoop = AudioClips.Loop("traffic/drone.wav");
        _steamLoop = AudioClips.Loop("train/steam.wav");
        _whistleClip = AudioClips.OneShot("train/whistle.wav");
        _moos = AudioClips.Takes(1.07f, 2f, "cow/moo1.wav", "cow/moo2.wav", "cow/moo3.wav", "cow/moo4.wav", "cow/moo5.wav");
    }

    /// <summary>
    /// Puts a moo on a cow's node (the prop at <paramref name="prop"/>): a cow is heard over a field, full within ten
    /// metres of its head and still there at three hundred.
    /// </summary>
    public void AttachCow(Node3D cow, int prop)
    {
        if (_moos is null) return;
        var player = Placed(_moos, unitSize: 10f, maxDb: 6f, maxDistance: 320f, new Vector3(1.1f, 1.2f, 0f));
        cow.AddChild(player);
        _cows[prop] = player;
    }

    /// <summary>
    /// Puts a drone's engine on its view, behind the cabin where the period car carries it. The loop starts at a
    /// random point so that the drones never beat together.
    /// </summary>
    public void AttachDrone(Node3D view, TrafficCar car)
    {
        if (_droneLoop is null) return;
        var player = Placed(_droneLoop, unitSize: 6f, maxDb: 0f, maxDistance: 260f, new Vector3(0f, 0.2f, 1.2f));
        view.AddChild(player);
        player.Play((float)(_random.NextDouble() * _droneLoop.GetLength()));
        _drones.Add((player, car, 0.94f + 0.12f * (float)_random.NextDouble()));
    }

    /// <summary>
    /// Forgets the drones of the last credit: their views, and the players on them, are freed with the session. Kept
    /// (until 0.8.1), the next update touched a disposed player and threw every frame from then on.
    /// </summary>
    public void DetachDrones() => _drones.Clear();

    /// <summary>Puts the locomotive's running sound and its whistle on the locomotive's node.</summary>
    public void AttachTrain(Node3D locomotive, Train train)
    {
        _train = train;
        if (_steamLoop is not null)
        {
            _steam = Placed(_steamLoop, unitSize: 24f, maxDb: 3f, maxDistance: 1200f, new Vector3(0f, 3f, 0f));
            locomotive.AddChild(_steam);
        }
        if (_whistleClip is not null)
        {
            // A steam whistle carries for miles: full at a hundred metres, still there at three kilometres.
            _whistle = Placed(_whistleClip, unitSize: 110f, maxDb: 10f, maxDistance: 4000f, new Vector3(0f, 4.5f, 2f));
            locomotive.AddChild(_whistle);
        }
    }

    /// <summary>A source in the world: inverse-distance attenuation from <paramref name="unitSize"/> metres out; its Doppler shift is set in <see cref="Update"/>.</summary>
    private static AudioStreamPlayer3D Placed(AudioStream stream, float unitSize, float maxDb, float maxDistance, Vector3 position) => new()
    {
        Stream = stream,
        Bus = BusName,
        UnitSize = unitSize,
        MaxDb = maxDb,
        MaxDistance = maxDistance,
        AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
        Position = position,
    };

    /// <summary>Once a frame; <paramref name="listener"/> is where the camera is, from which its velocity is taken.</summary>
    public void Update(RaceSession session, Vector3 listener, float dt)
    {
        if (_bus < 0) return;
        var listenerVelocity = _listenerKnown && dt > 0f ? (listener - _listener) / dt : Vector3.Zero;
        // A cut (the camera switched, the car put back) is not motion.
        if (listenerVelocity.Length() > 120f) listenerVelocity = Vector3.Zero;
        _listener = listener;
        _listenerKnown = true;
        AudioServer.SetBusMute(_bus, Retro);
        _glassMix += ((Interior ? 1f : 0f) - _glassMix) * MathUtil.Clamp01(dt * 8f);
        // The glass takes the top off and a few decibels with it; the cut-off slides on a log scale so the move is even.
        if (_glass is not null) _glass.CutoffHz = 20500f * MathF.Pow(1500f / 20500f, _glassMix);
        AudioServer.SetBusVolumeDb(_bus, Mathf.LinearToDb(MathF.Max(Volume, 0.001f)) - 7f * _glassMix);

        // The drones and the train move only while the credit runs; on the title screen and in the replay they stand.
        var moving = session.Phase is SessionPhase.Driving or SessionPhase.StartEngine;
        foreach (var (player, car, tune) in _drones)
        {
            if (!IsInstanceValid(player)) continue;
            // The drones cruise in one gear: the note follows their speed a little, each car a touch off the others.
            var velocity = moving ? Convert.ToGodot(car.Forward * car.Speed) : Vector3.Zero;
            player.PitchScale = tune * (0.9f + 0.2f * MathUtil.Clamp01((car.Speed - 18f) / 10f)) * Doppler(player.GlobalPosition, velocity, listener, listenerVelocity);
            player.VolumeDb = moving ? -3f : -80f;
        }
        UpdateTrain(moving, listener, listenerVelocity);
        // The cows: a loud moo from one the car shoves, a quieter one now and then from one that is just grazing.
        if (session.Events.Contains(RaceEvent.CowHit) && _cows.TryGetValue(session.LastSoftHit, out var hit))
        {
            hit.VolumeDb = 2f;
            hit.Play();
        }
        if (session.Events.Contains(RaceEvent.CowMooed) && session.Herd.Mooed >= 0
            && _cows.TryGetValue(session.Herd.Cows[session.Herd.Mooed].Prop, out var grazing) && !grazing.Playing)
        {
            grazing.VolumeDb = -4f;
            grazing.Play();
        }
    }

    /// <summary>The Doppler factor of a source heard by a listener, both moving: the pitch rises as they close and falls as they part.</summary>
    private static float Doppler(Vector3 source, Vector3 sourceVelocity, Vector3 listener, Vector3 listenerVelocity)
    {
        const float speedOfSound = 343f;
        var line = source - listener;
        var distance = line.Length();
        if (distance < 1f) return 1f;
        var receding = (sourceVelocity - listenerVelocity).Dot(line / distance);
        return Mathf.Clamp(speedOfSound / (speedOfSound + receding), 0.5f, 2f);
    }

    private void UpdateTrain(bool moving, Vector3 listener, Vector3 listenerVelocity)
    {
        if (_train is null) return;
        var running = _train.Running && moving;
        var velocity = running ? Convert.ToGodot(_train.Direction * _train.Speed) : Vector3.Zero;
        if (_steam is not null) _steam.PitchScale = Doppler(_steam.GlobalPosition, velocity, listener, listenerVelocity);
        if (_whistle is not null) _whistle.PitchScale = Doppler(_whistle.GlobalPosition, velocity, listener, listenerVelocity);
        if (_steam is not null && running != _steam.Playing)
        {
            if (running) _steam.Play();
            else _steam.Stop();
        }
        if (!_train.Running)
        {
            _blasts = 0;
            return;
        }
        if (!moving || _whistle is null) return;
        // Two blasts for the crossing: one about nine seconds out, the other at five, as the train approaches it.
        var toCrossing = _train.SecondsToCrossing();
        if ((_blasts == 0 && toCrossing < 9f) || (_blasts == 1 && toCrossing < 5f))
        {
            _blasts++;
            if (toCrossing > 1f) _whistle.Play();
        }
    }
}
