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
    private float _knockFlash;

    /// <summary>The instrument panel (<see cref="InstrumentCluster"/>) drawn at the foot of the screen outside the cockpit.</summary>
    public Texture2D? Instruments { get; set; }
    /// <summary>The view is the driver's seat: the instruments are in the binnacle, so the foot of the screen stays clear.</summary>
    public bool Cockpit { get; set; }
    /// <summary>The gearbox the title screen will start with (the player's last choice).</summary>
    public TransmissionMode Gearbox { get; set; }
    public string Overlay { get; set; } = "";
    private float _glassCracks;
    private int _crackSeed = 7;
    /// <summary>
    /// The windscreen cracked by the crash (0.35), as the cabinet drew it: 1 shows white cracks over the view from the
    /// seat for the crash's own moment, 0 none; a new pattern each time it is switched on.
    /// </summary>
    public float GlassCracks
    {
        get => _glassCracks;
        set
        {
            if (value > 0f && _glassCracks <= 0f) _crackSeed = _crackSeed * 31 + 17;
            _glassCracks = value;
        }
    }
    /// <summary>Small line under the lap readout: updater status and the like.</summary>
    public string Notice { get; set; } = "";
    /// <summary>The high-score table and the entry of initials, drawn when the credit is over.</summary>
    public ScoreBoard? Scores { get; set; }
    public bool ShowHelp { get; set; } = true;
    public bool Retro { get; set; }
    /// <summary>The menu Esc opens (<see cref="PauseMenu"/>), drawn over everything while it is open.</summary>
    public PauseMenu? Menu { get; set; }
    /// <summary>The showroom (<see cref="Game.Showroom"/>) while it is open: its stage fills the screen and nothing of the race is drawn.</summary>
    public Showroom? Showroom { get; set; }
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
        if (_session.Events.Contains(RaceEvent.Knocked)) _knockFlash = MathUtil.Clamp01(_session.LastKnock.Speed / 10f) * 0.5f + 0.25f;
        _knockFlash = MathF.Max(_knockFlash - dt * 2.2f, 0f);
        QueueRedraw();
    }

    /// <summary>
    /// The cracked windscreen (0.35): white cracks radiating from the blow over the glass with side branches and a few
    /// broken rings round it, each line over a dark shadow so it reads on the sky and on the road alike.
    /// </summary>
    private void DrawGlassCracks(Vector2 size)
    {
        var rng = new Random(_crackSeed);
        var centre = new Vector2(size.X * (0.40f + 0.2f * rng.NextSingle()), size.Y * (0.28f + 0.14f * rng.NextSingle()));
        var white = new Color(1f, 1f, 1f, 0.85f * GlassCracks);
        var shade = new Color(0.08f, 0.1f, 0.14f, 0.4f * GlassCracks);
        var reach = size.Y * 0.8f;
        const int cracks = 15;
        for (var k = 0; k < cracks; k++)
        {
            var angle = k * MathF.Tau / cracks + rng.NextSingle() * 0.35f;
            var length = reach * (0.4f + 0.6f * rng.NextSingle());
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var across = new Vector2(-dir.Y, dir.X);
            const int steps = 7;
            var line = new Vector2[steps + 1];
            line[0] = centre;
            for (var s = 1; s <= steps; s++)
            {
                line[s] = centre + dir * (length * s / steps) + across * ((rng.NextSingle() - 0.5f) * 0.14f * length);
            }
            DrawPolyline(line, shade, 4f, true);
            DrawPolyline(line, white, 1.5f, true);
            // A branch or two off the longer cracks.
            if (length > reach * 0.6f)
            {
                var at = line[2 + rng.Next(3)];
                var side = rng.Next(2) == 0 ? 1f : -1f;
                var branch = new[] { at, at + (dir * 0.6f + across * side * 0.8f) * (0.18f * length), at + (dir * 0.9f + across * side * 1.1f) * (0.3f * length) };
                DrawPolyline(branch, shade, 3f, true);
                DrawPolyline(branch, white, 1.2f, true);
            }
        }
        for (var r = 0; r < 3; r++)
        {
            var radius = reach * (0.09f + 0.14f * r) * (0.9f + 0.2f * rng.NextSingle());
            var start = rng.NextSingle() * MathF.Tau;
            for (var arc = 0; arc < 3; arc++)
            {
                var from = start + arc * 2.2f + rng.NextSingle() * 0.4f;
                var to = from + 1.1f + rng.NextSingle() * 0.7f;
                DrawArc(centre, radius, from, to, 18, shade, 3f, true);
                DrawArc(centre, radius, from, to, 18, white, 1.2f, true);
            }
        }
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
        if (Showroom is { } room)
        {
            DrawShowroom(font, size, room);
            return;
        }

        // ---- instrument panel at the foot of the screen, except from the driver's seat ---------------------
        var bandTop = size.Y;
        if (!Cockpit && Instruments is not null && s.Phase != SessionPhase.Replay)
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
        var leftLine = 84f;
        if (s.Damage > 0.004f && s.Phase is SessionPhase.Driving or SessionPhase.StartEngine)
        {
            // The damage gauge: green to amber to red as the car is knocked about; full is a write-off (a crash).
            var gauge = new Rect2(20f, 76f, 170f, 9f);
            var fill = MathUtil.Clamp01(s.Damage);
            var tone = fill < 0.5f ? new Color(0.35f, 0.85f, 0.3f).Lerp(new Color(1f, 0.75f, 0.15f), fill * 2f) : new Color(1f, 0.75f, 0.15f).Lerp(new Color(1f, 0.2f, 0.12f), (fill - 0.5f) * 2f);
            DrawRect(gauge, new Color(0f, 0f, 0f, 0.55f));
            DrawRect(new Rect2(gauge.Position, new Vector2(gauge.Size.X * fill, gauge.Size.Y)), tone);
            DrawString(font, new Vector2(198f, 86f), "DAMAGE", HorizontalAlignment.Left, -1f, 13, new Color(0.92f, 0.92f, 0.92f));
            leftLine = 106f;
        }
        if (Retro) DrawString(font, new Vector2(20f, leftLine), "1989 MODE", HorizontalAlignment.Left, -1f, 16, new Color(1f, 0.6f, 0.3f));
        if (ShowFps) DrawString(font, new Vector2(20f, Retro ? leftLine + 20f : leftLine), $"{Engine.GetFramesPerSecond():0} FPS", HorizontalAlignment.Left, -1f, 16, new Color(0.7f, 0.9f, 0.7f));
        // The weather of the credit (0.12), when it is not a clear day: the grip is down on a wet road.
        if (s.Weather != Weather.Dry) DrawString(font, new Vector2(20f, leftLine + (Retro ? 20f : 0f) + (ShowFps ? 20f : 0f)), WeatherModel.Label(s.Weather), HorizontalAlignment.Left, -1f, 16, new Color(0.75f, 0.85f, 1f));
        if (_knockFlash > 0f)
        {
            // A knock: the screen's edges flash red for a moment, brighter the harder it was.
            var edge = new Color(0.9f, 0.08f, 0.05f, _knockFlash * 0.45f);
            const float band = 18f;
            DrawRect(new Rect2(0f, 0f, size.X, band), edge);
            DrawRect(new Rect2(0f, size.Y - band, size.X, band), edge);
            DrawRect(new Rect2(0f, band, band, size.Y - 2f * band), edge);
            DrawRect(new Rect2(size.X - band, band, band, size.Y - 2f * band), edge);
        }
        if (GlassCracks > 0f && Cockpit) DrawGlassCracks(size);

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
            // Held inside the panel's 880 px: with "or the stick" and the showroom's hint the line ran over its edge.
            Centred(font, $"{CarFigures(car.Spec)}     left / right: car {CarNumber(car.Spec)} of {Lineup.Count}     down or click the name: showroom", y + 90f, 15, grey);
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
            Centred(font, $"v{Updater.CurrentVersionText}   ESC menu and quit   F6 fps   F7 graphics   F8 1989 mode   F10 check for updates   F11 full screen", y + 346f, 15, grey);
        }
        else if (s.Phase == SessionPhase.Finished)
        {
            DrawFinished(font, size, s);
        }

        DrawHelp(font, size, car, s, bandTop);
        if (Menu is { Open: true } menu) DrawMenu(font, size, menu);
    }

    /// <summary>The menu: a dark panel over a dimmed game, the line under the cursor lit.</summary>
    private void DrawMenu(Font font, Vector2 size, PauseMenu menu)
    {
        var gold = new Color(1f, 0.85f, 0.3f);
        var grey = new Color(0.72f, 0.75f, 0.8f);
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0f, 0.45f));
        var panel = PauseMenu.Panel(size);
        DrawRect(panel, new Color(0.03f, 0.03f, 0.04f, 0.92f));
        DrawString(font, new Vector2(panel.Position.X, panel.Position.Y + 62f), "PAUSED", HorizontalAlignment.Center, panel.Size.X, 40, new Color(1f, 1f, 1f));
        for (var i = 0; i < PauseMenu.Items.Length; i++)
        {
            var line = PauseMenu.ItemRect(size, i);
            var current = i == menu.Cursor;
            if (current) DrawRect(line, new Color(1f, 0.85f, 0.3f, 0.16f));
            var label = menu.Labels[i] ?? PauseMenu.Items[i].ToString();
            DrawString(font, new Vector2(line.Position.X, line.Position.Y + 29f), label, HorizontalAlignment.Center, line.Size.X, 24, current ? gold : grey);
        }
        DrawString(font, new Vector2(panel.Position.X, panel.End.Y - 22f),
            "up / down: choose     ENTER or A: take it     left / right: change     ESC, SPACE or B: back",
            HorizontalAlignment.Center, panel.Size.X, 14, grey);
    }

    /// <summary>The car's name on the title screen, for the click that opens the showroom (<see cref="Bootstrap"/>).</summary>
    public static Rect2 TitleCarRect(Vector2 size) => new(size.X * 0.5f - 320f, size.Y * 0.22f + 100f, 640f, 44f);

    /// <summary>
    /// The showroom (<see cref="Game.Showroom"/>): its stage full-screen, the car's name in the bar over it, the chevrons
    /// at the sides, and on a panel at the foot the facts its spec gives, the paints as swatches (the chosen one framed,
    /// the ORIGINAL one in the car's own colour, the last one the car's mixed colour over a strip of hues), the paint's
    /// name and the keys; the colour picker over the stage while it is open. The rects are the showroom's and the
    /// picker's, so the mouse hits what is drawn.
    /// </summary>
    private void DrawShowroom(Font font, Vector2 size, Showroom room)
    {
        var white = new Color(1f, 1f, 1f);
        var grey = new Color(0.72f, 0.75f, 0.8f);
        var gold = new Color(1f, 0.85f, 0.3f);
        DrawTextureRect(room.Texture, new Rect2(Vector2.Zero, size), false);
        DrawRect(new Rect2(0f, 0f, size.X, 64f), new Color(0.05f, 0.05f, 0.06f, 0.5f));
        DrawString(font, new Vector2(20f, 44f), "SHOWROOM", HorizontalAlignment.Left, -1f, 30, white);
        // A withdrawn car (automation only) has no place in the line-up.
        var place = Lineup.Number(room.Index) is > 0 and var number ? $"CAR {number} OF {Showroom.Count}" : "NOT IN THE LINE-UP";
        DrawString(font, new Vector2(size.X - 320f, 44f), place, HorizontalAlignment.Right, 300f, 22, grey);
        // The name fades out and in with the cars sliding under it. In the bar: drawn over the stage (46 px, under the
        // bar) it lay on the roofs of the tall cars and of a car brought closer (the showroom's review).
        Centred(font, room.Spec.Name, 45f, 34, white with { A = room.TextAlpha });
        var left = Showroom.LeftArrow(size);
        var right = Showroom.RightArrow(size);
        DrawString(font, new Vector2(left.Position.X, left.Position.Y + 112f), "<", HorizontalAlignment.Center, left.Size.X, 96, gold);
        DrawString(font, new Vector2(right.Position.X, right.Position.Y + 112f), ">", HorizontalAlignment.Center, right.Size.X, 96, gold);

        var panel = Showroom.Panel(size);
        DrawRect(panel, new Color(0.03f, 0.03f, 0.04f, 0.80f));
        var y = panel.Position.Y;
        Centred(font, Showroom.LayoutLine(room.Spec), y + 38f, 19, grey);
        Centred(font, Showroom.FiguresLine(room.Spec), y + 70f, 20, white);
        var first = Showroom.Swatch(size, 0);
        DrawString(font, new Vector2(first.Position.X - 112f, first.Position.Y + 25f), "PAINT", HorizontalAlignment.Right, 96f, 20, grey);
        for (var i = 0; i < Showroom.SlotCount; i++)
        {
            var swatch = Showroom.Swatch(size, i);
            var colour = i == Showroom.CustomSlot ? room.MixedChoice.Colour : CarView.Paints[i].Paint?.Colour ?? room.CarColour;
            DrawRect(swatch, new Color(colour.R, colour.G, colour.B));
            // A faint edge on every swatch: the black one was lost on the dark panel.
            DrawRect(swatch, new Color(1f, 1f, 1f, 0.3f), false, 1f);
            // The mixed colour's swatch stands on a strip of every hue: the one that opens the picker.
            if (i == Showroom.CustomSlot) DrawTextureRect(ColourPicker.HueStrip, new Rect2(swatch.Position.X, swatch.End.Y - 6f, swatch.Size.X, 6f), false);
            if (i == room.PaintSlot) DrawRect(swatch.Grow(4f), gold, false, 3f);
        }
        var choice = room.Choice;
        Centred(font, choice.IsCustom ? $"{choice.Name}   {choice.Hex}" : choice.Name, y + 160f, 18, gold);
        var take = Showroom.TakeButton(size);
        var back = Showroom.BackButton(size);
        // While the picker is open the buttons wait for its DONE or CANCEL (Bootstrap ignores a click on them), so they are dimmed.
        var waiting = room.Picker is not null ? 0.35f : 1f;
        DrawRect(take, new Color(1f, 0.85f, 0.3f, 0.16f * waiting));
        DrawString(font, new Vector2(take.Position.X, take.Position.Y + 29f), "TAKE IT   (ENTER)", HorizontalAlignment.Center, take.Size.X, 20, gold with { A = waiting });
        DrawRect(back, new Color(1f, 1f, 1f, 0.06f * waiting));
        DrawString(font, new Vector2(back.Position.X, back.Position.Y + 29f), "BACK   (ESC)", HorizontalAlignment.Center, back.Size.X, 20, grey with { A = waiting });
        // Space as on the screen under the showroom: it starts the game on the title screen and goes back on the menu.
        var takeAndBack = room.FromMenu ? "ENTER or A: take it     ESC, SPACE or B: back" : "ENTER, SPACE or A: take it     ESC or B: back";
        Centred(font, $"left / right or A / D: car     up / down or W / S: paint     C or Y: mix a colour     drag the car to turn it, the wheel brings it closer     {takeAndBack}", y + 190f, 15, grey);
        // The figures are the simulation's, read from the spec (Showroom.FiguresLine), and say so: a mass there carries a
        // driver and fuel (VehicleSpec.MassKg), and the power and torque are its curve's peaks, which can sit off the
        // brochure's (the showroom's review read them against the real cars' published figures).
        var kept = room.FromMenu ? "another car starts a new game; the car and every car's paint are kept for next time" : "the car and every car's paint are kept for next time";
        Centred(font, $"{kept}     ·     the figures are the simulation's, the mass with a driver and fuel", y + 216f, 14, new Color(0.55f, 0.58f, 0.62f));
        if (room.Picker is { } picker) DrawPicker(font, size, picker);
    }

    /// <summary>
    /// The colour picker (<see cref="ColourPicker"/>): the square of saturation and lightness in the picker's hue with a
    /// ring at the colour, the bar of hues with a mark at its hue, the three values and the finish as rows (the focused
    /// one lit, as the menu's line under its cursor), and DONE and CANCEL.
    /// </summary>
    private void DrawPicker(Font font, Vector2 size, ColourPicker picker)
    {
        var white = new Color(1f, 1f, 1f);
        var grey = new Color(0.72f, 0.75f, 0.8f);
        var gold = new Color(1f, 0.85f, 0.3f);
        var panel = ColourPicker.Panel(size);
        DrawRect(panel, new Color(0.03f, 0.03f, 0.04f, 0.88f));
        var choice = picker.Choice;
        DrawString(font, new Vector2(panel.Position.X + 20f, panel.Position.Y + 36f), "MIX A COLOUR", HorizontalAlignment.Left, -1f, 22, white);
        DrawRect(new Rect2(panel.End.X - 58f, panel.Position.Y + 16f, 38f, 26f), choice.Colour);
        DrawString(font, new Vector2(panel.Position.X, panel.Position.Y + 36f), choice.Hex, HorizontalAlignment.Right, panel.Size.X - 68f, 16, grey);

        var square = ColourPicker.Square(size);
        DrawTextureRect(picker.SquareTexture, square, false);
        var at = new Vector2(square.Position.X + picker.Saturation * square.Size.X, square.Position.Y + (1f - picker.Lightness) * square.Size.Y);
        // A ring that shows on any colour: black inside white.
        DrawArc(at, 8f, 0f, MathF.Tau, 24, new Color(0f, 0f, 0f, 0.8f), 3f, true);
        DrawArc(at, 6f, 0f, MathF.Tau, 24, white, 2f, true);
        var bar = ColourPicker.HueBar(size);
        DrawTextureRect(ColourPicker.HueColumn, bar, false);
        var hueY = bar.Position.Y + picker.Hue * bar.Size.Y;
        DrawRect(new Rect2(bar.Position.X - 4f, hueY - 3f, bar.Size.X + 8f, 6f), new Color(0f, 0f, 0f, 0.8f), false, 2f);
        DrawRect(new Rect2(bar.Position.X - 2f, hueY - 1.5f, bar.Size.X + 4f, 3f), white);

        var values = new[] { ("HUE", $"{picker.Hue * 360f:0}°"), ("SATURATION", $"{picker.Saturation * 100f:0} %"), ("LIGHTNESS", $"{picker.Lightness * 100f:0} %") };
        for (var i = 0; i < values.Length; i++)
        {
            var row = ColourPicker.RowRect(size, (ColourPicker.Row)i);
            var focused = picker.Focus == (ColourPicker.Row)i;
            if (focused) DrawRect(row, new Color(1f, 0.85f, 0.3f, 0.16f));
            DrawString(font, new Vector2(row.Position.X + 8f, row.Position.Y + 22f), values[i].Item1, HorizontalAlignment.Left, -1f, 16, focused ? gold : grey);
            DrawString(font, new Vector2(row.Position.X, row.Position.Y + 22f), values[i].Item2, HorizontalAlignment.Right, row.Size.X - 8f, 16, focused ? gold : white);
        }
        var finishRow = ColourPicker.RowRect(size, ColourPicker.Row.Finish);
        var finishFocused = picker.Focus == ColourPicker.Row.Finish;
        if (finishFocused) DrawRect(finishRow, new Color(1f, 0.85f, 0.3f, 0.16f));
        DrawString(font, new Vector2(finishRow.Position.X + 8f, finishRow.Position.Y + 22f), "FINISH", HorizontalAlignment.Left, -1f, 16, finishFocused ? gold : grey);
        foreach (var finish in Enum.GetValues<PaintFinish>())
        {
            var button = ColourPicker.FinishButton(size, finish);
            var chosen = finish == picker.Finish;
            DrawRect(button, chosen ? new Color(1f, 0.85f, 0.3f, 0.28f) : new Color(1f, 1f, 1f, 0.06f));
            DrawString(font, new Vector2(button.Position.X, button.Position.Y + 21f), PaintChoice.FinishName(finish), HorizontalAlignment.Center, button.Size.X, 13, chosen ? gold : grey);
        }
        var done = ColourPicker.DoneButton(size);
        var cancel = ColourPicker.CancelButton(size);
        DrawRect(done, new Color(1f, 0.85f, 0.3f, 0.16f));
        DrawString(font, new Vector2(done.Position.X, done.Position.Y + 24f), "DONE  (ENTER)", HorizontalAlignment.Center, done.Size.X, 15, gold);
        DrawRect(cancel, new Color(1f, 1f, 1f, 0.06f));
        DrawString(font, new Vector2(cancel.Position.X, cancel.Position.Y + 24f), "CANCEL  (ESC)", HorizontalAlignment.Center, cancel.Size.X, 15, grey);
        DrawString(font, new Vector2(panel.Position.X, panel.End.Y - 14f), "up / down: row     left / right: change", HorizontalAlignment.Center, panel.Size.X, 13, grey);
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
        Centred(font, "F5: new game     ESC or BACK: menu and quit", panel.End.Y - 34f, 20, grey);
    }

    /// <summary>
    /// The figures that tell the cars apart on the title screen: peak power in metric horsepower (the units the
    /// period brochures used) worked out from the torque curve, mass, driven wheels and gears.
    /// </summary>
    private static string CarFigures(VehicleSpec spec)
    {
        var power = spec.Engine.FullThrottleCurve.Max(p => p.Rpm * p.Torque) / 7023f;
        var drive = spec.FourWheelDrive is not null ? "FOUR-WHEEL DRIVE" : spec.FrontWheelDrive ? "FRONT-WHEEL DRIVE" : "REAR-WHEEL DRIVE";
        var turbo = spec.Engine.BoostShare > 0f ? "  TURBO" : "";
        return $"{power:0} HP{turbo}   {spec.MassKg:0} KG   {drive}   {spec.ForwardGearCount} GEARS";
    }

    /// <summary>The car's place in the line-up (Lineup): a withdrawn car, which only automation drives, shows the place of the next one.</summary>
    private static int CarNumber(VehicleSpec spec)
    {
        var index = System.Array.FindIndex(VehicleSpec.Catalogue, c => c.Name == spec.Name);
        return Lineup.Number(Lineup.IsDrivable(index) ? index : Lineup.Step(index, 1));
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
                $"arrows/WASD drive   E/Q or 1-{car.Spec.ForwardGearCount} gears   N neutral   shift = clutch   ENTER key   SPACE abort/skip   C camera   ESC menu",
                HorizontalAlignment.Left, size.X - 40f, 13, new Color(0.8f, 0.8f, 0.8f, 0.8f));
        }
    }
}
