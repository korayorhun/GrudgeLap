using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// Keeps the game current from GitHub Releases: at every start it looks for a newer release and, when there is one,
/// downloads it in the background, checks its SHA-256 and applies it as soon as the player is on the title screen (or
/// a credit is over), so that a copy that is opened always runs the newest version. An installed copy (put there by the
/// installer, which records its location in the registry) starts the new installer, which waits for the game to exit,
/// installs silently and starts it again; a portable copy unpacks the new zip over itself through a small PowerShell
/// script once the game has exited. F10 applies a ready update while driving, or asks again. <c>auto=false</c> under
/// <c>[update]</c> in settings.cfg goes back to offering the update instead of taking it; <c>enabled=false</c> stops
/// the check at start.
/// </summary>
/// <remarks>
/// <para>
/// The network work runs on the thread pool (<see cref="UpdateFeed"/>, <see cref="UpdateClient"/>): this node starts
/// it, polls it every frame and acts on its result on the main thread, so a dead connection can no longer freeze the
/// game, and quitting cancels it at once. Every step goes to user://update.log, and every failure, the automatic ones
/// included, shows its reason on the HUD and the title screen: until 0.76 a failed check at start said nothing.
/// </para>
/// <para>
/// settings.cfg [update]: <c>enabled</c>, <c>auto</c>, <c>last_check</c> (Unix seconds); <c>applying</c> and its
/// <c>applying_*</c> details while an update is being handed over (the next start compares them with its own version
/// to tell whether the update took); <c>failed_version</c>, <c>failed_count</c> and <c>failed_reason</c> for an update
/// that did not take (tried twice, then left to the player).
/// </para>
/// <para>
/// Hooks for checks without a player: GRUDGELAP_UPDATE_FEED (a stand-in for GitHub at that URL, laid out as
/// <see cref="UpdateEndpoints.ForFeed"/>), GRUDGELAP_UPDATE_DRYRUN (find the release, download and change nothing) and
/// Bootstrap's <c>--update-only</c> (<see cref="RunAlone"/>).
/// </para>
/// </remarks>
internal sealed partial class Updater : Node
{
    public const string RepoOwner = "korayorhun";
    public const string Repo = "GrudgeLap";
    /// <summary>Inno Setup AppId of the installer; its uninstall key tells an installed copy from a portable one.</summary>
    public const string InstallerAppId = "{871E561A-E89E-41ED-870A-A635D3E789C2}";
    private const string SettingsFile = "user://settings.cfg";
    private const string LogFile = "user://update.log";
    /// <summary>Starts within this long of the last check share it: a crash loop must not hammer GitHub.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);
    /// <summary>
    /// Seconds an installer must stay alive after its start before the game quits for it. One stopped at once (an
    /// antivirus that blocks or quarantines the new executable, a broken download) is caught while the game still runs.
    /// </summary>
    private const double InstallerGrace = 1.5;
    /// <summary>Failed installs of one version before the game stops applying it by itself and points to the release page.</summary>
    private const int MaxApplyFailures = 2;
    /// <summary>The result of the last update stays on screen this long, even when the check at start finds nothing new.</summary>
    private const double ResultHold = 30;

    private enum Stage { Idle, Checking, Downloading, Ready, Starting, Applying, Failed }

    private readonly CancellationTokenSource _quit = new();
    private UpdateLog? _log;
    private Task<UpdateRelease>? _check;
    private Task<string>? _download;
    private UpdateClient? _client;
    private Stage _stage = Stage.Idle;
    private bool _manual;
    private bool _sessionStarted;
    private bool _environmentLogged;
    private int _retries;
    private double _clock;
    private double _retryAt = -1;
    private double _holdUntil = -1;
    private string? _packagePath;
    private bool _installedCopy;
    private System.Diagnostics.Process? _installer;
    private double _installerStartedAt;
    private string _installerKind = "setup";
    private string _installerLog = "";
    private string? _gaveUp;
    private string _gaveUpReason = "";
    // A version whose download another program took (an antivirus, most likely): not downloaded again this session.
    private string? _blocked;
    private string _blockedReason = "";
    private bool _handOverFailed;
    private string _exePath = "";
    private bool _alone;
    private bool _aloneApply;
    private Action<int>? _aloneDone;

