using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GrudgeLap.Game;

/// <summary>
/// The download folder, %TEMP%\GrudgeLap-Update. It holds at most one package: until 0.76 every applied installer
/// stayed there (485 MB each, three on the development laptop the day the cause was found, ten gigabytes a day at the
/// night's release rate) and was only cleared when a day old, at the next download.
/// </summary>
internal static partial class UpdateFolder
{
    public static string Default => Path.Combine(Path.GetTempPath(), "GrudgeLap-Update");

    /// <summary>
    /// Deletes what earlier updates left in <paramref name="folder"/>: packages and partial downloads other than
    /// <paramref name="keep"/> (and its .part), checksum files, the apply scripts, unpack folders, and with
    /// <paramref name="logsToo"/> the installers' logs. Returns the names it deleted; what is in use stays for next time.
    /// </summary>
    public static IReadOnlyList<string> Clean(string folder, string? keep, bool logsToo = false)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(folder)) return deleted;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            if (keep is not null && (name.Equals(keep, StringComparison.OrdinalIgnoreCase) || name.Equals(keep + ".part", StringComparison.OrdinalIgnoreCase))) continue;
            if (!IsOurs(name, logsToo)) continue;
            try
            {
                File.Delete(path);
                deleted.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open (an installer that is running, an antivirus scan): next time.
            }
        }
        foreach (var path in Directory.EnumerateDirectories(folder, "unpack-*"))
        {
            try
            {
                Directory.Delete(path, recursive: true);
                deleted.Add(Path.GetFileName(path) + "\\");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The same.
            }
        }
        return deleted;
    }

    /// <summary>
    /// Deletes the packages (and partial downloads) of versions this copy already runs or has passed, and the 0.76
    /// updater's script: what an update applied by the old updater, or by hand, left behind. Newer packages stay.
    /// </summary>
    public static IReadOnlyList<string> CleanApplied(string folder, Version current)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(folder)) return deleted;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            var match = PackageName().Match(name);
            var stale = match.Success ? ReleaseVersion.TryParse(match.Groups["version"].Value, out var version) && version <= current
                : name.Equals("apply-setup.ps1", StringComparison.OrdinalIgnoreCase);
            if (!stale) continue;
            try
            {
                File.Delete(path);
                deleted.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use: next time.
            }
        }
        return deleted;
    }

    [GeneratedRegex(@"^GrudgeLap-(Setup|Portable)-(?<version>\d+(\.\d+){1,3})\.(exe|zip)(\.part|\.sha256)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackageName();

    private static bool IsOurs(string name, bool logsToo) =>
        Matches(name, "GrudgeLap-Setup-", ".exe") || Matches(name, "GrudgeLap-Portable-", ".zip")
        || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
        || name.Equals("apply.ps1", StringComparison.OrdinalIgnoreCase) || name.Equals("apply-setup.ps1", StringComparison.OrdinalIgnoreCase)
        || logsToo && (Matches(name, "setup-", ".log") || name.Equals("apply.log", StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string name, string prefix, string suffix) =>
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Why an update did not take, read from what its installer or unpack script left behind.</summary>
internal static partial class InstallerOutcome
{
    /// <summary>The last <paramref name="count"/> lines of a log, empty when it cannot be read.</summary>
    public static IReadOnlyList<string> Tail(string? path, int count)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // An installer's log runs to a few hundred kilobytes; its end is enough.
            if (stream.Length > 256 * 1024) stream.Seek(-256 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            return lines.Count <= count ? lines : lines[^count..];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The reason the update to the next version did not take, in a few words. <paramref name="kind"/> is "setup"
    /// (an Inno Setup log written through /LOG) or "portable" (the unpack script's apply.log).
    /// </summary>
    public static string Reason(string kind, string? logPath)
    {
        var lines = Tail(logPath, 400);
        if (kind == "portable")
        {
            var run = lines.Skip(lines.ToList().FindLastIndex(l => l.Contains(" start ", StringComparison.Ordinal)) is var at and >= 0 ? at : 0).ToList();
            if (run.Count == 0) return "the unpack script left no log: it did not run (an antivirus may have stopped it)";
            if (run.LastOrDefault(l => l.Contains("FAILED:", StringComparison.Ordinal)) is { } failed) return Shorten(failed[(failed.IndexOf("FAILED:", StringComparison.Ordinal) + 7)..].Trim());
            if (run.Any(l => l.EndsWith(" done", StringComparison.Ordinal))) return "the files were unpacked, but this copy is still the old version";
            return "the unpack script stopped before the end (last step: " + Shorten(Message(run[^1]), 70) + ")";
        }
        if (lines.Count == 0) return "the installer left no log: it did not run (an antivirus may have stopped it)";
        if (lines.Any(l => l.Contains("Installation process succeeded", StringComparison.Ordinal))) return "the installer finished, but this copy is still the old version";
        // The last message box first: Inno logs the box's own words for what went wrong on the untimed lines after it
        // ("An error occurred while trying to replace the existing file: DeleteFile failed; code 5. Access is denied.").
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!Message(lines[i]).Contains("message box", StringComparison.OrdinalIgnoreCase)) continue;
            var words = lines.Skip(i + 1).TakeWhile(l => !Stamp().IsMatch(l)).Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Contains(":\\", StringComparison.Ordinal)).ToList();
            if (words.Count > 0) return Shorten(string.Join(" ", words));
        }
        // Otherwise the last failure or exception ("Exception message:" puts its words on the next line); "Got EAbort
        // exception." and "Rolling back changes." only follow from an earlier one.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var message = Message(lines[i]);
            if (!Trouble().IsMatch(message) || message.StartsWith("Got EAbort", StringComparison.Ordinal)) continue;
            if (message.EndsWith(':') && i + 1 < lines.Count) message += " " + Message(lines[i + 1]);
            else if (i > 0 && Message(lines[i - 1]) is { } before && before.EndsWith(':') && Trouble().IsMatch(before)) message = before + " " + message;
            return Shorten(message);
        }
        return "the installer stopped before the end (last step: " + Shorten(Message(lines[^1]), 70) + ")";
    }

    /// <summary>What an installer that ended at once said with its exit code (Inno Setup's codes; anything else for the script).</summary>
    public static string ExitReason(string kind, int code) => kind != "setup"
        ? $"the unpack script ended at once (exit code {code})"
        : code switch
        {
            1 => "the installer could not start (exit code 1)",
            2 or 5 => "the installer was cancelled",
            3 => "the installer failed while preparing (exit code 3)",
            4 => "the installer failed while installing (exit code 4)",
            6 => "the installer was stopped (exit code 6)",
            7 => "the installer could not prepare (exit code 7)",
            8 => "the installer needs a restart of Windows first",
            _ => $"the installer ended at once (exit code {code})",
        };

    /// <summary>A log line without its time stamp ("2026-11-09 01:02:03.456   " for Inno, "01:02:03 " or a date for the script).</summary>
    private static string Message(string line) => Stamp().Replace(line, "").Trim();

    private static string Shorten(string text, int max = 110) => text.Length <= max ? text : text[..(max - 3)] + "...";

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} )?\d{2}:\d{2}:\d{2}(\.\d+)?\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Stamp();

    [GeneratedRegex(@"exception|failed|error|not enough|access is denied|cannot|could not", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Trouble();
}

/// <summary>The command lines that hand an update over, and the portable copy's unpack script.</summary>
internal static class UpdateCommands
{
    /// <summary>
    /// Inno Setup's switches for an update: /SILENT shows only the progress, /SP- skips the "this will install" prompt,
    /// /NOCANCEL keeps a click from stopping it halfway, /NORESTART never restarts Windows, /LOG keeps a log for the
    /// next start to read. /UPDATE tells GrudgeLap.iss that the game started it: wait for the game to exit, and with 1
    /// start it again afterwards, with 2 not (an update applied as the game quits, or a check run without a player).
    /// </summary>
    public static string InstallerArguments(string logPath, bool relaunch) =>
        $"/SILENT /NORESTART /SP- /NOCANCEL /UPDATE={(relaunch ? 1 : 2).ToString(CultureInfo.InvariantCulture)} /LOG=\"{logPath}\"";

    /// <summary>Windows PowerShell by its full path: a bare powershell.exe was looked up on the PATH, which an unlucky folder can shadow.</summary>
    public static string PowerShell => Path.Combine(System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static IReadOnlyList<string> UnpackArguments(string script, int processId, string zip, string dir, string exe, string version, bool relaunch)
    {
        var args = new List<string>
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
            "-ProcessId", processId.ToString(CultureInfo.InvariantCulture), "-Zip", zip, "-Dir", dir, "-Exe", exe, "-Version", version,
        };
        if (!relaunch) args.Add("-NoRestart");
        return args;
    }

    /// <summary>Arguments joined into one Windows command line, quoted the way the C runtime (and PowerShell's -File) splits it again.</summary>
    public static string CommandLine(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    /// <summary>An argument quoted when it holds a space, a tab or a quote; backslashes before a quote are doubled ("C:\Games\" stays one argument).</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>
    /// Waits for the game to exit and its executable to be released (for about two seconds after it quits the runtime
    /// unloads and an antivirus looks), unpacks the portable zip beside itself, copies it over the game's folder and
    /// starts the game again. It logs to apply.log beside the zip ("start", then "done" or "FAILED: why"), which the
    /// game's next start reads, and stops at the first failure with the old files in place.
    /// </summary>
    public const string UnpackScript = """
        param([int]$ProcessId, [string]$Zip, [string]$Dir, [string]$Exe, [string]$Version, [switch]$NoRestart)
        $ErrorActionPreference = 'Stop'
        $log = Join-Path (Split-Path -Parent $Zip) 'apply.log'
        function Note($text) { Add-Content -LiteralPath $log -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture) + ' ' + $text) -Encoding UTF8 }
        Note "start $Version from $Zip over $Dir (waiting for process $ProcessId)"
        try { Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch {}
        for ($attempt = 1; $attempt -le 30 -and (Test-Path -LiteralPath $Exe); $attempt++) {
            try {
                $handle = [System.IO.File]::Open($Exe, 'Open', 'ReadWrite', 'None')
                $handle.Close()
                break
            } catch { Start-Sleep -Seconds 1 }
        }
        $tmp = Join-Path (Split-Path -Parent $Zip) ("unpack-" + [guid]::NewGuid().ToString("N"))
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $tmp)
            $entries = @(Get-ChildItem -LiteralPath $tmp)
            $src = if ($entries.Count -eq 1 -and $entries[0].PSIsContainer) { $entries[0].FullName } else { $tmp }
            Note "unpacked $($entries.Count) entries"
            for ($attempt = 1; $attempt -le 5; $attempt++) {
                try {
                    Copy-Item -Path (Join-Path $src '*') -Destination $Dir -Recurse -Force
                    Note "copied into $Dir (attempt $attempt)"
                    break
                } catch {
                    if ($attempt -eq 5) { throw }
                    Note "copy failed, retrying: $($_.Exception.Message)"
                    Start-Sleep -Seconds 2
                }
            }
            Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $Zip -Force -ErrorAction SilentlyContinue
            Note "done"
        } catch {
            Note "FAILED: $($_.Exception.Message)"
            Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (-not $NoRestart) { Start-Process -FilePath $Exe -WorkingDirectory $Dir }
        """;
}
