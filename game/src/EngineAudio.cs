using Godot;
using GrudgeLap.Audio;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The car's sound. Engine, tyres and road roar, and the V8's overrun pops come from recordings
/// (<see cref="SampledEngine"/>, <see cref="TyreAudio"/>, the pop one-shots scheduled here), the wind, the gears' whine
/// and the anti-lock pump are synthesised beside the tyres (<see cref="TyreAudio"/>, <see cref="GrudgeLap.Audio.BodyNoise"/>); this
/// node mixes the procedural layers on top: suspension thumps over bumps, the shifter's clunk, bonus
/// bleeps, the moo, the sign, the cabinet's chime, starter and jingles (<see cref="Jingle"/>), and the 1989-mode
/// voices (pulse-train engine, filtered-noise squeal, a noise-burst crash and the train's clack); otherwise the
/// crash is a recording and the train and the drones are placed in the world by <see cref="WorldAudio"/>. Like
/// the original it drives without music.
/// </summary>
internal sealed partial class EngineAudio : Node
{
    private const int MixRate = 22050;
    private AudioStreamGeneratorPlayback? _playback;
    private readonly System.Random _random = new(7);

    private double _phase;
    // The revs, the load and the limiter's cut as the voices follow them (EngineFollower: the load is the engine's throttle).
    private readonly EngineFollower _follower = new();
    // The 1989 synth's load: the throttle as the limiter's cut shuts it, followed as in 0.19 (see Update).
    private float _synthLoad;
    private float _squeal;
    private float _lowpass;
    private float _bp1, _bp2;
    private float _crash;
    private float _bleep;
    private float _bleepPhase;
    private bool _running;
    private float _moo, _mooPhase, _mooPitch;
    private float _clang, _clangPhase, _clangPhase2;
    private float _trainLevel, _trainClock;
    private float _chime, _chimePhase;
    private float _starter, _starterPhase, _starterLp;
    private float _clunk, _clunkPhase, _clunkLp;
    private int _lastGear;
    private Note[]? _song;
    private float _songFade = 1f;
    private float _songGain = 1f;
    private bool _songFading;
    // The instant replay's music: our own tune in the manner of the late-eighties arcade (tools/make-replay-music.py),
    // played from its recording; 1989 mode plays the same score on this node's synthesiser (ReplaySong). It fades out
    // in a third of a second when the replay ends or is skipped, as the synthesised one does.
    private AudioStreamPlayer? _replayMusic;
    private float _replayMusicFade;
    private double _songTime;
    private int _songNext;
    private readonly List<Voice> _voices = new();
    private SessionPhase _lastPhase = SessionPhase.SelectTransmission;
    private bool _replayPlayingBefore;

    private SampledEngine _sampled = null!;
    private TyreAudio _tyres = null!;
    private CarStyle _style = CarStyle.FlatTwelve;
    private EngineSpec _engineSpec = EngineSpec.FlatTwelve;
    // The corner of the low-pass the cabin hears the engine bay through (EngineVoicing.CabinCutoff), for the turbo.
    private float _cabinCutoff = 2200f;
    private readonly TurboNoise _turbo = new(MixRate);
    private TurboState _turboState;
    private float _turboDt;
    private float[] _turboBlock = [];
    private float _thump, _thumpPhase;
    private StructureAcoustics? _acoustics;
    private GrudgeLap.Physics.Track.TrackGeometry? _acousticsTrack;
    private float _rainLp, _rainLp2;
    private float _lastLoad;
    private readonly float[] _lastCompression = new float[4];
    private readonly List<float> _popsPending = new();   // seconds until each scheduled pop
    private AudioStreamPlayer? _pops;
    private AudioStreamPlayer? _crashTakes;
    private AudioStreamPlayer? _knockTakes;
    private static readonly string[] PopFiles = ["pop1.wav", "pop2.wav", "pop3.wav", "pop4.wav", "pop5.wav", "crackle.wav"];

    public float Volume { get; set; } = 0.8f;
    /// <summary>1989 mode: the synthesised pulse-train engine instead of the recordings.</summary>
    public bool Retro { get; set; }
    /// <summary>Heard from the driver's seat: the engine comes through the cabin's bulkhead.</summary>
    public bool Interior { get; set; }
    /// <summary>How hard it rains, 0 to 1 (0.12): a steady hiss of low-passed noise, half as loud through the glass.</summary>
    public float Rain { get; set; }
    /// <summary>The file name of the player's own replay tune when one plays (<see cref="CustomReplayMusic"/>), for the menu; null for the built-in tune.</summary>
    public string? CustomReplayMusicName { get; private set; }