    /// <summary>A newer release, once the check has found one.</summary>
    public UpdateRelease? Available { get; private set; }
    /// <summary>One line for the HUD and the title screen: what the updater is doing, offering or failed at; empty when there is nothing to say.</summary>
    public string Status { get; private set; } = "";
    public bool Busy => _stage is Stage.Checking or Stage.Downloading or Stage.Starting or Stage.Applying;
    /// <summary>Downloaded and verified: <see cref="ApplyNow"/> restarts the game on the new version.</summary>
    public bool ReadyToApply => _stage == Stage.Ready;
    /// <summary>Whether a newer release is taken without being asked (the settings' update/auto, true by default).</summary>
    public bool AutoInstall { get; private set; } = true;
    /// <summary>
    /// How the game quits once an installer has taken over: Bootstrap's own shutdown, which saves and lets go of the
    /// one-copy lock the installer waits for. Without it the tree is quit directly.
    /// </summary>
    public Action? RequestQuit { get; set; }

    public static Version CurrentVersion
    {
        get
        {
            var forced = System.Environment.GetEnvironmentVariable("GRUDGELAP_FAKE_VERSION");
            if (!string.IsNullOrEmpty(forced) && Version.TryParse(forced, out var fake)) return ReleaseVersion.Normalise(fake);
            var text = ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
            return ReleaseVersion.Normalise(Version.TryParse(text, out var v) ? v : new Version(0, 0, 0));
        }
    }

    public static string CurrentVersionText => ReleaseVersion.Text(CurrentVersion);

    /// <summary>Check without downloading or changing anything (GRUDGELAP_UPDATE_DRYRUN=1).</summary>
    public static bool DryRun => System.Environment.GetEnvironmentVariable("GRUDGELAP_UPDATE_DRYRUN") is { Length: > 0 } value && value != "0";

    private static UpdateEndpoints Endpoints =>
        System.Environment.GetEnvironmentVariable("GRUDGELAP_UPDATE_FEED") is { Length: > 0 } feed && Uri.TryCreate(feed, UriKind.Absolute, out var uri)
            ? UpdateEndpoints.ForFeed(uri, RepoOwner, Repo)
            : UpdateEndpoints.GitHub(RepoOwner, Repo);

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

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    /// <summary>The game is closing: whatever runs in the background stops now, without being waited for.</summary>
    public override void _ExitTree() => _quit.Cancel();

    /// <summary>Starts a check unless one ran moments ago (or <paramref name="manual"/> is set); a newer release is then downloaded when <see cref="AutoInstall"/> allows.</summary>
    public void CheckIfDue(bool manual)
    {
        if (Busy || ReadyToApply) return;
        StartSession();
        var settings = LoadSettings();
        AutoInstall = settings.GetValue("update", "auto", true).AsBool();
        if (manual) _retryAt = -1;
        else
        {
            if (!settings.GetValue("update", "enabled", true).AsBool())
            {
                Log("the check at start is switched off (settings.cfg [update] enabled=false)");
                return;
            }
            var last = settings.GetValue("update", "last_check", 0L).AsInt64();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!UpdateSchedule.IsDue(last, now, CheckInterval))
            {
                Log($"checked {now - last} s ago: no check at this start");
                return;
            }
        }
        if (!DryRun)
        {
            settings.SetValue("update", "last_check", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            settings.Save(SettingsFile);
        }
        _retries = 0;
        StartCheck(manual);
    }

    /// <summary>
    /// The updater alone, for <c>--update-only</c>: a check, then (unless a dry run) the download and its verification,
    /// and with <paramref name="apply"/> the installer started without the game started again afterwards.
    /// <paramref name="done"/> gets 0 when every step asked for went through, 2 when the check failed, 3 the download,
    /// 4 the hand-over, 5 when the version is one the game gave up on.
    /// </summary>
    public void RunAlone(bool apply, Action<int> done)
    {
        _alone = true;
        _aloneApply = apply;
        _aloneDone = done;
        StartSession();
        AutoInstall = true;
        StartCheck(manual: true);
    }

    /// <summary>F10 with a release known: downloads the right package for this copy, or after repeated failed installs opens its page.</summary>
    public void DownloadAndInstall() => StartDownload(manual: true);

