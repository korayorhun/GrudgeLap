using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Race readouts drawn with Control primitives, and outside the cockpit the instrument panel at the foot of
/// the screen (from the driver's seat the car's own binnacle shows it). The panel scrolls sideways with
/// lateral acceleration, a nod to the original's moving dashboard, and everything else is big enough to read
/// at a glance at speed.
/// </summary>
internal sealed partial class Hud : Control
{
    private RaceSession? _session;
    private float _dashOffset;
    private float _flash;
    private float _drivingTime;

    /// <summary>The instrument panel (<see cref="InstrumentCluster"/>) drawn at the foot of the screen outside the cockpit.</summary>
    public Texture2D? Instruments { get; set; }
    /// <summary>The view is the driver's seat: the instruments are in the binnacle, so the foot of the screen stays clear.</summary>
    public bool Cockpit { get; set; }
    /// <summary>The gearbox the title screen will start with (the player's last choice).</summary>
    public TransmissionMode Gearbox { get; set; }
    public string Overlay { get; set; } = "";
    /// <summary>Small line under the lap readout: updater status and the like.</summary>
    public string Notice { get; set; } = "";
    /// <summary>The high-score table and the entry of initials, drawn when the credit is over.</summary>
    public ScoreBoard? Scores { get; set; }
    public bool ShowHelp { get; set; } = true;
    public bool Retro { get; set; }
    /// <summary>The frame rate, top left (F6).</summary>
    public bool ShowFps { get; set; }

    public void Bind(RaceSession session)
    {
        _session = session;
        _drivingTime = 0f;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        var dt = (float)delta;
        // Head-on-a-spring: the cluster lags the lateral G and settles back, filtered like the 1989 patent describes.
        var target = -_session.Car.LateralAcceleration * 6f;
        _dashOffset += (target - _dashOffset) * MathUtil.Clamp01(dt * 5f);
        _flash += dt;
        if (_session.Phase == SessionPhase.Driving) _drivingTime += dt;
        QueueRedraw();
    }

    private void Centred(Font font, string text, float y, int fontSize, Color colour) =>
        DrawString(font, new Vector2(0f, y), text, HorizontalAlignment.Center, GetViewportRect().Size.X, fontSize, colour);

    public override void _Draw()
    {
        if (_session is null) return;
        var font = ThemeDB.FallbackFont;
        var size = GetViewportRect().Size;
        var car = _session.Car;
        var s = _session;

        // ---- instrument panel at the foot of the screen, except from the driver's seat ---------------------
        var bandTop = size.Y;
        if (!Cockpit && Instruments is not null)
        {
            var width = MathF.Min(size.X * 0.36f, 580f);
            var height = width * Instruments.GetHeight() / Instruments.GetWidth();
            bandTop = size.Y - height - 16f;
            DrawRect(new Rect2(0f, bandTop, size.X, size.Y - bandTop), new Color(0.05f, 0.05f, 0.06f, 0.55f));
            DrawTextureRect(Instruments, new Rect2(size.X * 0.5f - width * 0.5f + _dashOffset, size.Y - height - 8f, width, height), false);
        }

        // ---- top readouts -------------------------------------------------------------------------
        DrawRect(new Rect2(0f, 0f, size.X, 64f), new Color(0.05f, 0.05f, 0.06f, 0.5f));
        var timeColour = s.TimeRemaining < 10f && (int)(_flash * 4f) % 2 == 0 ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 1f, 1f);
        DrawString(font, new Vector2(20f, 44f), $"TIME {s.TimeRemaining:00.0}", HorizontalAlignment.Left, -1f, 34, timeColour);
        DrawString(font, new Vector2(size.X * 0.5f - 200f, 44f), $"SCORE {s.Score:N0}", HorizontalAlignment.Center, 400f, 30, new Color(1f, 0.85f, 0.3f));
        DrawString(font, new Vector2(size.X - 320f, 44f), $"LAP {s.Lap}  {s.LapTime:0.0}s", HorizontalAlignment.Right, 300f, 26, new Color(0.9f, 0.9f, 0.9f));
        var route = s.Route switch { RouteKind.Speed => "SPEED TRACK", RouteKind.Stunt => "STUNT TRACK", _ => "" };
        if (route != "") DrawString(font, new Vector2(size.X - 320f, 62f), route, HorizontalAlignment.Right, 300f, 14, new Color(0.7f, 0.85f, 1f));
        if (s.GrudgeMatch)
        {
            var ghost = s.GhostVisible ? "GHOST  " : "";
            DrawString(font, new Vector2(size.X - 320f, 84f), $"{ghost}BEST {RaceSession.FormatLapTime(s.BestLapTime)}", HorizontalAlignment.Right, 300f, 16, new Color(0.6f, 0.9f, 1f));
        }
        if (Notice != "") DrawString(font, new Vector2(size.X - 520f, 104f), Notice, HorizontalAlignment.Right, 500f, 14, new Color(1f, 0.85f, 0.4f));
        if (Retro) DrawString(font, new Vector2(20f, 84f), "1989 MODE", HorizontalAlignment.Left, -1f, 16, new Color(1f, 0.6f, 0.3f));
        if (ShowFps) DrawString(font, new Vector2(20f, Retro ? 104f : 84f), $"{Engine.GetFramesPerSecond():0} FPS", HorizontalAlignment.Left, -1f, 16, new Color(0.7f, 0.9f, 0.7f));

