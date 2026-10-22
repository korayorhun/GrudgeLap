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
/// one-copy-per-computer lock of <see cref="SingleInstance"/>) and <c>--quit-after=&lt;seconds&gt;</c>.
/// </summary>
public sealed partial class Bootstrap : Node3D
{
    private const string BestLapFile = "user://bestlap.bin";
    private const string SettingsFile = "user://settings.cfg";

    private RaceSession _session = null!;
    private TrackGeometry _track = null!;
    private CarView _carView = null!;
    private CarView? _ghostView;
    private readonly List<CarView> _droneViews = new();
    private readonly Dictionary<int, Node3D> _propNodes = new();
    private readonly List<(Node3D Node, float Start, float Yaw, Vector3 Axis)> _tipping = new();
    private TrainView? _trainView;
    private CameraRig _cameraRig = null!;
    private Hud _hud = null!;
    private InstrumentCluster _instruments = null!;
    private Texture2D _instrumentPanel = null!;
    private RearMirror? _mirror;
    private EngineAudio _audio = null!;
    private Updater _updater = null!;
    private readonly KeyboardDriver _keyboard = new();
    private readonly ScriptedDriver _scripted = new();
    private LapRecording? _savedBestLap;
    private bool _attract;
    private bool _startChase;
    private bool _retro;
    private bool _invisible;
    private bool _leaving;      // a second launch on its way out: nothing was built
    private bool _noGhost;
    private bool _noTraffic;
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

    public override void _Ready()
    {
        InputSetup.Register();
        _carIndex = Math.Clamp(LoadChoice("car", 0), 0, VehicleSpec.Catalogue.Length - 1);
        _gearbox = (TransmissionMode)Math.Clamp(LoadChoice("transmission", (int)TransmissionMode.Automatic), 0, 2);
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
        WorldBuilder.Build(this, _track);
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
        }
        GetViewport().UseTaa = true;