    /// <summary>
    /// Makes a music file the instant replay's tune (0.15: a file dropped on the game's window): copied into the user
    /// folder as replay.ogg, .mp3 or .wav, the other two taken away so this one plays, and its own name written beside
    /// it for the menu. Returns that name, or null when the file is not ogg, mp3 or wav, cannot be copied or does not
    /// decode.
    /// </summary>
    public string? AdoptReplayMusic(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ogg" or ".mp3" or ".wav")) return null;
        var folder = ProjectSettings.GlobalizePath("user://music");
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            foreach (var old in new[] { ".ogg", ".mp3", ".wav" })
            {
                var file = System.IO.Path.Combine(folder, "replay" + old);
                if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
            }
            System.IO.File.Copy(path, System.IO.Path.Combine(folder, "replay" + extension));
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "replay-name.txt"), System.IO.Path.GetFileName(path));
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"replay music {path} could not be copied: {ex.Message}");
            return null;
        }
        CustomReplayMusicName = null;
        RefreshReplayMusic();
        return CustomReplayMusicName;
    }

    /// <summary>Looks in the user folder again (the menu opened): a tune dropped in while the game runs plays from the next replay.</summary>
    public void RefreshReplayMusic()
    {
        var custom = CustomReplayMusic();
        if (custom is null || _replayMusic is null || _replayMusic.Playing) return;
        _replayMusic.Stream = custom;
    }

    /// <summary>Selects the recordings of the chosen car's engine, voiced for its body, and its turbo if it has one.</summary>
    public void SetCar(VehicleSpec spec)
    {
        _style = spec.Style;
        _engineSpec = spec.Engine;
        _cabinCutoff = SampledEngine.VoicingOf(spec).CabinCutoff;
        _sampled.Load(spec);
        _tyres.SetCar(spec);
    }

    /// <summary>Any engine with a turbo's share of torque has its turbo heard (<see cref="TurboNoise"/>).</summary>
    private bool HasTurbo => _engineSpec.BoostShare > 0f;

    /// <summary>
    /// The cabinet's jingles, in its spirit: it drove in silence and sang only at the moments that mattered. A
    /// brass fanfare when a lap beats the record (the original's "qualified" moment), a driving tune under the
    /// instant replay (the original's most remembered piece of music) and an FM tune when the credit is over (its
    /// high-score screen). The notes are ours, not the original's: its music is Atari's. The timbres are the FM
    /// and pulse chips of the day.
    /// </summary>
    public enum Jingle
    {
        Fanfare,
        Replay,
        GameOver,
    }

    /// <summary>One note of a jingle: when it starts (seconds), how long it sounds, its MIDI pitch, its voice and level.</summary>
    private readonly record struct Note(float Start, float Length, int Midi, int Voice, float Level);

    /// <summary>A sounding note: its oscillator phases and age.</summary>
    private sealed class Voice
    {
        public Note Note;
        public double Phase;
        public double ModPhase;
        public float Age;
        public float Lowpass;
    }

    private const int Brass = 0, Lead = 1, Bass = 2, Hat = 3, Snare = 4, Pulse = 5, Kick = 6, Tom = 7, Cymbal = 8;

    private static Note N(float beat, float beats, int midi, int voice, float level, float bpm) =>
        new(beat * 60f / bpm, beats * 60f / bpm, midi, voice, level);

    /// <summary>Six seconds of fanfare in B flat: the call, its answer up the triad and the held chord, over a bass that marks the bars.</summary>
    private static readonly Note[] FanfareSong = BuildFanfare();

    private static Note[] BuildFanfare()
    {
        const float bpm = 132f;
        var notes = new List<Note>();
        // Bar 1: the call. Bar 2: up the triad. Bar 3-4: the chord held.
        int[] call = [65, 65, 65, 70];
        float[] callBeats = [0f, 0.5f, 1f, 1.5f];
        float[] callLength = [0.45f, 0.45f, 0.45f, 2.4f];
        for (var i = 0; i < call.Length; i++) notes.Add(N(callBeats[i], callLength[i], call[i], Brass, 0.55f, bpm));
        int[] answer = [74, 72, 70, 74, 77];
        float[] answerBeats = [4f, 4.5f, 5f, 5.5f, 6f];
        float[] answerLength = [0.45f, 0.45f, 0.45f, 0.45f, 2f];
        for (var i = 0; i < answer.Length; i++) notes.Add(N(answerBeats[i], answerLength[i], answer[i], Brass, 0.55f, bpm));
        foreach (var midi in new[] { 70, 74, 77, 82 }) notes.Add(N(8f, 4f, midi, Brass, 0.42f, bpm));
        foreach (var beat in new[] { 0f, 2f, 4f, 6f, 8f, 10f }) notes.Add(N(beat, 1.5f, 46, Bass, 0.5f, bpm));
        return [.. notes];
    }

    /// <summary>
    /// The instant replay's tune, the score of the recording that plays outside 1989 mode (tools/make-replay-music.py
    /// writes both, the notes into ReplayScore.cs): ten seconds in A mixolydian at 136 bpm with swung sixteenths, the
    /// band's intro hit and a tom fill, a four-bar hook on the pulse lead over slap bass, horn stabs and a clav comp
    /// (A, G to D/F#, A, then F and G as band hits) and the button, an A chord with a crash, on the sixth downbeat.
    /// </summary>
    private static readonly Note[] ReplaySong = BuildReplay();

    private static Note[] BuildReplay()
    {
        var notes = ReplayScore().Select(r => N(r.Beat, r.Beats, r.Midi, r.Voice, r.Level, ReplayBpm)).ToList();
        notes.Sort((a, b) => a.Start.CompareTo(b.Start));
        return [.. notes];
    }

    /// <summary>Fourteen seconds for the end of a credit: an FM lead over a walking bass in A minor, hats on the eighths and a snare on two and four.</summary>
    private static readonly Note[] GameOverSong = BuildGameOver();

    private static Note[] BuildGameOver()
    {
        const float bpm = 124f;
        var notes = new List<Note>();
        // Eight bars of melody (beat, length, pitch), then the last phrase resolves on the tonic.
        (float Beat, float Length, int Midi)[] melody =
        [
            (0f, 0.5f, 69), (0.5f, 0.5f, 72), (1f, 1f, 76), (2f, 0.5f, 74), (2.5f, 0.5f, 72), (3f, 1f, 74),
            (4f, 0.5f, 72), (4.5f, 0.5f, 69), (5f, 1f, 67), (6f, 2f, 69),
            (8f, 0.5f, 69), (8.5f, 0.5f, 72), (9f, 1f, 76), (10f, 0.5f, 77), (10.5f, 0.5f, 76), (11f, 1f, 74),
            (12f, 0.5f, 72), (12.5f, 0.5f, 74), (13f, 0.5f, 72), (13.5f, 0.5f, 71), (14f, 2f, 69),
            (16f, 0.5f, 76), (16.5f, 0.5f, 77), (17f, 1f, 79), (18f, 0.5f, 77), (18.5f, 0.5f, 76), (19f, 1f, 74),
            (20f, 0.5f, 72), (20.5f, 0.5f, 74), (21f, 1f, 76), (22f, 2f, 71),
            (24f, 0.5f, 72), (24.5f, 0.5f, 74), (25f, 1f, 76), (26f, 0.5f, 74), (26.5f, 0.5f, 72), (27f, 1f, 71),
            (28f, 1f, 72), (29f, 1f, 71), (30f, 2f, 69),
        ];
        foreach (var (beat, length, midi) in melody) notes.Add(N(beat, length, midi, Lead, 0.5f, bpm));
        // Bass: root and fifth of Am, F, C, G, twice; the last bar lands on A.
        int[] roots = [45, 41, 48, 43, 45, 41, 48, 43];
        for (var bar = 0; bar < roots.Length; bar++)
        {
            var root = bar == 7 ? 43 : roots[bar];
            notes.Add(N(bar * 4f, 0.9f, root, Bass, 0.6f, bpm));
            notes.Add(N(bar * 4f + 1.5f, 0.4f, root + 7, Bass, 0.45f, bpm));
            notes.Add(N(bar * 4f + 2f, 0.9f, root, Bass, 0.6f, bpm));
            notes.Add(N(bar * 4f + 3f, 0.4f, root + 12, Bass, 0.4f, bpm));
        }
        notes.Add(N(32f, 2f, 45, Bass, 0.6f, bpm));
        for (var eighth = 0; eighth < 64; eighth++) notes.Add(N(eighth * 0.5f, 0.1f, 0, Hat, eighth % 2 == 0 ? 0.18f : 0.12f, bpm));
        for (var bar = 0; bar < 8; bar++)
        {
            notes.Add(N(bar * 4f + 1f, 0.2f, 0, Snare, 0.35f, bpm));
            notes.Add(N(bar * 4f + 3f, 0.2f, 0, Snare, 0.35f, bpm));
        }
        return [.. notes];
    }

    /// <summary>Starts a jingle from its first note, cutting one still playing.</summary>
    public void PlayJingle(Jingle jingle)
    {
        _song = jingle switch { Jingle.Fanfare => FanfareSong, Jingle.Replay => ReplaySong, _ => GameOverSong };
        // The replay's tune plays alone (the engine is silent then): 9 dB up, a little under a car being driven.
        _songGain = jingle == Jingle.Replay ? 2.8f : 1f;
        _songFade = 1f;
        _songFading = false;
        if (jingle == Jingle.Replay && !Retro && _replayMusic is not null)
        {
            // The recording instead of the notes.
            _song = null;
            _replayMusicFade = 0f;
            _replayMusic.VolumeDb = ReplayMusicDb;
            _replayMusic.Play();
            return;
        }
        _songTime = 0.0;
        _songNext = 0;
        _voices.Clear();
    }

    /// <summary>The replay music's level: it plays alone (the engine is silent in the replay), a little under a car being driven.</summary>
    private const float ReplayMusicDb = -5f;

    /// <summary>The cabinet's chime for a choice made on the title screen.</summary>
    public void Chime()
    {
        _chime = 1f;
        _chimePhase = 0f;
    }

    public override void _Ready()
    {
        var generator = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.12f };
        var player = new AudioStreamPlayer { Stream = generator, VolumeDb = -6f, Autoplay = true };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        // The car's bus (0.40) before the engine's and the tyres' players: the tyres' bus sends to it, and a bus sends
        // only to one made before it.
        StructureAcoustics.EnsureBus();
        _sampled = new SampledEngine();
        AddChild(_sampled);
        _tyres = new TyreAudio();
        AddChild(_tyres);
        _pops = PopPlayer();
        _crashTakes = CrashPlayer();
        _knockTakes = CrashPlayer();
        // The instant replay's music: a recording of the player's own from the user folder when there is one (0.11:
        // user://music/replay.ogg, .mp3 or .wav, so the tune of a game you remember can play in your copy without
        // being shipped with this one), otherwise the game's own recording of its tune.
        var custom = CustomReplayMusic();
        if (custom is not null)
        {
            _replayMusic = new AudioStreamPlayer { Stream = custom, VolumeDb = ReplayMusicDb };
            AddChild(_replayMusic);
        }
        else if (ResourceLoader.Exists("res://assets/audio/music/replay.ogg"))
        {
            _replayMusic = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/audio/music/replay.ogg"), VolumeDb = ReplayMusicDb };
            AddChild(_replayMusic);
        }
        // The engine, tyres, pops and these layers add up on the master bus: a limiter there instead of the output's clipping.
        if (AudioServer.GetBusEffectCount(0) == 0) AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.5f });
    }

    /// <summary>
    /// A replay tune the player put in the user folder (<c>user://music/replay.ogg</c>, <c>.mp3</c> or <c>.wav</c>; the
    /// folder is made on the first run so it is easy to find), decoded from the file, or null when there is none or
    /// it does not decode. Only this copy of the game plays it: nothing in the folder is part of the game.
    /// </summary>
    private AudioStream? CustomReplayMusic()
    {
        const string folder = "user://music";
        DirAccess.MakeDirRecursiveAbsolute(folder);
        var candidates = new (string Name, System.Func<string, AudioStream?> Load)[]
        {
            ("replay.ogg", path => AudioStreamOggVorbis.LoadFromFile(path)),
            ("replay.mp3", path => AudioStreamMP3.LoadFromFile(path)),
            ("replay.wav", path => AudioStreamWav.LoadFromFile(path)),
        };
        foreach (var (name, load) in candidates)
        {
            var path = $"{folder}/{name}";
            if (!Godot.FileAccess.FileExists(path)) continue;
            try
            {
                var stream = load(path);
                if (stream is not null)
                {
                    GD.Print($"replay music: {ProjectSettings.GlobalizePath(path)}");
                    // The name the file had when it was dropped on the window (AdoptReplayMusic), else its own.
                    var label = $"{folder}/replay-name.txt";
                    var original = Godot.FileAccess.FileExists(label) ? Godot.FileAccess.GetFileAsString(label).Trim() : "";
                    CustomReplayMusicName = original.Length > 0 ? original : name;
                    return stream;
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"replay music {path} did not load: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// The V8's overrun pops: one-shots cut from a CC0 recording (assets/audio/SOURCES.md), drawn at random with
    /// a little spread of pitch and level so that a burst never repeats itself; up to four ring at once.
    /// </summary>
    private AudioStreamPlayer? PopPlayer()
    {
        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitch = 1.12f,
            RandomVolumeOffsetDb = 3f,
        };
        foreach (var file in PopFiles)
        {
            var path = $"res://assets/audio/v8/{file}";
            if (ResourceLoader.Exists(path)) randomizer.AddStream(-1, GD.Load<AudioStreamWav>(path));
            else GD.PrintErr($"exhaust pop missing: {file}");
        }
        if (randomizer.StreamsCount == 0) return null;
        var player = new AudioStreamPlayer { Stream = randomizer, MaxPolyphony = 4, VolumeDb = -8f };
        AddChild(player);
        return player;
    }

    /// <summary>The crash: three recordings of metal and glass (assets/audio/SOURCES.md), one drawn at random per crash.</summary>
    private AudioStreamPlayer? CrashPlayer()
    {
        var takes = AudioClips.Takes(1.08f, 2f, "crash/crash1.wav", "crash/crash2.wav", "crash/crash3.wav");
        if (takes is null) return null;
        var player = new AudioStreamPlayer { Stream = takes, MaxPolyphony = 2, VolumeDb = -1f };
        AddChild(player);
        return player;
    }

    /// <summary>
    /// A knock the car survived: the crash recordings cut short and lighter (quieter and a little higher for a light
    /// one), with the thump under them; a cow is only the thump, a landing the thump and a scrape of the sump.
    /// </summary>
    private void Knock(KnockInfo knock)
    {
        var hard = MathUtil.Clamp01(knock.Speed / 13f);
        _thump = MathF.Max(_thump, 0.35f + 0.65f * hard);
        if (Retro || _knockTakes is null || knock.Kind == KnockKind.Cow) return;
        _knockTakes.VolumeDb = Mathf.Lerp(-18f, -3f, hard);
        _knockTakes.PitchScale = Mathf.Lerp(1.25f, 1.0f, hard);
        _knockTakes.Play();
    }

    /// <summary>The crash heard again when the instant replay reaches the blow (<see cref="CrashScene"/>).</summary>
    public void ReplayCrash()
    {
        if (Retro || _crashTakes is null) _crash = 1f;
        else _crashTakes.Play();
        _thump = 1f;
    }

    public void Update(RaceSession session, float dt)
    {
        var car = session.Car;
        // The echo under a deck and the joints at its ends (0.40); none in 1989 mode, nor in the replay.
        if (!ReferenceEquals(_acousticsTrack, session.Track))
        {
            _acoustics = new StructureAcoustics(session.Track);
            _acousticsTrack = session.Track;
        }
        var joint = _acoustics!.Update(session, dt, silent: Retro || session.Phase == SessionPhase.Replay);
        if (joint > 0f) _thump = MathF.Max(_thump, joint);
        var engine = car.Drivetrain.Engine;
        // The instant replay has no engine: the car stands wrecked while the crash is shown (the simulation holds
        // its last revs through the replay, which played on as a frozen note).
        _running = engine.Running && session.Phase != SessionPhase.Replay;
        // The engine's load is its throttle (0.19), held through the limiter's cut, which the voice plays pulse by pulse.
        var load = engine.Throttle;
        var cut = engine.LimiterCut;
        _follower.Update(engine.Rpm, load, cut, dt);
        // The 1989 synth has no pulses to cut: it takes the throttle unheld, so it dips on the limiter as it did in 0.19,
        // where the voice's held load would play it as a steady tone.
        _synthLoad += (load - _synthLoad) * MathUtil.Clamp01(dt * EngineFollower.LoadRate);

        var squeal = 0f;
        foreach (var w in car.Wheels)
        {
            if (w.InContact && w.Surface == SurfaceType.Road) squeal = MathF.Max(squeal, w.SlipIntensity);
        }
        if (session.Phase == SessionPhase.Replay) squeal = 0f;   // the wreck slides in silence but for the fire (0.35)
        var speedFactor = MathUtil.Clamp01((MathF.Abs(car.Speed) - 3f) / 10f);
        _squeal += (squeal * speedFactor - _squeal) * MathUtil.Clamp01(dt * 10f);

        foreach (var e in session.Events)
        {
            switch (e)
            {
                case RaceEvent.Crashed:
                    // The recording, with the weight of a thump under it; 1989 mode keeps the cabinet's burst of noise.
                    if (Retro || _crashTakes is null) _crash = 1f;
                    else _crashTakes.Play();
                    _thump = 1f;
                    break;
                case RaceEvent.CheckpointPassed:
                case RaceEvent.LapCompleted: _bleep = 0.35f; break;
                case RaceEvent.EngineStarted: _starter = 1f; _starterPhase = 0f; break;
                case RaceEvent.NewBestLap: PlayJingle(Jingle.Fanfare); break;
                // The recorded moos come from the cows themselves (WorldAudio); 1989 mode keeps the cabinet's synthesised one.
                case RaceEvent.CowHit when Retro: _moo = 1f; _mooPhase = 0f; _mooPitch = 0.9f + 0.2f * (float)_random.NextDouble(); break;
                case RaceEvent.SignHit: _clang = 1f; break;
                case RaceEvent.Knocked: Knock(session.LastKnock); break;
            }
        }
        // A turbo: its whistle follows the shaft, and shutting the throttle on boost vents it or makes it surge, or on the
        // diesel, which has no throttle to shut, lets it spool down (TurboNoise).
        _turboState = new TurboState(
            Boost: _running ? engine.Boost : 0f,
            Throttle: _running ? _follower.Load : 0f,
            // The follower holds its last revs through the replay; the turbo runs down with the engine instead.
            RevShare: _running ? _follower.Rpm / _engineSpec.RedlineRpm : 0f,
            BoostShare: _engineSpec.BoostShare,
            Lift: _engineSpec.LiftOff switch
            {
                TurboLiftOff.Surges => TurboLift.Surge,
                TurboLiftOff.SpoolsDown => TurboLift.SpoolDown,
                _ => TurboLift.Vent,
            },
            Interior: Interior,
            CabinCutoff: _cabinCutoff);
        // Accumulated until the block is rendered: a frame whose generator was full renders nothing and passes its time on.
        _turboDt += dt;
        // The credit's end: the tune of the original's high-score screen, once.
        if (session.Phase == SessionPhase.Finished && _lastPhase != SessionPhase.Finished) PlayJingle(Jingle.GameOver);
        // The instant replay has its tune; skipped or over, it fades out in a third of a second.
        // The replay's tune starts with the playback, after the crash's own moment (0.35), and in the replay only the tune
        // plays: the crash's thump sounds once, as it happens, and not again at the replayed blow.
        var playing = session.ReplayPlaying;
        if (playing && !_replayPlayingBefore) PlayJingle(Jingle.Replay);
        else if (!playing && _replayPlayingBefore && ReferenceEquals(_song, ReplaySong)) _songFading = true;
        if (!playing && _replayPlayingBefore && _replayMusic is { Playing: true }) _replayMusicFade = 1f;
        _replayPlayingBefore = playing;
        if (_replayMusicFade > 0f && _replayMusic is not null)
        {
            _replayMusicFade = MathF.Max(_replayMusicFade - dt * 3f, 0f);
            _replayMusic.VolumeDb = ReplayMusicDb + Mathf.LinearToDb(MathF.Max(_replayMusicFade, 0.001f));
            if (_replayMusicFade <= 0f) _replayMusic.Stop();
        }
        _lastPhase = session.Phase;
        // The shifter: a clunk on every change of gear while the car is driven (the automatic's too).
        var gear = car.Drivetrain.Gear;
        if (gear != _lastGear && session.Phase == SessionPhase.Driving && _running)
        {
            _clunk = 1f;
            _clunkPhase = 0f;
        }
        _lastGear = gear;
        // The train's clack of 1989 mode is heard when it runs within a couple of hundred metres.
        var trainTarget = 0f;
        if (session.Track.Train is { Running: true } train)
        {
            var nearest = float.MaxValue;
            foreach (var c in train.Cars) nearest = MathF.Min(nearest, SN.Vector3.Distance(c, car.Position));
            trainTarget = MathUtil.Clamp01(1f - nearest / 260f);
        }
        _trainLevel += (trainTarget - _trainLevel) * MathUtil.Clamp01(dt * 3f);

        // The wind is TyreAudio's (BodyNoise), calibrated against the tyres' roar; 1989 mode has none, as the cabinet had none.
        // A sudden change of suspension travel is a bump: a low thump, louder for a harder hit.
        var jolt = 0f;
        for (var i = 0; i < 4 && i < car.Wheels.Length; i++)
        {
            var c = car.Wheels[i].InContact ? car.Wheels[i].Compression : 0f;
            if (dt > 0f) jolt = MathF.Max(jolt, MathF.Abs(c - _lastCompression[i]) / dt);
            _lastCompression[i] = c;
        }
        if (jolt > 0.8f) _thump = MathF.Max(_thump, MathUtil.Clamp01((jolt - 0.8f) / 2.5f));
        // Sport exhaust: lifting off at high revs sets off two to four pops over the next half second (the driver lifting,
        // not the limiter's cut, which shuts the throttle for a moment too). The 458's flat-plane V8 crackles on the
        // overrun as the 570S's sports exhaust does (cars13).
        if (_style is CarStyle.V8 or CarStyle.FlatPlaneV8 && _running && !cut && _lastLoad > 0.5f && load < 0.15f && _follower.Rpm > 4800f && _popsPending.Count == 0)
        {
            var count = 2 + _random.Next(3);
            var at = 0.04f;
            for (var i = 0; i < count; i++)
            {
                _popsPending.Add(at);
                at += 0.08f + 0.16f * (float)_random.NextDouble();
            }
        }
        for (var i = _popsPending.Count - 1; i >= 0; i--)
        {
            _popsPending[i] -= dt;
            if (_popsPending[i] > 0f) continue;
            _popsPending.RemoveAt(i);
            // 1989 mode has no exhaust pops, as the cabinet had none.
            if (!Retro) _pops?.Play();
        }
        if (!cut) _lastLoad = load;
        _sampled.Interior = Interior;
        _sampled.Update(_follower.Rpm, _follower.Load, _follower.Cut, _running, muted: Retro);
        _tyres.Interior = Interior;
        _tyres.Update(session, dt, muted: Retro, running: _running);
        Fill();
    }

    private void Fill()
    {
        if (_playback is null) return;
        var frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        // Twelve cylinders fire six times per revolution; the fundamental is rpm / 60 * 6.
        var frequency = _running ? _follower.Rpm / 60.0 * 6.0 : 0.0;
        var step = frequency / MixRate;
        // The synthesised engine only plays in 1989 mode; otherwise the recordings carry the engine note.
        var load = _synthLoad;
        var engineGain = _running && Retro ? 0.22f + 0.28f * load : 0f;
        var cutoff = 0.08f + 0.5f * load; // one-pole low-pass opens with the throttle: the "wide open" growl
        var buffer = new Vector2[frames];
        // The turbo for the whole block at once; silent in 1989 mode, which had no turbo.
        var turbo = HasTurbo && !Retro;
        if (turbo)
        {
            if (_turboBlock.Length < frames) _turboBlock = new float[frames];
            System.Array.Clear(_turboBlock, 0, frames);
            _turbo.Render(_turboBlock.AsSpan(0, frames), _turboState, _turboDt);
            _turboDt = 0f;
        }

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
            // The filtered-noise squeal belongs to 1989 mode; otherwise the recorded tyres carry it.
            var squeal = Retro ? _bp2 * _squeal * 0.5f : 0f;

            var crash = 0f;
            if (_crash > 0f)
            {
                crash = noise * _crash * 0.9f;
                _crash -= 1.4f / MixRate;
            }
            var bleep = 0f;
            if (_bleep > 0f)
            {
                _bleepPhase += 880f / MixRate;
                bleep = MathF.Sin(_bleepPhase * MathF.Tau) * 0.25f * MathF.Min(1f, _bleep * 6f);
                _bleep -= 1f / MixRate;
            }

            var music = Song() + ChimeSample() + Starter(noise) + (turbo ? _turboBlock[i] : 0f);
            var extras = Moo() + Clang(noise) + Clunk(noise) + (Retro ? TrainClack(noise) : Road(noise) + RainHiss(noise));
            var sample = MathF.Tanh((engine + squeal + crash + bleep + music + extras) * Volume);
            buffer[i] = new Vector2(sample, sample);
        }
        _playback.PushBuffer(buffer);
    }

    /// <summary>The rain on the car and the road: noise through two one-pole low-passes, a steady hiss.</summary>
    private float RainHiss(float noise)
    {
        if (Rain <= 0f) return 0f;
        _rainLp += (noise - _rainLp) * 0.3f;
        _rainLp2 += (_rainLp - _rainLp2) * 0.3f;
        return _rainLp2 * 1.8f * Rain * (Interior ? 0.07f : 0.14f);
    }

    /// <summary>The bump thump: a 48 Hz burst with a little noise.</summary>
    private float Road(float noise)
    {
        var thump = 0f;
        if (_thump > 0f)
        {
            _thumpPhase += 48f / MixRate;
            if (_thumpPhase >= 1f) _thumpPhase -= 1f;
            thump = MathF.Sin(_thumpPhase * MathF.Tau) * _thump * 0.55f + noise * _thump * 0.08f;
            _thump -= 1f / (MixRate * 0.16f);
        }
        return thump;
    }

    /// <summary>The shifter going into a gear: a low thud of ninety milliseconds with the click of the linkage on top.</summary>
    private float Clunk(float noise)
    {
        if (_clunk <= 0f) return 0f;
        var t = 1f - _clunk;
        _clunkPhase += 110f / MixRate;
        if (_clunkPhase >= 1f) _clunkPhase -= 1f;
        _clunkLp += (noise - _clunkLp) * 0.35f;
        var thud = MathF.Sin(_clunkPhase * MathF.Tau) * MathF.Exp(-t * 28f);
        var click = _clunkLp * MathF.Exp(-t * 90f) * 0.8f;
        _clunk -= 1f / (MixRate * 0.09f);
        return (thud + click) * 0.28f;
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

    /// <summary>One sample of the jingle playing, if any: notes start on their beat and sound through their voice's envelope.</summary>
    private float Song()
    {
        if (_song is null) return 0f;
        if (_songFading)
        {
            _songFade -= 3f / MixRate;
            if (_songFade <= 0f)
            {
                _song = null;
                _voices.Clear();
                return 0f;
            }
        }
        _songTime += 1.0 / MixRate;
        while (_songNext < _song.Length && _song[_songNext].Start <= _songTime)
        {
            _voices.Add(new Voice { Note = _song[_songNext] });
            _songNext++;
        }
        var sum = 0f;
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            var v = _voices[i];
            var note = v.Note;
            v.Age += 1f / MixRate;
            if (v.Age > note.Length + 0.4f)
            {
                _voices.RemoveAt(i);
                continue;
            }
            var frequency = 440f * MathF.Pow(2f, (note.Midi - 69) / 12f);
            var release = v.Age > note.Length ? MathF.Exp(-(v.Age - note.Length) * 14f) : 1f;
            float value;
            switch (note.Voice)
            {
                case Brass:
                {
                    // A sawtooth through a low-pass that opens with a quick attack: the brass patch of the FM chips.
                    v.Phase += frequency / MixRate;
                    if (v.Phase >= 1.0) v.Phase -= 1.0;
                    var saw = (float)(v.Phase * 2.0 - 1.0);
                    var open = MathF.Min(v.Age / 0.05f, 1f) * (0.55f + 0.45f * MathF.Exp(-v.Age * 2.5f));
                    v.Lowpass += (saw - v.Lowpass) * (0.12f + 0.55f * open);
                    value = v.Lowpass * MathF.Min(v.Age / 0.02f, 1f) * release;
                    break;
                }
                case Lead:
                {
                    // Two-operator FM, modulator at three times the pitch, its index falling away: a bright electric piano.
                    v.ModPhase += frequency * 3f / MixRate;
                    v.Phase += frequency / MixRate;
                    var index = 2.2f * MathF.Exp(-v.Age * 5f) + 0.2f;
                    value = MathF.Sin((float)(v.Phase * MathF.Tau) + index * MathF.Sin((float)(v.ModPhase * MathF.Tau)));
                    value *= MathF.Min(v.Age / 0.006f, 1f) * MathF.Exp(-v.Age * 1.6f) * release;
                    break;
                }
                case Bass:
                {
                    v.ModPhase += frequency / MixRate;
                    v.Phase += frequency / MixRate;
                    var index = 1.4f * MathF.Exp(-v.Age * 7f) + 0.3f;
                    value = MathF.Sin((float)(v.Phase * MathF.Tau) + index * MathF.Sin((float)(v.ModPhase * MathF.Tau)));
                    value *= MathF.Min(v.Age / 0.005f, 1f) * MathF.Exp(-v.Age * 2.2f) * release;
                    break;
                }
                case Hat:
                    value = (float)(_random.NextDouble() * 2.0 - 1.0) * MathF.Exp(-v.Age * 60f);
                    break;
                case Pulse:
                {
                    // A quarter-duty pulse, softened a little, with a vibrato that comes in on the held notes: the
                    // arcade lead of the late eighties.
                    var vibrato = 1f + 0.005f * MathF.Sin(v.Age * MathF.Tau * 5.5f) * MathUtil.Clamp01((v.Age - 0.12f) / 0.1f);
                    v.Phase += frequency * vibrato / MixRate;
                    if (v.Phase >= 1.0) v.Phase -= 1.0;
                    var pulse = v.Phase < 0.25 ? 1f : -0.33f;
                    v.Lowpass += (pulse - v.Lowpass) * 0.45f;
                    value = v.Lowpass * MathF.Min(v.Age / 0.004f, 1f) * (0.75f + 0.25f * MathF.Exp(-v.Age * 6f)) * release;
                    break;
                }
                case Kick:
                {
                    // A sine falling from 140 to 50 Hz in a few hundredths of a second.
                    v.Phase += (50f + 90f * MathF.Exp(-v.Age * 30f)) / MixRate;
                    value = MathF.Sin((float)(v.Phase * MathF.Tau)) * MathF.Exp(-v.Age * 9f);
                    break;
                }
                case Tom:
                {
                    // The drum machine's tom: a sine at the note's pitch that starts half as high again and drops. It dies
                    // as fast as the kick: at exp(-5 t) it was still at 8 % when the voice is let go (0.4 s past the note)
                    // and that step clicked.
                    v.Phase += frequency * (1f + 0.5f * MathF.Exp(-v.Age * 25f)) / MixRate;
                    value = MathF.Sin((float)(v.Phase * MathF.Tau)) * MathF.Exp(-v.Age * 9f);
                    break;
                }
                case Cymbal:
                {
                    // Differenced noise (its top end only), dying over the note's length: an open hat for a short
                    // note, a crash for a long one. Lowpass holds the last noise sample for the difference.
                    var noise = (float)(_random.NextDouble() * 2.0 - 1.0);
                    value = (noise - v.Lowpass) * 0.5f * MathF.Exp(-v.Age * 4.5f / MathF.Max(note.Length, 0.05f));
                    v.Lowpass = noise;
                    break;
                }
                default:
                {
                    v.Phase += 185f / MixRate;
                    value = (float)(_random.NextDouble() * 2.0 - 1.0) * MathF.Exp(-v.Age * 18f) + MathF.Sin((float)(v.Phase * MathF.Tau)) * MathF.Exp(-v.Age * 30f) * 0.8f;
                    break;
                }
            }
            sum += value * note.Level;
        }
        if (_songNext >= _song.Length && _voices.Count == 0) _song = null;
        return sum * 0.35f * _songGain * _songFade;
    }

    /// <summary>The title screen's chime: a bell of two partials that rings for half a second.</summary>
    private float ChimeSample()
    {
        if (_chime <= 0f) return 0f;
        _chimePhase += 1046.5f / MixRate;
        var t = 1f - _chime;
        var bell = MathF.Sin(_chimePhase * MathF.Tau) * 0.6f + MathF.Sin(_chimePhase * MathF.Tau * 2.76f) * 0.3f * MathF.Exp(-t * 9f);
        _chime -= 1f / (MixRate * 0.55f);
        return bell * 0.3f * MathF.Exp(-t * 5f);
    }

    /// <summary>
    /// The starter motor at the turn of the key: a chugging pulse of low-passed noise with a whine on it for most of a
    /// second, the engine catching under it as the voice comes in.
    /// </summary>
    private float Starter(float noise)
    {
        if (_starter <= 0f) return 0f;
        var t = 1f - _starter;
        _starterPhase += (11f + 4f * t) / MixRate;
        if (_starterPhase >= 1f) _starterPhase -= 1f;
        var chug = 0.35f + 0.65f * MathF.Pow(MathF.Max(MathF.Sin(_starterPhase * MathF.Tau), 0f), 2f);
        _starterLp += (noise - _starterLp) * 0.09f;
        var whine = MathF.Sin(_starterPhase * MathF.Tau * 18f) * 0.25f;
        _starter -= 1f / (MixRate * 0.85f);
        return (_starterLp * 1.8f + whine) * chug * 0.35f * MathF.Min(_starter * 4f, 1f);
    }
}
