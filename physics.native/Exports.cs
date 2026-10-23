using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GrudgeLap.Physics.Native;

/// <summary>
/// The glp_* entry points of include/grudgelap_physics.h. Each one catches everything: an exception that crosses
/// into native code ends the host process, so a failure becomes -1 (or a 0 handle) and its message waits in
/// glp_last_error. A session handle is a GCHandle to its NativeSession, freed by glp_destroy.
/// </summary>
public static unsafe class Exports
{
    [ThreadStatic] private static string? _lastError;

    [UnmanagedCallersOnly(EntryPoint = "glp_abi_version", CallConvs = [typeof(CallConvCdecl)])]
    public static int AbiVersion() => NativeApi.AbiVersion;

    [UnmanagedCallersOnly(EntryPoint = "glp_create", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Create(int car)
    {
        try
        {
            return GCHandle.ToIntPtr(GCHandle.Alloc(NativeApi.Create(car)));
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_destroy", CallConvs = [typeof(CallConvCdecl)])]
    public static void Destroy(nint session)
    {
        if (session != 0) GCHandle.FromIntPtr(session).Free();
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_choose_transmission", CallConvs = [typeof(CallConvCdecl)])]
    public static int ChooseTransmission(nint session, int mode) => Guard(() => NativeApi.ChooseTransmission(Resolve(session), mode));

    [UnmanagedCallersOnly(EntryPoint = "glp_scripted_inputs", CallConvs = [typeof(CallConvCdecl)])]
    public static int ScriptedInputs(nint session, NativeInputs* output)
    {
        try
        {
            *output = NativeApi.ScriptedInputs(Resolve(session));
            return 0;
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_step", CallConvs = [typeof(CallConvCdecl)])]
    public static int Step(nint session, NativeInputs* inputs, float dt)
    {
        try
        {
            NativeApi.Step(Resolve(session), *inputs, dt);
            return 0;
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_get_state", CallConvs = [typeof(CallConvCdecl)])]
    public static int GetState(nint session, NativeState* output)
    {
        try
        {
            *output = NativeApi.State(Resolve(session));
            return 0;
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_message", CallConvs = [typeof(CallConvCdecl)])]
    public static int Message(nint session, byte* buffer, int capacity)
    {
        try
        {
            return NativeApi.CopyText(Resolve(session).Session.Message, buffer, capacity);
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static int LastError(byte* buffer, int capacity) => NativeApi.CopyText(_lastError ?? "", buffer, capacity);

    private static NativeSession Resolve(nint session) =>
        session != 0 && GCHandle.FromIntPtr(session).Target is NativeSession native
            ? native
            : throw new ArgumentException("not a session handle", nameof(session));

    private static int Guard(Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }
}
