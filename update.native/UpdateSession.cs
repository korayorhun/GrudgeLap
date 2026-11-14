using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GrudgeLap.Game;

/// <summary>
/// What a host game knows about the copy it runs in and the updater cannot find out by itself. The Godot version
/// asked its engine for these (the project's version, the running executable, user://).
/// </summary>
/// <param name="Version">This copy's version, "0.78.0" or "v0.78.0".</param>
/// <param name="LauncherExe">
/// The executable a player starts the game with. In the Unreal package that is the small launcher in the root folder,
/// not the engine's executable under Binaries\Win64 that the updater runs in: a portable copy's unpack script starts
/// it again once the new files are in place.
/// </param>
/// <param name="RootDir">
/// The folder the whole game lives under: the installer records it as InstallLocation (which tells an installed copy
/// from a portable one), and a portable zip is unpacked over it.
/// </param>
/// <param name="DataDir">The player's data folder, where update.log and update-state.ini are kept.</param>
internal sealed record UpdateHost(string Version, string LauncherExe, string RootDir, string DataDir);

/// <summary>What the tests replace in an <see cref="UpdateSession"/>; the game leaves every one of them as it is.</summary>
internal sealed record UpdateSessionOptions
{
    /// <summary>The HTTP client; by default one of the session's own, through the system's proxy unless the feed is on this computer.</summary>
    public HttpClient? Http { get; init; }
    /// <summary>Where the releases are read from; by default GitHub, or the feed GRUDGELAP_UPDATE_FEED names.</summary>
    public UpdateEndpoints? Endpoints { get; init; }
    /// <summary>Where packages are downloaded to; by default <see cref="UpdateFolder.Default"/>.</summary>
    public string? DownloadFolder { get; init; }
    /// <summary>The download client's waits; by default the game's, with the free space this kind of copy needs.</summary>
    public UpdateClientOptions? Client { get; init; }
    /// <summary>Whether this is an installed copy; by default what the registry says about <see cref="UpdateHost.RootDir"/>.</summary>
    public bool? InstalledCopy { get; init; }
    /// <summary>The longest a metadata request may take; by default the feed's own limit.</summary>
    public TimeSpan? FeedTimeout { get; init; }
    /// <summary>How long a started installer is given to show that it runs; by default <see cref="UpdateSession.InstallerSignWait"/>.</summary>
    public TimeSpan? InstallerSignWait { get; init; }
    /// <summary>A monotonic clock; by default a stopwatch started with the session.</summary>
    public Func<TimeSpan>? Clock { get; init; }
    /// <summary>Starts the installer or the unpack script; by default <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
    public Func<ProcessStartInfo, Process?>? StartProcess { get; init; }
    /// <summary>Reads an environment variable (the GRUDGELAP_* hooks); by default the process's own.</summary>
    public Func<string, string?> EnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;
}

/// <summary>Where an <see cref="UpdateSession"/> stands. The numbers are the C interface's (glp_update_state.phase in grudgelap_update.h).</summary>
internal enum UpdatePhase
{
    /// <summary>Nothing asked for yet; the status may tell how the update handed over in the last session ended.</summary>
    Idle = 0,
    /// <summary>The check the player asked for is running, or waits to be tried again after a lost connection.</summary>
    Checking = 1,
    UpToDate = 2,
    /// <summary>A newer release was found. Nothing of it has been downloaded.</summary>
    Available = 3,
    /// <summary>The download the player asked for is running, or waits to be tried again after a lost connection.</summary>
    Downloading = 4,
    Verifying = 5,
    /// <summary>Downloaded and verified: <see cref="UpdateSession.Apply"/> hands it over.</summary>
    Ready = 6,
    /// <summary>The installer (or the unpack script) has been started and is watched through its first moments.</summary>
    Starting = 7,
    /// <summary>
    /// The installer runs and waits for the game to exit: the host must quit now. Also what a session starts in when
    /// the last session's installer or unpack script is still at work (the game was started again too early).
    /// </summary>
    QuitNow = 8,
    /// <summary>The last step failed; the status says why, <see cref="UpdateSnapshot.Retry"/> and <see cref="UpdateSnapshot.Page"/> what can be done.</summary>
    Failed = 9,
}

/// <summary>Which request tries a failed step again. The numbers are the C interface's (glp_update_state.can_retry).</summary>
internal enum UpdateRetry
{
    None = 0,
    Check = 1,
    Download = 2,
    Apply = 3,
}

/// <summary>What <see cref="UpdateSession.Poll"/> tells the host, every frame.</summary>
/// <param name="PackageSize">The size of the available release's package for this copy; 0 when the release does not state it.</param>
/// <param name="Latest">The version of the release the last check found, "" before one.</param>
/// <param name="Status">One line for the menu, without key hints: what the updater is doing, offering or failed at.</param>
/// <param name="Page">
/// The release's page, when opening it is all that is left to do: the update did not install twice, an antivirus took
/// the download, or the release has no package for this kind of copy. "" otherwise.
/// </param>
internal readonly record struct UpdateSnapshot(
    UpdatePhase Phase, bool InstalledCopy, UpdateRetry Retry, long BytesDone, long BytesTotal, long PackageSize, string Latest, string Status, string Page);

