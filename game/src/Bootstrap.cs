using System.Linq;
using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Root of the game: builds the world from the track definition, owns the race session and wires input,
/// camera, HUD, audio, traffic, the ghost of the best lap and the updater to it. Also understands a few
/// command-line switches used for automated checks: <c>--attract</c> (scripted driver), <c>--screenshot=&lt;file&gt;</c>
/// with <c>--at=&lt;seconds&gt;</c>, <c>--chase</c>, <c>--orbit=yaw,pitch,distance</c>, <c>--speed-route</c>,
/// <c>--retro</c>, <c>--no-traffic</c>, <c>--show-all</c>, <c>--invisible</c> (no window on screen, and no claim on the
/// one-copy-per-computer lock of <see cref="SingleInstance"/>), <c>--park</c> (the car waits at the start with its
/// engine off while the traffic and the train go by: checks of the placed sounds), <c>--time=&lt;seconds&gt;</c> (a credit
/// of that length), <c>--try-table</c> (the attract driver's credit is offered to the high-score table: checks of the
/// entry screen), <c>--quality=low|medium|high</c> (the graphics preset for this run), <c>--showroom[=&lt;car&gt;]</c>
/// (the showroom open from the start, on that car, through the title screen or with <c>--showroom-entry=menu</c> through
/// the Esc menu's CAR line; <c>--turn=&lt;degrees&gt;</c> the turntable held at that angle; <c>--showroom-paint=&lt;paint&gt;</c>
/// its car in that paint, as if chosen there; <c>--picker</c> the colour picker open), <c>--paint-book-test</c> (the
/// per-car paint settings written, read back and migrated in a file of their own, then quit with 0 when every check
/// holds) and <c>--quit-after=&lt;seconds&gt;</c>.
/// </summary>
public sealed partial class Bootstrap : Node3D
{
    private const string BestLapFile = "user://bestlap.bin";
    private const string SettingsFile = "user://settings.cfg";

    private RaceSession _session = null!;
    private SkidMarks? _skidMarks;
    private WheelDust? _wheelDust;
    private TrackGeometry _track = null!;
    private CarView _carView = null!;
    private CrashScene? _crash;
    private bool _impactShown;   // the crash's own moment (the fire, the cracked glass) is on, before the replay (0.35)
    /// <summary>--ram-speed=: the speed (m/s) the ramming run holds, 0 for flat out.</summary>
    private float _ramSpeed;
    /// <summary>--look=kind[:n[:distance[:bearing]]]: the camera on the n-th prop of a kind (House, Barn, Cow...), for inspection shots.</summary>
    private string? _look;
    private Vector2 _seatLook;        // automation: the cockpit view turned, yaw and pitch in degrees (--seat-look=)
    private readonly KnockSmoke _knockSmoke = new() { Name = "KnockSmoke" };
    private string? _ram;
    private SN.Vector3? _ramTarget;
    private CarView? _ghostView;
    private readonly List<CarView> _droneViews = new();
    private int _droneSerial;   // drones drawn so far, over the credits: the next one's car and colour (BuildDroneViews)
    private readonly Dictionary<int, Node3D> _propNodes = new();
    private readonly List<(Node3D Node, float Start, float Yaw, Vector3 Axis)> _tipping = new();
    private TrainView? _trainView;
    private CameraRig _cameraRig = null!;
    private Hud _hud = null!;
    private ScoreBoard _scores = null!;
    private bool _scoreOffered;   // the credit's end has been put to the high-score table
    private InstrumentCluster _instruments = null!;
    private Texture2D _instrumentPanel = null!;
    private RearMirror? _mirror;
    private EngineAudio _audio = null!;
    private WorldAudio _world = null!;
    private Thunderstorm _thunder = null!;   // the rain's lightning and thunder (0.63)
    private Updater _updater = null!;
    private readonly KeyboardDriver _keyboard = new();
    private readonly ScriptedDriver _scripted = new();
    private LapRecording? _savedBestLap;
    private bool _attract;
    private bool _park;
    private bool _tryTable;
    private float _creditSeconds = -1f;
    private float _newGameAt = -1f;   // automation: a second credit, as F5 starts one (--new-game-after)
    private float _menuAt = -1f;      // automation: the menu opened for a picture of it (--menu-after)
    private string? _dropFile;        // automation: a file dropped on the window two seconds in (--drop=)
    private bool _perfLog;            // automation: GPU and CPU time per frame, logged every five seconds (--perf-log)
    private ulong _startMark;         // --perf-log: the engine's clock at the last start-up mark (StartMark)
    private bool _firstFrameMarked;
    private bool _holdBrake;          // automation: the brake held down all the time, for pictures of the brake lamps (--hold-brake)
    private bool _openMenu;           // automation: the pause menu opened a second in, for pictures of it (--menu, 0.55)
    private bool _noPuddles;          // automation: no standing water under the tyres, for comparisons (--puddles=0, 0.56)
    private bool _dentTest;           // automation: a deep dent in the car's nose two seconds in, for pictures of it (--dent-test)
    private double _perfGpu, _perfCpu, _perfSince;
    private int _perfFrames;
    private string? _jingle;          // automation: a jingle played a second in, to check the synth (--jingle)
    private readonly HashSet<string> _soundFaults = new();
    private readonly PauseMenu _menu = new();
    private bool _menuClick;
    private Showroom? _showroom;        // the car choice on its turntable (0.25), open over the title screen or the menu
    private bool _showroomTake;         // the showroom's TAKE IT button was clicked
    private bool _showroomBack;         // and its BACK button
    private bool _collectAfterShowroom; // the showroom was closed last frame: its cars' resources are let go now (CloseShowroom)
    private HashSet<string>? _propMaterialsBeforeShowroom; // the prop materials made before it opened: those made since go with it
    private bool _titleCarClicked;      // the car's name on the title screen was clicked: the showroom
    private string? _showroomArg;       // automation: the showroom open from the start, on this car (--showroom[=car])
    private bool _showroomFromMenu;     // automation: reached through the Esc menu's CAR line (--showroom-entry=menu)
    private float _turn = float.NaN;    // automation: the turntable held at this angle, degrees (--turn=)
    private string? _showroomPaint;     // automation: the showroom's car in this paint (--showroom-paint=)
    private bool _showroomPicker;       // automation: the colour picker open (--picker)
    private bool _paintBookTest;        // automation: the paint settings' round trip, then quit (--paint-book-test)
    private bool _startChase;
    private bool _retro;
    private GraphicsQuality _quality = GraphicsQuality.High;
    private float _overlayUntil = -1f;   // a short notice (graphics preset) on the HUD until then
    private bool _invisible;
    private bool _automated;    // --invisible, --screenshot or --quit-after: never writes the player's paints, never phones home
    private bool _leaving;      // a second launch on its way out: nothing was built
    private bool _noGhost;
    private bool _noTraffic;
    private Weather? _weather;   // --weather=: a fixed weather for automated runs; otherwise the menu's choice
    private int _weatherChoice;  // the menu's weather: 0-3 a fixed Weather, 4 random for every credit; kept in the settings
    private int _lightChoice;    // the menu's hour (0.55): 0 the day, 1 the sunrise, 2 drawn for every credit, 3 the night (0.57); kept in the settings
    private WorldBuilder.Hour? _hourArg;   // automation: --light=day|sunrise|night
    private PaintBook _paints = null!;   // every car's paint (0.25: the showroom, the menu's PAINT line); kept in the settings
    private string? _paintArg;   // --paint=: the run's car in this paint, this run only
    private bool _repaintLofted; // a lofted car repainted during a crash's replay: built again in the new colour next credit
    private bool _noHud;        // pictures of the 3D scene alone, e.g. to compare it with another engine's
    private Vector3? _orbit;
    private bool _showAll;      // debug: draw the whole car from the driver's seat too
    private bool _saveBest;
    private string? _exportWorld;  // --export-world=<file.glb>: write the track world for another engine, then quit
    private int _carIndex;
    private string? _screenshotPath;
    private float _screenshotAt = 4f;
    /// <summary>--at=4,8,12: every time a picture is taken; one picture a time, named after the path with the time added (automation, 0.34).</summary>
    private readonly List<float> _screenshotAts = [];
    private int _screenshotsTaken;
    /// <summary>--pose-log: the car's place on the course twice a second and every event, printed, so a hidden run can be read like a lap chart (0.34).</summary>
    private bool _poseLog;
    /// <summary>--shake=0: the seat's road vibration off, to see what it does to the picture (0.36).</summary>
    private bool _shake = true;
    /// <summary>--fsr2=0.75: the 3D picture rendered at that share of the window and brought up with FSR 2 (0.36 experiment).</summary>
    private float _fsr2Scale;
    private float _poseLoggedAt = -1f;
    /// <summary>--camera=x,y,z,tx,ty,tz[,fov]: a fixed camera (simulation axes: x east, y up, z north) for pictures of a place as the car passes (0.34).</summary>
    private (Vector3 Eye, Vector3 Target, float Fov)? _fixedCamera;
    /// <summary>--hold=path:from:to:speed (m/s; repeatable): the scripted driver holds a speed over a stretch of a path, for stunts taken wrong on purpose (0.34).</summary>
    private readonly List<(PathKind Path, float From, float To, float Speed)> _holds = [];
    private float _quitAfter = -1f;
    // Automation (--audio-dump=<file.wav>, 0.40): the master bus recorded from the start and written at the quit, for
    // checks of the sound by measurement (the echo under a bridge, a joint's knock) on a machine that plays it silently.
    private string? _audioDump;
    private AudioEffectRecord? _recorder;
    private float _elapsed;
    private bool _screenshotTaken;
    private TransmissionMode _attractMode = TransmissionMode.ManualAutoClutch;
    private TransmissionMode _gearbox;  // what the title screen starts with: the last choice, automatic at first
    private bool _clicked;
    private bool _focused = true;
    private float _frameCapCheckAt;

    /// <summary>
    /// A start-up stage done (--perf-log, U3): its name, the engine's clock and the stage's own time, so a slow start can
    /// be read off the log (the owner's "the game opens very late", 22 October 2026).
    /// </summary>
    private void StartMark(string stage)
    {
        var now = Time.GetTicksMsec();
        GD.Print($"start: {stage} at {now} ms (+{now - _startMark} ms)");
        _startMark = now;
    }

