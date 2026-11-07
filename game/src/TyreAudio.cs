using Godot;
using GrudgeLap.Audio;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Tyre sounds from recordings (CC0, see assets/audio/SOURCES.md) and the body's synthesised noises beside them: the
/// rolling roar of the tyres on the asphalt as heard in a cabin, a cornering squeal at the tyres' limit, the howl of a
/// tyre sliding sideways past it, the scrub of a locked or spinning wheel, gravel for the wheels off the asphalt; and,
/// rendered by <see cref="BodyNoise"/>, the wind, a wet road's spray, the gears' whine and the anti-lock pump. The levels
/// and rates come from <see cref="TyreMix"/>, which reads where each tyre is on its force curve (0.19). The synthesised
/// squeal of 1989 mode lives in <see cref="EngineAudio"/>.
/// </summary>
/// <remarks>
/// The loops and the synthesised noises play on the same bus at the same volume: the wind is calibrated against the
/// roar's recording to take over from it at about 110 km/h, which only holds if both reach the mix alike.
/// </remarks>
internal sealed partial class TyreAudio : Node
{
    private const string BusName = "Tyres";
    private const int MixRate = 22050;
    private const int Chunk = 256;
    private AudioStreamPlayer _road = null!;
    private AudioStreamPlayer _squeal = null!;
    private AudioStreamPlayer _howl = null!;
    private AudioStreamPlayer _skid = null!;
    private AudioStreamPlayer _gravel = null!;
    private AudioStreamGeneratorPlayback? _body;
    private readonly TyreMix _mix = new();
    private readonly BodyNoise _noise = new(MixRate);
    private readonly TyreContact[] _contacts = new TyreContact[4];
    private readonly float[] _block = new float[Chunk];
    private readonly Vector2[] _frames = new Vector2[Chunk];
    private float _peakTorque = 500f;

    public float Volume { get; set; } = 0.9f;
    /// <summary>Heard from the driver's seat: the wind at the pillars, the gears through the tunnel, the anti-lock pump through the pedal.</summary>
    public bool Interior { get; set; }

