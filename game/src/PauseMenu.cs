using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The menu Esc (or the pad's Back) opens over the game: carry on, a new game, the camera, the picture, the units,
/// full screen and quitting. While it is open the race stands still and the sound is off; the arrow keys, the d-pad
/// and the mouse move its cursor instead of the car.
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
        Camera,
        Graphics,
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

    public static Rect2 Panel(Vector2 size) => new(size.X * 0.5f - 300f, size.Y * 0.5f - 270f, 600f, 540f);

    public static Rect2 ItemRect(Vector2 size, int index)
    {
        var panel = Panel(size);
        return new Rect2(panel.Position.X + 40f, panel.Position.Y + 96f + index * 48f, panel.Size.X - 80f, 42f);
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
