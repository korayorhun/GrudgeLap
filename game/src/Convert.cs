using Godot;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// Bridges the simulation's coordinate system (right-handed, +Y up, forward +Z, right +X) and Godot's
/// (right-handed, +Y up, forward -Z). Only Z flips; everything else is copied straight across.
/// </summary>
internal static class Convert
{
    public static Vector3 ToGodot(SN.Vector3 v) => new(v.X, v.Y, -v.Z);

    public static SN.Vector3 ToSim(Vector3 v) => new(v.X, v.Y, -v.Z);

    /// <summary>Builds a node transform whose -Z is the car's forward and +X its right.</summary>
    public static Transform3D ToGodot(SN.Vector3 position, SN.Quaternion orientation)
    {
        var right = SN.Vector3.Transform(SN.Vector3.UnitX, orientation);
        var up = SN.Vector3.Transform(SN.Vector3.UnitY, orientation);
        var forward = SN.Vector3.Transform(SN.Vector3.UnitZ, orientation);
        var basis = new Basis(ToGodot(right), ToGodot(up), ToGodot(-forward));
        return new Transform3D(basis, ToGodot(position));
    }

    /// <summary>Transform from an orthonormal frame given as sim-space axes.</summary>
    public static Transform3D FrameToGodot(SN.Vector3 origin, SN.Vector3 forward, SN.Vector3 up)
    {
        var right = SN.Vector3.Cross(up, forward);
        var basis = new Basis(ToGodot(right), ToGodot(up), ToGodot(-forward));
        return new Transform3D(basis, ToGodot(origin));
    }
}
