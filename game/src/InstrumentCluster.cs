using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The car's instrument panel, drawn into its own viewport: a speedometer with an odometer, a tachometer with
/// its red zone, a gear readout between them and the red warning lights that glow while the engine is off.
/// The texture is the face of the binnacle in the cockpit (<see cref="CarView.MountInstruments"/>) and the
/// cluster at the foot of the screen in the other views (<see cref="Hud"/>), so both always agree.
/// </summary>
internal sealed partial class InstrumentCluster : Control
{
    public static readonly Vector2I PanelSize = new(1024, 400);
    private static readonly Color Face = new(0.025f, 0.025f, 0.03f);
    private static readonly Color Ink = new(0.92f, 0.92f, 0.9f);
    private static readonly Color Needle = new(1f, 0.42f, 0.12f);

    private RaceSession? _session;
    private double _odometer;    // metres since the credit started

    public bool UseKmh { get; set; }

    /// <summary>Builds the viewport the panel is drawn into; add it to the tree and use its texture.</summary>
    public static (SubViewport Viewport, InstrumentCluster Cluster) Create()
    {
        var viewport = new SubViewport
        {
            Name = "Instruments",
            Size = PanelSize,
            TransparentBg = false,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var cluster = new InstrumentCluster { Size = PanelSize };
        viewport.AddChild(cluster);
        return (viewport, cluster);
    }

    public void Bind(RaceSession session)
    {
        _session = session;
        _odometer = 0;
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        _odometer += MathF.Abs(_session.Car.Speed) * delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, PanelSize), Face);
        if (_session is null) return;
        var font = ThemeDB.FallbackFont;
        var car = _session.Car;
        var running = car.Drivetrain.Engine.Running;

        var speed = UseKmh ? car.SpeedMph / MathUtil.MphPerMps * MathUtil.KmhPerMps : car.SpeedMph;
        var speedMax = UseKmh ? MathF.Round(car.Spec.SpeedoMaxMph * 1.6f / 20f) * 20f : car.Spec.SpeedoMaxMph;
        var speedCentre = new Vector2(262f, 206f);
        // Numbers every 20 (every 40 on a long km/h scale) with four ticks between: denser scales are unreadable at a glance.
        var step = UseKmh && speedMax > 280f ? 40f : 20f;
        Dial(font, speedCentre, 176f, speed, speedMax, (int)MathF.Round(speedMax / step), 4, UseKmh ? "km/h" : "MPH", -1f);
        var distance = UseKmh ? _odometer / 1000.0 : _odometer / 1609.344;
        Readout(font, speedCentre + new Vector2(0f, 92f), distance.ToString("00000.0"), 22);

        var rpm = car.Drivetrain.Engine.Rpm;
        var redline = car.Spec.Engine.RedlineRpm;
        var tachMax = MathF.Ceiling((redline + 800f) / 1000f);
        var tachCentre = new Vector2(762f, 206f);
        Dial(font, tachCentre, 176f, rpm / 1000f, tachMax, (int)tachMax, 5, "RPM x 1000", redline / 1000f);

        // Gear readout and warning lights in the column between the dials.
        var gear = car.Drivetrain.Gear switch { -1 => "R", 0 => "N", var g => g.ToString() };
        var box = new Rect2(474f, 120f, 76f, 76f);
        DrawRect(box, new Color(0.01f, 0.03f, 0.02f));
        DrawRect(box, new Color(0.3f, 0.3f, 0.32f), false, 2f);
        DrawString(font, new Vector2(box.Position.X, box.End.Y - 14f), gear, HorizontalAlignment.Center, box.Size.X, 54, new Color(1f, 0.75f, 0.2f));
        var mode = car.Drivetrain.Mode switch
        {
            TransmissionMode.Automatic => "AUTO",
            TransmissionMode.ManualAutoClutch => "STICK",
            _ => "CLUTCH",
        };
        DrawString(font, new Vector2(442f, 222f), mode, HorizontalAlignment.Center, 140f, 16, new Color(0.65f, 0.65f, 0.65f));
        // Oil pressure and charge lamps light up with the ignition on and the engine stopped, as in any car.
        Lamp(font, new Vector2(490f, 268f), "OIL", !running);
        Lamp(font, new Vector2(534f, 268f), "BAT", !running);
    }

    private void Dial(Font font, Vector2 centre, float radius, float value, float max, int majors, int minorsPerMajor, string caption, float redlineFrom)
    {
        const float start = Mathf.Pi * 0.75f;
        const float sweep = Mathf.Pi * 1.5f;
        DrawCircle(centre, radius + 8f, new Color(0.16f, 0.16f, 0.17f));
        DrawCircle(centre, radius + 3f, new Color(0.005f, 0.005f, 0.008f));
        if (redlineFrom > 0f)
        {
            var a0 = start + sweep * (redlineFrom / max);
            DrawArc(centre, radius - 9f, a0, start + sweep, 16, new Color(0.85f, 0.12f, 0.08f), 12f, true);
        }
        var steps = majors * minorsPerMajor;
        for (var i = 0; i <= steps; i++)
        {
            var major = i % minorsPerMajor == 0;
            var a = start + sweep * i / steps;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(centre + dir * (radius - (major ? 22f : 11f)), centre + dir * (radius - 2f), Ink, major ? 4f : 2f, true);
            if (!major) continue;
            var label = (max * i / steps).ToString("0");
            DrawString(font, centre + dir * (radius - 48f) - new Vector2(24f, -9f), label, HorizontalAlignment.Center, 48f, 24, Ink);
        }
        DrawString(font, centre + new Vector2(-80f, 50f), caption, HorizontalAlignment.Center, 160f, 16, new Color(0.7f, 0.7f, 0.7f));
        var t = MathUtil.Clamp01(value / max);
        var na = start + sweep * t;
        var along = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
        var across = new Vector2(-along.Y, along.X);
        DrawColoredPolygon([centre - along * 24f + across * 5f, centre + along * (radius - 14f), centre - along * 24f - across * 5f], Needle);
        DrawCircle(centre, 13f, new Color(0.12f, 0.12f, 0.13f));
    }

    private void Readout(Font font, Vector2 centre, string text, int size)
    {
        var rect = new Rect2(centre - new Vector2(56f, 15f), new Vector2(112f, 30f));
        DrawRect(rect, new Color(0.0f, 0.0f, 0.0f));
        DrawRect(rect, new Color(0.25f, 0.25f, 0.27f), false, 1.5f);
        DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 7f), text, HorizontalAlignment.Center, rect.Size.X, size, new Color(0.85f, 0.85f, 0.8f));
    }

    private void Lamp(Font font, Vector2 centre, string label, bool lit)
    {
        DrawCircle(centre, 16f, lit ? new Color(0.95f, 0.12f, 0.08f) : new Color(0.12f, 0.03f, 0.03f));
        DrawString(font, centre + new Vector2(-20f, 5f), label, HorizontalAlignment.Center, 40f, 12, lit ? new Color(1f, 0.9f, 0.85f) : new Color(0.35f, 0.25f, 0.25f));
    }
}
