using System.Runtime.InteropServices;

namespace GrudgeLap.Physics.Native;

// Mirror include/grudgelap_physics.h field for field. Sequential layout pads like C: the int64 score follows
// four bytes of padding at offset 88, and glp_state ends padded to 352 bytes. Change both together and bump
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

/// <summary>glp_state: what a renderer and its sound need from one step.</summary>
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
    // ABI 2
    public int Car;
    public int ForwardGears;
    public int Transmission;
    public int EngineRunning;
    public float EngineTorque;
    public float LateralAcceleration;
    public float LongitudinalAcceleration;
    public fixed int WheelSurface[4];
    public fixed float WheelSlipAngle[4];
    public fixed float WheelSlipRatio[4];
    public int Route;
    public int OffRoad;
    public float OffRoadTimer;
    public int WrongWay;
    public int CrashCount;
    public int LastCrash;
    public uint Events;
    public int LastSoftHit;
    public int GrudgeMatch;
    public int GhostVisible;
    public fixed float GhostPosition[3];
    public fixed float GhostOrientation[4];
    public float GhostSteer;
    public float ReplayTime;
    public float ReplaySeconds;
    public fixed float ReplayPosition[3];
    public fixed float ReplayOrientation[4];
    public float ReplaySteer;
    public float Elapsed;
    public int TrainRunning;
}

/// <summary>glp_car_spec: the catalogue's static facts about a car.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeCarSpec
{
    public int Style;
    public int ForwardGears;
    public float SpeedoMaxMph;
    public float RedlineRpm;
    public float WheelRadius;
    public float HalfTrack;
    public float FrontAxle;
    public float RearAxle;
    public float HubHeight;
    public float SuspensionRest;
    public float CgHeight;
}

/// <summary>glp_body: a traffic car or a car of the train.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeBody
{
    public fixed float Position[3];
    public fixed float Orientation[4];
    public float Speed;
    public int Colour;
}

/// <summary>glp_prop: a roadside object of the track.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeProp
{
    public int Kind;
    public fixed float Position[3];
    public float YawDegrees;
    public fixed float Size[3];
    public int Knocked;
}
