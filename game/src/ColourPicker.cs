using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The showroom's colour picker (0.25): a square of saturation (across) and lightness (down) beside a bar of hue, the
/// three as rows the keys step through with the finish under them, so the player can mix a car's colour by keyboard,
/// pad or mouse and see it on the turning car as it changes (<see cref="Showroom"/> repaints it). HSL rather than HSV:
/// the lightness runs from black through the full colour to white, so the pastels and the dark shades of one hue lie
/// on one line of the square. The layout of what the mouse can hit lives here, as the showroom's does, so the HUD's
/// drawing (<c>Hud.DrawPicker</c>) and the hit test agree.
/// </summary>
internal sealed class ColourPicker
{
    /// <summary>The rows the keys move between: up and down choose one, left and right change it.</summary>
    public enum Row { Hue, Saturation, Lightness, Finish }

    private const int SquareTexels = 64;      // drawn at 240 px with the canvas's linear filter: a smooth gradient
    private static ImageTexture? _hueStrip;
    private static ImageTexture? _hueColumn;
    private ImageTexture? _square;
    private float _squareHue = -1f;
    private float _held;                      // seconds left or right has been held on a value row

    /// <param name="start">The colour it opens on.</param>
    /// <param name="before">The car's paint before it opened, put back by CANCEL.</param>
    /// <param name="hue">The hue the picker had when it last closed on this car, if it did.</param>
    public ColourPicker(PaintChoice start, PaintChoice before, float? hue = null)
    {
        (Hue, Saturation, Lightness) = Hsl.From(start.Colour);
        // A grey (and a colour so pale or dark that its hex rounds the hue away) has no hue of its own, and opened at 0,
        // red, wherever the player had left the bar (the showroom's review). The last hue is taken back when it gives
        // the same colour, so nothing about the colour changes.
        if (hue is { } kept && PaintChoice.Mixed(Hsl.ToColour(kept, Saturation, Lightness), start.Finish).Hex == start.Hex) Hue = kept;
        Finish = start.Finish;
        Before = before;
    }

    public float Hue { get; private set; }
    public float Saturation { get; private set; }
    public float Lightness { get; private set; }
    public PaintFinish Finish { get; private set; }
    public Row Focus { get; private set; }
    /// <summary>The car's paint when the picker opened: cancelling puts it back.</summary>
    public PaintChoice Before { get; }
    /// <summary>The mouse's button went down on the square or the hue bar and is held: the value follows the mouse.</summary>
    public Hit Dragging { get; set; }
    public PaintChoice Choice => PaintChoice.Mixed(Hsl.ToColour(Hue, Saturation, Lightness), Finish);

    public void MoveFocus(int step) => Focus = (Row)(((int)Focus + step + 4) % 4);

    /// <summary>
    /// Left and right on the focused row: a tap moves a value one step (a degree of hue, a per cent of saturation or
    /// lightness) or the finish to the next; held for a quarter of a second, a value runs on, faster the longer it is
    /// held (from 30 to 150 steps a second), so a tap is exact and the whole circle of hues is crossed in a few
    /// seconds. True when the colour or the finish changed.
    /// </summary>
    public bool Nudge(int held, int pressed, float dt)
    {
        if (held == 0 && pressed == 0)
        {
            _held = 0f;
            return false;
        }
        if (Focus == Row.Finish)
        {
            if (pressed == 0) return false;
            Finish = (PaintFinish)(((int)Finish + pressed + 3) % 3);
            return true;
        }
        var unit = Focus == Row.Hue ? 1f / 360f : 0.01f;
        float delta;
        if (pressed != 0)
        {
            _held = 0f;
            delta = pressed * unit;
        }
        else
        {
            _held += dt;
            if (_held < 0.25f) return false;
            delta = held * MathF.Min(30f + (_held - 0.25f) * 90f, 150f) * unit * dt;
        }
        switch (Focus)
        {
            case Row.Hue:
                Hue = (Hue + delta + 1f) % 1f;
                break;
            case Row.Saturation:
                Saturation = Math.Clamp(Saturation + delta, 0f, 1f);
                break;
            default:
                Lightness = Math.Clamp(Lightness + delta, 0f, 1f);
                break;
        }
        return true;
    }

    /// <summary>The mouse on the square (saturation and lightness) or the hue bar, clamped to them while it is dragged.</summary>
    public void Pick(Vector2 size, Hit where, Vector2 point)
    {
        if (where == Hit.Square)
        {
            var square = Square(size);
            Saturation = Math.Clamp((point.X - square.Position.X) / square.Size.X, 0f, 1f);
            Lightness = Math.Clamp(1f - (point.Y - square.Position.Y) / square.Size.Y, 0f, 1f);
            Focus = Row.Saturation;
        }
        else if (where == Hit.HueBar)
        {
            var bar = HueBar(size);
            Hue = Math.Clamp((point.Y - bar.Position.Y) / bar.Size.Y, 0f, 0.9999f);
            Focus = Row.Hue;
        }
    }

    public void SetFinish(PaintFinish finish)
    {
        Finish = finish;
        Focus = Row.Finish;
    }

