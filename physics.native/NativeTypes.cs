using System.Runtime.InteropServices;

namespace GrudgeLap.Physics.Native;

// Mirror include/grudgelap_physics.h field for field. Sequential layout pads like C: the int64 score follows
// four bytes of padding at offset 88, and glp_state is 152 bytes. Change both together and bump
// NativeApi.AbiVersion.

/// <summary>glp_inputs: the driver's controls as the C side sends them.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeInputs
{
    public float Steer;
    public float Throttle;
    public float Brake;
    public float Clutch;
    public int Gear;
    public int Ignition;
    public int ShiftUp;
    public int ShiftDown;
}

/// <summary>glp_state: what a renderer needs from one step.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeState
{
    public fixed float Position[3];
    public fixed float Orientation[4];
    public fixed float Velocity[3];
    public float Speed;
    public float SteerAngle;
    public float Rpm;
    public int Gear;
    public int Phase;
    public int Lap;
    public int OnRoad;
    public int Airborne;
    public float TimeRemaining;
    public float LapTime;
    public float BestLapTime;
    public long Score;
    public fixed float WheelSpin[4];
    public fixed float WheelCompression[4];
    public fixed int WheelContact[4];
    public float HubHeight;
    public float SuspensionRest;
}
