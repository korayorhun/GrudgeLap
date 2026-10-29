using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// Keeps the game current from GitHub Releases: at every start it asks for the latest release and, when that is
/// newer, downloads it at once, checks the published SHA-256 and applies it as soon as the player is on the
/// title screen (or at the end of a credit), so that a copy that is opened always runs the newest version. An
/// installed copy (put there by the installer, which records its location in the registry) runs the new
/// installer silently and is restarted by it; a portable copy unpacks the new zip over itself through a small
/// PowerShell script once the game has exited. F10 applies a ready update while driving, or asks again.
/// <c>auto=false</c> under <c>[update]</c> in settings.cfg goes back to offering the update instead of taking it.
/// </summary>
internal sealed partial class Updater : Node
{
    public const string RepoOwner = "korayorhun";
    public const string Repo = "GrudgeLap";
    /// <summary>Inno Setup AppId of the installer; its uninstall key tells an installed copy from a portable one.</summary>
    public const string InstallerAppId = "{871E561A-E89E-41ED-870A-A635D3E789C2}";
    private const string ApiLatest = $"https://api.github.com/repos/{RepoOwner}/{Repo}/releases/latest";
    private const string UserAgent = "GrudgeLap-Updater";
    private const string SettingsFile = "user://settings.cfg";
    /// <summary>Starts within this many seconds of the last check share it: a crash loop must not hammer the API (60 calls an hour unauthenticated).</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);

    private HttpRequest? _request;
    private string _downloadPath = "";
    private string _shaPath = "";
    private UpdateStage _stage = UpdateStage.Idle;
    private bool _installedCopy;