    /// <summary>The picker set to a mixed colour as a whole (automation: <c>--showroom-paint=#RRGGBB:finish</c>).</summary>
    public void Set(PaintChoice choice)
    {
        (Hue, Saturation, Lightness) = Hsl.From(choice.Colour);
        Finish = choice.Finish;
    }

    /// <summary>The square in the picker's hue, built again when the hue moves (64 x 64 texels, a fraction of a millisecond).</summary>
    public Texture2D SquareTexture
    {
        get
        {
            if (_square is not null && MathF.Abs(_squareHue - Hue) < 1e-4f) return _square;
            var bytes = new byte[SquareTexels * SquareTexels * 3];
            for (var y = 0; y < SquareTexels; y++)
            {
                for (var x = 0; x < SquareTexels; x++)
                {
                    var colour = Hsl.ToColour(Hue, x / (SquareTexels - 1f), 1f - y / (SquareTexels - 1f));
                    var at = (y * SquareTexels + x) * 3;
                    bytes[at] = (byte)Mathf.RoundToInt(colour.R * 255f);
                    bytes[at + 1] = (byte)Mathf.RoundToInt(colour.G * 255f);
                    bytes[at + 2] = (byte)Mathf.RoundToInt(colour.B * 255f);
                }
            }
            var image = Image.CreateFromData(SquareTexels, SquareTexels, false, Image.Format.Rgb8, bytes);
            if (_square is null) _square = ImageTexture.CreateFromImage(image);
            else _square.Update(image);
            _squareHue = Hue;
            return _square;
        }
    }

    /// <summary>The hues in a strip from red round to red, left to right (under the mixed colour's swatch).</summary>
    public static Texture2D HueStrip => _hueStrip ??= HueTexture(across: true);

    /// <summary>The same hues top to bottom, for the picker's bar.</summary>
    public static Texture2D HueColumn => _hueColumn ??= HueTexture(across: false);

    private static ImageTexture HueTexture(bool across)
    {
        const int texels = 256;
        var bytes = new byte[texels * 3];
        for (var i = 0; i < texels; i++)
        {
            var colour = Hsl.ToColour(i / (float)texels, 1f, 0.5f);
            bytes[i * 3] = (byte)Mathf.RoundToInt(colour.R * 255f);
            bytes[i * 3 + 1] = (byte)Mathf.RoundToInt(colour.G * 255f);
            bytes[i * 3 + 2] = (byte)Mathf.RoundToInt(colour.B * 255f);
        }
        var image = across
            ? Image.CreateFromData(texels, 1, false, Image.Format.Rgb8, bytes)
            : Image.CreateFromData(1, texels, false, Image.Format.Rgb8, bytes);
        return ImageTexture.CreateFromImage(image);
    }

    // ---- layout, shared with the HUD's drawing and the mouse's hit test --------------------------------

    /// <summary>
    /// The picker's panel: on the right of the stage, clear of the right chevron, between the bar with the car's name
    /// and the showroom's panel (900 lines high: 140 to 650 of the stage's 64 to 664), so the car, moved left and
    /// framed further off while it is open (<see cref="Showroom"/>), stays in view.
    /// </summary>
    public static Rect2 Panel(Vector2 size) => new(size.X - 480f, 140f, 330f, 510f);
    public static Rect2 Square(Vector2 size) => Inside(size, 20f, 52f, 240f, 240f);
    public static Rect2 HueBar(Vector2 size) => Inside(size, 276f, 52f, 34f, 240f);
    public static Rect2 RowRect(Vector2 size, Row row) => Inside(size, 12f, 302f + (int)row * 32f, 306f, 30f);
    public static Rect2 FinishButton(Vector2 size, PaintFinish finish) => Inside(size, 92f + (int)finish * 76f, 302f + 3 * 32f, 72f, 30f);
    public static Rect2 DoneButton(Vector2 size) => Inside(size, 20f, 446f, 140f, 36f);
    public static Rect2 CancelButton(Vector2 size) => Inside(size, 170f, 446f, 140f, 36f);

    private static Rect2 Inside(Vector2 size, float x, float y, float width, float height)
    {
        var panel = Panel(size);
        return new Rect2(panel.Position.X + x, panel.Position.Y + y, width, height);
    }

    /// <summary>What a click on the picker lands on.</summary>
    public enum Hit { Nothing, Square, HueBar, Finish, Done, Cancel, Panel }

    public static (Hit Hit, PaintFinish Finish) HitTest(Vector2 size, Vector2 point)
    {
        if (!Panel(size).HasPoint(point)) return (Hit.Nothing, default);
        // The square and the bar take a click a few pixels outside them: their edges are the extremes of a value.
        if (Square(size).Grow(6f).HasPoint(point)) return (Hit.Square, default);
        if (HueBar(size).Grow(6f).HasPoint(point)) return (Hit.HueBar, default);
        foreach (var finish in Enum.GetValues<PaintFinish>())
        {
            if (FinishButton(size, finish).HasPoint(point)) return (Hit.Finish, finish);
        }
        if (DoneButton(size).HasPoint(point)) return (Hit.Done, default);
        if (CancelButton(size).HasPoint(point)) return (Hit.Cancel, default);
        return (Hit.Panel, default);
    }
}
