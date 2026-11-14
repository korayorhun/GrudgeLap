using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;
using GrudgeLap.Game;

namespace GrudgeLap.Update.Native;

/// <summary>
/// glp_update_state of include/grudgelap_update.h, field for field; change both together and bump
/// <see cref="Exports.AbiVersion"/>. The texts are UTF-8, cut at a whole character and always terminated.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeUpdateState
{
    public const int LatestSize = 32;
    public const int StatusSize = 256;
    public const int PageSize = 256;

    public int Phase;
    public int InstalledCopy;
    public int CanRetry;
    public int HasPage;
    public long BytesDone;
    public long BytesTotal;
    public long PackageSize;
    public fixed byte Latest[LatestSize];
    public fixed byte Status[StatusSize];
    public fixed byte Page[PageSize];
}

/// <summary>
/// The glp_update_* entry points of include/grudgelap_update.h: one <see cref="UpdateSession"/> for the process,
/// driven from one host thread. Each entry point catches everything: an exception that crosses into native code ends
/// the host process, so a failure becomes -1 and its words wait in glp_update_last_error.
/// </summary>
internal static unsafe class Exports
{
    public const int AbiVersion = 1;
    /// <summary>The request was understood but is not possible now (a download already runs, nothing is ready); glp_update_last_error says why.</summary>
    private const int Refused = 1;
    private const int Failed = -1;

    private static UpdateSession? _session;
    private static string _lastError = "";

    [UnmanagedCallersOnly(EntryPoint = "glp_update_abi", CallConvs = [typeof(CallConvCdecl)])]
    public static int Abi() => AbiVersion;

    [UnmanagedCallersOnly(EntryPoint = "glp_update_init", CallConvs = [typeof(CallConvCdecl)])]
    public static int Init(byte* version, byte* launcherExe, byte* rootDir, byte* dataDir)
    {
        try
        {
            var host = new UpdateHost(Text(version, "version"), Text(launcherExe, "launcher_exe"), Text(rootDir, "root_dir"), Text(dataDir, "data_dir"));
            // A second call (the editor starts the game again in one process) starts over: the session before it ends first.
            _session?.Shutdown();
            _session = null;
            _session = new UpdateSession(host);
            return 0;
        }
        catch (Exception error)
        {
            return Fail(error);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_update_check", CallConvs = [typeof(CallConvCdecl)])]
    public static int Check() => Request(static session => session.Check());

    [UnmanagedCallersOnly(EntryPoint = "glp_update_download", CallConvs = [typeof(CallConvCdecl)])]
    public static int Download() => Request(static session => session.DownloadAndInstall());

    [UnmanagedCallersOnly(EntryPoint = "glp_update_apply", CallConvs = [typeof(CallConvCdecl)])]
    public static int Apply() => Request(static session => session.Apply());

    [UnmanagedCallersOnly(EntryPoint = "glp_update_cancel", CallConvs = [typeof(CallConvCdecl)])]
    public static void Cancel()
    {
        try
        {
            _session?.Cancel();
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_update_shutdown", CallConvs = [typeof(CallConvCdecl)])]
    public static void Shutdown()
    {
        try
        {
            _session?.Shutdown();
        }
        catch (Exception error)
        {
            Fail(error);
        }
        _session = null;
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_update_poll", CallConvs = [typeof(CallConvCdecl)])]
    public static int Poll(NativeUpdateState* state)
    {
        // Called every frame: no closure, and nothing allocated while the session's lines stay the same.
        try
        {
            if (_session is not { } session)
            {
                if (state != null) *state = default;
                _lastError = "glp_update_init has not been called";
                return Failed;
            }
            var snapshot = session.Poll();
            if (state != null) Write(snapshot, state);
            return 0;
        }
        catch (Exception error)
        {
            return Fail(error);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "glp_update_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static int LastError(byte* buffer, int size)
    {
        try
        {
            var needed = System.Text.Encoding.UTF8.GetByteCount(_lastError);
            if (buffer != null && size > 0) CopyText(_lastError, new Span<byte>(buffer, size));
            return needed;
        }
        catch (Exception)
        {
            return Failed;
        }
    }

    /// <summary>A snapshot as the C struct. A page that does not fit whole is left out: half an address opens the wrong page.</summary>
    internal static void Write(in UpdateSnapshot snapshot, NativeUpdateState* state)
    {
        state->Phase = (int)snapshot.Phase;
        state->InstalledCopy = snapshot.InstalledCopy ? 1 : 0;
        state->CanRetry = (int)snapshot.Retry;
        state->BytesDone = snapshot.BytesDone;
        state->BytesTotal = snapshot.BytesTotal;
        state->PackageSize = snapshot.PackageSize;
        CopyText(snapshot.Latest, new Span<byte>(state->Latest, NativeUpdateState.LatestSize));
        CopyText(snapshot.Status, new Span<byte>(state->Status, NativeUpdateState.StatusSize));
        var page = new Span<byte>(state->Page, NativeUpdateState.PageSize);
        if (!CopyText(snapshot.Page, page)) page[0] = 0;
        state->HasPage = page[0] != 0 ? 1 : 0;
    }

    /// <summary>
    /// <paramref name="text"/> as UTF-8 into <paramref name="buffer"/>, terminated, cut after the last whole character
    /// that fits. True when all of it fitted.
    /// </summary>
    internal static bool CopyText(string text, Span<byte> buffer)
    {
        var status = Utf8.FromUtf16(text, buffer[..^1], out _, out var written);
        buffer[written] = 0;
        return status == OperationStatus.Done;
    }

    /// <summary>Runs a request on the session: 0 when it was taken, <see cref="Refused"/> with the session's reason when not.</summary>
    private static int Request(Func<UpdateSession, bool> request)
    {
        try
        {
            if (_session is not { } session)
            {
                _lastError = "glp_update_init has not been called";
                return Failed;
            }
            if (request(session)) return 0;
            _lastError = session.Refusal;
            return Refused;
        }
        catch (Exception error)
        {
            return Fail(error);
        }
    }

    private static string Text(byte* utf8, string name) =>
        Marshal.PtrToStringUTF8((nint)utf8) ?? throw new ArgumentNullException(name);

    /// <summary>
    /// The failure for glp_update_last_error, without any sign-in an exception's words may quote, and once for
    /// update.log: a failure that repeats every frame (glp_update_poll) must not push the log's history out.
    /// </summary>
    private static int Fail(Exception error)
    {
        var text = UpdateLog.Redact(error.ToString());
        if (text != _lastError)
        {
            try
            {
                _session?.Note("unexpected failure in the updater's interface: " + text);
            }
            catch (Exception)
            {
                // The log is a witness, not a step.
            }
        }
        _lastError = text;
        return Failed;
    }
}
