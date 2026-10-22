namespace GrudgeLap.Physics.Vehicle;

/// <summary>Raw control positions for one simulation step. All axes are 0..1 except steering (-1..1, right positive).</summary>
public struct DriverInputs
{
    public float Steer;
    public float Throttle;
    public float Brake;
    /// <summary>1 = pedal fully pressed (clutch open), 0 = released (engaged).</summary>
    public float Clutch;
    /// <summary>Requested gear: -1 reverse, 0 neutral, 1..4 forward. Use <see cref="NoGearChange"/> to keep the current gear.</summary>
    public int Gear;
    /// <summary>Momentary: the driver turned the ignition key this step.</summary>
    public bool Ignition;
    /// <summary>Momentary shift requests used by sequential keyboard control; ignored when <see cref="Gear"/> is explicit.</summary>
    public bool ShiftUp;
    public bool ShiftDown;

    public const int NoGearChange = 99;

    public static DriverInputs Idle => new() { Gear = NoGearChange };
}

public enum TransmissionMode
{
    /// <summary>The game shifts and works the clutch; the player cannot stall.</summary>
    Automatic,
    /// <summary>The player chooses gears, the game works the clutch (default for keyboard play).</summary>
    ManualAutoClutch,
    /// <summary>The player works gears and clutch; releasing the clutch too early stalls the engine, as in the cockpit cabinet.</summary>
    ManualClutch,
}
