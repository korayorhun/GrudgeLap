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
/// entry screen), <c>--quality=low|medium|high</c> (the graphics preset for this run) and <c>--quit-after=&lt;seconds&gt;</c>.
/// </summary>
public sealed partial class Bootstrap : Node3D
{
    private const string BestLapFile = "user://bestlap.bin";
    private const string SettingsFile = "user://settings.cfg";

    private RaceSession _session = null!;
    private TrackGeometry _track = null!;
    private CarView _carView = null!;
    private CrashScene? _crash;
    private string? _ram;
    private SN.Vector3? _ramTarget;
    private CarView? _ghostView;
    private readonly List<CarView> _droneViews = new();
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
    private string? _jingle;          // automation: a jingle played a second in, to check the synth (--jingle)
    private readonly HashSet<string> _soundFaults = new();
    private readonly PauseMenu _menu = new();
    private bool _menuClick;
    private bool _startChase;
    private bool _retro;
    private GraphicsQuality _quality = GraphicsQuality.High;
    private float _overlayUntil = -1f;   // a short notice (graphics preset) on the HUD until then
    private bool _invisible;
    private bool _leaving;      // a second launch on its way out: nothing was built
    private bool _noGhost;
    private bool _noTraffic;
    private bool _noHud;        // pictures of the 3D scene alone, e.g. to compare it with another engine's
    private Vector3? _orbit;
    private bool _showAll;      // debug: draw the whole car from the driver's seat too
    private bool _saveBest;
    private string? _exportWorld;  // --export-world=<file.glb>: write the track world for another engine, then quit
    private int _carIndex;
    private string? _screenshotPath;
    private float _screenshotAt = 4f;
    private float _quitAfter = -1f;
    private float _elapsed;
    private bool _screenshotTaken;
    private TransmissionMode _attractMode = TransmissionMode.ManualAutoClutch;
    private TransmissionMode _gearbox;  // what the title screen starts with: the last choice, automatic at first
    private bool _clicked;
    private bool _focused = true;
    private float _frameCapCheckAt;

