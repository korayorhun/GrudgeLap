using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GrudgeLap.Game;

/// <summary>
/// The updater's own log, user://update.log: every check, download attempt, verification and installer start with
/// its time, so that a failure at home can be read afterwards. It lives outside Godot's logs/ folder, which the engine
/// rotates at every start (after a few starts the log of a failed update was gone), and is capped: past
/// <c>limit</c> bytes the file becomes update.old.log and a new one starts, so the two stay near half a megabyte.
/// </summary>
/// <remarks>
/// Thread-safe: the download's worker and the main thread write to it alike. A log that cannot be written never stops
/// an update. No line keeps a sign-in (<see cref="Redact"/>): the log is the thing a player sends when an update fails.
/// </remarks>
internal sealed partial class UpdateLog(string filePath, long limit = 256 * 1024)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();

    public string FilePath { get; } = filePath;
    public string OldPath => Path.Combine(Path.GetDirectoryName(FilePath) ?? "", Path.GetFileNameWithoutExtension(FilePath) + ".old" + Path.GetExtension(FilePath));

    /// <summary>
    /// The line with the user name and password of every address in it masked: <c>http://alice:s3cret@proxy:3128/</c>
    /// becomes <c>http://***@proxy:3128/</c>. A proxy set up with its sign-in in its address (HTTPS_PROXY, or a PAC
    /// that names one) hands that address to .NET, and .NET's errors quote it whole ("The proxy tunnel request to
    /// proxy 'http://alice:s3cret@…' failed with status code '407'"), so the mask is applied to every line rather
    /// than to the few that were written to name a proxy.
    /// </summary>
    public static string Redact(string line) => line.Contains('@') ? SignIn().Replace(line, "${scheme}***@") : line;

    // The scheme, then everything up to the last '@' before the address's first '/' or a space: the user name and
    // password, even when the password itself holds an '@' that was never escaped.
    [GeneratedRegex(@"(?<scheme>\b[A-Za-z][A-Za-z0-9+.\-]*://)[^/\s]*@", RegexOptions.CultureInvariant)]
    private static partial Regex SignIn();

    public void Write(string line)
    {
        var stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + Redact(line) + "\n";
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
            var products = AntivirusProducts();
            return products.Count > 0 ? string.Join(", ", products.Select(p => $"{p.Name} ({StateText(p.State)})")) : "none registered";
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// The antivirus products Security Center reports switched on, by the name Windows Security shows ("Microsoft
    /// Defender Antivirus", in the language of Windows), joined with "or"; null when none is known. A reason that names
    /// the product tells the player where to look for the file it took.
    /// </summary>
    public static string? ActiveAntivirus()
    {
        try
        {
            var on = AntivirusProducts().Where(p => StateText(p.State) == "on").Select(p => p.Name).ToList();
            return on.Count > 0 ? string.Join(" or ", on) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<(string Name, int State)> AntivirusProducts()
    {
        var products = new List<(string Name, int State)>();
        if (!OperatingSystem.IsWindows()) return products;
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Security Center\Provider\Av");
        if (key is null) return products;
        foreach (var id in key.GetSubKeyNames())
        {
            using var product = key.OpenSubKey(id);
            if (product?.GetValue("DISPLAYNAME") is not string name) continue;
            products.Add((name, product.GetValue("STATE") is int s ? s : -1));
        }
        return products;
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

    /// <summary>The proxy Windows' settings (or HTTPS_PROXY) choose for <paramref name="target"/> (a PAC script may run: call it off the main thread).</summary>
    public static string Proxy(Uri target) => Proxy(target, HttpClient.DefaultProxy);

    /// <summary>
    /// The proxy <paramref name="proxy"/> chooses for <paramref name="target"/>: scheme, host and port only. The address
    /// .NET hands back keeps the user name and password it was given in (HTTPS_PROXY=http://user:password@host:port),
    /// so the line says only that there is a sign-in.
    /// </summary>
    internal static string Proxy(Uri target, IWebProxy proxy)
    {
        try
        {
            if (proxy.IsBypassed(target)) return "none (direct)";
            var through = proxy.GetProxy(target);
            if (through is null || through == target) return "none (direct)";
            var address = through.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
            return through.UserInfo.Length > 0 ? address + " (with a sign-in in its address, not logged)" : address;
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
