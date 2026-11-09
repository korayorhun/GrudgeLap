using System.Globalization;
using System.Text;

namespace GrudgeLap.Game;

/// <summary>
/// The updater's own log, user://update.log: every check, download attempt, verification and installer start with
/// its time, so that a failure at home can be read afterwards. It lives outside Godot's logs/ folder, which the engine
/// rotates at every start (after a few starts the log of a failed update was gone), and is capped: past
/// <c>limit</c> bytes the file becomes update.old.log and a new one starts, so the two stay near half a megabyte.
/// </summary>
/// <remarks>Thread-safe: the download's worker and the main thread write to it alike. A log that cannot be written never stops an update.</remarks>
internal sealed class UpdateLog(string filePath, long limit = 256 * 1024)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();

    public string FilePath { get; } = filePath;
    public string OldPath => Path.Combine(Path.GetDirectoryName(FilePath) ?? "", Path.GetFileNameWithoutExtension(FilePath) + ".old" + Path.GetExtension(FilePath));

    public void Write(string line)
    {
        var stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + "\n";
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > limit) File.Move(FilePath, OldPath, overwrite: true);
                File.AppendAllText(FilePath, stamped, Utf8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The log is a witness, not a step: the update goes on without it.
            }
        }
    }
}

/// <summary>Facts about the computer that explain failures at home, for the session's first lines in the log.</summary>
internal static class UpdateDiagnostics
{
    /// <summary>
    /// The antivirus products Windows Security Center knows, with their state: "Cyber Protect (on), Microsoft
    /// Defender Antivirus (passive)". Read from the registry rather than WMI's SecurityCenter2 (the same records, without
    /// starting WMI from the game); an antivirus that quarantines the new installer is the likeliest silent failure.
    /// </summary>
    public static string Antivirus()
    {
        if (!OperatingSystem.IsWindows()) return "not Windows";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Security Center\Provider\Av");
            if (key is null) return "none registered";
            var products = new List<string>();
            foreach (var id in key.GetSubKeyNames())
            {
                using var product = key.OpenSubKey(id);
                if (product?.GetValue("DISPLAYNAME") is not string name) continue;
                var state = product.GetValue("STATE") is int s ? s : -1;
                products.Add($"{name} ({StateText(state)})");
            }
            return products.Count > 0 ? string.Join(", ", products) : "none registered";
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.GetType().Name})";
        }
    }

    /// <summary>Security Center's product state: its second byte says whether the scanner runs (0x10 on, 0x01 passive, 0x00 off, 0x11 expired).</summary>
    internal static string StateText(int state) => state < 0 ? "state unknown" : ((state >> 8) & 0xFF) switch
    {
        0x10 => "on",
        0x01 => "passive",
        0x00 => "off",
        0x11 => "expired",
        var other => "state 0x" + other.ToString("X2", CultureInfo.InvariantCulture),
    };

    /// <summary>The proxy Windows' settings choose for <paramref name="target"/> (a PAC script may run: call it off the main thread).</summary>
    public static string Proxy(Uri target)
    {
        try
        {
            var proxy = HttpClient.DefaultProxy;
            if (proxy.IsBypassed(target)) return "none (direct)";
            var through = proxy.GetProxy(target);
            return through is null || through == target ? "none (direct)" : through.GetLeftPart(UriPartial.Authority);
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.GetType().Name})";
        }
    }

    public static string FreeSpace(string folder)
    {
        var free = UpdateClient.FreeSpaceOf(folder);
        var drive = Path.GetPathRoot(Path.GetFullPath(folder))?.TrimEnd('\\') ?? folder;
        return free == long.MaxValue ? $"{drive} unknown" : $"{drive} {(free / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture)} GB free";
    }

    /// <summary>Whether this user can write in <paramref name="folder"/>: a portable copy under Program Files cannot be unpacked over itself.</summary>
    public static bool CanWrite(string folder)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(folder, $".grudgelap-write-{Guid.NewGuid():N}"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
