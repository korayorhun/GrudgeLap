using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// One GrudgeLap per computer. The first instance holds a machine-wide named mutex; a later launch asks it to
/// come to the front through a named event and quits before building anything.
/// </summary>
/// <remarks>
/// Both objects live in the Global namespace, so a launch from another Windows session counts too. Windows
/// releases the mutex when the process ends, also after a crash or the updater's quick exit; the next launch
/// then finds it abandoned and takes it over. Automated runs (<c>--invisible</c>) never take the lock, so
/// pictures and checks can run while somebody plays.
/// </remarks>
internal static class SingleInstance
{
    private const string MutexName = @"Global\GrudgeLap.SingleInstance";
    private const string ActivateName = @"Global\GrudgeLap.Activate";
    private const int AnyProcess = -1;   // ASFW_ANY

    private static System.Threading.Mutex? _mutex;   // qualified: Godot has a Mutex class of its own
    private static EventWaitHandle? _activate;

    /// <summary>True when no other instance runs; this one then owns the lock until <see cref="Release"/> or its end.</summary>
    public static bool TryAcquire()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            _mutex = new System.Threading.Mutex(false, MutexName);
            try
            {
                if (!_mutex.WaitOne(0)) return false;
            }
            catch (AbandonedMutexException)
            {
                // The previous owner ended without releasing it; the wait still handed the lock to us.
            }
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Another Windows user's game created the lock and its security keeps us out: it is running.
            return false;
        }
    }

    /// <summary>
    /// Asks the running instance to show itself. A process in the background may not take the foreground on
    /// its own; the launch the user just started may grant it that right first.
    /// </summary>
    public static void ActivateOther()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            AllowSetForegroundWindow(AnyProcess);
            if (!EventWaitHandle.TryOpenExisting(ActivateName, out var handle)) return;
            handle.Set();
            handle.Dispose();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"could not reach the running game: {ex.Message}");
        }
    }

    /// <summary>Polled by the running instance every frame; true once for each later launch.</summary>
    public static bool ActivationRequested() => _activate?.WaitOne(0) == true;

    /// <summary>Gives the lock back on a normal exit; must run on the thread that took it (the main thread).</summary>
    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread (the lock was never taken); nothing to give back.
        }
        _mutex?.Dispose();
        _mutex = null;
        _activate?.Dispose();
        _activate = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