    public override void _Ready()
    {
        InputSetup.Register();
        _carIndex = Math.Clamp(LoadChoice("car", 0), 0, VehicleSpec.Catalogue.Length - 1);
        _gearbox = (TransmissionMode)Math.Clamp(LoadChoice("transmission", (int)TransmissionMode.Automatic), 0, 2);
        _quality = (GraphicsQuality)Math.Clamp(LoadChoice("quality", (int)GraphicsQuality.High), 0, 2);
        ParseArgs();   // may override the saved car with --car=
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

        _track = OriginalInspiredTrack.Build();
        if (_ram == "test")
        {
            // The crash replay's test (--ram=test): a tree on the start straight, 80 m on, in the middle of the road.
            var spot = _track.Common.FrameAt(80f);
            _track.Props.Add(new TrackProp(PropKind.Tree, spot.Origin, 0f, new SN.Vector3(1.0f, 7f, 1.0f), "crash-test"));
        }
        WorldBuilder.Build(this, _track);
        // Before the train and the drones: they hand their nodes to it for their sounds.
        _world = new WorldAudio();
        AddChild(_world);
        for (var i = 0; i < _track.Props.Count; i++)
        {
            var node = PropBuilder.Build(_track.Props[i], _track.Terrain);
            _propNodes[i] = node;
            AddChild(node);
        }
        if (_track.Train is { } train)
        {
            _trainView = TrainView.Create(train, _track.Terrain);
            AddChild(_trainView);
            AddChild(TrainView.BuildRailway(train, _track));
            _world.AttachTrain(_trainView.Locomotive, train);
        }
        GetViewport().UseTaa = true;
        // A window resized (or made full screen) changes the render ceiling of ApplyGraphics.
        GetViewport().SizeChanged += () =>
        {
            if (!_retro && !_leaving) ApplyGraphics();
        };

        _carView = CarView.Create(VehicleSpec.Catalogue[_carIndex]);
        AddChild(_carView);
        _cameraRig = new CameraRig { Mode = _orbit is not null ? CameraMode.Orbit : _startChase ? CameraMode.Chase : CameraMode.Cockpit };
        if (_orbit is { } orbit) _cameraRig.Orbit = orbit;
        AddChild(_cameraRig);
        _hud = new Hud { Gearbox = _gearbox, ShowFps = LoadChoice("fps", 0) != 0 };
        _scores = new ScoreBoard();
        _hud.Scores = _scores;
        _hud.Menu = _menu;
        var layer = new CanvasLayer { Visible = !_noHud };
        layer.AddChild(_hud);
        AddChild(layer);
        _audio = new EngineAudio();
        AddChild(_audio);
        _updater = new Updater();
        AddChild(_updater);
        SubViewport panel;
        (panel, _instruments) = InstrumentCluster.Create();
        AddChild(panel);
        _instrumentPanel = panel.GetTexture();
        _hud.Instruments = _instrumentPanel;
        FitOutPlayerCar();

        _savedBestLap = LoadBestLap();
        NewSession();
        if (_exportWorld is not null)
        {
            // After the session: the car stands at the start and the cockpit camera is placed.
            _leaving = true;
            WorldExport.Write(this, _exportWorld, _carView, GetViewport().GetCamera3D());
            GetTree().Quit();
            return;
        }
        GetWindow().Title = $"GrudgeLap {Updater.CurrentVersionText}";
        if (_retro) SetRetro(true);
        else ApplyGraphics();
        ApplyFrameCap();
        // Automated runs never phone home; a player can also switch the check off in settings.cfg.
        if (!_attract && _screenshotPath is null && System.Environment.GetEnvironmentVariable("GRUDGELAP_NO_UPDATE") is null) _updater.CheckIfDue(manual: false);
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
            else if (arg.StartsWith("--ram", System.StringComparison.Ordinal)) _ram = arg.StartsWith("--ram=", System.StringComparison.Ordinal) ? arg["--ram=".Length..] : "tree";   // automated runs: drive into a tree, a building or a drone for the crash replay
            else if (arg == "--try-table") _tryTable = true;
            else if (arg.StartsWith("--quality=", System.StringComparison.Ordinal) && Enum.TryParse<GraphicsQuality>(arg["--quality=".Length..], ignoreCase: true, out var quality)) _quality = quality;   // this run only
            else if (arg.StartsWith("--time=", System.StringComparison.Ordinal)) float.TryParse(arg["--time=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _creditSeconds);
            else if (arg == "--chase") _startChase = true;
            else if (arg == "--retro") _retro = true;
            else if (arg == "--invisible") _invisible = true;   // automated runs: no window on screen, no focus taken
            else if (arg == "--no-ghost") _noGhost = true;       // pictures of the car without the best lap's ghost over it
            else if (arg == "--no-traffic") _noTraffic = true;
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
            else if (arg.StartsWith("--at=", System.StringComparison.Ordinal)) float.TryParse(arg["--at=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _screenshotAt);
            else if (arg.StartsWith("--quit-after=", System.StringComparison.Ordinal)) float.TryParse(arg["--quit-after=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _quitAfter);
            else if (arg.StartsWith("--new-game-after=", System.StringComparison.Ordinal)) float.TryParse(arg["--new-game-after=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _newGameAt);
            else if (arg.StartsWith("--menu-after=", System.StringComparison.Ordinal)) float.TryParse(arg["--menu-after=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _menuAt);
            else if (arg.StartsWith("--jingle=", System.StringComparison.Ordinal)) _jingle = arg["--jingle=".Length..];
            else if (arg == "--auto") _attractMode = TransmissionMode.Automatic;
            else if (arg == "--show-all") _showAll = true;
            else if (arg == "--save-best") _saveBest = true;   // debug: let an attract lap become the saved best lap
            else if (arg.StartsWith("--export-world=", System.StringComparison.Ordinal)) _exportWorld = arg["--export-world=".Length..];
            else if (arg.StartsWith("--car=", System.StringComparison.Ordinal)) _carIndex = CarIndex(arg["--car=".Length..], _carIndex);
        }
    }

    /// <summary>The car a --car= switch names: its index in the catalogue or one of the short names; otherwise the current one.</summary>
    private static int CarIndex(string name, int current)
    {
        string[] names = ["flat12", "v8", "hothatch", "five", "turbohatch"];
        var index = System.Array.IndexOf(names, name.ToLowerInvariant());
        if (index < 0 && int.TryParse(name, out var number)) index = number;
        return index >= 0 && index < VehicleSpec.Catalogue.Length ? index : current;
    }

    /// <summary>
    /// What only the player's car has: the lit instruments in its binnacle, a working rear-view mirror, and the
    /// eye point the cockpit camera sits at. Drones and the ghost stay without.
    /// </summary>
    private void FitOutPlayerCar()
    {
        _carView.MountInstruments(_instrumentPanel);
        _mirror = RearMirror.Create(new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.055f), Roughness = 0.5f });
        _carView.MountMirror(_mirror);
        _cameraRig.Eye = _carView.Eye;
    }

    private void NewSession()
    {
        var options = _noTraffic ? RaceOptions.MediumNoTraffic : RaceOptions.Medium;
        if (_creditSeconds > 0f) options = options with { InitialTime = _creditSeconds };
        var spec = VehicleSpec.Catalogue[_carIndex];
        _session = new RaceSession(_track, spec, options);
        EndCrash();
        if (_carView.IsInsideTree() && _carView.Name != $"Car-{spec.Style}")
        {
            _carView.QueueFree();
            _carView = CarView.Create(spec);
            AddChild(_carView);
            FitOutPlayerCar();
        }
        _carView.Name = $"Car-{spec.Style}";
        _audio.SetCar(spec);
        if (_savedBestLap is not null && !_noGhost) _session.LoadBestLap(_savedBestLap);
        _hud.Bind(_session);
        _instruments.Bind(_session);
        _scores.Dismiss();
        _scoreOffered = false;
        _keyboard.Reset();
        _cameraRig.ResetChase();
        BuildDroneViews();
        BuildGhostView();
        if (_attract || _park || _ram is not null)
        {
            _session.ChooseTransmission(_attractMode);
        }
        SyncView();
    }

    private void BuildDroneViews()
    {
        _world.DetachDrones();
        foreach (var view in _droneViews) view.QueueFree();
        _droneViews.Clear();
        if (_session.Traffic is null) return;
        foreach (var drone in _session.Traffic.Cars)
        {
            // The drones are the period cars of the original, whatever the player drives.
            var view = CarView.Create(VehicleSpec.FlatTwelve, CarView.DronePaints[drone.Colour % CarView.DronePaints.Length]);
            view.Name = "Drone";
            view.SetViewpoint(false);
            AddChild(view);
            _world.AttachDrone(view, drone);
            _droneViews.Add(view);
        }
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
        if (_leaving) return;
        var dt = (float)delta;
        _elapsed += dt;
        if (SingleInstance.ActivationRequested()) BringToFront();
        HandleGlobalKeys();
        if (Input.IsActionJustPressed(InputSetup.Menu)) ShowMenu(!_menu.Open);
        if (_menu.Open)
        {
            // The race stands still under the menu: no input reaches the car, the session does not step.
            HandleMenu();
            FillMenuLabels();
            SyncView();
            AutomationAfterFrame();
            return;
        }
        WorldBuilder.Tick(dt);
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

        var input = _ram is not null ? RamInputs() : _park ? DriverInputs.Idle : _attract ? _scripted.Next(_session) : _keyboard.Sample(dt, MathF.Abs(_session.Car.Speed));
        if (!_attract && !_park && HandleMenuKeys()) input.Ignition = true;

        if (_session.Phase == SessionPhase.Replay && Input.IsActionJustPressed(InputSetup.Abort)) _session.Abort();
        else if (_session.Phase == SessionPhase.Driving && _session.OffRoad && Input.IsActionJustPressed(InputSetup.Abort)) _session.Abort();

        _session.Step(input, dt);
        if (_session.Events.Contains(RaceEvent.NewBestLap) && _session.BestLap is not null && (!_attract || _saveBest))
        {
            SaveBestLap(_session.BestLap);
            if (_ghostView is null) BuildGhostView();
        }
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
        // The sound may fail without the race noticing: a fault there is logged once and the rest of the frame (the
        // picture, the clock, quitting) carries on. In 0.8.0 the world sound threw on every frame of a second credit
        // and the view stopped following the car: it stood at the line while the engine revved and the clock ran.
        try
        {
            _audio.Update(_session, dt);
            _world.Update(_session, _cameraRig.Listener, dt);
        }
        catch (System.Exception e)
        {
            if (_soundFaults.Add($"{e.GetType().Name}: {e.Message}")) GD.PushError($"sound update failed (logged once): {e}");
        }
        _hud.Notice = _updater.Status;
        SyncView();

        AutomationAfterFrame();
        // A downloaded update restarts the game only between credits: on the title screen or once the time is up.
        _updater.ApplyWhenIdle(_session.Phase is SessionPhase.SelectTransmission or SessionPhase.Finished);
    }

    /// <summary>
    /// The automation's picture (--screenshot) and its end (--quit-after), checked at the end of every frame, the
    /// menu's included: a run that opened the menu never reached them and recorded on for ever.
    /// </summary>
    private void AutomationAfterFrame()
    {
        if (_screenshotPath is not null && !_screenshotTaken && _elapsed >= _screenshotAt)
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
        var cockpit = _cameraRig.Mode == CameraMode.Cockpit && _session.Phase != SessionPhase.Replay;
        _carView.SetViewpoint(insideCar: cockpit && !_showAll);
        _hud.Cockpit = cockpit;
        _audio.Interior = cockpit;
        _world.Interior = cockpit;
        _mirror?.SetActive(cockpit);
        SyncTraffic(dt);
        SyncGhost();
        _trainView?.Sync();
        if (_session.Phase == SessionPhase.Replay && _session.Replay.Count > 1)
        {
            // The recorded approach, then the crash played out and the car damaged (CrashScene).
            _crash ??= new CrashScene(this, _carView, _session, effects: !_retro);
            var pose = _crash.Pose(_session.ReplayTime, _session.Options.ReplaySeconds);
            if (_crash.StruckThisFrame) _audio.ReplayCrash();
            _carView.Transform = pose;
            _cameraRig.Replay(pose.Origin, _crash.ViewForward, _crash.CloseUp);
            return;
        }
        EndCrash();
        _carView.SetPose(car.Position, car.Orientation);
        _carView.SetWheels(car.Wheels, car.SteerAngle, car.Spec);
        _cameraRig.Roughness = car.Wheels.Any(w => w.InContact && w.Surface != SurfaceType.Road) ? 1f : 0f;
        _cameraRig.Acceleration = new Vector2(car.LateralAcceleration, car.LongitudinalAcceleration);
        _cameraRig.FollowCar(_carView.GlobalTransform, MathF.Abs(car.Speed), dt);
    }

    /// <summary>
    /// For the crash replay in automated runs (--ram=tree, --ram=building, --ram=traffic, --ram=test with a tree
    /// planted on the start straight): the course is driven as in the attract mode until a thing of that kind comes
    /// up within 50 m ahead and 25 degrees of the nose, and then the car is driven flat out into it.
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
        if (_ram == "traffic" && _session.Traffic is not null)
        {
            foreach (var drone in _session.Traffic.Cars) Consider(drone.Position);
        }
        else
        {
            foreach (var prop in _session.Track.Props)
            {
                if (prop.Solid && (_ram is "tree" or "test") == (prop.Kind == PropKind.Tree)) Consider(prop.Position);
            }
        }
        // Once a target is taken it is held (a drone's is followed as it moves).
        if (found is { } point && (_ramTarget is null || _ram == "traffic")) _ramTarget = point;
        if (_ramTarget is not { } target) return input;
        var toTarget = target - car.Position;
        var angle = MathF.Atan2(SN.Vector3.Dot(toTarget, car.Right), MathF.Max(SN.Vector3.Dot(toTarget, car.Forward), 0.1f));
        input.Throttle = 1f;
        input.Brake = 0f;
        input.Steer = MathUtil.Clamp(angle / car.MaxSteerAngleAt(car.Speed), -1f, 1f);
        return input;
    }

    /// <summary>The replay is over or cut short: the car is repaired and the debris cleared.</summary>
    private void EndCrash()
    {
        _crash?.End();
        _crash = null;
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

    private void SyncTraffic(float dt)
    {
        if (_session.Traffic is null) return;
        var cars = _session.Traffic.Cars;
        for (var i = 0; i < cars.Count && i < _droneViews.Count; i++)
        {
            _droneViews[i].SetPose(cars[i].Position, cars[i].Orientation);
            if (_session.Phase is SessionPhase.Driving or SessionPhase.StartEngine) _droneViews[i].AdvanceWheels(cars[i].Speed * dt, 0f);
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
        // Left/right steps through the cars, either way round; the session is rebuilt around the new spec.
        var step = Input.IsActionJustPressed(InputSetup.SteerLeft) ? -1 : Input.IsActionJustPressed(InputSetup.SteerRight) ? 1 : 0;
        if (step != 0)
        {
            var count = VehicleSpec.Catalogue.Length;
            _carIndex = (_carIndex + step + count) % count;
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
        if (_menu.Open && @event is InputEventMouse mouse)
        {
            var line = PauseMenu.HitTest(GetViewport().GetVisibleRect().Size, mouse.Position);
            if (line >= 0) _menu.Select(line);
            if (line >= 0 && mouse is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _menuClick = true;
            return;
        }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _clicked = true;
    }

    /// <summary>Opens or closes the menu; the race and its sound stand still while it is open.</summary>
    private void ShowMenu(bool open)
    {
        if (open == _menu.Open) return;
        if (open) _menu.Show();
        else _menu.Hide();
        AudioServer.SetBusMute(0, open);
        _keyboard.Reset();
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
            case PauseMenu.Item.Camera:
                ToggleCamera();
                break;
            case PauseMenu.Item.Graphics:
                CycleQuality(left ? -1 : 1);
                break;
            case PauseMenu.Item.Retro:
                SetRetro(!_retro);
                break;
            case PauseMenu.Item.Units:
                _instruments.UseKmh = !_instruments.UseKmh;
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
        labels[(int)PauseMenu.Item.Camera] = $"CAMERA:  {(_cameraRig.Mode == CameraMode.Cockpit ? "DRIVER'S SEAT" : "BEHIND THE CAR")}";
        labels[(int)PauseMenu.Item.Graphics] = $"GRAPHICS:  {_quality.ToString().ToUpperInvariant()}";
        labels[(int)PauseMenu.Item.Retro] = $"1989 MODE:  {(_retro ? "ON" : "OFF")}";
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
        if (Input.IsActionJustPressed(InputSetup.Camera)) ToggleCamera();
        if (Input.IsActionJustPressed(InputSetup.Units)) _instruments.UseKmh = !_instruments.UseKmh;
        if (Input.IsActionJustPressed(InputSetup.NewGame))
        {
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
        viewport.Msaa3D = low ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X;
        viewport.ScreenSpaceAA = low ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = !low;
        WorldBuilder.SetQuality(_quality);
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
