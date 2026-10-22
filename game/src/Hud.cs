using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Dashboard and race readouts drawn with Control primitives. The gauge cluster scrolls sideways with
/// lateral acceleration, a nod to the original's moving dashboard, and everything else is big enough to read
/// at a glance at speed.
/// </summary>
internal sealed partial class Hud : Control
{
    private RaceSession? _session;
    private float _dashOffset;
    private float _flash;

    public bool UseKmh { get; set; }
    public string Overlay { get; set; } = "";
    /// <summary>Small line under the lap readout: updater status and the like.</summary>
    public string Notice { get; set; } = "";
    public bool ShowHelp { get; set; } = true;
    public bool Retro { get; set; }

    public void Bind(RaceSession session) => _session = session;

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
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_session is null) return;
        var font = ThemeDB.FallbackFont;
        var size = GetViewportRect().Size;
        var car = _session.Car;
        var s = _session;

        // ---- gauge cluster at the bottom ----------------------------------------------------------
        var baseY = size.Y - 30f;
        var centreX = size.X * 0.5f + _dashOffset;
        DrawRect(new Rect2(0f, size.Y - 190f, size.X, 190f), new Color(0.05f, 0.05f, 0.06f, 0.55f));

        var speed = UseKmh ? car.SpeedMph / MathUtil.MphPerMps * MathUtil.KmhPerMps : car.SpeedMph;
        var speedMax = UseKmh ? MathF.Round(car.Spec.SpeedoMaxMph * 1.6f / 20f) * 20f : car.Spec.SpeedoMaxMph;
        Gauge(font, new Vector2(centreX - 190f, baseY - 20f), 82f, speed, speedMax, UseKmh ? "km/h" : "MPH", new Color(0.95f, 0.95f, 0.95f), (int)MathF.Round(speedMax / (UseKmh ? 40f : 20f)));
        var rpm = car.Drivetrain.Engine.Rpm;
        var redline = car.Spec.Engine.RedlineRpm;
        var tachMax = MathF.Ceiling((redline + 800f) / 1000f);
        Gauge(font, new Vector2(centreX + 190f, baseY - 20f), 82f, rpm / 1000f, tachMax, "RPM x1000", rpm > redline - 400f ? new Color(1f, 0.3f, 0.25f) : new Color(0.95f, 0.95f, 0.95f), (int)tachMax, redlineFrom: redline / 1000f);

        // Gear box
        var gear = car.Drivetrain.Gear switch { -1 => "R", 0 => "N", var g => g.ToString() };
        DrawRect(new Rect2(centreX - 34f, baseY - 112f, 68f, 68f), new Color(0.12f, 0.12f, 0.14f));
        DrawRect(new Rect2(centreX - 34f, baseY - 112f, 68f, 68f), new Color(0.6f, 0.6f, 0.6f), false, 2f);
        DrawString(font, new Vector2(centreX - 34f, baseY - 60f), gear, HorizontalAlignment.Center, 68f, 44, new Color(1f, 0.85f, 0.3f));
        var mode = car.Drivetrain.Mode switch
        {
            TransmissionMode.Automatic => "AUTO",
            TransmissionMode.ManualAutoClutch => "STICK",
            _ => "STICK+CLUTCH",
        };
        DrawString(font, new Vector2(centreX - 80f, baseY - 20f), mode, HorizontalAlignment.Center, 160f, 14, new Color(0.7f, 0.7f, 0.7f));
        if (!car.Drivetrain.Engine.Running)
        {
            DrawString(font, new Vector2(centreX - 80f, baseY - 2f), "ENGINE OFF", HorizontalAlignment.Center, 160f, 14, new Color(1f, 0.4f, 0.3f));
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

        if (ShowHelp && s.Phase == SessionPhase.Driving)
        {
            // Inside the dashboard band, above the gauges, so it never sits on the road.
            DrawString(font, new Vector2(20f, size.Y - 168f),
                $"arrows/WASD drive   E/Q or 1-{car.Spec.ForwardGearCount} gears   N neutral   shift = clutch   ENTER key   SPACE abort/skip   C camera   U units   F8 1989   F11 fullscreen",
                HorizontalAlignment.Left, size.X - 40f, 13, new Color(0.8f, 0.8f, 0.8f, 0.8f));
        }
    }

    private void Gauge(Font font, Vector2 centre, float radius, float value, float max, string caption, Color colour, int ticks, float redlineFrom = -1f)
    {
        const float start = Mathf.Pi * 0.75f;
        const float sweep = Mathf.Pi * 1.5f;
        DrawCircle(centre, radius + 6f, new Color(0.1f, 0.1f, 0.12f));
        DrawArc(centre, radius, start, start + sweep, 48, new Color(0.65f, 0.65f, 0.68f), 3f, true);
        if (redlineFrom > 0f)
        {
            var a0 = start + sweep * (redlineFrom / max);
            DrawArc(centre, radius - 6f, a0, start + sweep, 12, new Color(0.9f, 0.2f, 0.15f), 6f, true);
        }
        for (var i = 0; i <= ticks; i++)
        {
            var a = start + sweep * i / ticks;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(centre + dir * (radius - 12f), centre + dir * radius, new Color(0.85f, 0.85f, 0.85f), 2f, true);
            var label = (max * i / ticks).ToString("0");
            DrawString(font, centre + dir * (radius - 30f) - new Vector2(16f, -5f), label, HorizontalAlignment.Center, 32f, 12, new Color(0.8f, 0.8f, 0.8f));
        }
        var t = MathUtil.Clamp01(value / max);
        var na = start + sweep * t;
        var needle = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
        DrawLine(centre - needle * 10f, centre + needle * (radius - 10f), new Color(1f, 0.25f, 0.2f), 3f, true);
        DrawCircle(centre, 5f, new Color(0.9f, 0.9f, 0.9f));
        DrawString(font, centre + new Vector2(-50f, radius - 8f), caption, HorizontalAlignment.Center, 100f, 13, colour);
        DrawString(font, centre + new Vector2(-50f, 32f), max > 20f ? value.ToString("0") : (value * 1000f).ToString("0"), HorizontalAlignment.Center, 100f, 20, colour);
    }
}