/// <summary>
/// update-state.ini in the host's data folder: what the updater must still know at the next start, as
/// <c>key=value</c> lines in UTF-8. <c>applying</c> and its <c>applying_*</c> details while an update is being handed
/// over (the next start compares them with its own version to tell whether the update took; <c>applying_cut</c> marks
/// a hand-over whose start the game's closing cut off); <c>failed_version</c>,
/// <c>failed_count</c> and <c>failed_reason</c> for an update that did not take. The Godot version kept the same keys
/// under [update] in its settings.cfg; the file exists only while there is something to remember.
/// </summary>
internal sealed class UpdateState
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly SortedDictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly string _path;

    private UpdateState(string path) => _path = path;

    /// <summary>Why the file could not be read, when it could not: the state then starts empty.</summary>
    public string? LoadError { get; private set; }

    public static UpdateState Load(string path)
    {
        var state = new UpdateState(path);
        try
        {
            if (!File.Exists(path)) return state;
            foreach (var line in File.ReadAllLines(path, Utf8))
            {
                var split = line.IndexOf('=');
                // Anything else (an empty line, a note somebody added) is not a value and is not kept.
                if (split <= 0 || line[0] is '#' or ';') continue;
                state._values[line[..split].Trim()] = line[(split + 1)..];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.LoadError = ex.Message;
        }
        return state;
    }

    public string Get(string key, string fallback = "") => _values.TryGetValue(key, out var value) ? value : fallback;

    public int GetInt(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

    /// <summary>A value is one line: a line break in it (an error message of Windows can hold one) becomes a space.</summary>
    public void Set(string key, string value) => _values[key] = value.ReplaceLineEndings(" ");

    public void Set(string key, long value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);

    public void Remove(string key) => _values.Remove(key);

    /// <summary>
    /// Writes the file, or deletes it when nothing is left to remember. The lines go to a file beside it that then
    /// takes its place: a game that is killed half-way (the installer closes one that does not quit) leaves the old
    /// state or the new one, never half a file. Returns why it could not be written, null when it was.
    /// </summary>
    public string? Save()
    {
        try
        {
            if (_values.Count == 0)
            {
                File.Delete(_path);
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var text = new StringBuilder();
            foreach (var (key, value) in _values) text.Append(key).Append('=').Append(value).Append('\n');
            var fresh = _path + ".new";
            File.WriteAllText(fresh, text.ToString(), Utf8);
            File.Move(fresh, _path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}

/// <summary>
/// The updater for a host that is not the Godot game (the Unreal version, through the C interface of Exports.cs): the
/// state machine of that game's Updater node without the engine, and with the owner's rule of 14 November 2026 that
/// nothing is downloaded unless the player asks. <see cref="Check"/> looks for a newer release on GitHub and says
/// what it found; only <see cref="DownloadAndInstall"/> downloads the package for this copy (the installer for an
/// installed copy, the zip for a portable one), resumably, and checks its SHA-256; <see cref="Apply"/> then starts the
/// installer or the unpack script and, once that has stayed alive for a moment, tells the host to quit so that the
/// game can be replaced and started again.
/// </summary>
/// <remarks>
/// <para>
/// The host calls everything from one thread and <see cref="Poll"/> every frame: the network work runs on the thread
/// pool (<see cref="UpdateFeed"/>, <see cref="UpdateClient"/>), and its results are acted on in Poll, on the host's
/// thread. Time is read from a monotonic clock inside, so no frame time is passed in and a stalled frame costs nothing.
/// </para>
/// <para>
/// No check at start and no download by itself, ever. What still happens unasked stays inside an operation the
/// player asked for: a check or a download that a lost or silent connection stopped is tried again in 30 s and then in
/// 3 minutes (<see cref="UpdateSchedule"/>; the .part keeps what came), and can be cancelled meanwhile. A retry of a
/// check only checks; a retry of a download does not look for a newer release first.
/// </para>
/// <para>
/// Creating the session writes the log's first lines, judges the update the last session handed over from
/// update-state.ini (it took; it did not; it failed twice, and only a check that finds a newer release goes on from
/// there; it is still at work, and the game must make way; its start was cut off by the game closing), and clears the
/// download folder of what that leaves, off the calling thread.
/// </para>
/// <para>
/// Hooks for checks without a player, as in the Godot version: GRUDGELAP_UPDATE_FEED (a stand-in for GitHub at that
/// URL, laid out as <see cref="UpdateEndpoints.ForFeed"/>), GRUDGELAP_UPDATE_DRYRUN (the check only: nothing is
/// downloaded, written or cleared) and GRUDGELAP_FAKE_VERSION (the version this copy claims to be). Two more are
/// for the packaged game's own checks: GRUDGELAP_UPDATE_UNATTENDED (the installer shows nothing, not even its
/// progress, and the game is not started again afterwards: whoever runs the check starts it, hidden) and
/// GRUDGELAP_UPDATE_APPID (the installer AppId whose uninstall key tells an installed copy: a test build of the
/// installer registers under its own, beside the player's real copy, and would otherwise count as portable).
/// </para>
/// </remarks>
internal sealed class UpdateSession
{
    public const string RepoOwner = "korayorhun";
    public const string Repo = "GrudgeLap";
    /// <summary>Inno Setup AppId of the installer; its uninstall key tells an installed copy from a portable one.</summary>
    public const string InstallerAppId = "{871E561A-E89E-41ED-870A-A635D3E789C2}";
    public const string LogFileName = "update.log";
    public const string StateFileName = "update-state.ini";
    /// <summary>
    /// How long an installer must stay alive after its start before the host is told to quit for it. One stopped at once
    /// (an antivirus that blocks or quarantines the new executable, a broken download) is caught while the game still runs.
    /// </summary>
    public static readonly TimeSpan InstallerGrace = TimeSpan.FromSeconds(1.5);
    /// <summary>
    /// How long a started installer is given to show that it runs, by its log (Inno Setup opens it as its first act,
    /// the unpack script writes its first line), before the host is told to quit for it all the same. Until then the
    /// game stays on the screen and says what it waits for, so that the player is not left before an empty desktop.
    /// </summary>
    public static readonly TimeSpan InstallerSignWait = TimeSpan.FromSeconds(30);
    /// <summary>Failed installs of one version before the updater stops applying it and points to the release page.</summary>
    public const int MaxApplyFailures = 2;
    /// <summary>
    /// How long after a hand-over an installer or an unpack script whose log has begun and not ended counts as still at
    /// work. A game started within it makes way at once (<see cref="ReviewLastApply"/>); after it the log is read as it
    /// stands, so a run that died keeps the game from starting for these minutes at most.
    /// </summary>
    public static readonly TimeSpan ApplyingWindow = TimeSpan.FromMinutes(5);

    private static readonly string[] ApplyingKeys = ["applying", "applying_from", "applying_kind", "applying_log", "applying_package", "applying_at", "applying_cut"];
    private static readonly string[] FailureKeys = ["failed_version", "failed_count", "failed_reason"];

    private enum Operation { Check, Download }

    private readonly UpdateSessionOptions _options;
    private readonly UpdateLog _log;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly UpdateEndpoints _endpoints;
    private readonly Func<TimeSpan> _clock;
    private readonly string _launcher;
    private readonly string _rootDir;
    private readonly string _downloadFolder;
    private readonly string _statePath;
    private readonly Version _current;
    private readonly string _currentText;
    private readonly bool _installed;

    private UpdatePhase _phase = UpdatePhase.Idle;
    private string _status = "";
    private UpdateRetry _retry;
    private string _page = "";
    private UpdateRelease? _available;
    private string _latest = "";
    private long _packageSize;
    private long _bytesDone;
    private long _bytesTotal;

    private Task<UpdateRelease>? _check;
    private Task<string>? _download;
    private UpdateClient? _client;
    private (DownloadPhase Phase, long Megabytes, long Percent, long Total, string Note) _shown;
    private CancellationTokenSource? _cancel;
    // What must have let go of the download folder before the next download touches it: the session's own clearing,
    // and a download that was cancelled a moment ago and still holds its .part.
    private Task _cleanup = Task.CompletedTask;
    private Task _stopped = Task.CompletedTask;
    private bool _environmentLogged;
    private bool _ended;

    private int _retries;
    private TimeSpan? _retryAt;
    private Operation _retryOperation;

    private string? _packagePath;
    // The start of the installer or the unpack script, running off the host's thread, with the clock's time at which
    // the process was there.
    private Task<(Process? Process, TimeSpan At)>? _starting;
    private string _startingPackage = "";
    private Process? _installer;
    private TimeSpan _installerStartedAt;
    private string _installerKind = "setup";
    private string _installerLog = "";
    private string? _gaveUp;
    private string _gaveUpReason = "";
    // A version whose download another program took (an antivirus, most likely): not downloaded again this session.
    private string? _blocked;
    private string _blockedReason = "";

    /// <summary>Starts the session: the log's first lines, the verdict on the last hand-over, the download folder cleared.</summary>
    /// <exception cref="ArgumentException">The host's version is not a version, or one of its paths is empty or not a path.</exception>
    public UpdateSession(UpdateHost host, UpdateSessionOptions? options = null)
    {
        _options = options ?? new UpdateSessionOptions();
        // A version that cannot be read would make every release look newer, for ever: better no updater than that one.
        if (!ReleaseVersion.TryParse(host.Version, out var version)) throw new ArgumentException($"the game's version \"{host.Version}\" is not a version like 0.78.0", nameof(host));
        _launcher = FullPath(host.LauncherExe, "the launcher's path");
        _rootDir = Path.TrimEndingDirectorySeparator(FullPath(host.RootDir, "the game's root folder"));
        var dataDir = FullPath(host.DataDir, "the data folder");

        var forced = _options.EnvironmentVariable("GRUDGELAP_FAKE_VERSION");
        _current = !string.IsNullOrEmpty(forced) && Version.TryParse(forced, out var fake) ? ReleaseVersion.Normalise(fake) : version;
        _currentText = ReleaseVersion.Text(_current);
        DryRun = _options.EnvironmentVariable("GRUDGELAP_UPDATE_DRYRUN") is { Length: > 0 } dry && dry != "0";
        Unattended = _options.EnvironmentVariable("GRUDGELAP_UPDATE_UNATTENDED") is { Length: > 0 } alone && alone != "0";
        var feed = _options.EnvironmentVariable("GRUDGELAP_UPDATE_FEED");
        _endpoints = _options.Endpoints ?? (!string.IsNullOrEmpty(feed) && Uri.TryCreate(feed, UriKind.Absolute, out var uri)
            ? UpdateEndpoints.ForFeed(uri, RepoOwner, Repo)
            : UpdateEndpoints.GitHub(RepoOwner, Repo));
        _ownsHttp = _options.Http is null;
        // A feed on this computer is a test's: no proxy should see it, and none is asked about it.
        _http = _options.Http ?? UpdateHttp.CreateClient(ReleaseVersion.Text(version), useProxy: !_endpoints.Web.IsLoopback);
        var watch = Stopwatch.StartNew();
        _clock = _options.Clock ?? (() => watch.Elapsed);
        _downloadFolder = _options.DownloadFolder ?? UpdateFolder.Default;
        var appId = _options.EnvironmentVariable("GRUDGELAP_UPDATE_APPID");
        _installed = _options.InstalledCopy ?? IsInstalledAt(_rootDir, string.IsNullOrEmpty(appId) ? InstallerAppId : appId);
        _statePath = Path.Combine(dataDir, StateFileName);
        try
        {
            Directory.CreateDirectory(dataDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the folder there is no log and no memory of a hand-over, but a check and a download still work.
        }
        _log = new UpdateLog(Path.Combine(dataDir, LogFileName));

        Log($"---- GrudgeLap {_currentText}, {(_installed ? "installed" : "portable")} copy: {_launcher}"
            + (DryRun ? " (dry run)" : "") + (Unattended ? " (unattended)" : "") + (string.IsNullOrEmpty(feed) ? "" : $" (feed {feed})"));
        if (DryRun) return;
        var updated = ReviewLastApply();
        var current = _current;
        var folder = _downloadFolder;
        // Off the calling thread: an antivirus can make deleting a 480 MB file slow.
        _cleanup = Task.Run(() =>
        {
            try
            {
                // After a confirmed update nothing in the folder is needed; otherwise the packages of this version and older go.
                var deleted = updated ? UpdateFolder.Clean(folder, keep: null, logsToo: true) : UpdateFolder.CleanApplied(folder, current);
                foreach (var name in deleted) Log($"deleted {name} (this copy is v{ReleaseVersion.Text(current)})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log($"the download folder could not be cleared: {ex.Message}");
            }
        });
    }

    public UpdatePhase Phase => _phase;
    /// <summary>One line for the menu: what the updater is doing, offering or failed at; empty when there is nothing to say.</summary>
    public string Status => _status;
    /// <summary>The newer release the last check found.</summary>
    public UpdateRelease? Available => _available;
    /// <summary>True when this copy was put in place by the installer, which is then what updates it; a portable copy takes the zip.</summary>
    public bool InstalledCopy => _installed;
    /// <summary>The check only: nothing is downloaded, written or cleared (GRUDGELAP_UPDATE_DRYRUN=1).</summary>
    public bool DryRun { get; }
    /// <summary>
    /// A check without a player (GRUDGELAP_UPDATE_UNATTENDED=1): the installer shows nothing and neither it nor the
    /// unpack script starts the game again.
    /// </summary>
    public bool Unattended { get; }
    /// <summary>Why the last request that returned false was not taken.</summary>
    public string Refusal { get; private set; } = "";
    /// <summary>The clearing of the download folder that the session started with; the tests wait for it.</summary>
    internal Task Cleanup => _cleanup;
    /// <summary>The start of the installer or the unpack script that the last <see cref="Apply"/> began; the tests wait for it.</summary>
    internal Task HandOver => _starting is { } starting ? starting.ContinueWith(static _ => { }, TaskScheduler.Default) : Task.CompletedTask;

    private bool Working => _check is not null || _download is not null;

    /// <summary>
    /// True when the installer's uninstall key names <paramref name="rootDir"/> (Inno Setup writes InstallLocation for
    /// the AppId). <paramref name="appId"/> is the game's own unless a test build of the installer is being checked.
    /// </summary>
    public static bool IsInstalledAt(string rootDir, string appId = InstallerAppId)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{appId}_is1");
            if (key?.GetValue("InstallLocation") is not string { Length: > 0 } location) return false;
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDir)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Looks for a newer release: ends in <see cref="UpdatePhase.UpToDate"/>, <see cref="UpdatePhase.Available"/> or
    /// <see cref="UpdatePhase.Failed"/> with a reason. Downloads nothing. False (see <see cref="Refusal"/>) while a
    /// check or a download runs, a package waits to be applied or the installer has been started.
    /// </summary>
    public bool Check()
    {
        if (!Free()) return false;
        _retryAt = null;
        _retries = 0;
        StartCheck();
        return true;
    }

    /// <summary>
    /// Downloads the available release's package for this copy and verifies it: ends in <see cref="UpdatePhase.Ready"/>
    /// or <see cref="UpdatePhase.Failed"/>. A download stopped earlier goes on from where it stopped, and a package
    /// already verified is not fetched again. False when no release is known, when that release is left to the player
    /// (the status and <see cref="UpdateSnapshot.Page"/> say so) or at the times <see cref="Check"/> gives.
    /// </summary>
    public bool DownloadAndInstall()
    {
        if (!Free()) return false;
        if (_available is not { } release) return Refuse("no newer release is known: check first");
        if (DryRun) return Refuse("dry run (GRUDGELAP_UPDATE_DRYRUN): nothing is downloaded");
        _retryAt = null;
        _retries = 0;
        return StartDownload(release);
    }

    /// <summary>
    /// Hands the verified package to the installer or the unpack script, without waiting for it to start (Windows can
    /// hold a new executable for most of a minute first). <see cref="Poll"/> then reports
    /// <see cref="UpdatePhase.Starting"/> and, once it has stayed alive for <see cref="InstallerGrace"/> and shown that
    /// it runs, <see cref="UpdatePhase.QuitNow"/>; when it could not start or ended at once,
    /// <see cref="UpdatePhase.Failed"/> and the game stays. False unless a package is ready, or its last hand-over
    /// failed in this session.
    /// </summary>
    public bool Apply()
    {
        if (_ended) return Refuse("the session has ended");
        if (_phase is UpdatePhase.Starting or UpdatePhase.QuitNow) return Refuse("the installer has been started");
        var ready = _phase == UpdatePhase.Ready || _phase == UpdatePhase.Failed && _retry == UpdateRetry.Apply;
        if (!ready || _available is not { } release || _packagePath is null) return Refuse("no downloaded update is ready to install");
        StartInstaller(release, _packagePath);
        return true;
    }

    /// <summary>
    /// Stops the check or the download that runs or waits to be tried again (the player backed out), without waiting
    /// for it. The .part keeps what came: a later <see cref="DownloadAndInstall"/> goes on from there. An installer that
    /// has been started is not taken back.
    /// </summary>
    public void Cancel()
    {
        if (_ended || _phase is UpdatePhase.Starting or UpdatePhase.QuitNow) return;
        if (!Working && _retryAt is null) return;
        var download = _download is not null || _retryAt is not null && _retryOperation == Operation.Download;
        Stop();
        _bytesDone = 0;
        _bytesTotal = 0;
        Log(download ? "the download was cancelled; the .part keeps what came" : "the check was cancelled");
        Rest();
    }

    /// <summary>The game is closing: whatever runs in the background stops now, without being waited for.</summary>
    public void Shutdown()
    {
        if (_ended) return;
        _ended = true;
        if (_download is not null) Log("the game is closing: the download stops here; the .part keeps what came");
        if (_starting is { IsCompleted: false })
        {
            // Windows still holds the start (an antivirus reads a new installer through first), and the call ends with
            // this process: the installer may come of it or not. The next start finds out from the installer's log, and
            // must not count a start that never happened as an install that failed.
            Log("the game is closing while the installer starts: if Windows has not started it yet, nothing gets installed");
            var state = LoadState();
            if (state.Get("applying").Length > 0)
            {
                state.Set("applying_cut", 1);
                Save(state);
            }
        }
        var check = _check as Task ?? Task.CompletedTask;
        Stop();
        _installer?.Dispose();
        _installer = null;
        // The client goes once nothing uses it any more: a request cut off by its disposal would be logged as a failure.
        if (_ownsHttp)
        {
            Task.WhenAll(check, _stopped, _cleanup).ContinueWith(ended =>
            {
                _ = ended.Exception;
                _http.Dispose();
            }, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Moves the session on and says where it stands; the host calls it every frame. A finished check or download is
    /// acted on here, a started installer is watched, and a retry that is due starts.
    /// </summary>
    public UpdateSnapshot Poll()
    {
        if (!_ended)
        {
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
            if (_download is not null && _client is { } client) ShowProgress(client);
            if (_phase == UpdatePhase.Starting) WatchInstaller();
            if (_retryAt is { } due && _clock() >= due)
            {
                _retryAt = null;
                if (_retryOperation == Operation.Check) StartCheck();
                else if (_available is { } release) StartDownload(release);
            }
        }
        return new UpdateSnapshot(_phase, _installed, _retry, _bytesDone, _bytesTotal, _packageSize, _latest, _status, _page);
    }

    private void StartCheck()
    {
        Enter(UpdatePhase.Checking, "checking for updates...");
        var feed = _options.FeedTimeout is { } limit ? new UpdateFeed(_http, _endpoints, Log) { RequestTimeout = limit } : new UpdateFeed(_http, _endpoints, Log);
        var describe = !_environmentLogged;
        _environmentLogged = true;
        _cancel = new CancellationTokenSource();
        var ct = _cancel.Token;
        Log($"checking for updates ({(_retries > 0 ? $"retry {_retries}" : "asked for")})");
        _check = Task.Run(async () =>
        {
            // Off the host's thread: the proxy lookup may run the network's PAC script.
            if (describe) LogEnvironment();
            return await feed.FindLatestAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    private void OnChecked(Task<UpdateRelease> task)
    {
        if (task.IsCanceled)
        {
            Rest();
            return;
        }
        if (task.Exception is { } error)
        {
            var reason = Reason(error);
            Log($"the check failed: {reason}");
            FailWithRetry(Operation.Check, "update check failed: " + UpdateTexts.Fit(reason), error.InnerException);
            return;
        }
        var release = task.Result;
        Log($"latest release v{release.VersionText} ({release.Source}); this copy v{_currentText}");
        if (release.Version <= _current)
        {
            SetAvailable(null);
            Enter(UpdatePhase.UpToDate, $"up to date (v{_currentText})");
            return;
        }
        SetAvailable(release);
        LogPackage("installer", release.Setup);
        LogPackage("portable", release.Portable);
        Offer(release);
    }

    /// <summary>
    /// Where the session rests with a newer release known: offering it, or failed for good when the updater can do
    /// nothing with it by itself (it did not install twice, an antivirus took its download in this session, it has no
    /// package for this kind of copy), with the release's page for the host to open.
    /// </summary>
    private void Offer(UpdateRelease release)
    {
        var version = release.VersionText;
        if (DryRun) Enter(UpdatePhase.Available, $"v{version} available (dry run: nothing downloaded)");
        else if (_gaveUp == version) Enter(UpdatePhase.Failed, GaveUpText(version, _gaveUpReason), page: release.Page);
        else if (_blocked == version) Enter(UpdatePhase.Failed, "update failed: " + UpdateTexts.Fit(_blockedReason), page: release.Page);
        else if (PackageFor(release) is null) Enter(UpdatePhase.Failed, $"v{version} has no {(_installed ? "installer" : "portable zip")} for this copy", page: release.Page);
        else Enter(UpdatePhase.Available, $"v{version} available");
    }

    /// <summary>Where the session stands with nothing running: at the offer of the release it knows, or idle.</summary>
    private void Rest()
    {
        if (_available is { } release) Offer(release);
        else Enter(UpdatePhase.Idle, "");
    }

    private bool StartDownload(UpdateRelease release)
    {
        if (_gaveUp == release.VersionText)
        {
            // Twice the installer did not take: the player gets the page, where the installer can be run by hand.
            Log($"v{release.VersionText} is left to the player: {release.Page}");
            Offer(release);
            return Refuse($"v{release.VersionText} did not install twice: its page has the installer");
        }
        if (_blocked == release.VersionText)
        {
            // Another copy would only be taken again: the page instead, where the browser lets the player keep the file.
            Log($"v{release.VersionText} is not downloaded again this session ({_blockedReason}): {release.Page}");
            Offer(release);
            return Refuse($"v{release.VersionText} is not downloaded again this session: {_blockedReason}");
        }
        if (PackageFor(release) is not { } package)
        {
            Log($"v{release.VersionText} has no {(_installed ? "installer" : "portable zip")}");
            Offer(release);
            return Refuse($"v{release.VersionText} has no {(_installed ? "installer" : "portable zip")} for this copy");
        }
        // The portable copy also unpacks the zip beside it before copying: room for the package twice over.
        var options = _options.Client ?? new UpdateClientOptions { Reserve = _installed ? 64L << 20 : Math.Max(package.Size ?? 0, 600L << 20) };
        var client = new UpdateClient(_http, options, Log);
        _client = client;
        _shown = default;
        _bytesDone = 0;
        _bytesTotal = 0;
        Enter(UpdatePhase.Downloading, $"downloading v{release.VersionText}...");
        var folder = _downloadFolder;
        var gameDir = _rootDir;
        var installed = _installed;
        var cleanup = _cleanup;
        var stopped = _stopped;
        _cancel = new CancellationTokenSource();
        var ct = _cancel.Token;
        _download = Task.Run(async () =>
        {
            await cleanup.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await stopped.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            ct.ThrowIfCancellationRequested();
            foreach (var name in UpdateFolder.Clean(folder, keep: package.Name)) Log($"deleted {name} (left by an earlier update)");
            if (!installed && !UpdateDiagnostics.CanWrite(gameDir))
            {
                throw new UpdateException(UpdateFailure.Disk, "the game's folder cannot be written: move the portable copy to a folder of your own, or use the installer");
            }
            return await client.DownloadAsync(package, folder, ct).ConfigureAwait(false);
        }, ct);
        return true;
    }

    private void OnDownloaded(Task<string> task)
    {
        if (task.IsCanceled)
        {
            Rest();
            return;
        }
        if (task.Exception is { } error)
        {
            var reason = Reason(error);
            Log($"the download failed: {reason}");
            if (error.InnerException is UpdateException { Kind: UpdateFailure.Blocked } && _available is { } release)
            {
                _blocked = release.VersionText;
                _blockedReason = reason;
                Log($"v{release.VersionText} is not downloaded again this session; its page: {release.Page}");
                Offer(release);
                return;
            }
            FailWithRetry(Operation.Download, "update failed: " + UpdateTexts.Fit(reason), error.InnerException);
            return;
        }
        _packagePath = task.Result;
        if (_client is { } client)
        {
            _bytesDone = client.BytesDone;
            _bytesTotal = client.BytesTotal;
        }
        Enter(UpdatePhase.Ready, $"v{_latest} ready to install");
    }

    private void ShowProgress(UpdateClient client)
    {
        var done = client.BytesDone;
        var total = client.BytesTotal;
        var phase = client.Phase;
        var note = client.Note;
        // Verified and in place, the task about to end: the last line stays until Poll takes its result.
        if (phase == DownloadPhase.Done) return;
        _bytesDone = done;
        _bytesTotal = total;
        _phase = phase == DownloadPhase.Verifying ? UpdatePhase.Verifying : UpdatePhase.Downloading;
        // The line changes a few times a second at most; a host that polls every frame gets the same string until it does.
        var shown = (phase, Megabytes: done >> 20, Percent: total > 0 ? done * 100 / total : 0, total, note);
        if (shown == _shown) return;
        _shown = shown;
        var megabytes = shown.Megabytes.ToString(CultureInfo.InvariantCulture);
        var percent = shown.Percent.ToString(CultureInfo.InvariantCulture);
        _status = phase switch
        {
            DownloadPhase.Idle => $"downloading v{_latest}...",
            DownloadPhase.Waiting => $"downloading v{_latest}: {note}",
            DownloadPhase.Verifying => total > 0 ? $"verifying v{_latest} {percent}%" : $"verifying v{_latest}...",
            DownloadPhase.Connecting when done == 0 => $"downloading v{_latest}: connecting...",
            _ => total > 0 ? $"downloading v{_latest} {percent}% ({megabytes} of {(total >> 20).ToString(CultureInfo.InvariantCulture)} MB)" : $"downloading v{_latest} {megabytes} MB",
        };
    }

    /// <summary>
    /// A failure of the check or the download. One that a lost or silent connection caused is tried again in 30 s,
    /// then in 3 minutes (the .part keeps what came), still as the operation the player asked for: it keeps its phase
    /// and can be cancelled, or asked for again at once. Any other would end the same way, and in the download cost
    /// the whole package again, so it waits for the player.
    /// </summary>
    private void FailWithRetry(Operation operation, string text, Exception? error)
    {
        var again = operation == Operation.Check ? UpdateRetry.Check : UpdateRetry.Download;
        var helps = UpdateSchedule.RetryHelps(error);
        if (helps && UpdateSchedule.RetryDelay(_retries + 1) is { } delay)
        {
            _retries++;
            _retryAt = _clock() + delay;
            _retryOperation = operation;
            var when = delay.TotalSeconds < 90
                ? $"{delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s"
                : $"{delay.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)} min";
            Log($"trying again in {when}");
            Enter(operation == Operation.Check ? UpdatePhase.Checking : UpdatePhase.Downloading, $"{text} - trying again in {when}", again);
            return;
        }
        Log(helps ? "not tried again by itself: it has been tried three times" : "not tried again by itself: it would end the same way");
        Enter(UpdatePhase.Failed, text, again);
    }

    private void StartInstaller(UpdateRelease release, string package)
    {
        var folder = Path.GetDirectoryName(package)!;
        _installerKind = _installed ? "setup" : "portable";
        _installerLog = Path.Combine(folder, _installed ? $"setup-{release.VersionText}.log" : "apply.log");
        // Both through ShellExecute: .NET's own CreateProcess lets the child inherit every inheritable handle of the
        // game, and a script that held the game's files open could not copy over them.
        ProcessStartInfo start;
        if (_installed)
        {
            // The installer itself, by its full path. /UPDATE=1: it waits for the game to exit and starts it again.
            start = new ProcessStartInfo(package, UpdateCommands.InstallerArguments(_installerLog, relaunch: !Unattended, unattended: Unattended))
            {
                UseShellExecute = true,
                WorkingDirectory = folder,
            };
        }
        else
        {
            var script = Path.Combine(folder, "apply.ps1");
            try
            {
                File.WriteAllText(script, UpdateCommands.UnpackScript);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FailToStart(release, $"the unpack script could not be written ({ex.Message})");
                return;
            }
            // The script waits for this process (the game itself, whatever started it), unpacks over the root folder
            // and starts the launcher: in the Unreal package the running executable sits two folders below the root.
            var args = UpdateCommands.UnpackArguments(script, Environment.ProcessId, package, _rootDir, _launcher, release.VersionText, relaunch: !Unattended);
            start = new ProcessStartInfo(UpdateCommands.PowerShell, UpdateCommands.CommandLine(args))
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = folder,
            };
        }
        // Written before the start: when the game next opens on the old version, it knows what was tried and where its log is.
        var state = LoadState();
        state.Set("applying", release.VersionText);
        state.Set("applying_from", _currentText);
        state.Set("applying_kind", _installerKind);
        state.Set("applying_log", _installerLog);
        state.Set("applying_package", package);
        state.Set("applying_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Save(state);
        // A log left by an earlier try would speak for an installer that never ran; the review reads only this try's.
        try
        {
            File.Delete(_installerLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"the old log {_installerLog} could not be cleared: {ex.Message}");
        }
        Log($"starting {(_installed ? "the installer" : "the unpack script")}: \"{start.FileName}\" {start.Arguments}");
        // Off the host's thread: Windows hands a new executable to the antivirus before it lets it run, and the call
        // does not return until that has read all of it (0.78's installer of 1.1 GB: 29 and 46 s on the development
        // laptop). On the game's thread that was a frozen picture and, to Windows, a program that no longer responds.
        var starter = _options.StartProcess ?? Process.Start;
        var clock = _clock;
        _starting = Task.Run(() => (starter(start), clock()));
        _startingPackage = package;
        _installer?.Dispose();
        _installer = null;
        _installerStartedAt = _clock();
        Enter(UpdatePhase.Starting, _installed ? "starting the installer (an antivirus may check it first)..." : "starting the update...");
    }

    /// <summary>
    /// The hand-over's first moments. A start that fails, or an installer that ends at once, failed: the game stays.
    /// One that keeps running gets the game out of its way once it has shown that it runs (its log is there), or
    /// <see cref="InstallerSignWait"/> has passed without that.
    /// </summary>
    private void WatchInstaller()
    {
        if (_starting is { } starting)
        {
            if (!starting.IsCompleted) return;
            _starting = null;
            if (starting.IsFaulted)
            {
                // Win32Exception: a file an antivirus has blocked ("contains a virus or potentially unwanted software") or
                // removed under our feet, access denied. The game stays, and says why, naming the antivirus it can.
                var ex = starting.Exception!.GetBaseException();
                var antivirus = (_options.Client?.Antivirus ?? UpdateDiagnostics.ActiveAntivirus)();
                FailToStart(_available!, _installed
                    ? UpdateTexts.StartFailure(ex, Path.GetFileName(_startingPackage), antivirus)
                    : $"the unpack script could not start ({ex.Message.TrimEnd('.')})");
                return;
            }
            var asked = _installerStartedAt;
            (_installer, _installerStartedAt) = starting.Result;
            Log($"started after {(_installerStartedAt - asked).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
            _status = "installing update...";
        }
        if (_installer is { } installer)
        {
            bool exited;
            try
            {
                exited = installer.HasExited;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                exited = false;   // no handle to watch, or Windows will not say: it is trusted to run
            }
            if (exited)
            {
                var reason = InstallerOutcome.ExitReason(_installerKind, installer.ExitCode);
                var logged = InstallerOutcome.Reason(_installerKind, _installerLog);
                LogTail(_installerLog);
                FailToStart(_available!, logged.Contains("left no log", StringComparison.Ordinal) ? reason : $"{reason}: {logged}");
                return;
            }
        }
        var running = _clock() - _installerStartedAt;
        if (running < InstallerGrace) return;
        // Its log is the sign that it runs: the old one was deleted before the start, so one that is there is this try's.
        var signed = File.Exists(_installerLog);
        if (!signed && running < (_options.InstallerSignWait ?? InstallerSignWait)) return;
        Log(signed
            ? "the installer is running: the game quits so that it can be replaced"
            : $"the installer has written no log in {running.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s: the game quits for it all the same");
        Enter(UpdatePhase.QuitNow, "installing update...");
    }

    /// <summary>A hand-over that failed while the game still runs: it counts as a failed install of that version.</summary>
    private void FailToStart(UpdateRelease release, string reason)
    {
        Log($"the update could not be handed over: {reason}");
        _installer?.Dispose();
        _installer = null;
        var state = LoadState();
        ClearApplying(state);
        var count = RecordFailure(state, release.VersionText, reason);
        Save(state);
        if (count >= MaxApplyFailures)
        {
            _gaveUp = release.VersionText;
            _gaveUpReason = reason;
            Offer(release);
        }
        else
        {
            // The verified package stays: asking again, in this session or the next, tries without a download. Only the
            // player asks: a host that applies whatever is ready would otherwise try a failing installer every frame.
            Enter(UpdatePhase.Failed, $"update failed: {UpdateTexts.Fit(reason)}", UpdateRetry.Apply);
        }
    }

    /// <summary>
    /// The verdict on the update the last session handed over: this start's version against the one it was going to.
    /// A failure is counted (twice and the updater stops applying that version) and its reason, read from the
    /// installer's log, becomes the status. Two hand-overs are not failures: one that is still at work (the session
    /// starts in <see cref="UpdatePhase.QuitNow"/>, with the record untouched) and one whose start the game's closing
    /// cut off. True when the update took: the download folder can then be emptied.
    /// </summary>
    private bool ReviewLastApply()
    {
        var state = LoadState();
        var applying = state.Get("applying");
        var failedVersion = state.Get("failed_version");
        var failedCount = state.GetInt("failed_count");
        var failedReason = state.Get("failed_reason");
        var updated = false;
        var changed = false;
        if (applying.Length > 0)
        {
            var from = state.Get("applying_from", "?");
            var kind = state.Get("applying_kind", "setup");
            var log = state.Get("applying_log");
            LogTail(log);
            if (ReleaseVersion.TryParse(applying, out var target) && _current >= target)
            {
                Log($"updated from v{from} to v{_currentText}");
                Enter(UpdatePhase.Idle, $"updated to v{_currentText}");
                ClearFailures(state);
                failedCount = 0;
                updated = true;
            }
            else if (StillAtWork(kind, log, state.Get("applying_at")))
            {
                // The installer or the unpack script is at work on this folder right now: the player started the game
                // again before it had finished (the script unpacks a gigabyte first and shows nothing; the installer
                // copies for half a minute) and got the version that is being replaced. A copy over a running game stops
                // half way and leaves two versions mixed, so this one makes way at once, and whatever is at work starts
                // the new one. Nothing is recorded: the start after that gives the verdict.
                Log($"the update to v{applying} is still being installed ({kind}): the game makes way for it");
                _latest = applying;
                Enter(UpdatePhase.QuitNow, "installing update...");
                return false;
            }
            else if (state.Get("applying_cut").Length > 0 && !File.Exists(log))
            {
                // The game was closed while Windows still held the installer's start (Shutdown), and no log says that it
                // ever ran: nothing was installed and nothing failed. The verified package is there for the next try.
                Log($"the update to v{applying} was never started: the game was closed while the installer was starting");
                Enter(UpdatePhase.Idle, $"the update to v{applying} was not installed: the game was closed while it was starting");
            }
            else
            {
                failedReason = InstallerOutcome.Reason(kind, log);
                failedCount = RecordFailure(state, applying, failedReason);
                failedVersion = applying;
                Log($"the update to v{applying} did not take ({failedCount} of {MaxApplyFailures} tries): {failedReason}");
                if (failedCount < MaxApplyFailures) Enter(UpdatePhase.Idle, $"the update to v{applying} did not install: {UpdateTexts.Fit(failedReason)}");
            }
            ClearApplying(state);
            changed = true;
        }
        else if (failedVersion.Length > 0 && (!ReleaseVersion.TryParse(failedVersion, out var stuck) || stuck <= _current))
        {
            // The version that would not install is here after all (installed by hand): the record is done with.
            ClearFailures(state);
            failedCount = 0;
            changed = true;
        }
        if (failedCount >= MaxApplyFailures)
        {
            _gaveUp = failedVersion;
            _gaveUpReason = failedReason;
            // No check has named the release yet, but its page follows from its version (tools/release.ps1 tags v<version>).
            // What the host offers from here is a check, not that page: a newer release may have come since, and a start
            // that offered only the page left a copy that could never learn of it (a check that still finds this version
            // ends at the page, through Offer).
            Enter(UpdatePhase.Failed, GaveUpText(failedVersion, failedReason), UpdateRetry.Check, page: _endpoints.Page("v" + failedVersion));
        }
        if (changed) Save(state);
        return updated;
    }

    /// <summary>
    /// Whether the hand-over on record is still running: its log has begun and not ended, and it was handed over less
    /// than <see cref="ApplyingWindow"/> ago by this computer's clock (an older log without an end is a run that died).
    /// </summary>
    private static bool StillAtWork(string kind, string log, string handedOverAt)
    {
        if (!long.TryParse(handedOverAt, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds;
        return age >= 0 && age < (long)ApplyingWindow.TotalSeconds && InstallerOutcome.Unfinished(kind, log);
    }

    private static int RecordFailure(UpdateState state, string version, string reason)
    {
        var count = state.Get("failed_version") == version ? state.GetInt("failed_count") + 1 : 1;
        state.Set("failed_version", version);
        state.Set("failed_count", count);
        state.Set("failed_reason", reason);
        return count;
    }

    private static void ClearFailures(UpdateState state)
    {
        foreach (var key in FailureKeys) state.Remove(key);
    }

    private static void ClearApplying(UpdateState state)
    {
        foreach (var key in ApplyingKeys) state.Remove(key);
    }

    private UpdateState LoadState()
    {
        var state = UpdateState.Load(_statePath);
        if (state.LoadError is { } error) Log($"{StateFileName} could not be read ({error}): starting without it");
        return state;
    }

    /// <summary>A state that cannot be written costs the next start its verdict on this hand-over, not the update itself.</summary>
    private void Save(UpdateState state)
    {
        if (state.Save() is { } error) Log($"{StateFileName} could not be written: {error}");
    }

    /// <summary>The package this copy takes from a release: the installer for an installed copy, the zip for a portable one.</summary>
    private UpdatePackage? PackageFor(UpdateRelease release) => _installed ? release.Setup : release.Portable;

    private void SetAvailable(UpdateRelease? release)
    {
        // A package downloaded for another release is not this one's to apply.
        if (release?.VersionText != _latest) _packagePath = null;
        _available = release;
        _latest = release?.VersionText ?? "";
        _packageSize = release is null ? 0 : PackageFor(release)?.Size ?? 0;
    }

    private void Enter(UpdatePhase phase, string status, UpdateRetry retry = UpdateRetry.None, Uri? page = null)
    {
        _phase = phase;
        _status = status;
        _retry = retry;
        _page = page?.AbsoluteUri ?? "";
    }

    /// <summary>Whether a new check or download can start now; otherwise <see cref="Refusal"/> says what is in its way.</summary>
    private bool Free()
    {
        if (_ended) return Refuse("the session has ended");
        if (_check is not null) return Refuse("a check is running");
        if (_download is not null) return Refuse("a download is running");
        if (_phase == UpdatePhase.Ready) return Refuse($"v{_latest} is downloaded and ready to install");
        if (_phase is UpdatePhase.Starting or UpdatePhase.QuitNow) return Refuse("the installer has been started");
        return true;
    }

    private bool Refuse(string why)
    {
        Refusal = why;
        return false;
    }

    /// <summary>Cancels what runs or waits, without waiting for it; a cancelled download is remembered until it has let go of its .part.</summary>
    private void Stop()
    {
        _cancel?.Cancel();
        _cancel = null;
        if (_download is { } download) _stopped = download;
        _check = null;
        _download = null;
        _client = null;
        _retryAt = null;
    }

    private static string GaveUpText(string version, string reason) => reason.Length == 0
        ? $"v{version} did not install twice"
        : $"v{version} did not install twice ({UpdateTexts.Fit(reason)})";

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
        var proxy = !_ownsHttp ? "as the client it was given has it" : _endpoints.Web.IsLoopback ? "none (the feed is on this computer)" : UpdateDiagnostics.Proxy(_endpoints.Web);
        Log($"download folder {_downloadFolder}; free space {UpdateDiagnostics.FreeSpace(_downloadFolder)} (downloads), {UpdateDiagnostics.FreeSpace(_rootDir)} (game)");
        Log($"proxy for {_endpoints.Web.Host}: {proxy}; antivirus: {UpdateDiagnostics.Antivirus()}");
        Log($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");
    }

    private void LogPackage(string what, UpdatePackage? package)
    {
        if (package is null) Log($"  {what}: none in this release");
        else Log($"  {what}: {package.Name}, {(package.Size is { } size ? UpdateHttp.Megabytes(size) : "size unknown")}, SHA-256 {(package.Sha256 ?? (package.ShaUrl is null ? "none" : "from its .sha256 file"))}");
    }

    /// <summary>The failure's reason for the screen, the log and update-state.ini; an unexpected exception's own words could quote a proxy's address with its sign-in.</summary>
    private static string Reason(AggregateException error) => UpdateLog.Redact(error.InnerException switch
    {
        UpdateException update => update.Message,
        { } other => $"{other.GetType().Name}: {other.Message}",
        null => error.Message,
    });

    private static string FullPath(string path, string what)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"{what} is empty");
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"{what} \"{path}\" is not a path ({ex.Message})", ex);
        }
    }

    /// <summary>A line for update.log from the session's host (a failure of the interface around it, which the player's log should show too).</summary>
    internal void Note(string line) => Log(line);

    private void Log(string line) => _log.Write(line);
}
