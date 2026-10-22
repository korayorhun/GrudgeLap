using Godot;

namespace GrudgeLap.Game;

/// <summary>Keyboard bindings, registered in code so the project file stays readable.</summary>
internal static class InputSetup
{
    public const string SteerLeft = "steer_left";
    public const string SteerRight = "steer_right";
    public const string Throttle = "throttle";
    public const string Brake = "brake";
    public const string Clutch = "clutch";
    public const string ShiftUp = "shift_up";
    public const string ShiftDown = "shift_down";
    public const string Gear1 = "gear_1";
    public const string Gear2 = "gear_2";
    public const string Gear3 = "gear_3";
    public const string Gear4 = "gear_4";
    public const string Gear5 = "gear_5";
    public const string Gear6 = "gear_6";
    public const string Gear7 = "gear_7";
    public const string Neutral = "gear_neutral";
    public const string Reverse = "gear_reverse";
    public const string Ignition = "ignition";
    public const string Abort = "abort";
    public const string Camera = "camera";
    public const string Fullscreen = "fullscreen";
    public const string Screenshot = "screenshot";
    public const string Units = "units";
    public const string NewGame = "new_game";
    public const string SelectAuto = "select_auto";
    public const string SelectManual = "select_manual";
    public const string SelectClutch = "select_clutch";
    public const string Attract = "attract";
    public const string Retro = "retro";
    public const string Update = "update";

    public static void Register()
    {
        Bind(SteerLeft, Key.Left, Key.A);
        Bind(SteerRight, Key.Right, Key.D);
        Bind(Throttle, Key.Up, Key.W);
        Bind(Brake, Key.Down, Key.S);
        Bind(Clutch, Key.Shift);
        Bind(ShiftUp, Key.E, Key.Pageup);
        Bind(ShiftDown, Key.Q, Key.Pagedown);
        Bind(Gear1, Key.Key1);
        Bind(Gear2, Key.Key2);
        Bind(Gear3, Key.Key3);
        Bind(Gear4, Key.Key4);
        Bind(Gear5, Key.Key5);
        Bind(Gear6, Key.Key6);
        Bind(Gear7, Key.Key7);
        Bind(Neutral, Key.N);
        Bind(Reverse, Key.R);
        Bind(Ignition, Key.Enter, Key.KpEnter);
        Bind(Abort, Key.Space);
        Bind(Camera, Key.C);
        Bind(Fullscreen, Key.F11);
        Bind(Screenshot, Key.F12);
        Bind(Units, Key.U);
        Bind(NewGame, Key.F5);
        Bind(SelectAuto, Key.Key1);
        Bind(SelectManual, Key.Key2);
        Bind(SelectClutch, Key.Key3);
        Bind(Attract, Key.F9);
        Bind(Retro, Key.F8);
        Bind(Update, Key.F10);
    }

    private static void Bind(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        foreach (var key in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}