        _carView = CarView.Create(VehicleSpec.Catalogue[_carIndex]);
        AddChild(_carView);
        _cameraRig = new CameraRig { Mode = _orbit is not null ? CameraMode.Orbit : _startChase ? CameraMode.Chase : CameraMode.Cockpit };
        if (_orbit is { } orbit) _cameraRig.Orbit = orbit;
        AddChild(_cameraRig);
        _hud = new Hud { Gearbox = _gearbox };
        var layer = new CanvasLayer();
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
            else if (arg == "--chase") _startChase = true;
            else if (arg == "--retro") _retro = true;
            else if (arg == "--invisible") _invisible = true;   // automated runs: no window on screen, no focus taken
            else if (arg == "--no-ghost") _noGhost = true;       // pictures of the car without the best lap's ghost over it
            else if (arg == "--no-traffic") _noTraffic = true;
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
            else if (arg == "--auto") _attractMode = TransmissionMode.Automatic;
            else if (arg == "--show-all") _showAll = true;
            else if (arg == "--save-best") _saveBest = true;   // debug: let an attract lap become the saved best lap
            else if (arg.StartsWith("--export-world=", System.StringComparison.Ordinal)) _exportWorld = arg["--export-world=".Length..];
            else if (arg == "--car=v8") _carIndex = 1;
            else if (arg == "--car=flat12") _carIndex = 0;
        }
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
        var spec = VehicleSpec.Catalogue[_carIndex];
        _session = new RaceSession(_track, spec, options);
        if (_carView.IsInsideTree() && _carView.Name != $"Car-{spec.Style}")
        {
            _carView.QueueFree();
            _carView = CarView.Create(spec);
            AddChild(_carView);
            FitOutPlayerCar();
        }
        _carView.Name = $"Car-{spec.Style}";
        _audio.SetCar(spec.Style);
        if (_savedBestLap is not null && !_noGhost) _session.LoadBestLap(_savedBestLap);
        _hud.Bind(_session);
        _instruments.Bind(_session);
        _keyboard.Reset();
        _cameraRig.ResetChase();
        BuildDroneViews();
        BuildGhostView();
        if (_attract)
        {
            _session.ChooseTransmission(_attractMode);
        }
        SyncView();
    }

    private void BuildDroneViews()
    {
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

        var input = _attract ? _scripted.Next(_session) : _keyboard.Sample(dt, MathF.Abs(_session.Car.Speed));
        if (!_attract && HandleMenuKeys()) input.Ignition = true;

        if (_session.Phase == SessionPhase.Replay && Input.IsActionJustPressed(InputSetup.Abort)) _session.Abort();
        else if (_session.Phase == SessionPhase.Driving && _session.OffRoad && Input.IsActionJustPressed(InputSetup.Abort)) _session.Abort();

        _session.Step(input, dt);
        if (_session.Events.Contains(RaceEvent.NewBestLap) && _session.BestLap is not null && (!_attract || _saveBest))
        {
            SaveBestLap(_session.BestLap);
            if (_ghostView is null) BuildGhostView();
        }
        _audio.MusicOn = _session.Phase is SessionPhase.SelectTransmission or SessionPhase.Replay or SessionPhase.Finished;
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
        _audio.Update(_session, dt);
        _hud.Notice = _updater.Status;
        SyncView();

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
        _mirror?.SetActive(cockpit);
        SyncTraffic(dt);
        SyncGhost();
        _trainView?.Sync();
        if (_session.Phase == SessionPhase.Replay && _session.Replay.Count > 1)
        {
            // Scrub through the recorded poses over the replay duration.
            var samples = _session.Replay;
            var t = MathUtil.Clamp01(_session.ReplayTime / _session.Options.ReplaySeconds);
            var index = (int)(t * (samples.Count - 1));
            var sample = samples[index];
            _carView.SetPose(sample.Position, sample.Orientation);
            var forward = SN.Vector3.Transform(SN.Vector3.UnitZ, sample.Orientation);
            _cameraRig.Replay(Convert.ToGodot(sample.Position), Convert.ToGodot(forward));
            return;
        }
        _carView.SetPose(car.Position, car.Orientation);
        _carView.SetWheels(car.Wheels, car.SteerAngle, car.Spec);
        _cameraRig.Roughness = car.Wheels.Any(w => w.InContact && w.Surface != SurfaceType.Road) ? 1f : 0f;
        _cameraRig.Acceleration = new Vector2(car.LateralAcceleration, car.LongitudinalAcceleration);
        _cameraRig.FollowCar(_carView.GlobalTransform, MathF.Abs(car.Speed), dt);
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
        // Left/right flips between the cars; the session is rebuilt around the new spec.
        if (Input.IsActionJustPressed(InputSetup.SteerLeft) || Input.IsActionJustPressed(InputSetup.SteerRight))
        {
            _carIndex = (_carIndex + 1) % VehicleSpec.Catalogue.Length;
            SaveChoice("car", _carIndex);
            NewSession();
            return false;
        }
        if (Input.IsActionJustPressed(InputSetup.SelectAuto)) SetGearbox(TransmissionMode.Automatic);
        else if (Input.IsActionJustPressed(InputSetup.SelectManual)) SetGearbox(TransmissionMode.ManualAutoClutch);
        else if (Input.IsActionJustPressed(InputSetup.SelectClutch)) SetGearbox(TransmissionMode.ManualClutch);
        if (!Input.IsActionJustPressed(InputSetup.Ignition) && !Input.IsActionJustPressed(InputSetup.Abort) && !click) return false;
        _session.ChooseTransmission(_gearbox);
        return true;
    }

    private void SetGearbox(TransmissionMode mode)
    {
        _gearbox = mode;
        _hud.Gearbox = mode;
        SaveChoice("transmission", (int)mode);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _clicked = true;
    }

    private void HandleGlobalKeys()
    {
        if (Input.IsActionJustPressed(InputSetup.Fullscreen))
        {
            var window = GetWindow();
            window.Mode = window.Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
        }
        if (Input.IsActionJustPressed(InputSetup.Camera))
        {
            _cameraRig.Mode = _cameraRig.Mode == CameraMode.Chase ? CameraMode.Cockpit : CameraMode.Chase;
            _cameraRig.ResetChase();
        }
        if (Input.IsActionJustPressed(InputSetup.Units)) _instruments.UseKmh = !_instruments.UseKmh;
        if (Input.IsActionJustPressed(InputSetup.NewGame)) NewSession();
        if (Input.IsActionJustPressed(InputSetup.Retro)) SetRetro(!_retro);
        if (Input.IsActionJustPressed(InputSetup.Update))
        {
            if (_updater.Available is not null) _updater.DownloadAndInstall();
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

    /// <summary>"1989 mode": a third of the resolution, 20 frames a second, flat colours and no atmosphere.</summary>
    private void SetRetro(bool retro)
    {
        _retro = retro;
        var viewport = GetViewport();
        viewport.Scaling3DScale = retro ? 0.32f : 1f;
        viewport.Msaa3D = retro ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = !retro;
        Engine.MaxFps = retro ? 20 : 0;
        WorldBuilder.SetRetro(retro);
        _audio.Retro = retro;
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
