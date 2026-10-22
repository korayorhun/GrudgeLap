using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;

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
    public string Overlay { get; set; } = "";
    /// <summary>Small line under the lap readout: updater status and the like.</summary>
    public string Notice { get; set; } = "";
    public bool ShowHelp { get; set; } = true;
    public bool Retro { get; set; }

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

        // ---- centre messages ------------------------------------------------------------------------
        var message = Overlay != "" ? Overlay : s.Message;
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
            var lines = new[]
            {
                "GRUDGELAP",
                "",
                $"<  {car.Spec.Name}  >   (left / right to change car)",
                "",
                "choose your transmission",
                "1   AUTOMATIC",
                "2   STICK SHIFT  (clutch worked for you)",
                "3   STICK SHIFT + CLUTCH  (you can stall)",
                "",
                "then turn the key: ENTER",
                "",
                s.GrudgeMatch ? $"grudge match: the ghost of your best lap, {RaceSession.FormatLapTime(s.BestLapTime)}, is waiting" : "finish a lap and its ghost will race you next time",
                $"v{Updater.CurrentVersionText}   F8 1989 mode   F10 check for updates   F11 full screen",
            };
            for (var i = 0; i < lines.Length; i++)
            {
                var small = i >= lines.Length - 2;
                DrawString(font, new Vector2(0f, size.Y * 0.34f + i * 34f), lines[i], HorizontalAlignment.Center, size.X, i == 0 ? 54 : small ? 18 : 26, small ? new Color(0.75f, 0.8f, 0.85f) : new Color(1f, 1f, 1f));
            }
        }
        else if (s.Phase == SessionPhase.Finished)
        {
            DrawString(font, new Vector2(0f, size.Y * 0.45f), $"GAME OVER   SCORE {s.Score:N0}", HorizontalAlignment.Center, size.X, 40, new Color(1f, 1f, 1f));
            DrawString(font, new Vector2(0f, size.Y * 0.45f + 44f), "F5 for a new game", HorizontalAlignment.Center, size.X, 24, new Color(0.8f, 0.8f, 0.8f));
        }

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
