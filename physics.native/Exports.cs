using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrudgeLap.Audio;

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

    [UnmanagedCallersOnly(EntryPoint = "glp_car_count", CallConvs = [typeof(CallConvCdecl)])]
    public static int CarCount() => NativeApi.CarCount;

    [UnmanagedCallersOnly(EntryPoint = "glp_car_name", CallConvs = [typeof(CallConvCdecl)])]
    public static int CarName(int car, byte* buffer, int capacity) => Guarded(() => NativeApi.CopyText(NativeApi.CarName(car), buffer, capacity));

    [UnmanagedCallersOnly(EntryPoint = "glp_get_car_spec", CallConvs = [typeof(CallConvCdecl)])]
    public static int CarSpec(int car, NativeCarSpec* output) => Guarded(() => *output = NativeApi.CarSpec(car));

    [UnmanagedCallersOnly(EntryPoint = "glp_create", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Create(int car, int flags)
    {
        try
        {
            return GCHandle.ToIntPtr(GCHandle.Alloc(NativeApi.Create(car, flags)));
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
    public static int ChooseTransmission(nint session, int mode) => Guarded(() => NativeApi.ChooseTransmission(Resolve(session), mode));

    [UnmanagedCallersOnly(EntryPoint = "glp_abort", CallConvs = [typeof(CallConvCdecl)])]
    public static int Abort(nint session) => Guarded(() => NativeApi.Abort(Resolve(session)));

    [UnmanagedCallersOnly(EntryPoint = "glp_scripted_inputs", CallConvs = [typeof(CallConvCdecl)])]
    public static int ScriptedInputs(nint session, NativeInputs* output) => Guarded(() => *output = NativeApi.ScriptedInputs(Resolve(session)));

    [UnmanagedCallersOnly(EntryPoint = "glp_step", CallConvs = [typeof(CallConvCdecl)])]
    public static int Step(nint session, NativeInputs* inputs, float dt) => Guarded(() => NativeApi.Step(Resolve(session), *inputs, dt));

    [UnmanagedCallersOnly(EntryPoint = "glp_get_state", CallConvs = [typeof(CallConvCdecl)])]
    public static int GetState(nint session, NativeState* output) => Guarded(() => *output = NativeApi.State(Resolve(session)));

    [UnmanagedCallersOnly(EntryPoint = "glp_message", CallConvs = [typeof(CallConvCdecl)])]
    public static int Message(nint session, byte* buffer, int capacity) => Guarded(() => NativeApi.CopyText(Resolve(session).Session.Message, buffer, capacity));

    [UnmanagedCallersOnly(EntryPoint = "glp_traffic", CallConvs = [typeof(CallConvCdecl)])]
    public static int Traffic(nint session, NativeBody* output, int capacity) =>
        Guarded(() => NativeApi.Traffic(Resolve(session), new Span<NativeBody>(output, Math.Max(capacity, 0))));

    [UnmanagedCallersOnly(EntryPoint = "glp_train", CallConvs = [typeof(CallConvCdecl)])]
    public static int Train(nint session, NativeBody* output, int capacity) =>
        Guarded(() => NativeApi.Train(Resolve(session), new Span<NativeBody>(output, Math.Max(capacity, 0))));

    [UnmanagedCallersOnly(EntryPoint = "glp_cows", CallConvs = [typeof(CallConvCdecl)])]
    public static int Cows(nint session, NativeBody* output, int capacity) =>
        Guarded(() => NativeApi.Cows(Resolve(session), new Span<NativeBody>(output, Math.Max(capacity, 0))));

    [UnmanagedCallersOnly(EntryPoint = "glp_props", CallConvs = [typeof(CallConvCdecl)])]
    public static int Props(nint session, NativeProp* output, int capacity) =>
        Guarded(() => NativeApi.Props(Resolve(session), new Span<NativeProp>(output, Math.Max(capacity, 0))));

    [UnmanagedCallersOnly(EntryPoint = "glp_best_lap", CallConvs = [typeof(CallConvCdecl)])]
    public static int BestLap(nint session, byte* buffer, int capacity) => Guarded(() =>
    {
        var bytes = NativeApi.BestLap(Resolve(session));
        if (buffer != null && capacity >= bytes.Length) bytes.CopyTo(new Span<byte>(buffer, capacity));
        return bytes.Length;
    });

    [UnmanagedCallersOnly(EntryPoint = "glp_load_best_lap", CallConvs = [typeof(CallConvCdecl)])]
    public static int LoadBestLap(nint session, byte* data, int length) =>
        Guarded(() => NativeApi.LoadBestLap(Resolve(session), new ReadOnlySpan<byte>(data, Math.Max(length, 0))));

    [UnmanagedCallersOnly(EntryPoint = "glp_engine_create", CallConvs = [typeof(CallConvCdecl)])]
    public static nint EngineCreate(byte* folder, int cylinders)
    {
        try
        {
            var path = Marshal.PtrToStringUTF8((nint)folder) ?? throw new ArgumentNullException(nameof(folder));
            return GCHandle.ToIntPtr(GCHandle.Alloc(NativeApi.CreateEngine(path, cylinders)));
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_engine_destroy", CallConvs = [typeof(CallConvCdecl)])]
    public static void EngineDestroy(nint engine)
    {
        if (engine != 0) GCHandle.FromIntPtr(engine).Free();
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_engine_sample_rate", CallConvs = [typeof(CallConvCdecl)])]
    public static int EngineSampleRate(nint engine) => Guarded(() => ResolveEngine(engine).SampleRate);

    [UnmanagedCallersOnly(EntryPoint = "glp_engine_render", CallConvs = [typeof(CallConvCdecl)])]
    public static int EngineRender(nint engine, float* output, int frames, float rpm, float load, int running, int interior, float volume)
    {
        // Called from a host's audio thread every few milliseconds: no closure (Guarded allocates one), nothing allocated.
        try
        {
            var voice = ResolveEngine(engine);
            voice.Interior = interior != 0;
            voice.Volume = volume;
            voice.Render(new Span<float>(output, Math.Max(frames, 0)), rpm, load, running != 0);
            return 0;
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static int LastError(byte* buffer, int capacity) => NativeApi.CopyText(_lastError ?? "", buffer, capacity);

    private static GranularEngine ResolveEngine(nint engine) =>
        engine != 0 && GCHandle.FromIntPtr(engine).Target is GranularEngine voice
            ? voice
            : throw new ArgumentException("not an engine handle", nameof(engine));

    private static NativeSession Resolve(nint session) =>
        session != 0 && GCHandle.FromIntPtr(session).Target is NativeSession native
            ? native
            : throw new ArgumentException("not a session handle", nameof(session));

    /// <summary>Runs an entry point's work: 0 when it returns nothing, its count or length otherwise, -1 on failure.</summary>
    private static int Guarded(Action action) => Guarded(() =>
    {
        action();
        return 0;
    });

    private static int Guarded(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            return -1;
        }
    }
}
