using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The menu Esc (or the pad's Back) opens over the game: carry on, a new game, the car (its showroom), the car's paint,
/// the camera, the picture, the weather, the units, full screen and quitting (0.71: no longer the replay music). While it is
/// open the race stands still and the sound is off; the arrow keys, the d-pad and the mouse move its cursor instead of
/// the car.
/// </summary>
/// <remarks>
/// Until 0.8.1 the game had no way out but closing its window, which a full-screen window does not offer: players
/// were left looking for a key that did not exist. The layout lives here so that the drawing (<see cref="Hud"/>) and
/// the mouse's hit test agree.
/// </remarks>
internal sealed class PauseMenu
{
    public enum Item
    {
        Continue,
        NewGame,
        /// <summary>The car, chosen in the showroom (0.25, <see cref="Showroom"/>): taking the line opens it on the car of the credit.</summary>
        Car,
        /// <summary>The car's colour: its own (its model's), one of the classic paints (0.21) or the colour mixed for it in the showroom (0.25); each car's own by default, and kept per car.</summary>
        Paint,
        Camera,
        Graphics,
        /// <summary>Clear, overcast, wet road, rain, or random for every credit (0.15); clear by default.</summary>
        Weather,
        /// <summary>The hour: the day, the sunrise, or one of them for every credit (0.55); the day by default.</summary>
        Light,
        Retro,
        Units,
        Fullscreen,
        Quit,
    }

    public static readonly Item[] Items = System.Enum.GetValues<Item>();

    public bool Open { get; private set; }
    public int Cursor { get; private set; }
    public Item Current => Items[Cursor];

    /// <summary>The text of each line, the setting's current value included, filled in by the game while open.</summary>
    public string[] Labels { get; } = new string[Items.Length];

    public void Show()
    {
        Open = true;
        Cursor = 0;
    }

    public void Hide() => Open = false;

    public void Move(int step) => Cursor = (Cursor + step + Items.Length) % Items.Length;

    public void Select(int index)
    {
        if (index >= 0 && index < Items.Length) Cursor = index;
    }

    // Thirteen lines since the LIGHT line (0.55) at a 44 px pitch: 704 px, room for the hint under the last. The canvas is
    // 1600 x 900 stretched with "expand", never under 900 lines high whatever the window.
    public static Rect2 Panel(Vector2 size) => new(size.X * 0.5f - 340f, size.Y * 0.5f - 352f, 680f, 704f);

    public static Rect2 ItemRect(Vector2 size, int index)
    {
        var panel = Panel(size);
        return new Rect2(panel.Position.X + 40f, panel.Position.Y + 90f + index * 44f, panel.Size.X - 80f, 40f);
    }

    /// <summary>The line under a point of the screen, or -1.</summary>
    public static int HitTest(Vector2 size, Vector2 point)
    {
        for (var i = 0; i < Items.Length; i++)
        {
            if (ItemRect(size, i).HasPoint(point)) return i;
        }
        return -1;
    }
}