    public override void _Ready()
    {
        _startMark = Time.GetTicksMsec();
        InputSetup.Register();
        GetWindow().FilesDropped += OnFilesDropped;
        _carIndex = Math.Clamp(LoadChoice("car", 0), 0, VehicleSpec.Catalogue.Length - 1);
        // A saved choice of a car taken out of the line-up (0.70, Lineup) moves on to the next one.
        if (!Lineup.IsDrivable(_carIndex)) _carIndex = Lineup.Step(_carIndex, 1);
        _gearbox = (TransmissionMode)Math.Clamp(LoadChoice("transmission", (int)TransmissionMode.Automatic), 0, 2);
        _quality = (GraphicsQuality)Math.Clamp(LoadChoice("quality", (int)GraphicsQuality.High), 0, 2);
        // The weather (0.15): clear by default; the menu chooses clear, overcast, wet road, rain or random, and keeps it.
        _weatherChoice = Math.Clamp(LoadChoice("weather", 0), 0, 4);
        _lightChoice = Math.Clamp(LoadChoice("light", 0), 0, 4);
        ParseArgs();   // may override the saved car with --car= and the paint with --paint=
        // The paints (0.21, per car since 0.25): every car in its own colour by default; the showroom and the menu
        // choose another, and keep it. An automated run reads them but never writes the player's file.
        _automated = _invisible || _screenshotPath is not null || _quitAfter >= 0f;
        _paints = PaintBook.Load(SettingsFile, keep: !_automated);
        if (_paintArg is not null)
        {
            if (PaintChoice.TryParse(_paintArg, out var paint)) _paints.Set(_carIndex, paint, keep: false);
            else GD.PrintErr($"--paint={_paintArg}: not a paint (a preset's name, or #RRGGBB with SOLID, METALLIC or PEARL)");
        }
        if (_invisible)
        {
            HideWindow();
        }
        else if (!SingleInstance.TryAcquire())
        {
            // Already running: hand over to that game and leave before the world is built. The hand-over comes
            // first, while this launch still has the foreground right it can pass on.
            _leaving = true;
            GD.Print("GrudgeLap is already running; bringing it to the front");
            SingleInstance.ActivateOther();
            HideWindow();
            GetTree().Quit();
            return;
        }
        if (_paintBookTest)
        {
            _leaving = true;
            GetTree().Quit(PaintBookTest() ? 0 : 1);
            return;
        }

        if (_perfLog) StartMark("settings, paints and arguments");
        // The car models on the loader thread while the world is built here (U3): the player's and the traffic's.
        CarView.RequestModels([VehicleSpec.Catalogue[_carIndex], .. TrafficCars]);
        _track = OriginalInspiredTrack.Build();
        if (_perfLog) StartMark("track");
        if (_ram == "test")
        {
            // The crash replay's test (--ram=test): a tree on the start straight, 80 m on, in the middle of the road.
            var spot = _track.Common.FrameAt(80f);
            _track.Props.Add(new TrackProp(PropKind.Tree, spot.Origin, 0f, new SN.Vector3(1.0f, 7f, 1.0f), "crash-test"));
        }
        // The cows before the world (U3, 0.31.0): their bodies take a few milliseconds each here, and their hides then
        // paint on the thread pool while the world is built; they join the tree with the other props, in order.
        var cowsAhead = new Dictionary<int, Node3D>();
        for (var i = 0; i < _track.Props.Count; i++)
        {
            if (_track.Props[i].Kind == PropKind.Cow) cowsAhead[i] = PropBuilder.Build(_track.Props[i], _track.Terrain);
        }
        if (_perfLog) StartMark("the cows' bodies");
        WorldBuilder.PerfLog = _perfLog;
        WorldBuilder.Build(this, _track);
        if (_perfLog) StartMark("world (terrain, roads, foliage, buildings)");
        AddChild(_knockSmoke);
        // Before the train and the drones: they hand their nodes to it for their sounds.
        _world = new WorldAudio();
        AddChild(_world);
        _thunder = new Thunderstorm();
        AddChild(_thunder);
        var propTimes = _perfLog ? new Dictionary<PropKind, (double Ms, int Count)>() : null;
        for (var i = 0; i < _track.Props.Count; i++)
        {
            var propWatch = propTimes is null ? null : System.Diagnostics.Stopwatch.StartNew();
            var node = cowsAhead.TryGetValue(i, out var cow) ? cow : PropBuilder.Build(_track.Props[i], _track.Terrain);
            if (node is CowRig rig) CowBuilder.FinishHide(rig);
            _propNodes[i] = node;
            AddChild(node);
            if (_track.Props[i].Kind == PropKind.Cow) _world.AttachCow(node, i);
            if (propTimes is not null)
            {
                var kind = _track.Props[i].Kind;
                var so = propTimes.GetValueOrDefault(kind);
                propTimes[kind] = (so.Ms + propWatch!.Elapsed.TotalMilliseconds, so.Count + 1);
            }
        }
        if (propTimes is not null)
        {
            foreach (var (kind, (ms, count)) in propTimes.OrderByDescending(e => e.Value.Ms)) GD.Print($"start:   props {kind}: {count} in {ms:0} ms");
        }
        if (_track.Train is { } train)
        {
            _trainView = TrainView.Create(train, _track.Terrain);
            AddChild(_trainView);
            AddChild(TrainView.BuildRailway(train, _track));
            _world.AttachTrain(_trainView.Locomotive, train);
        }
        // The wood's trunks near the roads are solid (0.35; until then only the hand-placed trees were, and a car off the road
        // drove through the forest). Added after the props are drawn, so the wood is drawn once, by FoliageBuilder.
        foreach (var trunk in FoliageBuilder.NearTrunks) _track.Props.Add(new TrackProp(PropKind.Tree, trunk, 0f, new SN.Vector3(0.7f, 9f, 0.7f), "forest"));
        if (_perfLog) StartMark("props, cows and the train");
        GetViewport().UseTaa = true;
        // A window resized (or made full screen) changes the render ceiling of ApplyGraphics.
        GetViewport().SizeChanged += () =>
        {
            if (!_retro && !_leaving) ApplyGraphics();
        };

        _carView = CarView.Create(VehicleSpec.Catalogue[_carIndex], PlayerPaint);
        AddChild(_carView);
        if (_perfLog) StartMark("the player's car");
        _carView.MarkPlayer();
        AddChild(WorldBuilder.BuildCarProbe());
        _cameraRig = new CameraRig { Mode = _orbit is not null ? CameraMode.Orbit : _startChase ? CameraMode.Chase : CameraMode.Cockpit };
        _cameraRig.Ground = p => _track.Terrain.Height(p.X, -p.Z);
        _cameraRig.Shake = _shake;
        if (_orbit is { } orbit) _cameraRig.Orbit = orbit;
        _cameraRig.SeatLook = _seatLook;
        AddChild(_cameraRig);
        _hud = new Hud { Gearbox = _gearbox, ShowFps = LoadChoice("fps", 0) != 0 };
        _scores = new ScoreBoard();
        _hud.Scores = _scores;
        _hud.Menu = _menu;
        var layer = new CanvasLayer { Visible = !_noHud };
        layer.AddChild(_hud);
        AddChild(layer);
        if (_perfLog) StartMark("camera, hud and scores");
        _audio = new EngineAudio();
        AddChild(_audio);
        if (_perfLog) StartMark("engine audio");
        _updater = new Updater();
        AddChild(_updater);
        SubViewport panel;
        (panel, _instruments) = InstrumentCluster.Create();
        // Metric by default since 0.14; U or the menu switches, and the choice is kept ("units": 1 km/h, 0 mph).
        _instruments.UseKmh = LoadChoice("units", 1) != 0;
        AddChild(panel);
        _instrumentPanel = panel.GetTexture();
        _hud.Instruments = _instrumentPanel;
        FitOutPlayerCar();
        if (_perfLog) StartMark("updater, instruments and the player's fit-out");

        _savedBestLap = LoadBestLap();
        NewSession();
        if (_perfLog) StartMark("session");
        if (_exportWorld is not null)
        {
            // After the session: the car stands at the start and the cockpit camera is placed.
            _leaving = true;
            WorldExport.Write(this, _exportWorld, _carView, GetViewport().GetCamera3D(), _track);
            GetTree().Quit();
            return;
        }
        GetWindow().Title = $"GrudgeLap {Updater.CurrentVersionText}";
        if (_retro) SetRetro(true);
        else ApplyGraphics();
        ApplyFrameCap();
        if (_perfLog) StartMark("graphics");
        if (_showroomArg is not null)
        {
            // The automation's showroom, reached as a player reaches it: through the menu's CAR line, or from the title.
            if (_showroomFromMenu)
            {
                ShowMenu(true);
                _menu.Select((int)PauseMenu.Item.Car);
            }
            OpenShowroom(_showroomArg == "" ? _carIndex : CarIndex(_showroomArg, _carIndex));
            if (_showroom is { } room)
            {
                if (_showroomPaint is not null && PaintChoice.TryParse(_showroomPaint, out var paint))
                {
                    if (!paint.IsCustom) room.ChooseSlot(paint.Preset);
                    else
                    {
                        // Mixed as a player mixes it: in the picker, then (without --picker) done.
                        room.OpenPicker();
                        room.Picker!.Set(paint);
                        room.ApplyPicker();
                        if (!_showroomPicker) room.ClosePicker(keep: true);
                    }
                }
                else if (_showroomPaint is not null) GD.PrintErr($"--showroom-paint={_showroomPaint}: not a paint");
                if (_showroomPicker) room.OpenPicker();
            }
        }
        // Automated runs never phone home; a player can also switch the check off in settings.cfg. Every automated run, not
        // only the scripted driver's and the screenshots': a hidden --showroom or --picker run without them (the
        // showroom's review) checked for updates and wrote its time into the settings.
        if (!_automated && !_attract && System.Environment.GetEnvironmentVariable("GRUDGELAP_NO_UPDATE") is null) _updater.CheckIfDue(manual: false);
    }

    /// <summary>
    /// Takes the window off the screen for automated runs (screenshots, checks): unfocusable, parked far
    /// outside the desktop and hidden. The engine keeps rendering a hidden window (it only stops for a
    /// minimised one), so the viewport readback still works.
    /// </summary>
    private void HideWindow()
    {
        var window = GetWindow();
        window.Unfocusable = true;
        window.Position = new Vector2I(-32000, -32000);
        if (!OperatingSystem.IsWindows()) return;
        var handle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (handle != IntPtr.Zero) ShowWindow(handle, SwHide);
    }