        // ---- centre messages ------------------------------------------------------------------------
        // The game-over panel says it all; the session's OUT OF TIME would print over the table.
        var message = Overlay != "" ? Overlay : s.Phase == SessionPhase.Finished ? "" : s.Message;
        if (message != "")
        {
            var blink = message.StartsWith("WRONG WAY", System.StringComparison.Ordinal) || message.StartsWith("OFF ROAD", System.StringComparison.Ordinal);
            if (!blink || (int)(_flash * 3f) % 2 == 0)
            {
                DrawString(font, new Vector2(0f, size.Y * 0.28f), message, HorizontalAlignment.Center, size.X, 44, new Color(1f, 0.95f, 0.6f));
            }
        }

        if (s.Phase == SessionPhase.SelectTransmission)
        {
            // The title on a dark panel, readable over the cockpit behind it, with one thing to do: press Enter.
            var white = new Color(1f, 1f, 1f);
            var grey = new Color(0.72f, 0.75f, 0.8f);
            var panel = new Rect2(size.X * 0.5f - 440f, size.Y * 0.22f, 880f, 450f);
            DrawRect(panel, new Color(0.03f, 0.03f, 0.04f, 0.8f));
            var y = panel.Position.Y + 70f;
            Centred(font, "GRUDGELAP", y, 58, white);
            Centred(font, $"<   {car.Spec.Name}   >", y + 62f, 28, white);
            Centred(font, "left / right or the stick: car", y + 90f, 15, grey);
            var options = new (string Key, string Label, TransmissionMode Mode)[]
            {
                ("1", "AUTOMATIC", TransmissionMode.Automatic),
                ("2", "STICK", TransmissionMode.ManualAutoClutch),
                ("3", "STICK + CLUTCH", TransmissionMode.ManualClutch),
            };
            for (var i = 0; i < options.Length; i++)
            {
                var (key, label, mode) = options[i];
                var chosen = mode == Gearbox;
                var x = size.X * 0.5f - 420f + i * 280f;
                if (chosen) DrawRect(new Rect2(x + 10f, y + 112f, 260f, 46f), new Color(1f, 0.85f, 0.3f, 0.16f));
                DrawString(font, new Vector2(x, y + 144f), $"{key}  {label}", HorizontalAlignment.Center, 280f, 22, chosen ? new Color(1f, 0.85f, 0.3f) : grey);
            }
            Centred(font, "STICK: you shift, the clutch is worked for you     STICK + CLUTCH: you can stall", y + 186f, 15, grey);
            Centred(font, "pad: stick steers   RT gas   LT brake   RB / LB shift (here: gearbox)   X clutch   A start   B skip   Y camera", y + 212f, 14, grey);
            var pulse = 0.7f + 0.3f * MathF.Sin(_flash * 4f);
            Centred(font, "PRESS ENTER OR A TO START", y + 258f, 38, new Color(1f, 0.85f, 0.3f, pulse));
            Centred(font, s.GrudgeMatch ? $"grudge match: the ghost of your best lap, {RaceSession.FormatLapTime(s.BestLapTime)}, is waiting" : "finish a lap and its ghost will race you next time", y + 312f, 18, new Color(0.6f, 0.9f, 1f));
            Centred(font, $"v{Updater.CurrentVersionText}   F6 fps   F7 graphics   F8 1989 mode   F10 check for updates   F11 full screen", y + 346f, 15, grey);
        }
        else if (s.Phase == SessionPhase.Finished)
        {
            DrawFinished(font, size, s);
        }