    public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? SetupUrl, string? SetupShaUrl, string? ZipUrl, string? ZipShaUrl, string Notes);

    private enum UpdateStage { Idle, Checking, DownloadingPackage, DownloadingHash, Ready, Applying, Failed }

    /// <summary>A newer release, once the check has found one.</summary>
    public UpdateInfo? Available { get; private set; }
    /// <summary>One line for the HUD: what the updater is doing or offering; empty when there is nothing to say.</summary>
    public string Status { get; private set; } = "";
    public bool Busy => _stage is UpdateStage.Checking or UpdateStage.DownloadingPackage or UpdateStage.DownloadingHash or UpdateStage.Applying;
    /// <summary>Downloaded and verified: <see cref="ApplyNow"/> restarts the game on the new version.</summary>
    public bool ReadyToApply => _stage == UpdateStage.Ready;
    /// <summary>Whether a newer release is taken without being asked (the settings' update/auto, true by default).</summary>
    public bool AutoInstall { get; private set; } = true;

    public static Version CurrentVersion
    {
        get
        {
            var forced = System.Environment.GetEnvironmentVariable("GRUDGELAP_FAKE_VERSION");
            if (!string.IsNullOrEmpty(forced) && Version.TryParse(forced, out var fake)) return fake;
            var text = ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
            return Version.TryParse(text, out var v) ? v : new Version(0, 0, 0);
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    /// <summary>True when this copy was put in place by the installer (Inno writes InstallLocation for the AppId).</summary>
    public static bool IsInstalledCopy
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}_is1");
                var location = key?.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(location)) return false;
                var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
                return string.Equals(location.TrimEnd('\\'), exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Starts a check unless one ran moments ago (or <paramref name="manual"/> is set); a newer release is then downloaded when <see cref="AutoInstall"/> allows.</summary>
    public void CheckIfDue(bool manual)
    {
        if (Busy || ReadyToApply) return;
        var settings = new ConfigFile();
        settings.Load(SettingsFile);
        AutoInstall = settings.GetValue("update", "auto", true).AsBool();
        if (!manual)
        {
            if (!settings.GetValue("update", "enabled", true).AsBool()) return;
            var last = settings.GetValue("update", "last_check", 0L).AsInt64();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last < CheckInterval.TotalSeconds) return;
        }
        settings.SetValue("update", "last_check", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        settings.Save(SettingsFile);

        _stage = UpdateStage.Checking;
        Status = manual ? "checking for updates..." : "";
        var request = NewRequest();
        request.RequestCompleted += (result, code, _, body) =>
        {
            request.QueueFree();
            _request = null;
            if (result != (long)HttpRequest.Result.Success || code != 200)
            {
                _stage = UpdateStage.Idle;
                Status = manual ? "update check failed" : "";
                GD.Print($"update check failed: result {result}, HTTP {code}");
                return;
            }
            try
            {
                var info = Parse(body);
                _stage = UpdateStage.Idle;
                if (info.Version > CurrentVersion)
                {
                    Available = info;
                    Status = $"v{info.Version.ToString(3)} available - F10 to update";
                    GD.Print($"update available: {info.Tag}");
                    if (AutoInstall) DownloadAndInstall();
                }
                else
                {
                    Available = null;
                    Status = manual ? $"up to date (v{CurrentVersionText})" : "";
                }
            }
            catch (Exception ex)
            {
                _stage = UpdateStage.Idle;
                Status = manual ? "update check failed" : "";
                GD.Print($"update check failed: {ex.Message}");
            }
        };
        var error = request.Request(ApiLatest, [$"User-Agent: {UserAgent}", "Accept: application/vnd.github+json"]);
        if (error != Error.Ok)
        {
            _stage = UpdateStage.Idle;
            Status = manual ? "update check failed" : "";
        }
    }

    /// <summary>
    /// Downloads the right package for this copy and verifies it; <see cref="ApplyNow"/> then hands over to the
    /// installer or the unpack script, at once when the player is not driving (<see cref="ApplyWhenIdle"/>).
    /// </summary>
    public void DownloadAndInstall()
    {
        if (Busy || ReadyToApply || Available is null) return;
        var installed = IsInstalledCopy;
        var url = installed ? Available.SetupUrl : Available.ZipUrl;
        var shaUrl = installed ? Available.SetupShaUrl : Available.ZipShaUrl;
        if (url is null || shaUrl is null)
        {
            // The release has no package for this kind of copy: at least open the page.
            OS.ShellOpen(Available.PageUrl);
            return;
        }
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GrudgeLap-Update");
        System.IO.Directory.CreateDirectory(dir);
        // Packages of updates that never got applied (the game closed first) stayed in the folder, 80 MB each.
        foreach (var stale in System.IO.Directory.EnumerateFiles(dir))
        {
            try
            {
                if (System.IO.File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1)) System.IO.File.Delete(stale);
            }
            catch (Exception)
            {
                // In use or not ours to delete: left for next time.
            }
        }
        _downloadPath = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(new Uri(url).AbsolutePath));
        _shaPath = _downloadPath + ".sha256";
        _stage = UpdateStage.DownloadingPackage;
        Status = "downloading update 0%";

        var request = NewRequest();
        request.DownloadFile = _downloadPath;
        request.RequestCompleted += (result, code, _, _) =>
        {
            request.QueueFree();
            _request = null;
            if (result != (long)HttpRequest.Result.Success || code != 200)
            {
                Fail($"download failed (result {result}, HTTP {code})");
                return;
            }
            DownloadHash(shaUrl, installed);
        };
        if (request.Request(url, [$"User-Agent: {UserAgent}"]) != Error.Ok) Fail("download could not start");
    }

    private void DownloadHash(string shaUrl, bool installed)
    {
        _stage = UpdateStage.DownloadingHash;
        Status = "verifying update...";
        var request = NewRequest();
        request.RequestCompleted += (result, code, _, body) =>
        {
            request.QueueFree();
            _request = null;
            if (result != (long)HttpRequest.Result.Success || code != 200)
            {
                Fail("could not download the checksum");
                return;
            }
            try
            {
                Verify(_downloadPath, System.Text.Encoding.ASCII.GetString(body));
                _installedCopy = installed;
                _stage = UpdateStage.Ready;
                Status = $"v{Available!.Version.ToString(3)} ready - restarts at the title screen (F10 now)";
                GD.Print($"update verified: {_downloadPath}");
            }
            catch (Exception ex)
            {
                try { System.IO.File.Delete(_downloadPath); } catch (Exception) { }
                Fail(ex.Message);
            }
        };
        if (request.Request(shaUrl, [$"User-Agent: {UserAgent}"]) != Error.Ok) Fail("checksum download could not start");
    }

    /// <summary>Applies a verified update while the player is between credits, so a drive is never cut short. Called every frame by the game.</summary>
    public void ApplyWhenIdle(bool idle)
    {
        if (idle && ReadyToApply && AutoInstall) ApplyNow();
    }

    /// <summary>Hands a verified update to the installer or the unpack script and quits; the game comes back on the new version.</summary>
    public void ApplyNow()
    {
        if (!ReadyToApply) return;
        Apply(_installedCopy);
    }

    private static void Verify(string file, string shaText)
    {
        var expected = shaText.Trim();
        if (expected.Length < 64) throw new InvalidDataException("invalid .sha256 file");
        expected = expected[..64];
        using var sha = SHA256.Create();
        using var stream = System.IO.File.OpenRead(file);
        var actual = System.Convert.ToHexString(sha.ComputeHash(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 mismatch");
    }

    private void Apply(bool installed)
    {
        _stage = UpdateStage.Applying;
        Status = "installing update...";
        GD.Print($"update verified, applying {_downloadPath} ({(installed ? "installed copy" : "portable copy")})");
        // Either way a script waits for this process to be gone: for about two seconds after the game has quit its
        // executable is still held (the runtime unloading, the antivirus looking), and an installer or a copy that
        // starts at once fails on it, leaving the old version to come back and fetch the update again at every start.
        var exe = OS.GetExecutablePath();
        var dir = System.IO.Path.GetDirectoryName(exe) ?? ".";
        var script = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_downloadPath)!, installed ? "apply-setup.ps1" : "apply.ps1");
        System.IO.File.WriteAllText(script, installed ? SetupScript : UnpackScript);
        var pid = System.Environment.ProcessId;
        OS.CreateProcess("powershell.exe",
        [
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
            "-ProcessId", pid.ToString(), "-Zip", _downloadPath, "-Dir", dir, "-Exe", exe,
        ]);
        GetTree().Quit();
    }

    /// <summary>
    /// Waits for the game to exit and its executable to be released, then runs the new installer: silent, no
    /// restart prompt; /UPDATE=1 makes the installer relaunch the game afterwards. Logs beside the download.
    /// </summary>
    private const string SetupScript = """
        param([int]$ProcessId, [string]$Zip, [string]$Dir, [string]$Exe)
        $log = Join-Path (Split-Path -Parent $Zip) 'apply.log'
        function Note($text) { Add-Content -LiteralPath $log -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $text) }
        Note "setup $Zip for $Dir (waiting for process $ProcessId)"
        try { Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch {}
        for ($attempt = 1; $attempt -le 30; $attempt++) {
            try {
                $handle = [System.IO.File]::Open($Exe, 'Open', 'ReadWrite', 'None')
                $handle.Close()
                Note "executable released after $attempt tries"
                break
            } catch { Start-Sleep -Seconds 1 }
        }
        Note "running the installer"
        Start-Process -FilePath $Zip -ArgumentList '/SILENT', '/NORESTART', '/SP-', '/UPDATE=1'
        """;

    /// <summary>
    /// Waits for the game to exit, unpacks the portable zip over the install folder and starts the game again. It
    /// keeps a log beside the zip (apply.log) and stops at the first failure with the old files in place: the game
    /// is only restarted on what was unpacked whole, and a locked file (an antivirus scan, a slow exit) gets retried.
    /// </summary>
    private const string UnpackScript = """
        param([int]$ProcessId, [string]$Zip, [string]$Dir, [string]$Exe)
        $ErrorActionPreference = 'Stop'
        $log = Join-Path (Split-Path -Parent $Zip) 'apply.log'
        function Note($text) { Add-Content -LiteralPath $log -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $text) }
        Note "apply $Zip over $Dir (waiting for process $ProcessId)"
        try { Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch {}
        Start-Sleep -Milliseconds 800
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
        Start-Process -FilePath $Exe -WorkingDirectory $Dir
        """;

    private void Fail(string message)
    {
        _stage = UpdateStage.Failed;
        Status = "update failed: " + message;
        GD.Print("update failed: " + message);
    }

    private HttpRequest NewRequest()
    {
        _request = new HttpRequest { UseThreads = true, Timeout = 60 };
        AddChild(_request);
        return _request;
    }

    public override void _Process(double delta)
    {
        if (_stage != UpdateStage.DownloadingPackage || _request is null) return;
        var total = _request.GetBodySize();
        var done = _request.GetDownloadedBytes();
        Status = total > 0 ? $"downloading update {done * 100 / total}%" : $"downloading update {done / 1048576} MB";
    }

    private static UpdateInfo Parse(byte[] body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) throw new FormatException("tag: " + tag);
        var page = root.GetProperty("html_url").GetString() ?? $"https://github.com/{RepoOwner}/{Repo}/releases/latest";
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string? setup = null, setupSha = null, zip = null, zipSha = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (name.StartsWith("GrudgeLap-Setup-", StringComparison.OrdinalIgnoreCase))
                {
                    if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) setupSha = url;
                    else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) setup = url;
                }
                else if (name.StartsWith("GrudgeLap-Portable-", StringComparison.OrdinalIgnoreCase))
                {
                    if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase)) zipSha = url;
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = url;
                }
            }
        }
        return new UpdateInfo(version, tag, page, setup, setupSha, zip, zipSha, notes);
    }
}