    /// <summary>Applies a verified update while the player is between credits, so a drive is never cut short. Called every frame by the game.</summary>
    public void ApplyWhenIdle(bool idle)
    {
        // After a hand-over that failed this session only F10 tries again: the title screen would otherwise retry it every frame.
        if (idle && ReadyToApply && AutoInstall && !_alone && !_handOverFailed) ApplyNow();
    }

    /// <summary>Hands a verified update to the installer or the unpack script; the game quits once that has started, and stays when it could not.</summary>
    public void ApplyNow()
    {
        if (!ReadyToApply || Available is not { } release || _packagePath is null) return;
        StartInstaller(release, _packagePath, relaunch: !_alone);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_check is { IsCompleted: true } check)
        {
            _check = null;
            OnChecked(check);
        }
        if (_download is { IsCompleted: true } download)
        {
            _download = null;
            OnDownloaded(download);
        }
        if (_stage == Stage.Downloading && _client is { } client) ShowProgress(client);
        if (_stage == Stage.Starting) WatchInstaller();
        if (_retryAt >= 0 && _clock >= _retryAt)
        {
            _retryAt = -1;
            if (!Busy && !ReadyToApply) StartCheck(manual: false);
        }
    }

    /// <summary>The session's first lines in the log, and the verdict on the update the last session handed over.</summary>
    private void StartSession()
    {
        if (_sessionStarted) return;
        _sessionStarted = true;
        _log = new UpdateLog(ProjectSettings.GlobalizePath(LogFile));
        // Godot gives the path with forward slashes; the log and the scripts get the Windows form.
        _exePath = System.IO.Path.GetFullPath(OS.GetExecutablePath());
        _installedCopy = IsInstalledCopy;
        var feed = System.Environment.GetEnvironmentVariable("GRUDGELAP_UPDATE_FEED");
        Log($"---- GrudgeLap {CurrentVersionText}, {(_installedCopy ? "installed" : "portable")} copy: {_exePath}"
            + (DryRun ? " (dry run)" : "") + (string.IsNullOrEmpty(feed) ? "" : $" (feed {feed})") + (_alone ? " (--update-only)" : ""));
        if (DryRun) return;
        var updated = ReviewLastApply();
        var current = CurrentVersion;
        // Off the main thread: an antivirus can make deleting a 480 MB file slow.
        _ = Task.Run(() =>
        {
            // After a confirmed update nothing in the folder is needed; otherwise the packages of this version and older go.
            var deleted = updated ? UpdateFolder.Clean(UpdateFolder.Default, keep: null, logsToo: true) : UpdateFolder.CleanApplied(UpdateFolder.Default, current);
            foreach (var name in deleted) Log($"deleted {name} (this copy is v{ReleaseVersion.Text(current)})");
        });
    }

    private void StartCheck(bool manual)
    {
        _manual = manual;
        _stage = Stage.Checking;
        if (manual) SetStatus("checking for updates...");
        var feed = new UpdateFeed(UpdateHttp.Shared, Endpoints, Log);
        var describe = !_environmentLogged;
        _environmentLogged = true;
        var ct = _quit.Token;
        Log($"checking for updates ({(manual ? "asked for" : _retries > 0 ? $"retry {_retries}" : "at start")})");
        _check = Task.Run(async () =>
        {
            // Off the main thread: the proxy lookup may run the network's PAC script.
            if (describe) LogEnvironment();
            return await feed.FindLatestAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    private void OnChecked(Task<UpdateRelease> task)
    {
        if (task.IsCanceled || _quit.IsCancellationRequested) return;
        if (task.Exception is { } error)
        {
            var reason = Reason(error);
            Log($"the check failed: {reason}");
            _stage = Stage.Failed;
            FailWithRetry("update check failed: " + UpdateTexts.Fit(reason), code: 2, error.InnerException);
            return;
        }
        var release = task.Result;
        _stage = Stage.Idle;
        Log($"latest release v{release.VersionText} ({release.Source}); this copy v{CurrentVersionText}");
        if (release.Version <= CurrentVersion)
        {
            Available = null;
            if (_manual) SetStatus($"up to date (v{CurrentVersionText})");
            else ClearStatus();
            AloneDone(0);
            return;
        }
        Available = release;
        LogPackage("installer", release.Setup);
        LogPackage("portable", release.Portable);
        if (DryRun)
        {
            SetStatus($"v{release.VersionText} available (dry run: nothing downloaded)");
            AloneDone(0);
            return;
        }
        if (_gaveUp == release.VersionText)
        {
            SetStatus(UpdateTexts.GaveUp(release.VersionText, _gaveUpReason));
            AloneDone(5);
            return;
        }
        SetStatus($"v{release.VersionText} available - F10 to update");
        if (AutoInstall) StartDownload(manual: _manual);
    }

    private void StartDownload(bool manual)
    {
        if (Busy || ReadyToApply || Available is not { } release) return;
        StartSession();
        // Only a key press opens the browser: a start, or a run without a player, never throws a page at anyone.
        var browse = manual && !_alone;
        if (_gaveUp == release.VersionText)
        {
            // Twice the installer did not take: the player gets the page, where the installer can be run by hand.
            Log($"v{release.VersionText} is left to the player: {release.Page}");
            if (browse) OS.ShellOpen(release.Page.AbsoluteUri);
            AloneDone(5);
            return;
        }
        if (_blocked == release.VersionText)
        {
            // Another copy would only be taken again: the page instead, where the browser lets the player keep the file.
            Log($"v{release.VersionText} is not downloaded again this session ({_blockedReason}): {release.Page}");
            SetStatus(UpdateTexts.Blocked(_blockedReason));
            if (browse) OS.ShellOpen(release.Page.AbsoluteUri);
            AloneDone(3);
            return;
        }
        var installed = _installedCopy;
        if ((installed ? release.Setup : release.Portable) is not { } package)
        {
            Log($"v{release.VersionText} has no {(installed ? "installer" : "portable zip")}");
            SetStatus(UpdateTexts.NoPackage(release.VersionText, installed));
            if (browse) OS.ShellOpen(release.Page.AbsoluteUri);
            AloneDone(3);
            return;
        }
        var folder = UpdateFolder.Default;
        var gameDir = System.IO.Path.GetDirectoryName(_exePath) ?? ".";
        // The portable copy also unpacks the zip beside it before copying: room for the package twice over.
        var options = new UpdateClientOptions { Reserve = installed ? 64L << 20 : Math.Max(package.Size ?? 0, 600L << 20) };
        var client = new UpdateClient(UpdateHttp.Shared, options, Log);
        _client = client;
        _stage = Stage.Downloading;
        SetStatus($"downloading v{release.VersionText}...");
        var ct = _quit.Token;
        _download = Task.Run(async () =>
        {
            foreach (var name in UpdateFolder.Clean(folder, keep: package.Name)) Log($"deleted {name} (left by an earlier update)");
            if (!installed && !UpdateDiagnostics.CanWrite(gameDir))
            {
                throw new UpdateException(UpdateFailure.Disk, "the game's folder cannot be written: move the portable copy to a folder of your own, or use the installer");
            }
            return await client.DownloadAsync(package, folder, ct).ConfigureAwait(false);
        }, ct);
    }

    private void OnDownloaded(Task<string> task)
    {
        if (task.IsCanceled || _quit.IsCancellationRequested) return;
        if (task.Exception is { } error)
        {
            var reason = Reason(error);
            Log($"the download failed: {reason}");
            _stage = Stage.Failed;
            if (error.InnerException is UpdateException { Kind: UpdateFailure.Blocked } && Available is { } release)
            {
                _blocked = release.VersionText;
                _blockedReason = reason;
                Log($"v{release.VersionText} is not downloaded again this session; F10 opens {release.Page}");
                SetStatus(UpdateTexts.Blocked(reason));
                AloneDone(3);
                return;
            }
            FailWithRetry("update failed: " + UpdateTexts.Fit(reason), code: 3, error.InnerException);
            return;
        }
        _packagePath = task.Result;
        _stage = Stage.Ready;
        SetStatus($"v{Available!.VersionText} ready - restarts at the title screen (F10 now)");
        if (!_alone) return;
        if (_aloneApply) ApplyNow();
        else AloneDone(0);
    }

    private void ShowProgress(UpdateClient client)
    {
        var version = Available?.VersionText ?? "?";
        var done = client.BytesDone;
        var total = client.BytesTotal;
        Status = client.Phase switch
        {
            DownloadPhase.Waiting => $"downloading v{version}: {client.Note}",
            DownloadPhase.Verifying => total > 0 ? $"verifying v{version} {done * 100 / total}%" : $"verifying v{version}...",
            DownloadPhase.Connecting when done == 0 => $"downloading v{version}: connecting...",
            _ => total > 0 ? $"downloading v{version} {done * 100 / total}% ({done >> 20} of {total >> 20} MB)" : $"downloading v{version} {done >> 20} MB",
        };
    }

    /// <summary>
    /// A failure of the check or the download. An automatic one that a lost or silent connection caused is tried again
    /// in 30 s, then in 3 minutes (the .part keeps what came); any other would end the same way, and in the download
    /// cost the whole package again, so it waits for F10.
    /// </summary>
    private void FailWithRetry(string text, int code, Exception? error)
    {
        if (!_manual && !_alone && UpdateSchedule.RetryHelps(error) && UpdateSchedule.RetryDelay(_retries + 1) is { } delay)
        {
            _retries++;
            _retryAt = _clock + delay.TotalSeconds;
            var when = delay.TotalSeconds < 90 ? $"{delay.TotalSeconds:0} s" : $"{delay.TotalMinutes:0} min";
            Log($"trying again in {when}");
            SetStatus($"{text} - trying again in {when}");
            return;
        }
        if (!_manual && !_alone && !UpdateSchedule.RetryHelps(error)) Log("not tried again by itself: it would end the same way");
        SetStatus(_alone ? text : $"{text} - F10 to try again");
        AloneDone(code);
    }

    private void StartInstaller(UpdateRelease release, string package, bool relaunch)
    {
        var folder = System.IO.Path.GetDirectoryName(package)!;
        var gameDir = System.IO.Path.GetDirectoryName(_exePath) ?? ".";
        _installerKind = _installedCopy ? "setup" : "portable";
        _installerLog = System.IO.Path.Combine(folder, _installedCopy ? $"setup-{release.VersionText}.log" : "apply.log");
        // Both through ShellExecute: .NET's own CreateProcess lets the child inherit every inheritable handle of the
        // game, and a script that held the game's files open could not copy over them.
        System.Diagnostics.ProcessStartInfo start;
        if (_installedCopy)
        {
            // The installer itself, by its full path: until 0.76 a hidden PowerShell from %TEMP% started it once the
            // game had gone, which is what antivirus software looks hardest at, and nobody heard when it failed.
            start = new System.Diagnostics.ProcessStartInfo(package, UpdateCommands.InstallerArguments(_installerLog, relaunch))
            {
                UseShellExecute = true,
                WorkingDirectory = folder,
            };
        }
        else
        {
            var script = System.IO.Path.Combine(folder, "apply.ps1");
            try
            {
                System.IO.File.WriteAllText(script, UpdateCommands.UnpackScript);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FailToStart(release, $"the unpack script could not be written ({ex.Message})");
                return;
            }
            var args = UpdateCommands.UnpackArguments(script, System.Environment.ProcessId, package, gameDir, _exePath, release.VersionText, relaunch);
            start = new System.Diagnostics.ProcessStartInfo(UpdateCommands.PowerShell, UpdateCommands.CommandLine(args))
            {
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = folder,
            };
        }
        // Written before the start: when the game next opens on the old version, it knows what was tried and where its log is.
        var settings = LoadSettings();
        settings.SetValue("update", "applying", release.VersionText);
        settings.SetValue("update", "applying_from", CurrentVersionText);
        settings.SetValue("update", "applying_kind", _installerKind);
        settings.SetValue("update", "applying_log", _installerLog);
        settings.SetValue("update", "applying_package", package);
        settings.SetValue("update", "applying_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        settings.Save(SettingsFile);
        // A log left by an earlier try would speak for an installer that never ran; the review reads only this try's.
        try
        {
            System.IO.File.Delete(_installerLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"the old log {_installerLog} could not be cleared: {ex.Message}");
        }
        Log($"starting {(_installedCopy ? "the installer" : "the unpack script")}: \"{start.FileName}\" {start.Arguments}");
        try
        {
            _installer = System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex)
        {
            // Win32Exception: a file an antivirus has blocked ("contains a virus or potentially unwanted software") or
            // removed under our feet, access denied. The game stays, and says why, naming the antivirus it can.
            FailToStart(release, _installedCopy
                ? UpdateTexts.StartFailure(ex, System.IO.Path.GetFileName(package), UpdateDiagnostics.ActiveAntivirus())
                : $"the unpack script could not start ({ex.Message.TrimEnd('.')})");
            return;
        }
        _installerStartedAt = _clock;
        _stage = Stage.Starting;
        SetStatus("installing update...");
    }

    /// <summary>The installer's first moments: one that ends at once failed (the game stays); one that keeps running gets the game out of its way.</summary>
    private void WatchInstaller()
    {
        if (_installer is { } installer)
        {
            bool exited;
            try
            {
                exited = installer.HasExited;
            }
            catch (InvalidOperationException)
            {
                exited = false;   // no handle to watch: it is trusted to run
            }
            if (exited)
            {
                var reason = InstallerOutcome.ExitReason(_installerKind, installer.ExitCode);
                var logged = InstallerOutcome.Reason(_installerKind, _installerLog);
                LogTail(_installerLog);
                FailToStart(Available!, logged.Contains("left no log", StringComparison.Ordinal) ? reason : $"{reason}: {logged}");
                return;
            }
        }
        if (_clock - _installerStartedAt < InstallerGrace) return;
        _stage = Stage.Applying;
        Log("the installer is running: the game quits so that it can be replaced");
        if (_alone)
        {
            AloneDone(0);
            return;
        }
        if (RequestQuit is { } quit) quit();
        else GetTree().Quit();
    }

    /// <summary>A hand-over that failed while the game still runs: it counts as a failed install of that version.</summary>
    private void FailToStart(UpdateRelease release, string reason)
    {
        Log($"the update could not be handed over: {reason}");
        _installer = null;
        var settings = LoadSettings();
        ClearApplying(settings);
        var count = RecordFailure(settings, release.VersionText, reason);
        settings.Save(SettingsFile);
        _handOverFailed = true;
        if (count >= MaxApplyFailures)
        {
            _gaveUp = release.VersionText;
            _gaveUpReason = reason;
            _stage = Stage.Idle;
            SetStatus(UpdateTexts.GaveUp(release.VersionText, reason), ResultHold);
        }
        else
        {
            // The verified package stays: F10, or the next start, tries again without a download.
            _stage = Stage.Ready;
            SetStatus($"update failed: {UpdateTexts.Fit(reason)} - F10 to try again", ResultHold);
        }
        AloneDone(4);
    }

    /// <summary>
    /// The verdict on the update the last session handed over: this start's version against the one it was going to.
    /// A failure is counted (twice and the game stops applying that version itself) and its reason, read from the
    /// installer's log, goes on screen. True when the update took: the download folder can then be emptied.
    /// </summary>
    private bool ReviewLastApply()
    {
        var settings = LoadSettings();
        var applying = settings.GetValue("update", "applying", "").AsString();
        var failedVersion = settings.GetValue("update", "failed_version", "").AsString();
        var failedCount = settings.GetValue("update", "failed_count", 0).AsInt32();
        var failedReason = settings.GetValue("update", "failed_reason", "").AsString();
        var updated = false;
        var changed = false;
        if (applying.Length > 0)
        {
            var from = settings.GetValue("update", "applying_from", "?").AsString();
            var kind = settings.GetValue("update", "applying_kind", "setup").AsString();
            var log = settings.GetValue("update", "applying_log", "").AsString();
            LogTail(log);
            if (ReleaseVersion.TryParse(applying, out var target) && CurrentVersion >= target)
            {
                Log($"updated from v{from} to v{CurrentVersionText}");
                SetStatus($"updated to v{CurrentVersionText}", ResultHold);
                ClearFailures(settings);
                failedCount = 0;
                updated = true;
            }
            else
            {
                failedReason = InstallerOutcome.Reason(kind, log);
                failedCount = RecordFailure(settings, applying, failedReason);
                failedVersion = applying;
                Log($"the update to v{applying} did not take ({failedCount} of {MaxApplyFailures} tries): {failedReason}");
                if (failedCount < MaxApplyFailures) SetStatus($"the update to v{applying} did not install: {UpdateTexts.Fit(failedReason)}", ResultHold);
            }
            ClearApplying(settings);
            changed = true;
        }
        else if (failedVersion.Length > 0 && (!ReleaseVersion.TryParse(failedVersion, out var stuck) || stuck <= CurrentVersion))
        {
            // The version that would not install is here after all (installed by hand): the record is done with.
            ClearFailures(settings);
            failedCount = 0;
            changed = true;
        }
        if (failedCount >= MaxApplyFailures)
        {
            _gaveUp = failedVersion;
            _gaveUpReason = failedReason;
            SetStatus(UpdateTexts.GaveUp(failedVersion, failedReason), ResultHold);
        }
        if (changed) settings.Save(SettingsFile);
        return updated;
    }

    private static int RecordFailure(ConfigFile settings, string version, string reason)
    {
        var count = settings.GetValue("update", "failed_version", "").AsString() == version ? settings.GetValue("update", "failed_count", 0).AsInt32() + 1 : 1;
        settings.SetValue("update", "failed_version", version);
        settings.SetValue("update", "failed_count", count);
        settings.SetValue("update", "failed_reason", reason);
        return count;
    }

    private static void ClearFailures(ConfigFile settings)
    {
        foreach (var key in new[] { "failed_version", "failed_count", "failed_reason" })
        {
            if (settings.HasSectionKey("update", key)) settings.EraseSectionKey("update", key);
        }
    }

    private static void ClearApplying(ConfigFile settings)
    {
        foreach (var key in new[] { "applying", "applying_from", "applying_kind", "applying_log", "applying_package", "applying_at" })
        {
            if (settings.HasSectionKey("update", key)) settings.EraseSectionKey("update", key);
        }
    }

    /// <summary>The last lines of the installer's or the script's log (an install that failed rolls back for a dozen lines after the error), copied into update.log before the folder is cleared.</summary>
    private void LogTail(string? path)
    {
        var lines = InstallerOutcome.Tail(path, 40);
        if (lines.Count == 0) return;
        Log($"last lines of {path}:");
        foreach (var line in lines) Log("  | " + line);
    }

    private void LogEnvironment()
    {
        var gameDir = System.IO.Path.GetDirectoryName(_exePath) ?? ".";
        Log($"download folder {UpdateFolder.Default}; free space {UpdateDiagnostics.FreeSpace(UpdateFolder.Default)} (downloads), {UpdateDiagnostics.FreeSpace(gameDir)} (game)");
        Log($"proxy for GitHub: {UpdateDiagnostics.Proxy(new Uri("https://github.com/"))}; antivirus: {UpdateDiagnostics.Antivirus()}");
        Log($"Windows {System.Environment.OSVersion.Version}, .NET {System.Environment.Version}");
    }

    private void LogPackage(string what, UpdatePackage? package)
    {
        if (package is null) Log($"  {what}: none in this release");
        else Log($"  {what}: {package.Name}, {(package.Size is { } size ? UpdateHttp.Megabytes(size) : "size unknown")}, SHA-256 {(package.Sha256 ?? (package.ShaUrl is null ? "none" : "from its .sha256 file"))}");
    }

    /// <summary>The failure's reason for the screen, the log and settings.cfg; an unexpected exception's own words could quote a proxy's address with its sign-in.</summary>
    private static string Reason(AggregateException error) => UpdateLog.Redact(error.InnerException switch
    {
        UpdateException update => update.Message,
        { } other => $"{other.GetType().Name}: {other.Message}",
        null => error.Message,
    });

    private void SetStatus(string text, double hold = 0)
    {
        Status = text;
        _holdUntil = hold > 0 ? _clock + hold : -1;
    }

    /// <summary>Nothing to say, unless the result of the last update is still being shown.</summary>
    private void ClearStatus()
    {
        if (_clock >= _holdUntil) Status = "";
    }

    private void AloneDone(int code)
    {
        if (!_alone || _aloneDone is not { } done) return;
        _aloneDone = null;
        Log($"--update-only: exit code {code} ({Status})");
        GD.Print($"update-only: {Status} (exit code {code}; log {_log?.FilePath})");
        done(code);
    }

    private static ConfigFile LoadSettings()
    {
        var settings = new ConfigFile();
        settings.Load(SettingsFile);
        return settings;
    }

    /// <summary>A line for update.log and Godot's own log, both without any sign-in (<see cref="UpdateLog.Redact"/>).</summary>
    private void Log(string line)
    {
        _log?.Write(line);
        GD.Print("update: " + UpdateLog.Redact(line));
    }
}