        DrawHelp(font, size, car, s, bandTop);
    }

    /// <summary>The end of the credit: the score, the entry of the initials when it made the table, and the table.</summary>
    private void DrawFinished(Font font, Vector2 size, RaceSession s)
    {
        var white = new Color(1f, 1f, 1f);
        var grey = new Color(0.72f, 0.75f, 0.8f);
        var gold = new Color(1f, 0.85f, 0.3f);
        var panel = new Rect2(size.X * 0.5f - 440f, size.Y * 0.12f, 880f, 580f);
        DrawRect(panel, new Color(0.03f, 0.03f, 0.04f, 0.8f));
        var y = panel.Position.Y + 58f;
        Centred(font, $"GAME OVER   SCORE {s.Score:N0}", y, 40, white);
        if (Scores is { Entering: true } entry)
        {
            Centred(font, "YOU MADE THE TOP TEN - ENTER YOUR INITIALS", y + 70f, 22, gold);
            var pulse = 0.55f + 0.45f * MathF.Sin(_flash * 6f);
            for (var i = 0; i < entry.Initials.Length; i++)
            {
                var x = size.X * 0.5f + (i - 1) * 90f - 45f;
                var current = i == entry.Cursor;
                DrawString(font, new Vector2(x, y + 180f), entry.Initials[i].ToString(), HorizontalAlignment.Center, 90f, 72, current ? gold with { A = pulse } : white);
                if (current) DrawRect(new Rect2(x + 20f, y + 194f, 50f, 4f), gold);
            }
            Centred(font, "up / down: letter     left / right: place     ENTER or A: take it", y + 250f, 16, grey);
        }
        else if (Scores is { } board)
        {
            var top = y + 56f;
            var x0 = panel.Position.X + 40f;
            DrawString(font, new Vector2(x0 + 70f, top), "NAME", HorizontalAlignment.Left, 100f, 15, grey);
            DrawString(font, new Vector2(x0 + 170f, top), "SCORE", HorizontalAlignment.Right, 160f, 15, grey);
            DrawString(font, new Vector2(x0 + 370f, top), "BEST LAP", HorizontalAlignment.Left, 120f, 15, grey);
            DrawString(font, new Vector2(x0 + 500f, top), "ROUTE", HorizontalAlignment.Left, 120f, 15, grey);
            DrawString(font, new Vector2(x0 + 620f, top), "CAR", HorizontalAlignment.Left, 200f, 15, grey);
            for (var i = 0; i < board.Table.Entries.Count; i++)
            {
                var e = board.Table.Entries[i];
                var row = top + 34f + i * 34f;
                var colour = i == board.Rank ? gold : i % 2 == 0 ? white : new Color(0.84f, 0.85f, 0.88f);
                DrawString(font, new Vector2(x0, row), $"{i + 1}.", HorizontalAlignment.Right, 44f, 22, colour);
                DrawString(font, new Vector2(x0 + 70f, row), e.Initials, HorizontalAlignment.Left, 100f, 22, colour);
                DrawString(font, new Vector2(x0 + 170f, row), $"{e.Score:N0}", HorizontalAlignment.Right, 160f, 22, colour);
                DrawString(font, new Vector2(x0 + 370f, row), e.HasLap ? RaceSession.FormatLapTime(e.BestLap) : "-:--.-", HorizontalAlignment.Left, 120f, 22, colour);
                DrawString(font, new Vector2(x0 + 500f, row), e.Route switch { RouteKind.Speed => "SPEED", RouteKind.Stunt => "STUNT", _ => "-" }, HorizontalAlignment.Left, 120f, 22, colour);
                DrawString(font, new Vector2(x0 + 620f, row), e.Car, HorizontalAlignment.Left, 220f, 15, colour);
            }
        }
        Centred(font, "F5 or BACK: new game", panel.End.Y - 34f, 20, grey);
    }

    private void DrawHelp(Font font, Vector2 size, VehicleSim car, RaceSession s, float bandTop)
    {
        // The keys: on the instrument band outside the cockpit; from the driver's seat only for the first
        // seconds of a run, on a thin strip over the dashboard.
        if (ShowHelp && s.Phase == SessionPhase.Driving && (!Cockpit || _drivingTime < 20f))
        {
            var y = bandTop < size.Y ? bandTop + 18f : size.Y - 8f;
            if (bandTop >= size.Y) DrawRect(new Rect2(0f, size.Y - 26f, size.X, 26f), new Color(0.05f, 0.05f, 0.06f, 0.55f));
            DrawString(font, new Vector2(20f, y),
                $"arrows/WASD drive   E/Q or 1-{car.Spec.ForwardGearCount} gears   N neutral   shift = clutch   ENTER key   SPACE abort/skip   C camera   U units   F8 1989   F11 fullscreen",
                HorizontalAlignment.Left, size.X - 40f, 13, new Color(0.8f, 0.8f, 0.8f, 0.8f));
        }
    }
}
