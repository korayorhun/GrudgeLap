using System.Text;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics.Native;

/// <summary>One race session behind a C handle, with the attract-mode driver that may drive it.</summary>
internal sealed class NativeSession(RaceSession session)
{
    public RaceSession Session { get; } = session;
    public ScriptedDriver Driver { get; } = new();
}

/// <summary>
/// The managed side of the C interface: everything Exports does, without the native calling convention, so the
/// tests can drive it. Each function maps one glp_* function of include/grudgelap_physics.h.
/// </summary>
internal static class NativeApi
{
    public const int AbiVersion = 1;

    /// <remarks>Traffic is off: the Unreal version has no cars to draw for it yet, and invisible traffic would
    /// still collide.</remarks>
    public static NativeSession Create(int car)
    {
        var catalogue = VehicleSpec.Catalogue;
        if (car < 0 || car >= catalogue.Length) throw new ArgumentOutOfRangeException(nameof(car), car, $"there are {catalogue.Length} cars");
        var options = new RaceOptions { TrafficSameDirection = 0, TrafficOncoming = 0 };
        return new NativeSession(new RaceSession(OriginalInspiredTrack.Build(), catalogue[car], options));
    }

    public static void ChooseTransmission(NativeSession native, int mode)
    {
        if (!Enum.IsDefined((TransmissionMode)mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown transmission mode");
        native.Session.ChooseTransmission((TransmissionMode)mode);
    }

    public static NativeInputs ScriptedInputs(NativeSession native) => ToNative(native.Driver.Next(native.Session));

    public static void Step(NativeSession native, in NativeInputs inputs, float dt) => native.Session.Step(FromNative(inputs), dt);

    public static unsafe NativeState State(NativeSession native)
    {
        var session = native.Session;
        var car = session.Car;
        var state = new NativeState
        {
            Speed = car.Speed,
            SteerAngle = car.SteerAngle,
            Rpm = car.Drivetrain.Engine.Rpm,
            Gear = car.Drivetrain.Gear,
            Phase = (int)session.Phase,
            Lap = session.Lap,
            OnRoad = car.OnRoad ? 1 : 0,
            Airborne = car.Airborne ? 1 : 0,
            TimeRemaining = session.TimeRemaining,
            LapTime = session.LapTime,
            BestLapTime = session.BestLapTime,
            Score = session.Score,
            HubHeight = car.Spec.HubHeight,
            SuspensionRest = car.Spec.SuspensionRest,
        };
        state.Position[0] = car.Position.X;
        state.Position[1] = car.Position.Y;
        state.Position[2] = car.Position.Z;
        state.Orientation[0] = car.Orientation.X;
        state.Orientation[1] = car.Orientation.Y;
        state.Orientation[2] = car.Orientation.Z;
        state.Orientation[3] = car.Orientation.W;
        state.Velocity[0] = car.Velocity.X;
        state.Velocity[1] = car.Velocity.Y;
        state.Velocity[2] = car.Velocity.Z;
        for (var i = 0; i < 4; i++)
        {
            var wheel = car.Wheels[i];
            state.WheelSpin[i] = wheel.SpinAngle;
            state.WheelCompression[i] = wheel.InContact ? wheel.Compression : 0f;
            state.WheelContact[i] = wheel.InContact ? 1 : 0;
        }
        return state;
    }

    /// <summary>Writes text as UTF-8 into a C buffer: cut to capacity - 1 bytes on a whole character, always
    /// terminated. Returns the text's full length in bytes so a caller can retry with a larger buffer.</summary>
    public static unsafe int CopyText(string text, byte* buffer, int capacity)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (buffer == null || capacity <= 0) return bytes.Length;
        var count = Math.Min(bytes.Length, capacity - 1);
        while (count > 0 && count < bytes.Length && (bytes[count] & 0xC0) == 0x80) count--;  // not inside a character
        bytes.AsSpan(0, count).CopyTo(new Span<byte>(buffer, capacity));
        buffer[count] = 0;
        return bytes.Length;
    }

    private static DriverInputs FromNative(in NativeInputs n) => new()
    {
        Steer = n.Steer,
        Throttle = n.Throttle,
        Brake = n.Brake,
        Clutch = n.Clutch,
        Gear = n.Gear,
        Ignition = n.Ignition != 0,
        ShiftUp = n.ShiftUp != 0,
        ShiftDown = n.ShiftDown != 0,
    };

    private static NativeInputs ToNative(in DriverInputs d) => new()
    {
        Steer = d.Steer,
        Throttle = d.Throttle,
        Brake = d.Brake,
        Clutch = d.Clutch,
        Gear = d.Gear,
        Ignition = d.Ignition ? 1 : 0,
        ShiftUp = d.ShiftUp ? 1 : 0,
        ShiftDown = d.ShiftDown ? 1 : 0,
    };
}