    public override void _Ready()
    {
        if (AudioServer.GetBusIndex(BusName) < 0)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, BusName);
        }
        // Into the car's bus (0.40) when it was made first: the tyres echo under a bridge with the engine.
        var car = AudioServer.GetBusIndex(StructureAcoustics.BusName);
        if (car >= 0 && car < AudioServer.GetBusIndex(BusName)) AudioServer.SetBusSend(AudioServer.GetBusIndex(BusName), StructureAcoustics.BusName);
        _road = Loop("road.wav");
        _squeal = Loop("squeal.wav");
        _howl = Loop("squeal_hard.wav");
        _skid = Loop("skid.wav");
        _gravel = Loop("gravel.wav");
        var player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.08f },
            Bus = BusName,
            Autoplay = true,
        };
        AddChild(player);
        player.Play();
        _body = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>The car's engine sets the scale of the torque its gears whine with.</summary>
    public void SetCar(VehicleSpec spec)
    {
        _peakTorque = 1f;
        foreach (var (_, torque) in spec.Engine.FullThrottleCurve) _peakTorque = MathF.Max(_peakTorque, torque);
    }

    private AudioStreamPlayer Loop(string file)
    {
        var stream = AudioClips.Loop($"tyres/{file}");
        var player = new AudioStreamPlayer { Stream = stream, Bus = BusName, VolumeDb = -80f, Autoplay = false };
        AddChild(player);
        if (stream is not null) player.Play();
        return player;
    }

    /// <summary>
    /// Reads the four wheels and sets the loops and the body's noises; <paramref name="muted"/> silences them (1989 mode
    /// uses the synthesised squeal), <paramref name="running"/> is whether the engine turns the gears.
    /// </summary>
    public void Update(RaceSession session, float dt, bool muted, bool running)
    {
        var car = session.Car;
        var spec = car.Spec;
        var staticLoad = spec.MassKg * MathUtil.Gravity / 4f;
        var count = Math.Min(car.Wheels.Length, _contacts.Length);
        for (var i = 0; i < count; i++)
        {
            var w = car.Wheels[i];
            var ground = !w.InContact ? TyreGround.Air : w.Surface switch
            {
                // The skid pad is asphalt too: up to 0.18 it was taken for gravel, and a slide on it crunched.
                SurfaceType.Road or SurfaceType.Pad => TyreGround.Asphalt,
                SurfaceType.None => TyreGround.Air,
                _ => TyreGround.Loose,
            };
            var tyre = w.IsFront ? spec.FrontTire : spec.RearTire;
            var toPeak = ground == TyreGround.Air ? 0f : TireModel.SlipToPeak(w.SlipRatio, w.SlipAngle, w.Load, w.Friction, tyre);
            _contacts[i] = TyreContact.Of(ground, toPeak, w.SlipRatio, w.LateralSpeed, w.GroundSpeed, w.Load / staticLoad, w.BrakePressure);
        }
        _mix.Update(_contacts.AsSpan(0, count), WeatherModel.Wetness(session.Weather), dt);

        var gain = muted ? 0f : Volume;
        Set(_road, gain * _mix.Roll.Level, _mix.Roll.Pitch);
        Set(_squeal, gain * _mix.Squeal.Level, _mix.Squeal.Pitch);
        Set(_howl, gain * _mix.Howl.Level * 0.9f, _mix.Howl.Pitch);
        Set(_skid, gain * _mix.Scrub.Level * 0.9f, _mix.Scrub.Pitch);
        Set(_gravel, gain * _mix.Gravel.Level, _mix.Gravel.Pitch);
        FillBody(car, muted, running);
    }

    /// <summary>Tops up the generator with the body's noises for the car's state.</summary>
    private void FillBody(VehicleSim car, bool muted, bool running)
    {
        if (_body is null) return;
        var spec = car.Spec;
        var drivetrain = car.Drivetrain;
        // The gearbox's output shaft turns with the driven wheels (spinning ones included) times the final drive.
        var first = spec.FrontWheelDrive ? 0 : 2;
        var wheelSpeed = 0f;
        for (var i = first; i < first + 2 && i < car.Wheels.Length; i++) wheelSpeed += MathF.Abs(car.Wheels[i].AngularVelocity) * 0.5f;
        var outputHz = wheelSpeed * spec.FinalDrive / MathF.Tau;
        // The teeth carry the engine's torque only with the clutch closed; on the overrun they are loaded the other way.
        var torque = running && drivetrain.ClutchLocked ? MathF.Abs(drivetrain.Engine.LastTorque) / _peakTorque : 0f;
        var state = new BodyState(
            Speed: muted ? 0f : MathF.Abs(car.Speed),
            Interior: Interior,
            Spray: muted ? 0f : _mix.Spray,
            Abs: muted ? 0f : _mix.Abs,
            AbsCycle: _mix.AbsCycle,
            GearRatio: muted ? 0f : spec.RatioForGear(drivetrain.Gear),
            OutputShaftHz: outputHz,
            Torque: torque);
        var gain = muted ? 0f : Volume;
        // Whole chunks only: the same two arrays every time, and what is left over waits for the next frame.
        while (_body.GetFramesAvailable() >= Chunk)
        {
            System.Array.Clear(_block);
            _noise.Render(_block, state);
            for (var i = 0; i < Chunk; i++)
            {
                var s = _block[i] * gain;
                _frames[i] = new Vector2(s, s);
            }
            _body.PushBuffer(_frames);
        }
    }

    private static void Set(AudioStreamPlayer player, float linear, float pitch)
    {
        player.PitchScale = pitch;
        player.VolumeDb = linear <= 0.002f ? -80f : Mathf.LinearToDb(linear);
    }
}