    private const int SwHide = 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void ParseArgs()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--attract") _attract = true;
            else if (arg == "--park") _park = true;
            else if (arg == "--ram" || arg.StartsWith("--ram=", System.StringComparison.Ordinal)) _ram = arg.StartsWith("--ram=", System.StringComparison.Ordinal) ? arg["--ram=".Length..] : "tree";   // automated runs: drive into a tree, a building or a drone for the crash replay
            else if (arg == "--try-table") _tryTable = true;
            else if (arg.StartsWith("--quality=", System.StringComparison.Ordinal) && Enum.TryParse<GraphicsQuality>(arg["--quality=".Length..], ignoreCase: true, out var quality)) _quality = quality;   // this run only
            else if (arg.StartsWith("--time=", System.StringComparison.Ordinal)) float.TryParse(arg["--time=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _creditSeconds);
            else if (arg == "--chase") _startChase = true;
            else if (arg == "--retro") _retro = true;
            else if (arg == "--invisible") _invisible = true;   // automated runs: no window on screen, no focus taken
            else if (arg == "--no-ghost") _noGhost = true;       // pictures of the car without the best lap's ghost over it
            else if (arg == "--no-traffic") _noTraffic = true;
            else if (arg.StartsWith("--weather=", System.StringComparison.Ordinal)) _weather = WeatherModel.Parse(arg["--weather=".Length..]);
            else if (arg.StartsWith("--light=", System.StringComparison.Ordinal)) _hourArg = arg["--light=".Length..] switch { "sunrise" => WorldBuilder.Hour.Sunrise, "night" => WorldBuilder.Hour.Night, "dusk" => WorldBuilder.Hour.Dusk, _ => WorldBuilder.Hour.Day };
            else if (arg == "--no-hud") _noHud = true;
            else if (arg.StartsWith("--orbit=", System.StringComparison.Ordinal))
            {
                // --orbit=yaw,pitch,distance: a fixed camera on the car for pictures of the car itself.
                var parts = arg["--orbit=".Length..].Split(',');
                if (parts.Length == 3
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var yaw)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pitch)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var distance))
                {
                    _orbit = new Vector3(yaw, pitch, distance);
                }
            }
            else if (arg == "--speed-route") _scripted.PreferStunt = false;
            else if (arg.StartsWith("--screenshot=", System.StringComparison.Ordinal)) _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--at=", System.StringComparison.Ordinal))
            {
                // One time, or a list of them: each a picture of its own.
                _screenshotAts.Clear();
                foreach (var part in arg["--at=".Length..].Split(','))
                {
                    if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at)) _screenshotAts.Add(at);
                }
                if (_screenshotAts.Count > 0) _screenshotAt = _screenshotAts[0];
            }
            else if (arg == "--pose-log") { _poseLog = true; StructureAcoustics.Log = true; SkidMarks.Log = true; CloudShadows.Log = true; RoadsideEchoes.Log = true; }
            else if (arg.StartsWith("--audio-dump=", System.StringComparison.Ordinal)) _audioDump = arg["--audio-dump=".Length..];
            else if (arg.StartsWith("--ssao=", System.StringComparison.Ordinal)) WorldBuilder.SsaoOverride = arg["--ssao=".Length..] != "0";
            else if (arg.StartsWith("--ssr=", System.StringComparison.Ordinal)) WorldBuilder.SsrOverride = arg["--ssr=".Length..] != "0";
            else if (arg.StartsWith("--volfog=", System.StringComparison.Ordinal)) WorldBuilder.VolumetricFogOverride = arg["--volfog=".Length..] != "0";
            else if (arg.StartsWith("--fog=", System.StringComparison.Ordinal)) WorldBuilder.FogOverride = arg["--fog=".Length..] != "0";
            else if (arg.StartsWith("--probe=", System.StringComparison.Ordinal)) WorldBuilder.ProbeOverride = arg["--probe=".Length..] != "0";
            else if (arg == "--probe-all") WorldBuilder.ProbeAffectsAll = true;
            else if (arg.StartsWith("--sdfgi=", System.StringComparison.Ordinal)) WorldBuilder.SdfgiOverride = arg["--sdfgi=".Length..] != "0";
            else if (arg.StartsWith("--land=", System.StringComparison.Ordinal)) WorldBuilder.LandOverride = arg["--land=".Length..] != "0";
            else if (arg.StartsWith("--motion-blur=", System.StringComparison.Ordinal)) WorldBuilder.MotionBlurOverride = arg["--motion-blur=".Length..] != "0";
            else if (arg.StartsWith("--volumetric-clouds=", System.StringComparison.Ordinal)) WorldBuilder.VolumetricCloudsOverride = arg["--volumetric-clouds=".Length..] != "0";
            else if (arg.StartsWith("--cloud-shadows=", System.StringComparison.Ordinal)) WorldBuilder.CloudShadowsOverride = arg["--cloud-shadows=".Length..] != "0";
            else if (arg.StartsWith("--meadow=", System.StringComparison.Ordinal)) WorldBuilder.MeadowOverride = arg["--meadow=".Length..] != "0";
            else if (arg.StartsWith("--windscreen=", System.StringComparison.Ordinal)) Windscreen.Enabled = arg["--windscreen=".Length..] != "0";
            else if (arg.StartsWith("--fsr2=", System.StringComparison.Ordinal)) float.TryParse(arg["--fsr2=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _fsr2Scale);
            else if (arg.StartsWith("--shake=", System.StringComparison.Ordinal)) _shake = arg["--shake=".Length..] != "0";
            else if (arg.StartsWith("--sun-shadow=", System.StringComparison.Ordinal)) WorldBuilder.SunShadowOverride = arg["--sun-shadow=".Length..] != "0";
            else if (arg.StartsWith("--shadow-filter=", System.StringComparison.Ordinal) && int.TryParse(arg["--shadow-filter=".Length..], out var filter))
            {
                // The sun's shadow filter for this run: 0 hard, 1-5 soft from very low to ultra (the project's setting is 3).
                RenderingServer.DirectionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)System.Math.Clamp(filter, 0, 5));
            }
            else if (arg.StartsWith("--camera=", System.StringComparison.Ordinal))
            {
                var parts = arg["--camera=".Length..].Split(',');
                var v = new float[parts.Length];
                var ok = parts.Length >= 6;
                for (var i = 0; ok && i < parts.Length; i++) ok = float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
                if (ok) _fixedCamera = (Convert.ToGodot(new SN.Vector3(v[0], v[1], v[2])), Convert.ToGodot(new SN.Vector3(v[3], v[4], v[5])), parts.Length > 6 ? v[6] : 50f);
                else GD.PrintErr($"{arg}: six or seven numbers wanted");
            }
            else if (arg.StartsWith("--hold=", System.StringComparison.Ordinal))
            {
                var parts = arg["--hold=".Length..].Split(':');
                if (parts.Length == 4 && System.Enum.TryParse<PathKind>(parts[0], ignoreCase: true, out var kind)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var from)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var to)
                    && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
                {
                    _holds.Add((kind, from, to, speed));
                }
                else GD.PrintErr($"{arg}: path:from:to:speed wanted");
            }
            else if (arg.StartsWith("--quit-after=", System.StringComparison.Ordinal)) float.TryParse(arg["--quit-after=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _quitAfter);
            else if (arg.StartsWith("--new-game-after=", System.StringComparison.Ordinal)) float.TryParse(arg["--new-game-after=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _newGameAt);
            else if (arg.StartsWith("--drop=", System.StringComparison.Ordinal)) _dropFile = arg["--drop=".Length..];
            else if (arg.StartsWith("--menu-after=", System.StringComparison.Ordinal)) float.TryParse(arg["--menu-after=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _menuAt);
            else if (arg.StartsWith("--jingle=", System.StringComparison.Ordinal)) _jingle = arg["--jingle=".Length..];
            else if (arg == "--auto") _attractMode = TransmissionMode.Automatic;
            else if (arg == "--show-all") _showAll = true;
            else if (arg == "--save-best") _saveBest = true;   // debug: let an attract lap become the saved best lap
            else if (arg.StartsWith("--export-world=", System.StringComparison.Ordinal))
            {
                _exportWorld = arg["--export-world=".Length..];
                WorldBuilder.ExportMode = true;
                // The Unreal level takes the lofted car and puts its own copy of the real model in its place
                // (unreal/Scripts/prepare_car_model.py): the export keeps the lofted bodies.
                CarView.UseModels = false;
            }
            else if (arg.StartsWith("--car=", System.StringComparison.Ordinal)) _carIndex = CarIndex(arg["--car=".Length..], _carIndex);
            else if (arg.StartsWith("--paint=", System.StringComparison.Ordinal)) _paintArg = arg["--paint=".Length..];   // this run only
            else if (arg.StartsWith("--look=", System.StringComparison.Ordinal)) _look = arg["--look=".Length..];
            else if (arg.StartsWith("--seat-look=", System.StringComparison.Ordinal)) _seatLook = ParseVector2(arg["--seat-look=".Length..]);
            else if (arg == "--showroom" || arg.StartsWith("--showroom=", System.StringComparison.Ordinal)) _showroomArg = arg.StartsWith("--showroom=", System.StringComparison.Ordinal) ? arg["--showroom=".Length..] : "";   // automated runs: the showroom open from the start, on this car
            else if (arg.StartsWith("--showroom-entry=", System.StringComparison.Ordinal)) _showroomFromMenu = arg["--showroom-entry=".Length..] == "menu";
            else if (arg.StartsWith("--turn=", System.StringComparison.Ordinal)) float.TryParse(arg["--turn=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _turn);
            else if (arg.StartsWith("--showroom-paint=", System.StringComparison.Ordinal)) _showroomPaint = arg["--showroom-paint=".Length..];
            else if (arg == "--picker") _showroomPicker = true;
            else if (arg == "--paint-book-test") _paintBookTest = true;
            else if (arg == "--lofted") CarView.UseModels = false;
            else if (arg == "--perf-log") _perfLog = true;
            else if (arg == "--dent-test") _dentTest = true;
            else if (arg == "--hold-brake") _holdBrake = true;
            else if (arg == "--menu") _openMenu = true;
            else if (arg == "--puddles=0") _noPuddles = true;
            else if (arg.StartsWith("--ram-speed=", System.StringComparison.Ordinal)) float.TryParse(arg["--ram-speed=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _ramSpeed);
        }
    }

    /// <summary>The car a --car= switch names: its index in the catalogue or one of the short names; otherwise the current one.</summary>
    private static int CarIndex(string name, int current)
    {
        // In the catalogue's order (VehicleSpec.Catalogue); the five of cars13 by their model file keys.
        string[] names = ["flat12", "v8", "hothatch", "five", "turbohatch", "saloon", "estate", "fleet", "458", "bravohgt", "bravo16", "leoncupra", "brz", "clio"];
        // The everyday cars and the hatchbacks answer to the names of the cars they follow too (the hatchbacks' keys
        // are those of the cars they once were, which the models' files still carry); the 458 and the Cupra to a short
        // name each.
        string[] aliases = ["", "", "peugeot205", "delta", "r32", "sahin", "toros", "w212", "flatv8", "", "", "cupra", "", "clio2"];
        var index = System.Array.IndexOf(names, name.ToLowerInvariant());
        if (index < 0) index = System.Array.IndexOf(aliases, name.ToLowerInvariant());
        if (index < 0 && int.TryParse(name, out var number)) index = number;
        return index >= 0 && index < VehicleSpec.Catalogue.Length ? index : current;
    }

    /// <summary>The player's paint, the car's own in the book: a colour and its finish, or null for its own (its model's).</summary>
    private CarView.CarPaint? PlayerPaint => _paints[_carIndex].Paint;

    /// <summary>
    /// What only the player's car has: the lit instruments in its binnacle, a working rear-view mirror, and the
    /// eye point the cockpit camera sits at. Drones and the ghost stay without.
    /// </summary>
    private void FitOutPlayerCar()
    {
        _carView.MountInstruments(_instrumentPanel);
        _instruments.GearTop = _carView.GearTop;
        _mirror = RearMirror.Create(new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.055f), Roughness = 0.5f });
        _carView.MountMirror(_mirror);
        _cameraRig.Eye = _carView.Eye;
    }

    private void NewSession()
    {
        var options = _noTraffic ? RaceOptions.MediumNoTraffic : RaceOptions.Medium;
        if (_creditSeconds > 0f) options = options with { InitialTime = _creditSeconds };
        // The weather of the credit (0.12): --weather= for an automated run, otherwise the menu's choice (0.15: clear
        // by default, or drawn for every credit when the menu says random).
        options = options with { Weather = _weather ?? (_weatherChoice == 4 ? WeatherModel.Draw(System.Random.Shared) : (Weather)_weatherChoice) };
        var spec = VehicleSpec.Catalogue[_carIndex];
        _session = new RaceSession(_track, spec, options);
        // The puddles the view draws are where the tyres meet standing water (0.56).
        if (!_noPuddles) _session.Car.Puddles = WorldBuilder.PuddleMap;
        // The road's rubber and the wheels' dust (0.42): a new session's road is clean.
        if (_skidMarks is null)
        {
            _skidMarks = new SkidMarks();
            AddChild(_skidMarks);
            _wheelDust = new WheelDust();
            AddChild(_wheelDust);
        }
        _skidMarks.Clear();
        if (_perfLog) StartMark("  session: the race session");
        // The credit's hour (0.55): --light= for an automated run, otherwise the menu's (the day by default, or drawn).
        WorldBuilder.SetHour(_hourArg ?? ChosenHour());
        WorldBuilder.SetWeather(_session.Weather);
        ApplyRain();
        EndCrash();
        // A car kept from the last credit (the same body) takes its dents with it otherwise.
        _carView.Repair();
        if (_carView.IsInsideTree() && (_carView.Name != $"Car-{spec.Style}" || _repaintLofted)) BuildPlayerCar();
        _carView.Name = $"Car-{spec.Style}";
        if (_perfLog) StartMark("  session: weather, repair, the car");
        _audio.SetCar(spec);
        if (_perfLog) StartMark("  session: the engine voice");
        if (_savedBestLap is not null && !_noGhost) _session.LoadBestLap(_savedBestLap);
        _hud.Bind(_session);
        _instruments.Bind(_session);
        _scores.Dismiss();
        _scoreOffered = false;
        _keyboard.Reset();
        _cameraRig.ResetChase();
        if (_perfLog) StartMark("  session: best lap, hud, instruments");
        BuildDroneViews();
        if (_perfLog) StartMark("  session: the drones");
        BuildGhostView();
        if (_perfLog) StartMark("  session: the ghost");
        if (_attract || _park || _ram is not null)
        {
            _session.ChooseTransmission(_attractMode);
        }
        SyncView();
    }

    /// <summary>Rain in the air and in the ears when the credit's weather is rain; none in 1989 mode, whose cabinet knew no weather.</summary>
    private void ApplyRain()
    {
        var raining = _session is not null && !_retro && WeatherModel.Raining(_session.Weather);
        _cameraRig?.SetRain(raining);
        // The glass (0.46): the rain's drops, or on a wet day a few left from the spray.
        var wet = _session is not null && !_retro && !raining ? WeatherModel.Wetness(_session.Weather) : 0f;
        _cameraRig?.SetGlass(raining ? 1f : 0f, wet);
        if (_audio is not null) _audio.Rain = raining ? 1f : 0f;
        CarView.Overcast = _session is not null && !_retro && WeatherModel.Overcast(_session.Weather);
    }

    /// <summary>
    /// The cars the traffic is drawn as, in turn (BuildDroneViews): since 0.70 every car with a model of its own, as the
    /// owner asked, the everyday ones between the sports cars so that a credit's eight drones are a mix. The lofted
    /// cars13 bodies are left out, as they are out of the line-up (Lineup).
    /// </summary>
    private static readonly VehicleSpec[] TrafficCars =
    [
        VehicleSpec.Saloon, VehicleSpec.HotHatch, VehicleSpec.Estate, VehicleSpec.FlatPlaneV8, VehicleSpec.Fleet, VehicleSpec.ClioSixteen,
        VehicleSpec.Boxer, VehicleSpec.TurboHatch, VehicleSpec.FiveHatch, VehicleSpec.FlatTwelve, VehicleSpec.V8Evo,
    ];

    /// <summary>The traffic's Ferraris keep their own red: the road's palette has beige, brown and ochre in it.</summary>
    private static bool KeepsOwnPaint(CarStyle style) => style is CarStyle.FlatTwelve or CarStyle.V8 or CarStyle.FlatPlaneV8;

    private void BuildDroneViews()
    {
        _world.DetachDrones();
        foreach (var view in _droneViews) view.QueueFree();
        _droneViews.Clear();
        if (_session.Traffic is null) return;
        // The traffic is the everyday cars of the road (0.9.1; until then every drone was the period sports car of the
        // original): the saloon, the estate and the fleet saloon, and since 0.21 the 205 and the Golf too (the 205
        // the GTi its model is, its red wheels, bucket seats and pinstripe with it) in the road's colours. Each drone
        // in turn takes the next car and the next colour of the road palette (CarView.TrafficPaints), counting on
        // from the last credit's, so no two drones of a credit look alike and the next credit's are others: five cars
        // and twelve colours repeat a pair only every sixty drones, and a credit has eight. Only the look: the drones
        // move as before (physics/Race/Traffic.cs), whatever car is drawn; their cabins are drawn only near
        // (CarView.TrimCabinForTraffic). Since 0.70 the drones are every car with a model (TrafficCars), eleven cars
        // and twelve colours, the Ferraris in their own red.
        var everyday = TrafficCars;
        foreach (var drone in _session.Traffic.Cars)
        {
            var index = _droneSerial++ % (everyday.Length * CarView.TrafficPaints.Length);
            var spec = everyday[index % everyday.Length];
            var view = CarView.Create(spec, KeepsOwnPaint(spec.Style) ? null : CarView.TrafficPaints[index % CarView.TrafficPaints.Length]);
            view.Name = "Drone";
            view.SetViewpoint(false);
            AddChild(view);
            view.TrimCabinForTraffic();
            _world.AttachDrone(view, drone);
            _droneViews.Add(view);
        }
    }

    /// <summary>The player's car built again (another car chosen, or a lofted body repainted), in the chosen paint.</summary>
    private void BuildPlayerCar()
    {
        var spec = VehicleSpec.Catalogue[_carIndex];
        _carView.QueueFree();
        _carView = CarView.Create(spec, PlayerPaint);
        _carView.Name = $"Car-{spec.Style}";
        AddChild(_carView);
        _carView.MarkPlayer();
        FitOutPlayerCar();
        _repaintLofted = false;
    }

    private void BuildGhostView()
    {
        _ghostView?.QueueFree();
        _ghostView = null;
        if (!_session.GrudgeMatch) return;
        _ghostView = CarView.Create(VehicleSpec.Catalogue[_carIndex]);
        _ghostView.Name = "Ghost";
        _ghostView.MakeGhost();
        _ghostView.Visible = false;
        AddChild(_ghostView);
    }

    private static int LoadChoice(string key, int fallback)
    {
        var settings = new ConfigFile();
        settings.Load(SettingsFile);
        return (int)settings.GetValue("game", key, fallback).AsInt64();
    }

    private static void SaveChoice(string key, int value)
    {
        var settings = new ConfigFile();
        settings.Load(SettingsFile);
        settings.SetValue("game", key, value);
        settings.Save(SettingsFile);
    }

    /// <summary>
    /// Automation (--paint-book-test): the per-car paints' round trip through a settings file of their own
    /// (user://paint-book-test.cfg, never the player's): an 0.24 file's one paint migrated to every car and its key
    /// dropped with the car's choice beside it untouched; a paint of every kind written for each car and read back the
    /// same; a second start migrating nothing; an automated run's book changing nothing on disk; the texts the file
    /// and --paint= take read as they should be; the picker's hue, saturation and lightness keeping a colour's hex.
    /// Prints each check and is true when all hold.
    /// </summary>
    private static bool PaintBookTest()
    {
        const string file = "user://paint-book-test.cfg";
        var failures = 0;
        void Check(bool ok, string what)
        {
            GD.Print($"paint book: {(ok ? "ok  " : "FAIL")}  {what}");
            if (!ok) failures++;
        }
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(file));
        var cars = VehicleSpec.Catalogue.Length;
        string Saved(ConfigFile settings, int car) => settings.GetValue("game", PaintBook.Key(VehicleSpec.Catalogue[car]), "").AsString();

        // An 0.24 file: SILVER (3) for whatever car was driven, and the car.
        var old = new ConfigFile();
        old.SetValue("game", "paint", 3);
        old.SetValue("game", "car", 2);
        old.Save(file);
        var book = PaintBook.Load(file, keep: true);
        Check(Enumerable.Range(0, cars).All(i => book[i] == PaintChoice.OfPreset(3)), $"0.24's one paint (SILVER) given to every car ({cars})");
        var migrated = new ConfigFile();
        migrated.Load(file);
        Check(!migrated.HasSectionKey("game", "paint"), "the 0.24 key dropped");
        Check(migrated.GetValue("game", "car", -1).AsInt32() == 2, "the car's choice beside it untouched");
        Check(Saved(migrated, 0) == "SILVER", $"the migrated paint written as text ({PaintBook.Key(VehicleSpec.Catalogue[0])}=\"{Saved(migrated, 0)}\")");

        // A paint of every kind (the car's own, presets, mixed colours in each finish), one for each car.
        PaintChoice[] kinds =
        [
            PaintChoice.Original,
            PaintChoice.OfPreset(5),
            PaintChoice.Mixed(Color.FromHtml("#1E4FA0"), PaintFinish.Metallic),
            PaintChoice.Mixed(Color.FromHtml("#C8102E"), PaintFinish.Pearl),
            PaintChoice.Mixed(Color.FromHtml("#F2F2EE"), PaintFinish.Solid),
            PaintChoice.OfPreset(7),
        ];
        for (var i = 0; i < cars; i++) book.Set(i, kinds[i % kinds.Length]);
        var reread = PaintBook.Load(file, keep: true);
        Check(Enumerable.Range(0, cars).All(i => reread[i] == kinds[i % kinds.Length]), "every car's paint read back as it was written");
        Check(reread.LastMixed(2) == kinds[2] && reread.LastMixed(1) is null, "a mixed colour is its car's mixed swatch after a restart, and only its car's");
        var written = new ConfigFile();
        written.Load(file);
        Check(Saved(written, 2) == "#1E4FA0 METALLIC" && Saved(written, 0) == "ORIGINAL" && Saved(written, 1) == "DARK BLUE", $"written as text (\"{Saved(written, 0)}\", \"{Saved(written, 1)}\", \"{Saved(written, 2)}\")");
        Check(!written.HasSectionKey("game", "paint") && written.GetValue("game", "car", -1).AsInt32() == 2, "a second start migrates nothing");
        var menu = reread.Options(3);
        Check(menu.Length == CarView.Paints.Length + 1 && menu[^1] == kinds[3] && reread.Options(1).Length == CarView.Paints.Length, "the menu's PAINT line offers a car its mixed colour after the presets");

        // An automated run's book: changes held in memory, the file as it was.
        var held = PaintBook.Load(file, keep: false);
        held.Set(0, PaintChoice.OfPreset(1));
        Check(held[0] == PaintChoice.OfPreset(1) && PaintBook.Load(file, keep: true)[0] == kinds[0], "an automated run's paint never reaches the file");
        old.Save(file);
        var memory = PaintBook.Load(file, keep: false);
        var untouched = new ConfigFile();
        untouched.Load(file);
        Check(memory[cars - 1] == PaintChoice.OfPreset(3) && untouched.HasSectionKey("game", "paint"), "an automated run migrates an 0.24 file in memory only");

        // The texts the file and --paint= take.
        var blue = Color.FromHtml("#1E4FA0");
        (string Text, PaintChoice? Expected)[] texts =
        [
            ("ORIGINAL", PaintChoice.Original),
            ("dark-blue", PaintChoice.OfPreset(5)),
            ("darkblue", PaintChoice.OfPreset(5)),
            ("DARK BLUE", PaintChoice.OfPreset(5)),
            ("3", PaintChoice.OfPreset(3)),
            ("#1e4fa0", PaintChoice.Mixed(blue, PaintFinish.Solid)),
            ("#1e4fa0:pearl", PaintChoice.Mixed(blue, PaintFinish.Pearl)),
            ("#1E4FA0 METALLIC", PaintChoice.Mixed(blue, PaintFinish.Metallic)),
            ("#1e4fa0:chrome", null),
            ("#1e4fa", null),
            ("purple", null),
            ("99", null),
            ("", null),
        ];
        foreach (var (text, expected) in texts)
        {
            var read = PaintChoice.TryParse(text, out var choice);
            Check(expected is { } wanted ? read && choice == wanted : !read, $"\"{text}\" -> {(expected is { } e ? e.Format() : "refused")}");
        }

        // The picker holds a colour as hue, saturation and lightness: a colour opened in it and kept is the same hex.
        foreach (var hex in new[] { "#1E4FA0", "#C8102E", "#F2F2EE", "#000000", "#FFFFFF", "#7F7F7F", "#D9A400" })
        {
            var (hue, saturation, lightness) = Hsl.From(Color.FromHtml(hex));
            var back = PaintChoice.Mixed(Hsl.ToColour(hue, saturation, lightness), PaintFinish.Solid).Hex;
            Check(back == hex, $"{hex} through hue, saturation and lightness: {back}");
        }
        // The cars after the eight of 0.24.0 (the five of cars13, the Clio of 0.33.0) have their own colours, each its real
        // car's launch or press paint (cars13 stage 2), not the lofted bodies' red: the stand-ins' ORIGINAL is their
        // style's LoftPaint, the 458's its model's paint.
        var later = VehicleSpec.Catalogue.Where(c => c.Style >= CarStyle.FlatPlaneV8).ToArray();
        Check(later.Length == VehicleSpec.Catalogue.Length - 8 && later.All(c => CarView.LoftPaint(c.Style).Colour != CarView.DefaultPaint)
              && later.Select(c => CarView.LoftPaint(c.Style).Colour).Distinct().Count() == later.Length,
            $"the later cars in their own colours ({string.Join(", ", later.Select(c => CarView.LoftPaint(c.Style).Colour.ToHtml(false).ToUpperInvariant()))})");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(file));
        GD.Print($"paint book: {(failures == 0 ? "every check holds" : $"{failures} checks FAILED")}");
        return failures == 0;
    }

    private static LapRecording? LoadBestLap()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(BestLapFile);
            if (!System.IO.File.Exists(path)) return null;
            var recording = LapRecording.Deserialize(System.IO.File.ReadAllBytes(path));
            if (recording is not null) GD.Print($"best lap loaded: {RaceSession.FormatLapTime(recording.Duration)} ({recording.Route})");
            return recording;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"best lap could not be loaded: {ex.Message}");
            return null;
        }
    }

    private void SaveBestLap(LapRecording recording)
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(BestLapFile);
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(path, recording.Serialize());
            _savedBestLap = recording;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"best lap could not be saved: {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (_audioDump is not null && _recorder is null)
        {
            // At the end of the master's chain, after the limiter the engine's sound puts there first.
            _recorder = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
            AudioServer.AddBusEffect(0, _recorder);
            _recorder.SetRecordingActive(true);
        }
        if (_leaving) return;
        if (_perfLog && !_firstFrameMarked)
        {
            _firstFrameMarked = true;
            StartMark("first frame");
        }
        var dt = (float)delta;
        _elapsed += dt;
        if (SingleInstance.ActivationRequested()) BringToFront();
        HandleGlobalKeys();
        Showroom.CollectAbandoned();
        if (_collectAfterShowroom)
        {
            // The showroom and its cars were freed at the end of the last frame, but their meshes, materials and textures
            // are held by .NET's wrappers until the collector runs, and on a heap of about 20 MB it ran a minute and a half
            // later (the showroom's review: 310 resources and 200 MB of video memory over the baseline until then). They
            // are collected now, on a frame that is already a change of screen. A wrapper holds its object strongly while
            // Godot also refers to it, so a material let go by one collection leaves its textures' wrappers to the next:
            // collected until a round frees nothing (one collection left a visit to every car 69 MB of video memory over;
            // three or four took 17-22 ms and left none).
            _collectAfterShowroom = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var collections = 0;
            void Collect()
            {
                var objects = Performance.GetMonitor(Performance.Monitor.ObjectCount);
                for (var round = 0; round < 6; round++)
                {
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                    collections++;
                    var left = Performance.GetMonitor(Performance.Monitor.ObjectCount);
                    if (left >= objects) break;
                    objects = left;
                }
            }
            Collect();
            // The cabin materials the showroom's lofted cars made (WorldBuilder.ReleaseUnusedPropMaterials): only once
            // the cars' meshes are collected does nothing but the cache hold them, and their textures need another round.
            var released = _propMaterialsBeforeShowroom is { } before ? WorldBuilder.ReleaseUnusedPropMaterials(before) : 0;
            _propMaterialsBeforeShowroom = null;
            if (released > 0) Collect();
            if (_perfLog) GD.Print($"showroom closed: its cars let go in {watch.Elapsed.TotalMilliseconds:0.0} ms, {collections} collections, {released} cabin material(s)");
        }
        if (_showroom is not null)
        {
            // The showroom over the title screen or the paused credit: its own keys and mouse, the session not stepping.
            // The title's ambience (the cows, the train) plays on under it; under the menu the bus is muted as before.
            HandleShowroom();
            if (!_menu.Open) UpdateSound(dt);
            if (_perfLog) LogPerformance();
            AutomationAfterFrame();
            return;
        }
        if (Input.IsActionJustPressed(InputSetup.Menu)) ShowMenu(!_menu.Open);
        if (_menu.Open)
        {
            // The race stands still under the menu: no input reaches the car, the session does not step.
            HandleMenu();
            // The storm with its clock stopped (0.64): a strike under way holds, but the weather or the 1989 look changed
            // from the menu puts its flash and bolt out at once.
            _thunder.Update(_session.Weather, _session.Phase, _retro, _cameraRig.Listener, 0f);
            FillMenuLabels();
            SyncView();
            AutomationAfterFrame();
            return;
        }
        WorldBuilder.Tick(dt, GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero);
        if (_elapsed >= _frameCapCheckAt)
        {
            // The window may have moved to a screen with another refresh rate.
            _frameCapCheckAt = _elapsed + 2f;
            ApplyFrameCap();
        }
        if (_menuAt > 0f && _elapsed >= _menuAt)
        {
            _menuAt = -1f;
            ShowMenu(true);
        }
        if (_perfLog) LogPerformance();
        if (_dentTest && _elapsed >= 2f)
        {
            _dentTest = false;
            var (nose, _, halfWidth, _) = _carView.Extent;
            _carView.Dent(new Vector3(halfWidth * 0.35f, 0f, nose + 0.15f), new Vector3(0f, -0.2f, 1f), 0.14f, 0.5f);
        }
        if (_dropFile is not null && _elapsed >= 2f)
        {
            OnFilesDropped([_dropFile]);
            _dropFile = null;
        }
        if (_jingle is not null && _elapsed >= 1f)
        {
            if (System.Enum.TryParse<EngineAudio.Jingle>(_jingle, ignoreCase: true, out var jingle)) _audio.PlayJingle(jingle);
            _jingle = null;
        }
        if (_newGameAt > 0f && _elapsed >= _newGameAt)
        {
            _newGameAt = -1f;
            NewSession();
        }
        if (_overlayUntil >= 0f && _elapsed >= _overlayUntil)
        {
            _hud.Overlay = "";
            _overlayUntil = -1f;
        }

        var input = _ram is not null ? RamInputs() : _park ? DriverInputs.Idle : _attract ? _scripted.Next(_session) : _keyboard.Sample(dt, _session.Car);
        if (_holdBrake) input.Brake = 1f;
        ApplyHolds(ref input);
        _carView.Braking = input.Brake > 0.05f;
        if (!_attract && !_park && HandleMenuKeys()) input.Ignition = true;

        if (_session.Phase == SessionPhase.Replay && Input.IsActionJustPressed(InputSetup.Abort)) _session.Abort();
        else if (_session.Phase == SessionPhase.Driving && _session.OffRoad && Input.IsActionJustPressed(InputSetup.Abort)) _session.Abort();

        _session.Car.Wind = PrairieWind(dt);
        _session.Step(input, dt);
        if (_poseLog) LogPose();
        if (_session.Events.Contains(RaceEvent.NewBestLap) && _session.BestLap is not null && (!_attract || _saveBest))
        {
            SaveBestLap(_session.BestLap);
            if (_ghostView is null) BuildGhostView();
        }
        if (_session.Events.Contains(RaceEvent.Knocked)) ShowKnock();
        if (_session.Events.Contains(RaceEvent.SignHit) && _propNodes.TryGetValue(_session.LastSoftHit, out var sign))
        {
            // Knock the sign over: it tips away from the car over the next third of a second.
            var away = sign.GlobalPosition - _carView.GlobalPosition;
            away.Y = 0f;
            // Up x away: turning about this axis leans the sign away from the car.
            var axis = Vector3.Up.Cross(away.Normalized());
            if (axis.LengthSquared() > 1e-4f) _tipping.Add((sign, _elapsed, sign.Rotation.Y, axis.Normalized()));
        }
        UpdateTipping();
        if (_session.Phase == SessionPhase.Finished)
        {
            if (!_scoreOffered)
            {
                // The credit is over: a score that makes the table asks for initials (the attract mode's does not).
                _scoreOffered = true;
                if (!_attract || _tryTable) _scores.Offer(_session.Score, _session.CreditBestLap, _session.CreditBestRoute, _session.Car.Spec.Name);
            }
            _scores.Update(dt);
        }
        UpdateSound(dt);
        _hud.Notice = _updater.Status;
        SyncView();

        AutomationAfterFrame();
        // A downloaded update restarts the game only between credits: on the title screen or once the time is up.
        _updater.ApplyWhenIdle(_session.Phase is SessionPhase.SelectTransmission or SessionPhase.Finished);
    }

    /// <summary>
    /// The sound may fail without the race noticing: a fault there is logged once and the rest of the frame (the
    /// picture, the clock, quitting) carries on. In 0.8.0 the world sound threw on every frame of a second credit
    /// and the view stopped following the car: it stood at the line while the engine revved and the clock ran.
    /// </summary>
    private void UpdateSound(float dt)
    {
        try
        {
            _audio.Update(_session, dt);
            _world.Update(_session, _cameraRig.Listener, dt);
            _thunder.Update(_session.Weather, _session.Phase, _retro, _cameraRig.Listener, dt);
        }
        catch (System.Exception e)
        {
            if (_soundFaults.Add($"{e.GetType().Name}: {e.Message}")) GD.PushError($"sound update failed (logged once): {e}");
        }
    }

    /// <summary>
    /// The automation's picture (--screenshot) and its end (--quit-after), checked at the end of every frame, the
    /// menu's included: a run that opened the menu never reached them and recorded on for ever.
    /// </summary>
    private void AutomationAfterFrame()
    {
        if (_openMenu && _elapsed >= 1.0)
        {
            _openMenu = false;
            ShowMenu(true);
        }
        if (_screenshotPath is not null && _screenshotAts.Count > 1)
        {
            // A list of times: each picture is the path with its time before the extension (shot.png -> shot-12.png).
            if (_screenshotsTaken < _screenshotAts.Count && _elapsed >= _screenshotAts[_screenshotsTaken])
            {
                var at = _screenshotAts[_screenshotsTaken++];
                var stem = System.IO.Path.ChangeExtension(_screenshotPath, null).TrimEnd('.');
                CallDeferred(nameof(TakeScreenshot), $"{stem}-{at.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}{System.IO.Path.GetExtension(_screenshotPath)}");
            }
        }
        else if (_screenshotPath is not null && !_screenshotTaken && _elapsed >= _screenshotAt)
        {
            _screenshotTaken = true;
            CallDeferred(nameof(TakeScreenshot), _screenshotPath);
        }
        if (_quitAfter > 0f && _elapsed >= _quitAfter) Shutdown();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_leaving) Shutdown();
        else if (what is (int)NotificationApplicationFocusOut or (int)NotificationApplicationFocusIn)
        {
            _focused = what == NotificationApplicationFocusIn;
            ApplyFrameCap();
        }
    }

    /// <summary>
    /// Holds the frame rate to an even fraction of the screen's refresh rate, at most 90 (72 on a 144 Hz panel), to
    /// 30 while the window is in the background and to 20 in 1989 mode.
    /// </summary>
    /// <remarks>
    /// Uncapped, the game asks the GPU for one frame per refresh: 144 a second on the development laptop's panel,
    /// more than the High preset can draw, so the GPU ran flat out for the whole drive. On that laptop (RTX 5060
    /// rendering for an Intel-driven panel) every such session ended in a driver timeout within a minute: Windows
    /// reset the GPU, Vulkan reported the device lost and the screen went black (28 October 2026). Vsync alone does
    /// not hold a hybrid laptop's discrete GPU back reliably; the engine's own limiter does. A cap that divides the
    /// refresh rate keeps every frame on screen for the same number of refreshes, so the motion stays even.
    /// </remarks>
    private void ApplyFrameCap()
    {
        var refresh = DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen());
        if (refresh < 30f) refresh = 60f;   // unknown (-1) on some drivers and remote sessions
        var cap = _retro ? 20 : !_focused ? 30 : Math.Max(30, (int)MathF.Round(refresh / MathF.Ceiling(refresh / 90f)));
        if (Engine.MaxFps == cap) return;
        Engine.MaxFps = cap;
        // One line per change: a crash report then says what the game was asking of the GPU.
        GD.Print($"frame cap: {cap} fps on a {refresh:0} Hz screen{(_retro ? " (1989 mode)" : _focused ? "" : " (in the background)")}");
    }

    /// <summary>Another launch asked for the game: restore and raise this window instead of opening a second copy.</summary>
    private void BringToFront()
    {
        GD.Print("another launch asked for the game; bringing it to the front");
        var window = GetWindow();
        if (window.Mode == Window.ModeEnum.Minimized) window.Mode = Window.ModeEnum.Windowed;
        window.GrabFocus();
    }

    /// <summary>
    /// Quits after letting .NET finalise what it can while the engine is still alive; wrappers finalised after
    /// shutdown are reported as leaks and have crashed the process on exit.
    /// </summary>
    private void Shutdown()
    {
        if (_recorder is not null && _audioDump is not null)
        {
            _recorder.SetRecordingActive(false);
            var error = _recorder.GetRecording()?.SaveToWav(_audioDump);
            GD.Print($"audio dump: {_audioDump} ({error})");
        }
        WorldBuilder.ReleaseShared();
        SingleInstance.Release();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        GetTree().Quit();
    }

    private void SyncView()
    {
        var car = _session.Car;
        var dt = (float)GetProcessDeltaTime();
        // From the driver's seat the cabin and the bonnet are drawn and the instruments are in the binnacle; in the
        // other views the shell is drawn and the HUD shows the instruments.
        // From the seat through the crash's own moment (0.35), then the replay from the crane.
        var cockpit = _cameraRig.Mode == CameraMode.Cockpit && (_session.Phase != SessionPhase.Replay || _session.ReplayTime < _session.Options.ImpactSeconds);
        _carView.SetViewpoint(insideCar: cockpit && !_showAll);
        _hud.Cockpit = cockpit;
        _audio.Interior = cockpit;
        _world.Interior = cockpit;
        _world.Wipers = _cameraRig.Wipers;
        // The lamps at night (0.57); the 1989 cabinet knew no night.
        var night = WorldBuilder.LampsLit;
        // The cars' lamps are on in the rain too (0.62), as drivers have them in the wet whatever the hour.
        _carView.Headlights = WorldBuilder.CarLampsLit;
        // The other lamps of the night (0.58): the farms' yard lamps and the train's.
        NightLamps.Night = night;
        if (_trainView is not null) _trainView.Night = night;
        _carView.CabinLight = WorldBuilder.CabinLight;
        _world.Covered = _audio.Covered;
        _mirror?.SetActive(cockpit);
        SyncTraffic(dt);
        SyncCows(dt);
        SyncGhost();
        _skidMarks?.Update(_session, _retro);
        _wheelDust?.Update(_session, _retro);
        if (_session.Traffic is not null) _wheelDust?.UpdateTraffic(_droneViews, _session.Traffic.Cars, _session.Weather, _retro);
        _trainView?.Sync();
        if (_session.Phase == SessionPhase.Replay && _session.Replay.Count > 1)
        {
            // The recorded approach, then the crash played out and the car damaged (CrashScene).
            _crash ??= new CrashScene(this, _carView, _session, effects: !_retro);
            var impactSeconds = _session.Options.ImpactSeconds;
            if (_session.ReplayTime < impactSeconds)
            {
                // The crash as it happens, from where the player sits (0.35): the car where it struck, the fire, the glass.
                if (!_impactShown)
                {
                    _impactShown = true;
                    _crash.Impact();
                    _hud.GlassCracks = cockpit ? 1f : 0f;
                }
                _carView.SetPose(car.Position, car.Orientation);
                _carView.SetWheels(car.Wheels, car.SteerAngle, car.Spec);
                _cameraRig.FollowCar(_carView.GlobalTransform, MathF.Abs(car.Speed), dt);
                if (_fixedCamera is { } impactCamera) _cameraRig.Look(impactCamera.Eye, impactCamera.Target, impactCamera.Fov);
                return;
            }
            if (_impactShown)
            {
                _impactShown = false;
                _crash.EndImpact();
                _hud.GlassCracks = 0f;
            }
            // Only the tune plays in the replay (0.35): the crash's thump sounded once, as it happened.
            var pose = _crash.Pose(_session.ReplayTime - impactSeconds);
            _carView.Transform = pose;
            SyncTrafficReplay(_session.ReplayTime - impactSeconds);
            // The steam of a car knocked about before the crash stops where it was: the replayed car drives elsewhere (0.35).
            _knockSmoke.Follow(_carView, 0f, muted: true);
            _cameraRig.Replay(pose.Origin, _crash.ViewForward, _crash.CloseUp, low: _session.LastCrash == CrashReason.FellOffBridge);
            if (_fixedCamera is { } replayCamera) _cameraRig.Look(replayCamera.Eye, replayCamera.Target, replayCamera.Fov);
            return;
        }
        EndCrash();
        _knockSmoke.Follow(_carView, _session.Damage, _retro || _session.Phase == SessionPhase.Finished);
        _carView.SetPose(car.Position, car.Orientation);
        _carView.SetWheels(car.Wheels, car.SteerAngle, car.Spec);
        WorldBuilder.FollowCar(_carView.GlobalPosition, _elapsed);
        _cameraRig.Roughness = car.Wheels.Any(w => w.InContact && w.Surface != SurfaceType.Road) ? 1f : 0f;
        _cameraRig.Acceleration = new Vector2(car.LateralAcceleration, car.LongitudinalAcceleration);
        _cameraRig.FollowCar(_carView.GlobalTransform, MathF.Abs(car.Speed), dt);
        if (_look is not null) LookAtProp(_look);
        if (_fixedCamera is { } fixedCamera) _cameraRig.Look(fixedCamera.Eye, fixedCamera.Target, fixedCamera.Fov);
    }

    /// <summary>
    /// --hold=path:from:to:speed: over that stretch of the path the scripted driver holds the speed (m/s), flat out under
    /// it and braking over it, as --ram-speed= does for a knock; so a hidden run can take the drawbridge too slowly, the
    /// loop without the run-up or the banked 180 flat out, and the crash or the fall be looked at (0.34).
    /// </summary>
    private void ApplyHolds(ref DriverInputs input)
    {
        if (_holds.Count == 0 || _session.Phase != SessionPhase.Driving) return;
        var car = _session.Car;
        var kind = car.Cursor.Path.Kind;
        var distance = car.Cursor.Distance;
        foreach (var (path, from, to, speed) in _holds)
        {
            if (path != kind || distance < from || distance > to) continue;
            var over = car.Speed - speed;
            input.Throttle = over < 0f ? 1f : 0f;
            input.Brake = over > 1f ? 0.6f : 0f;
            return;
        }
    }

    /// <summary>
    /// --pose-log: twice a second the car's place (path, distance, position, speed, height over the surface, in the air
    /// or not, damage) and on the frame they happen the session's events, printed as "pose:" lines (0.34).
    /// </summary>
    private void LogPose()
    {
        var car = _session.Car;
        var events = _session.Events;
        var due = _elapsed - _poseLoggedAt >= 0.5f;
        var notable = events.Contains(RaceEvent.Crashed) || events.Contains(RaceEvent.Knocked) || events.Contains(RaceEvent.CowHit) || events.Contains(RaceEvent.SignHit);
        if (!due && !notable) return;
        _poseLoggedAt = _elapsed;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var p = car.Position;
        var hit = car.BodyHit;
        var line = $"pose: t={_elapsed.ToString("0.0", c)} phase={_session.Phase} path={car.Cursor.Path.Kind} d={car.Cursor.Distance.ToString("0", c)} x={p.X.ToString("0.0", c)} y={p.Y.ToString("0.00", c)} z={p.Z.ToString("0.0", c)} v={(car.Speed * 3.6f).ToString("0", c)}km/h h={hit.Height.ToString("0.00", c)} {hit.Surface} air={(car.Airborne ? 1 : 0)} dmg={_session.Damage.ToString("0.00", c)} guard={car.GroundGuardLifts}";
        // How far the tyres float on standing water (0.56), the most of the four.
        var floating = 0f;
        foreach (var w in car.Wheels) floating = MathF.Max(floating, w.Aquaplaning);
        if (floating > 0.01f) line += $" aqua={floating.ToString("0.00", c)}";
        if (notable) line += $" events={string.Join("+", events)}";
        if (events.Contains(RaceEvent.Crashed)) line += $" crash={_session.LastCrash}";
        if (events.Contains(RaceEvent.Knocked)) line += $" knock={_session.LastKnock.Kind}@{(_session.LastKnock.Speed * 3.6f).ToString("0", c)}km/h";
        GD.Print(line);
    }

    /// <summary>
    /// The --look= shot: from <c>distance</c> metres (default two and a half times the prop's size) off its front
    /// corner (<c>bearing</c> degrees round from straight ahead, default 35), at eye height, at the n-th prop of the kind.
    /// </summary>
    private void LookAtProp(string look)
    {
        var parts = look.Split(':');
        if (parts[0] == "drone")
        {
            LookAtDrone(parts);
            return;
        }
        if (!System.Enum.TryParse<PropKind>(parts[0], ignoreCase: true, out var kind)) return;
        var n = parts.Length > 1 && int.TryParse(parts[1], out var index) ? index : 0;
        var props = _track.Props.Select((p, i) => (Prop: p, Index: i)).Where(p => p.Prop.Kind == kind).ToList();
        if (props.Count == 0) return;
        var (prop, at) = props[System.Math.Clamp(n, 0, props.Count - 1)];
        if (!_propNodes.TryGetValue(at, out var node)) return;
        var reach = MathF.Max(prop.Size.X, MathF.Max(prop.Size.Y, prop.Size.Z));
        var distance = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : reach * 2.5f;
        var bearing = parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 35f;
        var basis = node.GlobalTransform.Basis.Orthonormalized();
        var front = basis.Z.Rotated(Vector3.Up, Mathf.DegToRad(bearing));
        var centre = node.GlobalPosition + Vector3.Up * MathF.Min(prop.Size.Y * 0.45f, 3f);
        var eye = node.GlobalPosition + front * distance;
        eye.Y = _track.Terrain.Height(eye.X, -eye.Z) + 1.7f + distance * 0.08f;
        _cameraRig.Look(eye, centre, 50f);
    }

    /// <summary>
    /// The --look=drone:n[:distance[:bearing[:height]]] shot (0.21): the n-th drone of the traffic followed from
    /// <c>distance</c> metres (default 7) off its front corner (<c>bearing</c> degrees round from straight ahead, default
    /// 35) and <c>height</c> metres over the road (default 1.8), for pictures of the traffic's cars; a height of a few
    /// tenths looks along the road at where the tyres meet it.
    /// </summary>
    private void LookAtDrone(string[] parts)
    {
        var n = parts.Length > 1 && int.TryParse(parts[1], out var index) ? index : 0;
        if (_droneViews.Count == 0) return;
        var drone = _droneViews[System.Math.Clamp(n, 0, _droneViews.Count - 1)];
        var distance = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 7f;
        var bearing = parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 35f;
        var height = parts.Length > 4 && float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : 1.8f;
        // A car's nose is its -Z. The road under it is the traffic's, a fixed ride height below where it carries the
        // drone, whatever car is drawn: the eye is set from it, so a car drawn higher or lower shows against the same road.
        var front = (-drone.GlobalTransform.Basis.Z).Normalized().Rotated(Vector3.Up, Mathf.DegToRad(bearing));
        var traffic = _session.Traffic!.Cars[System.Math.Clamp(n, 0, _droneViews.Count - 1)];
        var road = Convert.ToGodot(traffic.Position - traffic.Frame.Normal * Traffic.RideHeight);
        var eye = road + front * distance + Vector3.Up * height;
        _cameraRig.Look(eye, road + Vector3.Up * MathF.Min(height, 0.6f), 50f);
    }

    /// <summary>
    /// For the crash replay and the knocks in automated runs (--ram=tree, --ram=building, --ram=traffic, --ram=cow,
    /// --ram=test with a tree planted on the start straight): the course is driven as in the attract mode until a thing
    /// of that kind comes up within 50 m ahead and 25 degrees of the nose, and then the car is driven into it, flat out
    /// or at the speed --ram-speed= holds.
    /// </summary>
    private DriverInputs RamInputs()
    {
        var input = _scripted.Next(_session);
        var car = _session.Car;
        if (_session.Phase != SessionPhase.Driving)
        {
            _ramTarget = null;
            return input;
        }
        SN.Vector3? found = null;
        var nearest = 50f;
        void Consider(SN.Vector3 point)
        {
            var to = point - car.Position;
            to.Y = 0f;
            var distance = to.Length();
            if (distance < 3f || distance > nearest || SN.Vector3.Dot(to / distance, car.Forward) < MathF.Cos(0.44f)) return;
            nearest = distance;
            found = point;
        }
        if ((_ram is "traffic" or "oncoming") && _session.Traffic is not null)
        {
            // The drones going the car's way, unless the oncoming ones are asked for (0.35: the nearest in the cone was
            // always the oncoming one in the next lane, a glancing clip, and then the car steered after a point behind it).
            foreach (var drone in _session.Traffic.Cars)
            {
                if ((SN.Vector3.Dot(drone.Forward, car.Forward) > 0f) == (_ram == "traffic")) Consider(drone.Position);
            }
        }
        else if (_ram == "cow")
        {
            foreach (var cow in _session.Herd.Cows) Consider(cow.Position);
        }
        else
        {
            foreach (var prop in _session.Track.Props)
            {
                // A kind by its name (pier, barrier, pump, tower...), "tree" (and the test's tree), or "building" for the rest.
                var wanted = System.Enum.TryParse<PropKind>(_ram, ignoreCase: true, out var kind) ? prop.Kind == kind
                    : (_ram is "tree" or "test") == (prop.Kind == PropKind.Tree);
                if (prop.Solid && wanted) Consider(prop.Position);
            }
        }
        // Once a target is taken it is held (a drone's or a cow's is followed as it moves).
        if (found is { } point && (_ramTarget is null || _ram is "traffic" or "cow")) _ramTarget = point;
        if (_ramTarget is not { } target) return input;
        // A target the car has passed is let go (0.35): held, it steered the car round after a point behind it and off the road.
        if (SN.Vector3.Dot(target - car.Position, car.Forward) < 0f)
        {
            _ramTarget = null;
            return input;
        }
        var toTarget = target - car.Position;
        var angle = MathF.Atan2(SN.Vector3.Dot(toTarget, car.Right), MathF.Max(SN.Vector3.Dot(toTarget, car.Forward), 0.1f));
        // Flat out, or held at --ram-speed= (m/s) to see a knock below the crash speed.
        var over = _ramSpeed > 0f ? car.Speed - _ramSpeed : -1f;
        input.Throttle = over < 0f ? 1f : 0f;
        input.Brake = over > 1f ? 0.6f : 0f;
        input.Steer = MathUtil.Clamp(angle / car.MaxSteerAngleAt(car.Speed), -1f, 1f);
        return input;
    }

    /// <summary>
    /// A knock the car survived (RaceSession.LastKnock): the dent where it struck, sparks off a hard one, and the jolt
    /// in the seat. The steam of a badly knocked car is <see cref="KnockSmoke"/>'s.
    /// </summary>
    private void ShowKnock()
    {
        var knock = _session.LastKnock;
        var point = Convert.ToGodot(knock.Point);
        var push = -Convert.ToGodot(knock.Normal);
        _carView.Knock(point, push, knock.Speed);
        _cameraRig.Jolt(-push, knock.Speed);
        if (!_retro && knock.Speed > 4f && knock.Kind != KnockKind.Cow) _knockSmoke.Sparks(_carView.GlobalTransform * point, _carView.GlobalTransform.Basis * push, knock.Speed);
    }

    /// <summary>The cows where the herd has them, each posed for what it is doing (CowRig).</summary>
    private void SyncCows(float dt)
    {
        foreach (var cow in _session.Herd.Cows)
        {
            if (_propNodes.TryGetValue(cow.Prop, out var node) && node is CowRig rig) rig.Pose(cow, _elapsed, dt);
        }
    }

    /// <summary>The replay is over or cut short: the car is repaired and the debris cleared.</summary>
    private void EndCrash()
    {
        if (_crash is null) return;
        _crash.End();
        _crash = null;
        _impactShown = false;
        _hud.GlassCracks = 0f;
        // The chase camera starts afresh behind the car set down (0.35: it eased over from the crash site for a second).
        _cameraRig.ResetChase();
    }

    private void UpdateTipping()
    {
        for (var i = _tipping.Count - 1; i >= 0; i--)
        {
            var (node, start, yaw, axis) = _tipping[i];
            var t = MathUtil.Clamp01((_elapsed - start) / 0.35f);
            var angle = Mathf.Lerp(0f, 1.45f, 1f - (1f - t) * (1f - t));
            // The sign keeps its yaw and leans over about its base; the lean is applied after the yaw (right-to-left).
            node.Basis = new Basis(axis, angle) * new Basis(Vector3.Up, yaw);
            if (t >= 1f) _tipping.RemoveAt(i);
        }
    }

    /// <summary>The drones' speeds a frame ago and how long their brake lamps stay lit (SyncTraffic, 0.69).</summary>
    private float[] _droneSpeedWas = [];
    private float[] _droneBrakeHold = [];

    private void SyncTraffic(float dt)
    {
        if (_session.Traffic is null) return;
        var cars = _session.Traffic.Cars;
        if (_droneSpeedWas.Length != cars.Count)
        {
            _droneSpeedWas = new float[cars.Count];
            _droneBrakeHold = new float[cars.Count];
            for (var i = 0; i < cars.Count; i++) _droneSpeedWas[i] = cars[i].Speed;
        }
        var running = _session.Phase is SessionPhase.Driving or SessionPhase.StartEngine;
        for (var i = 0; i < cars.Count && i < _droneViews.Count; i++)
        {
            _droneViews[i].SetTrafficPose(cars[i].Position, cars[i].Orientation);
            _droneViews[i].Headlights = WorldBuilder.CarLampsLit;
            if (running || (_session.Phase == SessionPhase.Replay && !_session.ReplayPlaying)) _droneViews[i].AdvanceWheels(cars[i].Speed * dt, 0f);
            // The traffic's brake lamps (0.69): lit while a drone slows by more than 1.2 m/s^2 (closing on the player or a
            // slower car), held 0.4 s so that they do not flicker; until now only the player's car ever lit its own.
            if (running && dt > 0f)
            {
                var slowing = (_droneSpeedWas[i] - cars[i].Speed) / dt;
                _droneBrakeHold[i] = slowing > 1.2f ? 0.4f : MathF.Max(0f, _droneBrakeHold[i] - dt);
            }
            _droneSpeedWas[i] = cars[i].Speed;
            _droneViews[i].Braking = running && _droneBrakeHold[i] > 0f;
        }
    }

    /// <summary>
    /// The drones in the replay (0.35; until then they stood frozen where the crash had left them): where the recording
    /// has them at the playback's game time, the crash and the wreck window included, by the same clock as the car
    /// (CrashScene.Pose: the approach at its pace, the first second after the blow slow).
    /// </summary>
    private void SyncTrafficReplay(float replayTime)
    {
        var samples = _session.ReplayTraffic;
        var poses = _session.Replay;
        if (samples.Count < 2 || _droneViews.Count == 0 || poses.Count != samples.Count) return;
        var crashTime = _session.CrashTime;
        var approach = _session.Options.ReplaySeconds - _session.Options.ImpactSeconds;
        var slowSpan = RaceSession.ReplaySlowSeconds / RaceSession.ReplaySlowRate;
        float gameTime;
        if (replayTime < approach) gameTime = crashTime - approach + replayTime;
        else
        {
            var into = replayTime - approach;
            gameTime = into < slowSpan ? crashTime + into * RaceSession.ReplaySlowRate : crashTime + RaceSession.ReplaySlowSeconds + (into - slowSpan);
        }
        var span = poses[^1].Time - poses[0].Time;
        var index = span > 1e-4f ? System.Math.Clamp((gameTime - poses[0].Time) / span * (samples.Count - 1), 0f, samples.Count - 1) : 0f;
        var i0 = (int)index;
        var i1 = System.Math.Min(i0 + 1, samples.Count - 1);
        var f = index - i0;
        for (var d = 0; d < _droneViews.Count; d++)
        {
            if (d >= samples[i0].Length || d >= samples[i1].Length) continue;
            var position = SN.Vector3.Lerp(samples[i0][d].Position, samples[i1][d].Position, f);
            var orientation = SN.Quaternion.Normalize(SN.Quaternion.Slerp(samples[i0][d].Orientation, samples[i1][d].Orientation, f));
            var view = _droneViews[d];
            var before = view.GlobalPosition;
            view.SetTrafficPose(position, orientation);
            view.AdvanceWheels(view.GlobalPosition.DistanceTo(before), 0f);
        }
    }

    private void SyncGhost()
    {
        if (_ghostView is null) return;
        var visible = _session.GhostVisible && _session.Phase == SessionPhase.Driving;
        if (visible)
        {
            var previous = _ghostView.GlobalPosition;
            _ghostView.SetPose(_session.GhostPosition, _session.GhostOrientation);
            if (_ghostView.Visible) _ghostView.AdvanceWheels(_ghostView.GlobalPosition.DistanceTo(previous), _session.GhostSteer);
        }
        _ghostView.Visible = visible;
    }

    /// <summary>
    /// The title screen: left/right change the car, 1-3 the gearbox, and Enter, Space or a click starts with
    /// them. Returns true for a start, which the caller turns into a turn of the key on the same frame, so one
    /// press goes from the title to a running engine (choosing and then turning the key lost players there).
    /// </summary>
    private bool HandleMenuKeys()
    {
        var click = _clicked;
        _clicked = false;
        if (_session.Phase != SessionPhase.SelectTransmission) return false;
        // Down (the key or the d-pad, not the brake trigger: a foot resting on it would open it), or a click on the car's
        // name: the showroom (0.25), where the cars are seen before one is taken.
        var titleCar = _titleCarClicked;
        _titleCarClicked = false;
        if (titleCar || (Input.IsActionJustPressed(InputSetup.Brake) && Input.GetJoyAxis(InputSetup.Pad, JoyAxis.TriggerLeft) < 0.4f))
        {
            OpenShowroom(_carIndex);
            return false;
        }
        // Left/right steps through the cars, either way round; the session is rebuilt around the new spec.
        var step = Input.IsActionJustPressed(InputSetup.SteerLeft) ? -1 : Input.IsActionJustPressed(InputSetup.SteerRight) ? 1 : 0;
        if (step != 0)
        {
            _carIndex = Lineup.Step(_carIndex, step);
            SaveChoice("car", _carIndex);
            NewSession();
            _audio.Chime();
            return false;
        }
        if (Input.IsActionJustPressed(InputSetup.SelectAuto)) SetGearbox(TransmissionMode.Automatic);
        else if (Input.IsActionJustPressed(InputSetup.SelectManual)) SetGearbox(TransmissionMode.ManualAutoClutch);
        else if (Input.IsActionJustPressed(InputSetup.SelectClutch)) SetGearbox(TransmissionMode.ManualClutch);
        // The shoulders (or E/Q) step through the gearboxes: a pad has no number keys.
        else if (Input.IsActionJustPressed(InputSetup.ShiftUp)) SetGearbox((TransmissionMode)(((int)_gearbox + 1) % 3));
        else if (Input.IsActionJustPressed(InputSetup.ShiftDown)) SetGearbox((TransmissionMode)(((int)_gearbox + 2) % 3));
        if (!Input.IsActionJustPressed(InputSetup.Ignition) && !Input.IsActionJustPressed(InputSetup.Abort) && !click) return false;
        _session.ChooseTransmission(_gearbox);
        return true;
    }

    private void SetGearbox(TransmissionMode mode)
    {
        _gearbox = mode;
        _hud.Gearbox = mode;
        SaveChoice("transmission", (int)mode);
        _audio.Chime();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_showroom is { } room)
        {
            HandleShowroomMouse(room, @event);
            return;
        }
        if (_menu.Open && @event is InputEventMouse mouse)
        {
            var line = PauseMenu.HitTest(GetViewport().GetVisibleRect().Size, mouse.Position);
            if (line >= 0) _menu.Select(line);
            if (line >= 0 && mouse is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _menuClick = true;
            return;
        }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            // On the title screen a click on the car's name opens the showroom; anywhere else it starts the game.
            if (_session is not null && _session.Phase == SessionPhase.SelectTransmission && Hud.TitleCarRect(GetViewport().GetVisibleRect().Size).HasPoint(click.Position)) _titleCarClicked = true;
            else _clicked = true;
        }
    }

    /// <summary>
    /// The showroom's mouse: a drag turns the car, the wheel brings it closer, a click takes a chevron, a swatch or a
    /// button; with the picker open, a click or a drag on its square or its hue bar mixes the colour, and its buttons
    /// take a finish, keep the colour or cancel it.
    /// </summary>
    private void HandleShowroomMouse(Showroom room, InputEvent @event)
    {
        var size = GetViewport().GetVisibleRect().Size;
        if (room.Picker is { } picker && HandlePickerMouse(room, picker, size, @event)) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when !button.Pressed:
                room.Dragging = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var (hit, index) = Showroom.HitTest(size, button.Position);
                switch (hit)
                {
                    case Showroom.Hit.Left:
                        room.Step(-1);
                        _audio.Chime();
                        break;
                    case Showroom.Hit.Right:
                        room.Step(1);
                        _audio.Chime();
                        break;
                    case Showroom.Hit.Swatch:
                        // The mixed colour's swatch opens the picker on it; another swatch is that paint.
                        if (index == Showroom.CustomSlot) room.OpenPicker();
                        else if (index == room.PaintSlot) break;
                        else room.ChooseSlot(index);
                        _audio.Chime();
                        break;
                    // With the picker open the two buttons wait for it, as Enter and Esc do (its DONE or CANCEL first; they
                    // are drawn dimmed): a click kept until then took the car or left the showroom once the picker closed,
                    // long after it was made, and lost the colour being mixed (the showroom's review).
                    case Showroom.Hit.Take:
                        if (room.Picker is null) _showroomTake = true;
                        break;
                    case Showroom.Hit.Back:
                        if (room.Picker is null) _showroomBack = true;
                        break;
                    default:
                        room.Dragging = true;
                        break;
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                room.Zoom(1);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                room.Zoom(-1);
                break;
            case InputEventMouseMotion motion when room.Dragging:
                room.Drag(motion.Relative.X);
                break;
        }
    }

    /// <summary>The picker's part of the mouse; true when the event was the picker's (the rest is the showroom's).</summary>
    private bool HandlePickerMouse(Showroom room, ColourPicker picker, Vector2 size, InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when picker.Dragging != ColourPicker.Hit.Nothing:
                picker.Dragging = ColourPicker.Hit.Nothing;
                return true;
            case InputEventMouseMotion motion when picker.Dragging != ColourPicker.Hit.Nothing:
                picker.Pick(size, picker.Dragging, motion.Position);
                room.ApplyPicker();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button:
                var (hit, finish) = ColourPicker.HitTest(size, button.Position);
                switch (hit)
                {
                    case ColourPicker.Hit.Square or ColourPicker.Hit.HueBar:
                        picker.Dragging = hit;
                        picker.Pick(size, hit, button.Position);
                        room.ApplyPicker();
                        break;
                    case ColourPicker.Hit.Finish:
                        picker.SetFinish(finish);
                        room.ApplyPicker();
                        break;
                    case ColourPicker.Hit.Done:
                        room.ClosePicker(keep: true);
                        _audio.Chime();
                        break;
                    case ColourPicker.Hit.Cancel:
                        room.ClosePicker(keep: false);
                        break;
                    case ColourPicker.Hit.Panel:
                        break;
                    default:
                        return false;
                }
                return true;
            default:
                return false;
        }
    }

    /// <summary>Opens or closes the menu; the race and its sound stand still while it is open.</summary>
    /// <summary>
    /// The menu's weather (0.15): left and right step through clear, overcast, wet road, rain and random (a draw for every
    /// credit). A fixed choice takes effect at once, on the road's grip and in the air; the choice is kept.
    /// </summary>
    private void CycleWeather(int step)
    {
        _weatherChoice = (_weatherChoice + step + 5) % 5;
        SaveChoice("weather", _weatherChoice);
        if (_weatherChoice < 4)
        {
            _session.SetWeather((Weather)_weatherChoice);
            WorldBuilder.SetWeather(_session.Weather);
            ApplyRain();
        }
    }

    /// <summary>"a,b" as a vector, invariant culture; zero when it does not parse.</summary>
    private static Vector2 ParseVector2(string text)
    {
        var parts = text.Split(',');
        return parts.Length == 2
            && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
            ? new Vector2(x, y) : Vector2.Zero;
    }

    /// <summary>
    /// The menu's paint (0.21): left and right step through the car's own colour, the classic ones and the colour it was
    /// last mixed in (the showroom's picker, 0.25); the car is repainted at once and the choice kept for this car. A
    /// lofted body (a checkout without the models) is built again in it.
    /// </summary>
    private void CyclePaint(int step)
    {
        var options = _paints.Options(_carIndex);
        var at = System.Array.IndexOf(options, _paints[_carIndex]);
        _paints.Set(_carIndex, options[(Math.Max(at, 0) + step + options.Length) % options.Length]);
        if (_carView.Repaint(PlayerPaint)) return;
        // During a crash's replay the car is the scene's (CrashScene): it is built again with the next credit.
        if (_crash is null) BuildPlayerCar();
        else _repaintLofted = true;
    }

    private string WeatherLabel() => _weatherChoice switch { 0 => "CLEAR", 1 => "OVERCAST", 2 => "WET ROAD", 3 => "RAIN", _ => "RANDOM" };

    /// <summary>
    /// The menu's hour (0.55): left and right step through the day, the sunrise and random (a draw for every credit). A
    /// fixed choice takes effect at once; the choice is kept.
    /// </summary>
    private void CycleLight(int step)
    {
        // The menu's order: day, sunrise, dusk, night, random; the stored numbers keep 0.55's (random 2) and add the
        // night as 3 (0.57) and the dusk as 4 (0.60).
        int[] order = [0, 1, 4, 3, 2];
        var at = Array.IndexOf(order, _lightChoice);
        _lightChoice = order[(Math.Max(at, 0) + step + order.Length) % order.Length];
        SaveChoice("light", _lightChoice);
        if (_lightChoice != 2) WorldBuilder.SetHour(ChosenHour());
    }

    /// <summary>The menu's hour: the one chosen, or one of the three drawn for the credit.</summary>
    private WorldBuilder.Hour ChosenHour() => _lightChoice switch
    {
        1 => WorldBuilder.Hour.Sunrise,
        3 => WorldBuilder.Hour.Night,
        4 => WorldBuilder.Hour.Dusk,
        2 => (WorldBuilder.Hour)System.Random.Shared.Next(4),
        _ => WorldBuilder.Hour.Day,
    };

    private string LightLabel() => _lightChoice switch { 0 => "DAY", 1 => "SUNRISE", 3 => "NIGHT", 4 => "DUSK", _ => "RANDOM" };

    private float _windClock;

    /// <summary>
    /// The prairie's wind on the car (0.61, VehicleSim.Wind): the way the grass leans (assets/shaders/wind.gdshaderinc,
    /// towards Godot's -x and -z), 6 m/s on average, a Wyoming day's, gusting from a third of that to half again over
    /// seconds (three slow waves, none a multiple of another); still in the 1989 look, whose cabinet knew no wind.
    /// </summary>
    private SN.Vector3 PrairieWind(float dt)
    {
        _windClock += dt;
        var t = _windClock;
        var gust = 1f + 0.35f * MathF.Sin(t * 0.9f) + 0.2f * MathF.Sin(t * 2.3f + 1f) + 0.15f * MathF.Sin(t * 0.37f + 2f);
        var speed = _retro ? 0f : 6f * MathF.Max(gust, 0.3f);
        return Convert.ToSim(new Vector3(-0.944f, 0f, -0.330f) * speed);
    }

    /// <summary>
    /// Automation (--perf-log): the main view's measured GPU time and the frame's CPU time, averaged over five
    /// seconds, under the game's own frame cap (an uncapped run has crashed the development laptop's GPU).
    /// </summary>
    private void LogPerformance()
    {
        var viewport = GetViewport().GetViewportRid();
        if (_perfFrames == 0 && _perfSince == 0.0) RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        _perfGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        _perfCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        if (_showroom is { } room)
        {
            // The stage renders in its own viewport while the main one draws no 3D: its time is the showroom's frame.
            RenderingServer.ViewportSetMeasureRenderTime(room.ViewportRid, true);
            _perfGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(room.ViewportRid);
            _perfCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(room.ViewportRid);
        }
        _perfFrames++;
        if (_elapsed - _perfSince < 5.0) return;
        GD.Print($"perf: {_perfFrames / (_elapsed - _perfSince):0.0} fps, gpu {_perfGpu / _perfFrames:0.00} ms, cpu {_perfCpu / _perfFrames:0.00} ms a frame{(_showroom is { } open ? $" (showroom: {open.Spec.Name}, turntable at {open.YawDegrees:0} deg)" : "")}");
        _perfGpu = _perfCpu = 0.0;
        _perfFrames = 0;
        _perfSince = _elapsed;
    }

    /// <summary>A long file name cut to fit the menu's panel, with an ellipsis.</summary>
    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 3)] + "...";

    /// <summary>
    /// A music file dropped on the game's window becomes the instant replay's tune (0.15, EngineAudio.AdoptReplayMusic);
    /// the menu opens on its line so the player sees it was taken.
    /// </summary>
    private void OnFilesDropped(string[] files)
    {
        foreach (var file in files)
        {
            if (_audio.AdoptReplayMusic(file) is null) continue;
            GD.Print($"replay music taken from {file}");
            ShowMenu(true);
            _menu.Select((int)PauseMenu.Item.Music);
            return;
        }
    }

    private void ShowMenu(bool open)
    {
        if (open == _menu.Open) return;
        // A tune dropped into the music folder while the game runs is picked up when the menu opens.
        if (open) _audio.RefreshReplayMusic();
        if (open) _menu.Show();
        else _menu.Hide();
        AudioServer.SetBusMute(0, open);
        _keyboard.Reset();
    }

    /// <summary>
    /// Opens the showroom (<see cref="Showroom"/>) on a car, over the title screen or over the paused credit (the menu's
    /// CAR line). The main viewport's 3D is switched off while it is open: the game's world would otherwise be drawn
    /// under the stage at a driven frame's cost and never seen.
    /// </summary>
    private void OpenShowroom(int carIndex)
    {
        if (_showroom is not null) return;
        _propMaterialsBeforeShowroom ??= WorldBuilder.PropMaterialKeys();
        _showroom = Showroom.Create(carIndex, _paints, float.IsNaN(_turn) ? Showroom.StartYawDegrees : _turn, fromMenu: _menu.Open, held: !float.IsNaN(_turn));
        _showroom.ScreenSize = GetViewport().GetVisibleRect().Size;
        AddChild(_showroom);
        _showroom.Resize(GetWindow().Size);
        _showroom.ApplyGraphics(_quality, _retro);
        GetViewport().Disable3D = true;
        _hud.Showroom = _showroom;
        _keyboard.Reset();
        _clicked = false;
        _showroomTake = _showroomBack = false;
    }

    /// <summary>
    /// Closes the showroom. Taken (Enter), the car and the paints chosen there are kept ("car" and each car's
    /// "paint.&lt;car&gt;" in the settings, <see cref="PaintBook"/>) and applied: another car by a new session, as the
    /// title screen's left/right always did, a paint on the car where it stands. Left (Esc), nothing changes. Either way
    /// the player is back where the showroom was opened, the title or the menu.
    /// </summary>
    private void CloseShowroom(bool take)
    {
        if (_showroom is not { } room) return;
        _showroom = null;
        _hud.Showroom = null;
        GetViewport().Disable3D = false;
        room.Dragging = false;
        room.QueueFree();
        _collectAfterShowroom = true;
        _keyboard.Reset();
        _clicked = false;
        _menuClick = false;
        if (!take) return;
        // A colour still being mixed is the car's: Enter in the picker and Enter here both mean "this one".
        room.ClosePicker(keep: true);
        var anotherCar = room.Index != _carIndex;
        _carIndex = room.Index;
        foreach (var (car, paint) in room.Edits)
        {
            if (paint != _paints[car]) _paints.Set(car, paint);
        }
        SaveChoice("car", _carIndex);
        if (anotherCar)
        {
            ShowMenu(false);
            NewSession();
            _audio.Chime();
        }
        else if (!_carView.Repaint(PlayerPaint))
        {
            // A lofted body (a checkout without the models) is built again in the paint; during a crash's replay the
            // car is the scene's and is built again with the next credit (as CyclePaint).
            if (_crash is null) BuildPlayerCar();
            else _repaintLofted = true;
        }
    }

    /// <summary>
    /// The showroom's keys: left and right (A, D, the stick, the d-pad) step the car, up and down (W, S) the paint, C or
    /// Y open the colour picker, Enter or A take the car and the paint, Esc, Back or B leave it as it was; Space takes
    /// it over the title screen and goes back over the menu, as it does on each. A step chimes, as the title screen's
    /// did. While the picker is open the keys are its own (<see cref="HandlePicker"/>).
    /// </summary>
    private void HandleShowroom()
    {
        var room = _showroom!;
        room.Resize(GetWindow().Size);
        room.ScreenSize = GetViewport().GetVisibleRect().Size;
        var abort = Input.IsActionJustPressed(InputSetup.Abort);
        // Space and the pad's B share an action. B goes back. Space takes over the title screen, where it starts the
        // game, and goes back over the menu, whose footer says so: taking there ends the race in progress, and it did on
        // a press the menu had taught to mean back (the showroom's review).
        var padB = abort && Input.IsJoyButtonPressed(InputSetup.Pad, JoyButton.B);
        var spaceBack = abort && !padB && room.FromMenu;
        if (room.Picker is { } picker)
        {
            // A click on TAKE IT or BACK is not kept for after the picker (HandleShowroomMouse ignores them; this is the
            // belt to its braces).
            _showroomTake = _showroomBack = false;
            HandlePicker(room, picker, abort, padB);
            return;
        }
        if (Input.IsActionJustPressed(InputSetup.Camera))
        {
            room.OpenPicker();
            _audio.Chime();
            return;
        }
        if (Input.IsActionJustPressed(InputSetup.Menu) || padB || spaceBack || _showroomBack)
        {
            CloseShowroom(take: false);
            return;
        }
        if (Input.IsActionJustPressed(InputSetup.Ignition) || (abort && !padB) || _showroomTake)
        {
            CloseShowroom(take: true);
            return;
        }
        var step = Input.IsActionJustPressed(InputSetup.SteerLeft) ? -1 : Input.IsActionJustPressed(InputSetup.SteerRight) ? 1 : 0;
        if (step != 0)
        {
            room.Step(step);
            _audio.Chime();
        }
        var paint = Input.IsActionJustPressed(InputSetup.Throttle) ? -1 : Input.IsActionJustPressed(InputSetup.Brake) ? 1 : 0;
        if (paint != 0)
        {
            room.CyclePaint(paint);
            _audio.Chime();
        }
    }

    /// <summary>
    /// The colour picker's keys: up and down choose the row (hue, saturation, lightness, finish), left and right change
    /// it (a tap a step, held faster and faster), Enter, Space, A, C or Y keep the colour, Esc, Back or B put the paint
    /// back as it was. Taking the car or leaving the showroom waits for the picker to close, so neither happens by a key
    /// meant for the picker.
    /// </summary>
    private void HandlePicker(Showroom room, ColourPicker picker, bool abort, bool padB)
    {
        if (Input.IsActionJustPressed(InputSetup.Menu) || padB)
        {
            room.ClosePicker(keep: false);
            return;
        }
        if (Input.IsActionJustPressed(InputSetup.Ignition) || Input.IsActionJustPressed(InputSetup.Camera) || abort)
        {
            room.ClosePicker(keep: true);
            _audio.Chime();
            return;
        }
        if (Input.IsActionJustPressed(InputSetup.Throttle)) picker.MoveFocus(-1);
        if (Input.IsActionJustPressed(InputSetup.Brake)) picker.MoveFocus(1);
        var held = (Input.IsActionPressed(InputSetup.SteerRight) ? 1 : 0) - (Input.IsActionPressed(InputSetup.SteerLeft) ? 1 : 0);
        var pressed = (Input.IsActionJustPressed(InputSetup.SteerRight) ? 1 : 0) - (Input.IsActionJustPressed(InputSetup.SteerLeft) ? 1 : 0);
        if (picker.Nudge(held, pressed, (float)GetProcessDeltaTime())) room.ApplyPicker();
    }

    /// <summary>The menu's keys: up and down move, Enter or A takes the line, left and right turn a setting, Space or B goes back.</summary>
    private void HandleMenu()
    {
        if (Input.IsActionJustPressed(InputSetup.Throttle)) _menu.Move(-1);
        if (Input.IsActionJustPressed(InputSetup.Brake)) _menu.Move(1);
        if (Input.IsActionJustPressed(InputSetup.Abort))
        {
            ShowMenu(false);
            return;
        }
        var left = Input.IsActionJustPressed(InputSetup.SteerLeft);
        var right = Input.IsActionJustPressed(InputSetup.SteerRight);
        var take = Input.IsActionJustPressed(InputSetup.Ignition) || _menuClick;
        _menuClick = false;
        if (!take && !left && !right) return;
        switch (_menu.Current)
        {
            case PauseMenu.Item.Continue when take:
                ShowMenu(false);
                break;
            case PauseMenu.Item.NewGame when take:
                ShowMenu(false);
                NewSession();
                break;
            case PauseMenu.Item.Car:
                // The showroom (0.25) over the paused credit; left and right go straight on to the next car there.
                OpenShowroom(_carIndex);
                if (!take && _showroom is not null) _showroom.Step(left ? -1 : 1);
                break;
            case PauseMenu.Item.Camera:
                ToggleCamera();
                break;
            case PauseMenu.Item.Graphics:
                CycleQuality(left ? -1 : 1);
                break;
            case PauseMenu.Item.Weather:
                CycleWeather(left ? -1 : 1);
                break;
            case PauseMenu.Item.Light:
                CycleLight(left ? -1 : 1);
                break;
            case PauseMenu.Item.Paint:
                CyclePaint(left ? -1 : 1);
                break;
            case PauseMenu.Item.Retro:
                SetRetro(!_retro);
                break;
            case PauseMenu.Item.Music when take:
                // The player's own replay tune goes into the user folder (EngineAudio.CustomReplayMusic): open it for them.
                OS.ShellOpen(ProjectSettings.GlobalizePath("user://music"));
                break;
            case PauseMenu.Item.Units:
                _instruments.UseKmh = !_instruments.UseKmh;
                SaveChoice("units", _instruments.UseKmh ? 1 : 0);
                break;
            case PauseMenu.Item.Fullscreen:
                ToggleFullscreen();
                break;
            case PauseMenu.Item.Quit when take:
                Shutdown();
                break;
        }
    }

    private void FillMenuLabels()
    {
        var labels = _menu.Labels;
        labels[(int)PauseMenu.Item.Continue] = "CONTINUE";
        labels[(int)PauseMenu.Item.NewGame] = "NEW GAME";
        labels[(int)PauseMenu.Item.Car] = $"CAR:  {VehicleSpec.Catalogue[_carIndex].Name}";
        labels[(int)PauseMenu.Item.Camera] = $"CAMERA:  {(_cameraRig.Mode == CameraMode.Cockpit ? "DRIVER'S SEAT" : "BEHIND THE CAR")}";
        labels[(int)PauseMenu.Item.Graphics] = $"GRAPHICS:  {_quality.ToString().ToUpperInvariant()}";
        labels[(int)PauseMenu.Item.Weather] = $"WEATHER:  {WeatherLabel()}";
        labels[(int)PauseMenu.Item.Light] = $"LIGHT:  {LightLabel()}";
        labels[(int)PauseMenu.Item.Paint] = $"PAINT:  {_paints[_carIndex].Name}";
        labels[(int)PauseMenu.Item.Retro] = $"1989 MODE:  {(_retro ? "ON" : "OFF")}";
        // The player's own tune by name (without its extension); the built-in one with the hint that Enter opens the folder.
        labels[(int)PauseMenu.Item.Music] = _audio.CustomReplayMusicName is { } tune
            ? $"REPLAY MUSIC:  {Shorten(System.IO.Path.GetFileNameWithoutExtension(tune).ToUpperInvariant(), 26)}"
            : "REPLAY MUSIC:  BUILT-IN  (ENTER: FOLDER)";
        labels[(int)PauseMenu.Item.Units] = $"UNITS:  {(_instruments.UseKmh ? "KM/H" : "MPH")}";
        labels[(int)PauseMenu.Item.Fullscreen] = $"FULL SCREEN:  {(GetWindow().Mode == Window.ModeEnum.Fullscreen ? "ON" : "OFF")}";
        labels[(int)PauseMenu.Item.Quit] = "QUIT GAME";
    }

    private void ToggleFullscreen()
    {
        var window = GetWindow();
        window.Mode = window.Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
    }

    private void ToggleCamera()
    {
        _cameraRig.Mode = _cameraRig.Mode == CameraMode.Chase ? CameraMode.Cockpit : CameraMode.Chase;
        _cameraRig.ResetChase();
    }

    private void CycleQuality(int step)
    {
        _quality = (GraphicsQuality)(((int)_quality + step + 3) % 3);
        SaveChoice("quality", (int)_quality);
        if (!_retro) ApplyGraphics();
    }

    private void HandleGlobalKeys()
    {
        if (Input.IsActionJustPressed(InputSetup.Fullscreen)) ToggleFullscreen();
        // In the showroom C (and the pad's Y) opens the colour picker: the camera behind it stays as it is.
        if (Input.IsActionJustPressed(InputSetup.Camera) && _showroom is null) ToggleCamera();
        if (Input.IsActionJustPressed(InputSetup.Units))
        {
            _instruments.UseKmh = !_instruments.UseKmh;
            SaveChoice("units", _instruments.UseKmh ? 1 : 0);
        }
        if (Input.IsActionJustPressed(InputSetup.NewGame))
        {
            CloseShowroom(take: false);
            ShowMenu(false);
            NewSession();
        }
        if (Input.IsActionJustPressed(InputSetup.Retro)) SetRetro(!_retro);
        if (Input.IsActionJustPressed(InputSetup.Quality))
        {
            CycleQuality(1);
            Notice($"GRAPHICS: {_quality.ToString().ToUpperInvariant()}");
        }
        if (Input.IsActionJustPressed(InputSetup.FpsCounter))
        {
            _hud.ShowFps = !_hud.ShowFps;
            SaveChoice("fps", _hud.ShowFps ? 1 : 0);
        }
        if (Input.IsActionJustPressed(InputSetup.Update))
        {
            if (_updater.ReadyToApply) _updater.ApplyNow();
            else if (_updater.Available is not null) _updater.DownloadAndInstall();
            else _updater.CheckIfDue(manual: true);
        }
        if (Input.IsActionJustPressed(InputSetup.Attract))
        {
            _attract = !_attract;
            if (_attract && _session.Phase == SessionPhase.SelectTransmission) _session.ChooseTransmission(_attractMode);
        }
        if (Input.IsActionJustPressed(InputSetup.Screenshot))
        {
            var dir = OS.GetUserDataDir();
            CallDeferred(nameof(TakeScreenshot), System.IO.Path.Combine(dir, $"grudgelap-{System.DateTime.Now:yyyyMMdd-HHmmss}.png"));
        }
    }

    /// <summary>A line on the HUD for a couple of seconds.</summary>
    private void Notice(string text)
    {
        _hud.Overlay = text;
        _overlayUntil = _elapsed + 2.5f;
    }

    /// <summary>
    /// The viewport's part of the graphics preset (the environment's is <see cref="WorldBuilder.SetQuality"/>): Low
    /// renders at three quarters of the window with FXAA instead of multisampling and temporal anti-aliasing. On a
    /// window taller than 1200 pixels the 3D picture is rendered at 1080 lines and scaled up with FSR, so that a
    /// large monitor cannot put the GPU back where it was before the frame cap (see <see cref="ApplyFrameCap"/>).
    /// </summary>
    private void ApplyGraphics()
    {
        var viewport = GetViewport();
        var low = _quality == GraphicsQuality.Low;
        var height = viewport.GetVisibleRect().Size.Y;
        var ceiling = height > 1200f ? 1080f / height : 1f;
        viewport.Scaling3DMode = ceiling < 1f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = (low ? 0.75f : 1f) * ceiling;
        // No multisampling under the motion blur (0.38): the effect cannot write a colour resolved from MSAA, and TAA
        // smooths the edges as well on its own.
        var blur = !low && WorldBuilder.MotionBlurOverride != false;
        viewport.Msaa3D = low || blur ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X;
        viewport.ScreenSpaceAA = low ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = !low;
        if (_fsr2Scale > 0f)
        {
            // FSR 2 does its own temporal anti-aliasing, and takes no multisampling.
            viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr2;
            viewport.Scaling3DScale = _fsr2Scale;
            viewport.Msaa3D = Viewport.Msaa.Disabled;
            viewport.UseTaa = false;
        }
        WorldBuilder.SetQuality(_quality);
        _showroom?.ApplyGraphics(_quality, _retro);
    }

    /// <summary>"1989 mode": a third of the resolution, 20 frames a second, flat colours and no atmosphere.</summary>
    private void SetRetro(bool retro)
    {
        _retro = retro;
        if (retro)
        {
            var viewport = GetViewport();
            viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
            viewport.Scaling3DScale = 0.32f;
            viewport.Msaa3D = Viewport.Msaa.Disabled;
            viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
            viewport.UseTaa = false;
        }
        ApplyFrameCap();
        WorldBuilder.SetRetro(retro);
        if (!retro) ApplyGraphics();
        _audio.Retro = retro;
        _world.Retro = retro;
        _hud.Retro = retro;
        ApplyRain();
        _showroom?.ApplyGraphics(_quality, retro);
    }

    private void TakeScreenshot(string path)
    {
        try
        {
            var image = GetViewport().GetTexture().GetImage();
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            var error = image.SavePng(path);
            GD.Print(error == Error.Ok ? $"screenshot saved: {path}" : $"screenshot failed: {error}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"screenshot failed: {ex.Message}");
        }
    }
}
