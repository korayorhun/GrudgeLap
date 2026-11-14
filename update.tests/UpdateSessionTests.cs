using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using GrudgeLap.Game;
using GrudgeLap.Update.Native;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Update.Tests;

/// <summary>
/// The session the Unreal version drives through the C interface (update.native), against the fake GitHub. The
/// owner's rule first: a check downloads nothing, and nothing starts that the player did not ask for. Then what the
/// Godot version's Updater node did and no test could reach: the download handed over, the verdict on it at the next
/// start, the give-up after two failed installs, and what a cancelled or dropped download costs (nothing that came).
/// No installer runs here: the session's process start is replaced, and the command line it was given is read.
/// </summary>
public sealed class UpdateSessionTests(ITestOutputHelper output) : IDisposable
{
    private const string SetupName = "GrudgeLap-Setup-0.79.0.exe";
    private const string PortableName = "GrudgeLap-Portable-0.79.0.zip";

    /// <summary>The game's settings with the waits shortened: silence after 1.5 s, retries after 50-100 ms.</summary>
    private static readonly UpdateClientOptions Fast = new()
    {
        Inactivity = TimeSpan.FromSeconds(1.5),
        Backoff = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)],
        AttemptsWithoutProgress = 3,
        BufferSize = 64 * 1024,
        Reserve = 0,
        Antivirus = () => "Test Antivirus",
    };

    private readonly string _folder = Directory.CreateTempSubdirectory("grudgelap-session-tests-").FullName;
    private readonly HttpClient _http = UpdateHttp.CreateClient("test", useProxy: false);
    private readonly List<UpdateSession> _sessions = [];
    private readonly List<ProcessStartInfo> _starts = [];
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    // What the tests skip of the session's waits (30 s before a retry, the installer's first moments).
    private TimeSpan _skipped;

    private string GameDir => Path.Combine(_folder, "Games and Toys", "GrudgeLap");
    private string Launcher => Path.Combine(GameDir, "GrudgeLap.exe");
    private string DataDir => Path.Combine(_folder, "data");
    // With a space in it, as a Windows user's name can put one in %TEMP%: every path on a command line is then quoted.
    private string Downloads => Path.Combine(_folder, "Grudge Lap-Update");
    private string StatePath => Path.Combine(DataDir, UpdateSession.StateFileName);

    public void Dispose()
    {
        foreach (var session in _sessions) session.Shutdown();
        // The session's own log, as update.log has it: what a failing run did.
        foreach (var line in LogLines()) output.WriteLine(line);
        _http.Dispose();
        // A download that was cancelled a moment ago may still be letting go of its .part.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 30) return;   // left for the system's temp cleaning
                Thread.Sleep(100);
            }
        }
    }

    private static byte[] Payload(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>A game release as tools/release.ps1 publishes it: both packages, their .sha256 files and update.json.</summary>
    private static (FakeRelease Release, FakeAsset Setup, FakeAsset Portable) Publish(FakeGitHub server, string tag = "v0.79.0", int setupSize = 1_200_000, bool setup = true, bool portable = true)
    {
        var version = tag.TrimStart('v');
        var release = server.AddRelease(tag);
        var setupAsset = setup ? release.Add($"GrudgeLap-Setup-{version}.exe", Payload(setupSize, 1)) : new FakeAsset([]);
        var portableAsset = portable ? release.Add($"GrudgeLap-Portable-{version}.zip", Payload(700_000, 2)) : new FakeAsset([]);
        release.AddManifest();
        return (release, setupAsset, portableAsset);
    }

    /// <summary>
    /// A session for a copy in the test's own folders. Nothing of it touches the computer's: the download folder and
    /// the data folder are the test's, the copy's kind is given (the registry is not asked), the environment is empty
    /// unless <paramref name="environment"/> says otherwise, and a process the session starts is only written down.
    /// </summary>
    private UpdateSession Session(FakeGitHub? server, string version = "0.78.0", bool installed = true, UpdateClientOptions? client = null,
        Func<ProcessStartInfo, Process?>? start = null, Func<string, string?>? environment = null, bool feedFromEnvironment = false,
        TimeSpan? signWait = null)
    {
        Directory.CreateDirectory(GameDir);
        var session = new UpdateSession(new UpdateHost(version, Launcher, GameDir, DataDir), new UpdateSessionOptions
        {
            Http = _http,
            // Without a server: a port nothing listens on.
            Endpoints = feedFromEnvironment ? null : server?.Endpoints ?? UpdateEndpoints.ForFeed(new Uri("http://127.0.0.1:1/"), "korayorhun", "GrudgeLap"),
            DownloadFolder = Downloads,
            Client = client ?? Fast,
            InstalledCopy = installed,
            // The stand-in installer writes no log: unless a test is about that sign of life, it is not waited for.
            InstallerSignWait = signWait ?? TimeSpan.Zero,
            FeedTimeout = TimeSpan.FromSeconds(10),
            Clock = () => _watch.Elapsed + _skipped,
            StartProcess = info =>
            {
                // Called on a thread of the pool: the tests read the list after the hand-over's task has ended.
                lock (_starts) _starts.Add(info);
                // By default an installer that keeps running: this process, which the session only looks at.
                return start is null ? Process.GetCurrentProcess() : start(info);
            },
            EnvironmentVariable = environment ?? (_ => null),
        });
        _sessions.Add(session);
        return session;
    }

    /// <summary>Polls as a host does every frame until <paramref name="reached"/>; <paramref name="each"/> sees every snapshot on the way.</summary>
    private static async Task<UpdateSnapshot> PollUntilAsync(UpdateSession session, Func<UpdateSnapshot, bool> reached, Action<UpdateSnapshot>? each = null, bool tight = false)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var state = session.Poll();
            each?.Invoke(state);
            if (reached(state)) return state;
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(20), $"waited 20 s; the session is {state.Phase}: {state.Status}");
            if (tight) await Task.Yield();
            else await Task.Delay(5);
        }
    }

    /// <summary>Polls for a while as a host does, for what must not happen meanwhile.</summary>
    private static async Task PollForAsync(UpdateSession session, int milliseconds)
    {
        var waited = Stopwatch.StartNew();
        while (waited.ElapsedMilliseconds < milliseconds)
        {
            session.Poll();
            await Task.Delay(5);
        }
    }

    private static async Task<UpdateSnapshot> CheckAsync(UpdateSession session)
    {
        Assert.True(session.Check(), session.Refusal);
        Assert.Equal(UpdatePhase.Checking, session.Phase);
        return await PollUntilAsync(session, s => s.Phase != UpdatePhase.Checking);
    }

    private static async Task<UpdateSnapshot> DownloadAsync(UpdateSession session)
    {
        Assert.True(session.DownloadAndInstall(), session.Refusal);
        return await PollUntilAsync(session, s => s.Phase is not (UpdatePhase.Downloading or UpdatePhase.Verifying));
    }

    private static List<FakeGitHub.Hit> PackageHits(FakeGitHub server) =>
        server.Hits.Where(h => h.Path.Contains("GrudgeLap-Setup-", StringComparison.Ordinal) || h.Path.Contains("GrudgeLap-Portable-", StringComparison.Ordinal)).ToList();

    private static List<FakeGitHub.Hit> BlobHits(FakeGitHub server, string name) =>
        server.Hits.Where(h => h.Path.StartsWith("/blob/", StringComparison.Ordinal) && h.Path.EndsWith("/" + name, StringComparison.Ordinal)).ToList();

    private static int ManifestRequests(FakeGitHub server) => server.Hits.Count(h => h.Path.EndsWith("/releases/latest/download/update.json", StringComparison.Ordinal));

    private List<string> LogLines()
    {
        var path = Path.Combine(DataDir, UpdateSession.LogFileName);
        if (!File.Exists(path)) return [];
        // The session may be writing a line this moment.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>update-state.ini read by the tests' own few lines, not by the code they test.</summary>
    private Dictionary<string, string> State() => !File.Exists(StatePath)
        ? []
        : File.ReadAllLines(StatePath).Where(l => l.Contains('=')).ToDictionary(l => l[..l.IndexOf('=')], l => l[(l.IndexOf('=') + 1)..]);

    private string[] DownloadedFiles() => Directory.Exists(Downloads)
        ? Directory.GetFiles(Downloads).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray()
        : [];

    [Fact]
    public async Task A_check_finds_a_newer_release_and_downloads_nothing()
    {
        await using var server = new FakeGitHub();
        var (release, setup, portable) = Publish(server);
        var session = Session(server);

        // Started and polled like a game on its title screen: the updater asks nobody anything by itself.
        await PollForAsync(session, 300);
        Assert.Equal(UpdatePhase.Idle, session.Phase);
        Assert.Empty(server.Hits);

        var found = await CheckAsync(session);
        await PollForAsync(session, 500);
        var later = session.Poll();

        Assert.Equal(UpdatePhase.Available, found.Phase);
        Assert.Equal("0.79.0", found.Latest);
        Assert.Equal("v0.79.0 available", found.Status);
        Assert.Equal(setup.Content.Length, found.PackageSize);
        Assert.Equal(UpdateRetry.None, found.Retry);
        Assert.Equal("", found.Page);
        Assert.True(found.InstalledCopy);
        Assert.Equal(found, later);
        // The release's description was read (update.json, through github.com's redirects), and that is all: no
        // package, no checksum file, no API call, nothing on disk.
        Assert.All(server.Hits, h => Assert.EndsWith("/update.json", h.Path));
        Assert.Empty(PackageHits(server));
        Assert.Equal(0, setup.Requests);
        Assert.Equal(0, portable.Requests);
        Assert.Equal(0, server.ApiCalls);
        Assert.Empty(DownloadedFiles());
        Assert.False(File.Exists(StatePath));
        Assert.Empty(_starts);
        Assert.Equal(release.Tag, session.Available!.Tag);
    }

    [Fact]
    public async Task Downloads_and_verifies_when_asked_and_ends_ready()
    {
        await using var server = new FakeGitHub();
        // Large enough, in small enough pieces, for a host that polls every frame to see each step.
        var (_, setup, portable) = Publish(server, setupSize: 8_000_000);
        var session = Session(server, client: Fast with { BufferSize = 8 * 1024 });
        await CheckAsync(session);
        var phases = new List<UpdatePhase>();
        var lines = new HashSet<string>();

        Assert.True(session.DownloadAndInstall(), session.Refusal);
        var ready = await PollUntilAsync(session, s => s.Phase is not (UpdatePhase.Downloading or UpdatePhase.Verifying), s =>
        {
            if (phases.Count == 0 || phases[^1] != s.Phase) phases.Add(s.Phase);
            lines.Add(s.Status);
        }, tight: true);

        Assert.Equal(UpdatePhase.Ready, ready.Phase);
        Assert.Equal([UpdatePhase.Downloading, UpdatePhase.Verifying, UpdatePhase.Ready], phases);
        Assert.Equal("v0.79.0 ready to install", ready.Status);
        Assert.Equal(setup.Content.Length, ready.BytesTotal);
        Assert.Equal(ready.BytesTotal, ready.BytesDone);
        Assert.Contains(lines, l => Regex.IsMatch(l, @"^downloading v0\.79\.0 \d+% \(\d of 7 MB\)$"));
        Assert.Contains(lines, l => Regex.IsMatch(l, @"^verifying v0\.79\.0 \d+%$"));
        // An installed copy takes the installer and never asks for the zip; the file is the release's, whole.
        Assert.Equal([SetupName], DownloadedFiles());
        Assert.Equal(setup.Content, await File.ReadAllBytesAsync(Path.Combine(Downloads, SetupName)));
        Assert.Single(BlobHits(server, SetupName));
        Assert.Equal(0, portable.Requests);
        // Ready is as far as it goes by itself: the host hands it over when the player is on the menu.
        await PollForAsync(session, 300);
        Assert.Equal(UpdatePhase.Ready, session.Phase);
        Assert.Empty(_starts);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task Resumes_a_download_whose_connection_dropped()
    {
        await using var server = new FakeGitHub();
        var (_, setup, _) = Publish(server, setupSize: 2_000_000);
        setup.DropAt = 0.4;
        var session = Session(server);
        await CheckAsync(session);
        var lines = new HashSet<string>();

        Assert.True(session.DownloadAndInstall(), session.Refusal);
        var ready = await PollUntilAsync(session, s => s.Phase is not (UpdatePhase.Downloading or UpdatePhase.Verifying), s => lines.Add(s.Status), tight: true);

        Assert.Equal(UpdatePhase.Ready, ready.Phase);
        // The player was told, and the download went on by itself.
        Assert.Contains(lines, l => Regex.IsMatch(l, @"^downloading v0\.79\.0: connection lost at \d+% - trying again in 1 s$"));
        Assert.Equal(setup.Content, await File.ReadAllBytesAsync(Path.Combine(Downloads, SetupName)));
        var blobs = BlobHits(server, SetupName);
        Assert.Equal(2, blobs.Count);
        Assert.Null(blobs[0].Range);
        // The second request asks for the rest, not the whole package again.
        Assert.InRange(long.Parse(blobs[1].Range!["bytes=".Length..].TrimEnd('-')), 1, setup.Content.Length - 1);
        Assert.Contains(LogLines(), l => l.Contains($"resuming {SetupName} at", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Says_up_to_date_when_there_is_nothing_newer()
    {
        await using var server = new FakeGitHub();
        Publish(server, "v0.78.0");
        var session = Session(server, version: "0.78.0");

        var state = await CheckAsync(session);

        Assert.Equal(UpdatePhase.UpToDate, state.Phase);
        Assert.Equal("up to date (v0.78.0)", state.Status);
        Assert.Equal("", state.Latest);
        Assert.Equal(0, state.PackageSize);
        Assert.Null(session.Available);
        Assert.False(session.DownloadAndInstall());
        Assert.Equal("no newer release is known: check first", session.Refusal);
        Assert.False(session.Apply());
        Assert.Empty(PackageHits(server));
        // And a copy that is ahead of the releases (a build of the next version) is up to date as well.
        var ahead = await CheckAsync(Session(server, version: "0.80.0"));
        Assert.Equal("up to date (v0.80.0)", ahead.Status);
    }

    [Fact]
    public async Task A_failed_check_says_why_and_can_be_asked_again()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        server.SignInPage = true;
        var session = Session(server);

        var failed = await CheckAsync(session);

        Assert.Equal(UpdatePhase.Failed, failed.Phase);
        Assert.Equal("update check failed: update.json is not JSON (a sign-in page of a hotspot or a proxy?)", failed.Status);
        Assert.Equal(UpdateRetry.Check, failed.Retry);
        Assert.Equal("", failed.Page);
        // Waiting would not help against a sign-in page: nothing is tried again until the player asks.
        var asked = server.Hits.Count;
        _skipped += TimeSpan.FromMinutes(10);
        await PollForAsync(session, 300);
        Assert.Equal(asked, server.Hits.Count);
        Assert.Contains(LogLines(), l => l.EndsWith("not tried again by itself: it would end the same way", StringComparison.Ordinal));

        server.SignInPage = false;
        var found = await CheckAsync(session);

        Assert.Equal(UpdatePhase.Available, found.Phase);
        Assert.Equal("v0.79.0 available", found.Status);
    }

    [Fact]
    public async Task A_check_that_lost_its_connection_is_tried_again_by_itself()
    {
        await using var server = new FakeGitHub();
        var (_, setup, _) = Publish(server);
        // github.com answers 503 and the API has no calls left: a passing trouble.
        server.WebDown = true;
        server.ApiRateLimited = true;
        var session = Session(server);

        Assert.True(session.Check());
        var waiting = await PollUntilAsync(session, s => s.Retry != UpdateRetry.None);

        // Still the check the player asked for: it says why it failed and when it tries again, and can be cancelled.
        Assert.Equal(UpdatePhase.Checking, waiting.Phase);
        Assert.Matches(@"^update check failed: GitHub's limit of update checks for this connection is used up \(until \d\d:\d\d\) - trying again in 30 s$", waiting.Status);
        Assert.Equal(UpdateRetry.Check, waiting.Retry);
        var asked = server.Hits.Count;
        _skipped += TimeSpan.FromSeconds(25);
        await PollForAsync(session, 300);
        Assert.Equal(asked, server.Hits.Count);

        // 30 s on, the trouble has passed.
        server.WebDown = false;
        server.ApiRateLimited = false;
        _skipped += TimeSpan.FromSeconds(6);
        var found = await PollUntilAsync(session, s => s.Phase != UpdatePhase.Checking);

        Assert.Equal(UpdatePhase.Available, found.Phase);
        Assert.Contains(LogLines(), l => l.EndsWith("checking for updates (retry 1)", StringComparison.Ordinal));
        // The retry was the check's: it found the release and, like the check, downloaded nothing.
        await PollForAsync(session, 300);
        Assert.Equal(0, setup.Requests);
        Assert.Empty(PackageHits(server));
    }

    [Fact]
    public async Task Gives_a_check_up_after_three_tries_and_takes_a_new_request_at_any_time()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        // github.com answers 503 and the API has no calls left, and it stays that way.
        server.WebDown = true;
        server.ApiRateLimited = true;
        var session = Session(server);
        int Tries() => LogLines().Count(l => l.Contains(" checking for updates (", StringComparison.Ordinal));

        Assert.True(session.Check());
        var first = await PollUntilAsync(session, s => s.Retry != UpdateRetry.None);
        Assert.EndsWith(" - trying again in 30 s", first.Status);
        _skipped += TimeSpan.FromSeconds(31);
        var second = await PollUntilAsync(session, s => s.Status.EndsWith("3 min", StringComparison.Ordinal));
        Assert.Equal(UpdatePhase.Checking, second.Phase);
        Assert.EndsWith(" - trying again in 3 min", second.Status);
        _skipped += TimeSpan.FromSeconds(181);
        var last = await PollUntilAsync(session, s => s.Phase == UpdatePhase.Failed);

        // Three tries in all, and then it is the player's turn.
        Assert.Matches(@"^update check failed: GitHub's limit of update checks for this connection is used up \(until \d\d:\d\d\)$", last.Status);
        Assert.Equal(UpdateRetry.Check, last.Retry);
        Assert.Equal(3, Tries());
        _skipped += TimeSpan.FromHours(1);
        await PollForAsync(session, 200);
        Assert.Equal(UpdatePhase.Failed, session.Phase);
        Assert.Equal(3, Tries());

        // Asked again, it starts over, and while it waits for its next try the player's request starts one at once.
        Assert.True(session.Check());
        await PollUntilAsync(session, s => s.Retry != UpdateRetry.None);
        Assert.True(session.Check());
        Assert.Equal("checking for updates...", session.Status);
        var again = await PollUntilAsync(session, s => s.Retry != UpdateRetry.None);
        Assert.EndsWith(" - trying again in 30 s", again.Status);
        Assert.Equal(5, Tries());

        // And backing out of the wait ends it: nothing is tried later.
        session.Cancel();
        Assert.Equal(UpdatePhase.Idle, session.Phase);
        Assert.Equal("", session.Status);
        _skipped += TimeSpan.FromHours(1);
        await PollForAsync(session, 200);
        Assert.Equal(5, Tries());
        Assert.Equal(UpdatePhase.Idle, session.Phase);
    }

    [Fact]
    public async Task A_download_that_lost_its_connection_is_tried_again_without_a_new_check()
    {
        await using var server = new FakeGitHub();
        var (_, setup, _) = Publish(server);
        var session = Session(server);
        await CheckAsync(session);
        var checks = ManifestRequests(server);
        setup.FailWith = 503;

        Assert.True(session.DownloadAndInstall(), session.Refusal);
        var waiting = await PollUntilAsync(session, s => s.Retry != UpdateRetry.None);

        // The client's own attempts are used up; the session keeps the download going, as the player asked.
        Assert.Equal(UpdatePhase.Downloading, waiting.Phase);
        Assert.Equal("update failed: 127.0.0.1 answered HTTP 503 - trying again in 30 s", waiting.Status);
        Assert.Equal(UpdateRetry.Download, waiting.Retry);
        Assert.Equal(Fast.AttemptsWithoutProgress, server.Hits.Count(h => h.Status == 503));

        setup.FailWith = null;
        _skipped += TimeSpan.FromSeconds(31);
        var ready = await PollUntilAsync(session, s => s.Phase is UpdatePhase.Ready or UpdatePhase.Failed);

        Assert.Equal(UpdatePhase.Ready, ready.Phase);
        Assert.Equal(setup.Content, await File.ReadAllBytesAsync(Path.Combine(Downloads, SetupName)));
        Assert.Equal(checks, ManifestRequests(server));
    }

    [Fact]
    public async Task Cancelling_a_download_keeps_what_came_and_the_next_one_goes_on_from_there()
    {
        await using var server = new FakeGitHub();
        var (_, setup, _) = Publish(server, setupSize: 2_000_000);
        // Five seconds for the package: time to back out part-way.
        setup.BytesPerSecond = 400_000;
        var session = Session(server);
        await CheckAsync(session);
        var part = Path.Combine(Downloads, SetupName + ".part");

        Assert.True(session.DownloadAndInstall(), session.Refusal);
        // While it runs nothing else is taken.
        Assert.False(session.Check());
        Assert.Equal("a download is running", session.Refusal);
        Assert.False(session.DownloadAndInstall());
        Assert.False(session.Apply());
        var running = await PollUntilAsync(session, s => s.BytesDone >= 300_000);
        Assert.Equal(UpdatePhase.Downloading, running.Phase);
        Assert.Equal(setup.Content.Length, running.BytesTotal);
        var watch = Stopwatch.StartNew();
        session.Cancel();

        // At once, and back to the offer: the release is still known.
        Assert.True(watch.ElapsedMilliseconds < 500, $"cancel took {watch.ElapsedMilliseconds} ms");
        var offered = session.Poll();
        Assert.Equal(UpdatePhase.Available, offered.Phase);
        Assert.Equal("v0.79.0 available", offered.Status);
        Assert.Equal(0, offered.BytesDone);
        await PollForAsync(session, 400);
        Assert.Equal(UpdatePhase.Available, session.Phase);
        Assert.True(File.Exists(part));
        Assert.False(File.Exists(Path.Combine(Downloads, SetupName)));
        Assert.Single(BlobHits(server, SetupName));

        setup.BytesPerSecond = 0;
        var ready = await DownloadAsync(session);

        Assert.Equal(UpdatePhase.Ready, ready.Phase);
        Assert.Equal(setup.Content, await File.ReadAllBytesAsync(Path.Combine(Downloads, SetupName)));
        var blobs = BlobHits(server, SetupName);
        Assert.Equal(2, blobs.Count);
        // The second download asked only for what the first had not brought.
        Assert.Equal(206, blobs[1].Status);
        Assert.InRange(long.Parse(blobs[1].Range!["bytes=".Length..].TrimEnd('-')), 300_000, setup.Content.Length - 1);
        Assert.Contains(LogLines(), l => l.EndsWith("the download was cancelled; the .part keeps what came", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hands_the_installer_over_and_tells_the_host_to_quit()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        var session = Session(server);
        await CheckAsync(session);
        await DownloadAsync(session);
        var package = Path.Combine(Downloads, SetupName);
        var log = Path.Combine(Downloads, "setup-0.79.0.log");
        // A log left by an earlier try would speak for this one.
        await File.WriteAllTextAsync(log, "2026-11-09 01:10:00.100   Log opened.\n");

        Assert.True(session.Apply(), session.Refusal);
        // The start runs off the host's thread (an antivirus can hold a new installer for most of a minute): until it
        // is there the session says so, and takes nothing back.
        var asked = session.Phase;
        await session.HandOver;

        // The installer itself, silently, told to wait for the game and to start it again, with a log of its own.
        Assert.Equal(UpdatePhase.Starting, asked);
        var start = Assert.Single(_starts);
        Assert.Equal(package, start.FileName);
        Assert.Equal($"/SILENT /NORESTART /SP- /NOCANCEL /UPDATE=1 /LOG=\"{log}\"", start.Arguments);
        Assert.True(start.UseShellExecute);
        Assert.Equal(Downloads, start.WorkingDirectory);
        Assert.False(File.Exists(log));
        // Written before the start: the next start knows what was tried.
        var state = State();
        Assert.Equal("0.79.0", state["applying"]);
        Assert.Equal("0.78.0", state["applying_from"]);
        Assert.Equal("setup", state["applying_kind"]);
        Assert.Equal(log, state["applying_log"]);
        Assert.Equal(package, state["applying_package"]);
        Assert.InRange(long.Parse(state["applying_at"]), DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Its first moments are watched with the game still there; then the game is told to go.
        var starting = session.Poll();
        Assert.Equal(UpdatePhase.Starting, starting.Phase);
        Assert.Equal("installing update...", starting.Status);
        _skipped += TimeSpan.FromSeconds(1);
        Assert.Equal(UpdatePhase.Starting, session.Poll().Phase);
        // The installer's own log, opened as its first act: the sign that it runs.
        await File.WriteAllTextAsync(log, "2026-11-09 01:12:00.100   Log opened.\n");
        _skipped += TimeSpan.FromSeconds(1);
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
        // Nothing takes a started installer back.
        session.Cancel();
        Assert.False(session.Check());
        Assert.False(session.Apply());
        Assert.Equal("the installer has been started", session.Refusal);
        Assert.Single(_starts);
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
        Assert.Contains(LogLines(), l => l.EndsWith("the installer is running: the game quits so that it can be replaced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_portable_copy_hands_its_zip_to_the_unpack_script()
    {
        await using var server = new FakeGitHub();
        var (_, setup, portable) = Publish(server);
        var session = Session(server, installed: false);
        var found = await CheckAsync(session);
        Assert.False(found.InstalledCopy);
        Assert.Equal(portable.Content.Length, found.PackageSize);
        await DownloadAsync(session);
        var zip = Path.Combine(Downloads, PortableName);
        var script = Path.Combine(Downloads, "apply.ps1");

        Assert.True(session.Apply(), session.Refusal);
        await session.HandOver;

        Assert.Equal(portable.Content, await File.ReadAllBytesAsync(zip));
        Assert.Equal(0, setup.Requests);
        // Windows PowerShell by its full path, hidden, on the script written beside the zip. It waits for this process
        // (the game itself), unpacks over the game's root folder and starts the launcher there again.
        var start = Assert.Single(_starts);
        Assert.Equal(UpdateCommands.PowerShell, start.FileName);
        Assert.Equal("-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden"
            + $" -File \"{script}\" -ProcessId {Environment.ProcessId} -Zip \"{zip}\" -Dir \"{GameDir}\" -Exe \"{Launcher}\" -Version 0.79.0", start.Arguments);
        Assert.True(start.UseShellExecute);
        Assert.Equal(ProcessWindowStyle.Hidden, start.WindowStyle);
        Assert.Equal(UpdateCommands.UnpackScript, await File.ReadAllTextAsync(script));
        var state = State();
        Assert.Equal("portable", state["applying_kind"]);
        Assert.Equal(Path.Combine(Downloads, "apply.log"), state["applying_log"]);
        _skipped += TimeSpan.FromSeconds(2);
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_check_without_a_player_shows_nothing_and_does_not_start_the_game_again(bool installed)
    {
        await using var server = new FakeGitHub();
        Publish(server);
        var session = Session(server, installed: installed, environment: name => name == "GRUDGELAP_UPDATE_UNATTENDED" ? "1" : null);
        Assert.True(session.Unattended);
        await CheckAsync(session);
        await DownloadAsync(session);

        Assert.True(session.Apply(), session.Refusal);
        await session.HandOver;

        var start = Assert.Single(_starts);
        if (installed)
        {
            var log = Path.Combine(Downloads, "setup-0.79.0.log");
            Assert.Equal($"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOCANCEL /UPDATE=2 /LOG=\"{log}\"", start.Arguments);
        }
        else
        {
            Assert.EndsWith(" -Version 0.79.0 -NoRestart", start.Arguments);
        }
        // The game still gets out of the way: whoever runs the check starts the new copy.
        _skipped += TimeSpan.FromSeconds(2);
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
        Assert.Contains(LogLines(), l => l.Contains(" (unattended)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_player_s_session_is_not_unattended_and_a_test_installer_s_key_is_its_own()
    {
        Assert.False(Session(server: null).Unattended);
        Assert.False(Session(server: null, environment: name => name == "GRUDGELAP_UPDATE_UNATTENDED" ? "0" : null).Unattended);
        // No installer has ever registered this AppId: a copy checked against it is a portable one.
        Assert.False(UpdateSession.IsInstalledAt(GameDir, "{00000000-0000-0000-0000-00000000F00D}"));
    }

    [Fact]
    public async Task A_start_that_Windows_holds_up_leaves_the_host_free_and_the_game_running()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        // What an antivirus does with a new installer of a gigabyte: the call that starts it does not return for a while.
        using var held = new ManualResetEventSlim();
        var session = Session(server, start: _ =>
        {
            held.Wait(TimeSpan.FromSeconds(30));
            return Process.GetCurrentProcess();
        });
        await CheckAsync(session);
        await DownloadAsync(session);

        var asked = Stopwatch.StartNew();
        Assert.True(session.Apply(), session.Refusal);
        var waiting = session.Poll();
        asked.Stop();

        // The host has its thread back at once and is told what it waits for; however long that takes, the game stays.
        Assert.InRange(asked.ElapsedMilliseconds, 0, 2000);
        Assert.Equal(UpdatePhase.Starting, waiting.Phase);
        Assert.Equal("starting the installer (an antivirus may check it first)...", waiting.Status);
        _skipped += TimeSpan.FromMinutes(2);
        Assert.Equal(UpdatePhase.Starting, session.Poll().Phase);
        Assert.False(session.Apply());
        Assert.Equal("the installer has been started", session.Refusal);

        held.Set();
        await session.HandOver;
        // The grace runs from the moment the installer was there, not from the request.
        var started = session.Poll();
        Assert.Equal(UpdatePhase.Starting, started.Phase);
        Assert.Equal("installing update...", started.Status);
        _skipped += TimeSpan.FromSeconds(2);
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
        Assert.Single(_starts);
        Assert.Contains(LogLines(), l => l.Contains(" started after ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_game_stays_until_the_installer_shows_that_it_runs()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        var session = Session(server, signWait: TimeSpan.FromSeconds(30));
        await CheckAsync(session);
        await DownloadAsync(session);
        var log = Path.Combine(Downloads, "setup-0.79.0.log");

        Assert.True(session.Apply(), session.Refusal);
        await session.HandOver;
        session.Poll();
        // Alive past the grace, but no log yet: the installer's own start can take its time too.
        _skipped += TimeSpan.FromSeconds(10);
        Assert.Equal(UpdatePhase.Starting, session.Poll().Phase);
        // Inno Setup opens its log as its first act: now the game can go.
        await File.WriteAllTextAsync(log, "2026-11-14 17:47:49.509   Log opened.\n");
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
        Assert.Contains(LogLines(), l => l.EndsWith("the installer is running: the game quits so that it can be replaced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_installer_that_never_writes_its_log_is_not_waited_for_for_ever()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        var session = Session(server, signWait: TimeSpan.FromSeconds(30));
        await CheckAsync(session);
        await DownloadAsync(session);

        Assert.True(session.Apply(), session.Refusal);
        await session.HandOver;
        session.Poll();
        _skipped += TimeSpan.FromSeconds(29);
        Assert.Equal(UpdatePhase.Starting, session.Poll().Phase);
        _skipped += TimeSpan.FromSeconds(2);
        Assert.Equal(UpdatePhase.QuitNow, session.Poll().Phase);
        Assert.Contains(LogLines(), l => l.EndsWith("the game quits for it all the same", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_installer_that_ends_at_once_leaves_the_game_running_and_is_given_up_after_two()
    {
        await using var server = new FakeGitHub();
        var (release, setup, _) = Publish(server);
        // What an installer that cannot go on does: it is gone, with its exit code, before the game has quit for it.
        static Process EndsAtOnce(ProcessStartInfo _)
        {
            var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 4") { CreateNoWindow = true, UseShellExecute = false })!;
            process.WaitForExit();
            return process;
        }
        var session = Session(server, start: EndsAtOnce);
        await CheckAsync(session);
        await DownloadAsync(session);

        Assert.True(session.Apply(), session.Refusal);
        await session.HandOver;
        var failed = session.Poll();

        Assert.Equal(UpdatePhase.Failed, failed.Phase);
        Assert.Equal("update failed: the installer failed while installing (exit code 4)", failed.Status);
        // The verified package stays and only the player tries again: a host that applies whatever is ready would
        // otherwise run a failing installer every frame.
        Assert.Equal(UpdateRetry.Apply, failed.Retry);
        Assert.Equal("", failed.Page);
        await PollForAsync(session, 200);
        Assert.Single(_starts);
        var state = State();
        Assert.Equal("0.79.0", state["failed_version"]);
        Assert.Equal("1", state["failed_count"]);
        Assert.Equal("the installer failed while installing (exit code 4)", state["failed_reason"]);
        Assert.DoesNotContain("applying", state.Keys);

        Assert.True(session.Apply(), session.Refusal);
        await session.HandOver;
        var gaveUp = session.Poll();

        Assert.Equal(UpdatePhase.Failed, gaveUp.Phase);
        Assert.Equal("v0.79.0 did not install twice (the installer failed while installing (exit code 4))", gaveUp.Status);
        Assert.Equal(UpdateRetry.None, gaveUp.Retry);
        Assert.Equal(server.Endpoints.Page(release.Tag).AbsoluteUri, gaveUp.Page);
        Assert.Equal("2", State()["failed_count"]);
        // From here the updater leaves this version alone: no third try, no other download of it.
        Assert.False(session.Apply());
        Assert.False(session.DownloadAndInstall());
        Assert.Equal("v0.79.0 did not install twice: its page has the installer", session.Refusal);
        Assert.Equal(UpdatePhase.Failed, (await CheckAsync(session)).Phase);
        Assert.Equal(gaveUp, session.Poll());
        Assert.Equal(2, _starts.Count);
        Assert.Equal(1, setup.Requests);
    }

    [Fact]
    public async Task A_failed_hand_over_is_remembered_across_starts_and_given_up_after_two()
    {
        await using var server = new FakeGitHub();
        var (release, setup, _) = Publish(server);
        // What Windows answers when an antivirus has locked the new executable away.
        static Process Refused(ProcessStartInfo _) => throw new Win32Exception(5, "Access is denied.");

        // The first start: found, downloaded, and the installer does not start.
        var first = Session(server, start: Refused);
        await CheckAsync(first);
        await DownloadAsync(first);
        Assert.True(first.Apply(), first.Refusal);
        await first.HandOver;
        var failed = first.Poll();
        Assert.Equal(UpdatePhase.Failed, failed.Phase);
        Assert.Equal("update failed: the installer could not start (Access is denied)", failed.Status);
        Assert.Equal(UpdateRetry.Apply, failed.Retry);
        Assert.Equal("1", State()["failed_count"]);
        first.Shutdown();

        // The second start says nothing by itself. The player asks again: the package kept from the first start is
        // verified where it lies, not downloaded again, and the installer fails a second time.
        var second = Session(server, start: Refused);
        await second.Cleanup;
        Assert.Equal(UpdatePhase.Idle, second.Phase);
        Assert.Equal("", second.Status);
        Assert.Equal(UpdatePhase.Available, (await CheckAsync(second)).Phase);
        Assert.Equal(UpdatePhase.Ready, (await DownloadAsync(second)).Phase);
        Assert.Equal(1, setup.Requests);
        Assert.Contains(LogLines(), l => l.Contains("reusing the package downloaded earlier", StringComparison.Ordinal));
        Assert.True(second.Apply(), second.Refusal);
        await second.HandOver;
        var gaveUp = second.Poll();
        Assert.Equal(UpdatePhase.Failed, gaveUp.Phase);
        Assert.Equal("v0.79.0 did not install twice (the installer could not start (Access is denied))", gaveUp.Status);
        Assert.Equal(UpdateRetry.None, gaveUp.Retry);
        Assert.Equal(server.Endpoints.Page(release.Tag).AbsoluteUri, gaveUp.Page);
        second.Shutdown();

        // The third start knows before any check. What it offers is a check, not only the page: a host whose line led
        // to the page alone had no way left to learn of a newer release. A check that still finds that version changes
        // nothing: it is the player's to install, from its page.
        var third = Session(server, start: Refused);
        var atStart = third.Poll();
        Assert.Equal(UpdatePhase.Failed, atStart.Phase);
        Assert.Equal(gaveUp.Status, atStart.Status);
        Assert.Equal(gaveUp.Page, atStart.Page);
        Assert.Equal(UpdateRetry.Check, atStart.Retry);
        var checkedAgain = await CheckAsync(third);
        Assert.Equal(UpdatePhase.Failed, checkedAgain.Phase);
        Assert.Equal(gaveUp.Status, checkedAgain.Status);
        Assert.Equal(gaveUp.Page, checkedAgain.Page);
        Assert.Equal(UpdateRetry.None, checkedAgain.Retry);
        Assert.Equal("0.79.0", checkedAgain.Latest);
        Assert.False(third.DownloadAndInstall());
        Assert.Equal(2, _starts.Count);
        Assert.Equal(1, setup.Requests);
        third.Shutdown();

        // A newer release is a new chance, and the start's own offer leads to it.
        Publish(server, "v0.80.0");
        var fourth = Session(server, start: Refused);
        Assert.Equal(UpdatePhase.Failed, fourth.Poll().Phase);
        Assert.Equal(UpdateRetry.Check, fourth.Poll().Retry);
        var newer = await CheckAsync(fourth);
        Assert.Equal(UpdatePhase.Available, newer.Phase);
        Assert.Equal("v0.80.0 available", newer.Status);
        Assert.Equal("", newer.Page);
    }

    [Fact]
    public async Task The_next_start_confirms_the_update_and_clears_the_download_folder()
    {
        // As the session that handed over left things, with an earlier failure of the same version on record.
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Downloads);
        var log = Path.Combine(Downloads, "setup-0.79.0.log");
        await File.WriteAllLinesAsync(StatePath,
        [
            "applying=0.79.0", "applying_from=0.78.0", "applying_kind=setup", $"applying_log={log}",
            $"applying_package={Path.Combine(Downloads, SetupName)}", "applying_at=1794650000",
            "failed_version=0.79.0", "failed_count=1", "failed_reason=the installer was cancelled",
        ]);
        await File.WriteAllLinesAsync(log, ["2026-11-14 01:10:00.100   Log opened.", "2026-11-14 01:10:30.000   Installation process succeeded.", "2026-11-14 01:10:31.000   Log closed."]);
        foreach (var name in new[] { SetupName, "GrudgeLap-Setup-0.78.0.exe.part", "GrudgeLap-Portable-0.80.0.zip.part", "apply.ps1", "apply.log", "notes.txt" })
        {
            await File.WriteAllTextAsync(Path.Combine(Downloads, name), name);
        }
        Directory.CreateDirectory(Path.Combine(Downloads, "unpack-0123456789abcdef", "data"));

        var session = Session(server: null, version: "0.79.0");
        var state = session.Poll();
        await session.Cleanup;

        Assert.Equal(UpdatePhase.Idle, state.Phase);
        Assert.Equal("updated to v0.79.0", state.Status);
        Assert.Equal(UpdateRetry.None, state.Retry);
        // Nothing in the folder is needed any more, the installer's log included: its last lines are in update.log.
        Assert.Equal(["notes.txt"], DownloadedFiles());
        Assert.Empty(Directory.GetDirectories(Downloads));
        Assert.False(File.Exists(StatePath));
        var lines = LogLines();
        Assert.Contains(lines, l => l.EndsWith("---- GrudgeLap 0.79.0, installed copy: " + Launcher, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("updated from v0.78.0 to v0.79.0", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("  | 2026-11-14 01:10:30.000   Installation process succeeded.", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith($"deleted {SetupName} (this copy is v0.79.0)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_next_start_says_why_an_update_did_not_take_and_gives_up_after_the_second()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Downloads);
        var log = Path.Combine(Downloads, "setup-0.79.0.log");
        string[] handedOver = ["applying=0.79.0", "applying_from=0.78.0", "applying_kind=setup", $"applying_log={log}", "applying_at=1794650000"];
        await File.WriteAllLinesAsync(StatePath, handedOver);
        await File.WriteAllLinesAsync(log,
        [
            "2026-11-14 01:10:00.100   Log opened. (Time zone: UTC+03:00)",
            "2026-11-14 01:10:07.000   Message box (Abort/Retry/Ignore):",
            "                           C:\\Users\\x\\AppData\\Local\\Programs\\GrudgeLap\\GrudgeLap.exe",
            "",
            "                           An error occurred while trying to replace the existing file:",
            "                           DeleteFile failed; code 5.",
            "                           Access is denied.",
            "2026-11-14 01:10:09.000   User chose Abort.",
        ]);
        await File.WriteAllTextAsync(Path.Combine(Downloads, SetupName), "the package that did not take");
        await File.WriteAllTextAsync(Path.Combine(Downloads, "GrudgeLap-Setup-0.77.0.exe"), "applied long ago");
        const string reason = "An error occurred while trying to replace the existing file: DeleteFile failed; code 5. Access is denied.";

        // Still the old version: the hand-over did not take, and the installer's log says why.
        var first = Session(server: null);
        var state = first.Poll();
        await first.Cleanup;

        Assert.Equal(UpdatePhase.Idle, state.Phase);
        Assert.Equal("the update to v0.79.0 did not install: " + reason, state.Status);
        var remembered = State();
        Assert.Equal(["failed_count", "failed_reason", "failed_version"], remembered.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("1", remembered["failed_count"]);
        Assert.Equal(reason, remembered["failed_reason"]);
        // The package of the newer version stays for the next try; one this copy has passed goes.
        Assert.Equal([SetupName, "setup-0.79.0.log"], DownloadedFiles());
        Assert.Contains(LogLines(), l => l.EndsWith("the update to v0.79.0 did not take (1 of 2 tries): " + reason, StringComparison.Ordinal));
        first.Shutdown();

        // Handed over once more, and once more the old version starts.
        await File.AppendAllLinesAsync(StatePath, handedOver);
        var second = Session(server: null);
        var gaveUp = second.Poll();

        Assert.Equal(UpdatePhase.Failed, gaveUp.Phase);
        Assert.Equal($"v0.79.0 did not install twice ({reason})", gaveUp.Status);
        // A check is what can still be asked for: it finds either this version again (then its page) or a newer one.
        Assert.Equal(UpdateRetry.Check, gaveUp.Retry);
        Assert.Equal("http://127.0.0.1:1/korayorhun/GrudgeLap/releases/tag/v0.79.0", gaveUp.Page);
        Assert.Equal("2", State()["failed_count"]);
        second.Shutdown();

        // Installed by hand from that page: the record is done with.
        var third = Session(server: null, version: "0.79.0");
        Assert.Equal(UpdatePhase.Idle, third.Poll().Phase);
        Assert.Equal("", third.Status);
        Assert.False(File.Exists(StatePath));
    }

    [Theory]
    [InlineData("portable")]
    [InlineData("setup")]
    public async Task A_game_started_while_the_update_is_still_at_work_makes_way_for_it(string kind)
    {
        // The player started the game again before the unpack script (or the installer) had finished: the old version
        // runs from the files that are being replaced.
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Downloads);
        var portable = kind == "portable";
        var log = Path.Combine(Downloads, portable ? "apply.log" : "setup-0.79.0.log");
        var package = Path.Combine(Downloads, portable ? PortableName : SetupName);
        var handedOver = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 20;
        string[] record = ["applying=0.79.0", "applying_from=0.78.0", $"applying_kind={kind}", $"applying_log={log}", $"applying_package={package}", $"applying_at={handedOver}"];
        await File.WriteAllLinesAsync(StatePath, record);
        // Begun and not ended: the script's "start" without "done" or "FAILED", the installer's log without "Log closed".
        string[] begun = portable
            ? [$"2026-11-14 19:10:31 start 0.79.0 from {package} over {GameDir} (waiting for process 1)", "2026-11-14 19:10:41 unpacked 8 entries"]
            : ["2026-11-14 19:14:28.920   Log opened. (Time zone: UTC+03:00)", "2026-11-14 19:14:32.199   Waited 3000 ms for the game to exit"];
        await File.WriteAllLinesAsync(log, begun);
        await File.WriteAllTextAsync(package, "the package that is being installed");
        var recorded = await File.ReadAllBytesAsync(StatePath);

        var session = Session(server: null, installed: !portable);
        var state = session.Poll();
        await session.Cleanup;

        // The host quits at once, as it does for an installer it started itself; nothing is judged or cleared.
        Assert.Equal(UpdatePhase.QuitNow, state.Phase);
        Assert.Equal("installing update...", state.Status);
        Assert.Equal("0.79.0", state.Latest);
        Assert.False(session.Check());
        Assert.Equal("the installer has been started", session.Refusal);
        Assert.Equal(recorded, await File.ReadAllBytesAsync(StatePath));
        Assert.Contains(Path.GetFileName(package), DownloadedFiles());
        Assert.Contains(LogLines(), l => l.EndsWith($"the update to v0.79.0 is still being installed ({kind}): the game makes way for it", StringComparison.Ordinal));
        session.Shutdown();

        // The same record once its window has passed is a run that died: read as it stands, and counted.
        await File.WriteAllLinesAsync(StatePath, record.Select(l => l.StartsWith("applying_at=", StringComparison.Ordinal)
            ? $"applying_at={handedOver - (long)UpdateSession.ApplyingWindow.TotalSeconds}" : l));
        var later = Session(server: null, installed: !portable);
        var died = later.Poll();
        Assert.Equal(UpdatePhase.Idle, died.Phase);
        Assert.StartsWith("the update to v0.79.0 did not install: ", died.Status);
        Assert.Equal("1", State()["failed_count"]);
        Assert.DoesNotContain("applying", State().Keys);
    }

    [Fact]
    public async Task A_game_closed_while_the_installer_starts_has_not_failed_to_install()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        // Windows holds the start (an antivirus reads the installer first) and the player, seeing nothing happen, quits:
        // the call ends with the game's process, and no installer comes of it.
        using var held = new ManualResetEventSlim();
        var first = Session(server, start: _ =>
        {
            held.Wait(TimeSpan.FromSeconds(30));
            return null;
        });
        await CheckAsync(first);
        await DownloadAsync(first);
        Assert.True(first.Apply(), first.Refusal);
        Assert.Equal(UpdatePhase.Starting, first.Poll().Phase);
        var handOver = first.HandOver;
        first.Shutdown();
        Assert.Equal("1", State()["applying_cut"]);
        Assert.Equal("0.79.0", State()["applying"]);
        held.Set();
        await handOver;

        // The next start, still the old version and with no installer's log: nothing ran, so nothing failed. The
        // verified package is still there, and asking again installs it without another download.
        var second = Session(server);
        var state = second.Poll();
        await second.Cleanup;
        Assert.Equal(UpdatePhase.Idle, state.Phase);
        Assert.Equal("the update to v0.79.0 was not installed: the game was closed while it was starting", state.Status);
        Assert.False(File.Exists(StatePath));
        Assert.Contains(SetupName, DownloadedFiles());
        Assert.Contains(LogLines(), l => l.EndsWith("the update to v0.79.0 was never started: the game was closed while the installer was starting", StringComparison.Ordinal));
        second.Shutdown();

        // With an installer's log the start did happen, whatever the mark says: that one is judged by its log.
        var log = Path.Combine(Downloads, "setup-0.79.0.log");
        await File.WriteAllLinesAsync(StatePath, ["applying=0.79.0", "applying_from=0.78.0", "applying_kind=setup", $"applying_log={log}", "applying_at=1794650000", "applying_cut=1"]);
        await File.WriteAllLinesAsync(log, ["2026-11-14 01:10:00.100   Log opened.", "2026-11-14 01:10:09.000   User chose Abort.", "2026-11-14 01:10:10.000   Log closed."]);
        var third = Session(server);
        Assert.StartsWith("the update to v0.79.0 did not install: ", third.Poll().Status);
        Assert.Equal("1", State()["failed_count"]);
        Assert.DoesNotContain("applying_cut", State().Keys);
    }

    [Fact]
    public async Task A_download_an_antivirus_holds_is_not_fetched_again_and_points_to_the_page()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var server = new FakeGitHub();
        var (release, setup, _) = Publish(server);
        var session = Session(server, client: Fast with { HeldFileWait = TimeSpan.FromSeconds(0.6) });
        await CheckAsync(session);
        // The whole package is on disk and another program keeps it shut, for longer than the updater waits.
        Directory.CreateDirectory(Downloads);
        var part = Path.Combine(Downloads, SetupName + ".part");
        await File.WriteAllBytesAsync(part, setup.Content);

        UpdateSnapshot blocked;
        await using (new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            blocked = await DownloadAsync(session);
        }

        Assert.Equal(UpdatePhase.Failed, blocked.Phase);
        Assert.Equal("update failed: the downloaded installer stayed locked by another program (Test Antivirus?)", blocked.Status);
        // Another copy would go the same way: the page instead, where the browser lets the player keep the file.
        Assert.Equal(UpdateRetry.None, blocked.Retry);
        Assert.Equal(server.Endpoints.Page(release.Tag).AbsoluteUri, blocked.Page);
        Assert.False(session.DownloadAndInstall());
        Assert.StartsWith("v0.79.0 is not downloaded again this session", session.Refusal);
        var again = await CheckAsync(session);
        Assert.Equal(blocked, again);
        _skipped += TimeSpan.FromHours(1);
        await PollForAsync(session, 200);
        Assert.Equal(0, setup.Requests);
        Assert.Empty(_starts);
    }

    [Fact]
    public async Task A_release_without_a_package_for_this_copy_points_to_its_page()
    {
        await using var server = new FakeGitHub();
        var (release, _, portable) = Publish(server, setup: false);
        var session = Session(server);

        var state = await CheckAsync(session);

        Assert.Equal(UpdatePhase.Failed, state.Phase);
        Assert.Equal("v0.79.0 has no installer for this copy", state.Status);
        Assert.Equal("0.79.0", state.Latest);
        Assert.Equal(0, state.PackageSize);
        Assert.Equal(UpdateRetry.None, state.Retry);
        Assert.Equal(server.Endpoints.Page(release.Tag).AbsoluteUri, state.Page);
        Assert.False(session.DownloadAndInstall());
        Assert.Equal("v0.79.0 has no installer for this copy", session.Refusal);
        // The zip is another kind of copy's: an installed copy never takes it instead.
        Assert.Equal(0, portable.Requests);
        Assert.Empty(PackageHits(server));
    }

    [Fact]
    public async Task A_dry_run_checks_and_changes_nothing()
    {
        await using var server = new FakeGitHub();
        var (_, setup, _) = Publish(server);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Downloads);
        // A hand-over on record and an old package: a dry run neither judges the one nor clears the other.
        await File.WriteAllLinesAsync(StatePath, ["applying=0.79.0", "applying_from=0.78.0", "applying_kind=setup"]);
        var recorded = await File.ReadAllBytesAsync(StatePath);
        await File.WriteAllTextAsync(Path.Combine(Downloads, "GrudgeLap-Setup-0.70.0.exe"), "old");

        var session = Session(server, environment: name => name == "GRUDGELAP_UPDATE_DRYRUN" ? "1" : null);
        await session.Cleanup;
        Assert.True(session.DryRun);
        Assert.Equal("", session.Status);
        var found = await CheckAsync(session);

        Assert.Equal(UpdatePhase.Available, found.Phase);
        Assert.Equal("v0.79.0 available (dry run: nothing downloaded)", found.Status);
        Assert.Equal(setup.Content.Length, found.PackageSize);
        Assert.False(session.DownloadAndInstall());
        Assert.Equal("dry run (GRUDGELAP_UPDATE_DRYRUN): nothing is downloaded", session.Refusal);
        Assert.Equal(found, session.Poll());
        Assert.Empty(PackageHits(server));
        Assert.Equal(recorded, await File.ReadAllBytesAsync(StatePath));
        Assert.Equal(["GrudgeLap-Setup-0.70.0.exe"], DownloadedFiles());
        Assert.Contains(LogLines(), l => l.EndsWith(" (dry run)", StringComparison.Ordinal));
        // "0" switches it off, as in the Godot version.
        Assert.False(Session(server, environment: name => name == "GRUDGELAP_UPDATE_DRYRUN" ? "0" : null).DryRun);
    }

    [Fact]
    public async Task Takes_its_feed_and_its_version_from_the_hooks()
    {
        await using var server = new FakeGitHub();
        Publish(server);
        string? Hooks(string name) => name switch
        {
            "GRUDGELAP_UPDATE_FEED" => server.Root.AbsoluteUri,
            "GRUDGELAP_FAKE_VERSION" => "0.79.0",
            _ => null,
        };

        // The build is 0.78.0 and claims to be 0.79.0; the releases are read from the feed the hook names.
        var claiming = Session(server, version: "0.78.0", environment: Hooks, feedFromEnvironment: true);
        var state = await CheckAsync(claiming);

        Assert.Equal(UpdatePhase.UpToDate, state.Phase);
        Assert.Equal("up to date (v0.79.0)", state.Status);
        Assert.Equal(1, ManifestRequests(server));
        Assert.Contains(LogLines(), l => l.EndsWith($"---- GrudgeLap 0.79.0, installed copy: {Launcher} (feed {server.Root.AbsoluteUri})", StringComparison.Ordinal));
        // Without the claim the same feed has something newer.
        var honest = Session(server, version: "0.78.0", environment: name => name == "GRUDGELAP_FAKE_VERSION" ? null : Hooks(name), feedFromEnvironment: true);
        Assert.Equal(UpdatePhase.Available, (await CheckAsync(honest)).Phase);
    }

    [Fact]
    public void Refuses_a_host_whose_version_or_folders_cannot_be_read()
    {
        // A version that is not one would make every release look newer, for ever.
        var error = Assert.Throws<ArgumentException>(() => new UpdateSession(new UpdateHost("development build", Launcher, GameDir, DataDir)));
        Assert.Contains("\"development build\" is not a version like 0.78.0", error.Message);
        Assert.Throws<ArgumentException>(() => new UpdateSession(new UpdateHost("0.78.0", "", GameDir, DataDir)));
        Assert.Throws<ArgumentException>(() => new UpdateSession(new UpdateHost("0.78.0", Launcher, " ", DataDir)));
        Assert.Throws<ArgumentException>(() => new UpdateSession(new UpdateHost("0.78.0", Launcher, GameDir, "")));
        // Nothing was started for them: no log, no folder.
        Assert.False(Directory.Exists(DataDir));
        // A folder nobody installed to is a portable copy's, whatever the registry holds.
        Assert.False(UpdateSession.IsInstalledAt(_folder));
        // "v0.78.0" is a version too, and the session says which kind of copy it was told it is.
        Assert.False(Session(server: null, version: "v0.78.0", installed: false).InstalledCopy);
    }

    [Fact]
    public void Keeps_its_state_as_plain_lines()
    {
        Directory.CreateDirectory(DataDir);
        var state = UpdateState.Load(StatePath);
        Assert.Null(state.LoadError);
        Assert.Equal("", state.Get("applying"));
        Assert.Equal("?", state.Get("applying_from", "?"));
        Assert.Equal(0, state.GetInt("failed_count"));

        state.Set("applying", "0.79.0");
        state.Set("applying_log", @"C:\Users\Ayşe Yılmaz\AppData\Local\Temp\GrudgeLap-Update\setup-0.79.0.log");
        state.Set("failed_count", 2);
        // A reason is whatever Windows or an installer said: an equals sign stays, a line break does not.
        state.Set("failed_reason", "exit code = 5\r\nAccess is denied.");
        Assert.Null(state.Save());

        Assert.Equal(
            "applying=0.79.0\n"
            + "applying_log=C:\\Users\\Ayşe Yılmaz\\AppData\\Local\\Temp\\GrudgeLap-Update\\setup-0.79.0.log\n"
            + "failed_count=2\n"
            + "failed_reason=exit code = 5 Access is denied.\n", File.ReadAllText(StatePath, Encoding.UTF8));
        // UTF-8 without a byte-order mark, and no half-written file beside it.
        Assert.Equal((byte)'a', File.ReadAllBytes(StatePath)[0]);
        Assert.Equal([UpdateSession.StateFileName], Directory.GetFiles(DataDir).Select(f => Path.GetFileName(f)).ToArray());

        var read = UpdateState.Load(StatePath);
        Assert.Equal(@"C:\Users\Ayşe Yılmaz\AppData\Local\Temp\GrudgeLap-Update\setup-0.79.0.log", read.Get("applying_log"));
        Assert.Equal(2, read.GetInt("failed_count"));
        Assert.Equal("exit code = 5 Access is denied.", read.Get("failed_reason"));

        // A file somebody edited (a byte-order mark, Windows line ends, a note, an empty line) still reads.
        File.WriteAllText(StatePath, "# edited by hand\r\n\r\napplying=0.80.0\r\nfailed_count=many\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var edited = UpdateState.Load(StatePath);
        Assert.Equal("0.80.0", edited.Get("applying"));
        Assert.Equal(0, edited.GetInt("failed_count"));

        // Nothing left to remember: no file.
        edited.Remove("applying");
        edited.Remove("failed_count");
        Assert.Null(edited.Save());
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public unsafe void The_C_struct_is_laid_out_as_the_header_says()
    {
        // glp_update_state in grudgelap_update.h: four int32, three int64, char[32], char[256], char[256].
        NativeUpdateState state = default;
        var start = (byte*)&state;
        Assert.Equal(584, sizeof(NativeUpdateState));
        Assert.Equal(0, (byte*)&state.Phase - start);
        Assert.Equal(4, (byte*)&state.InstalledCopy - start);
        Assert.Equal(8, (byte*)&state.CanRetry - start);
        Assert.Equal(12, (byte*)&state.HasPage - start);
        Assert.Equal(16, (byte*)&state.BytesDone - start);
        Assert.Equal(24, (byte*)&state.BytesTotal - start);
        Assert.Equal(32, (byte*)&state.PackageSize - start);
        Assert.Equal(40, state.Latest - start);
        Assert.Equal(72, state.Status - start);
        Assert.Equal(328, state.Page - start);

        // And the header itself says the same numbers as the code: they change together.
        var header = File.ReadAllText(HeaderPath());
        Assert.Contains($"#define GLP_UPDATE_ABI {Exports.AbiVersion}\n", header.ReplaceLineEndings("\n"));
        Assert.Contains($"char latest[{NativeUpdateState.LatestSize}];", header);
        Assert.Contains($"char status[{NativeUpdateState.StatusSize}];", header);
        Assert.Contains($"char page[{NativeUpdateState.PageSize}];", header);
        var constants = Regex.Matches(header, @"\b(GLP_UPDATE_[A-Z_]+) = (\d+)").ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
        foreach (var phase in Enum.GetValues<UpdatePhase>())
        {
            Assert.Equal((int)phase, constants["GLP_UPDATE_" + Regex.Replace(phase.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant()]);
        }
        foreach (var retry in Enum.GetValues<UpdateRetry>())
        {
            Assert.Equal((int)retry, constants["GLP_UPDATE_RETRY_" + retry.ToString().ToUpperInvariant()]);
        }
        Assert.Equal(Enum.GetValues<UpdatePhase>().Length + Enum.GetValues<UpdateRetry>().Length, constants.Count);
    }

    private static string HeaderPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "update.native", "include", "grudgelap_update.h");

    [Fact]
    public unsafe void Fills_the_C_struct_with_terminated_utf8()
    {
        NativeUpdateState state;
        // Whatever the host's memory held before.
        new Span<byte>(&state, sizeof(NativeUpdateState)).Fill(0xCC);
        const string page = "https://github.com/korayorhun/GrudgeLap/releases/tag/v0.79.0";

        Exports.Write(new UpdateSnapshot(UpdatePhase.Failed, true, UpdateRetry.Apply, 123, 1L << 33, 3_000_000_000, "0.79.0", "update failed: Erişim engellendi", page), &state);

        Assert.Equal(9, state.Phase);
        Assert.Equal(1, state.InstalledCopy);
        Assert.Equal(3, state.CanRetry);
        Assert.Equal(1, state.HasPage);
        Assert.Equal(123, state.BytesDone);
        Assert.Equal(1L << 33, state.BytesTotal);
        Assert.Equal(3_000_000_000, state.PackageSize);
        Assert.Equal("0.79.0", Encoding.UTF8.GetString(FieldBytes(state.Latest, NativeUpdateState.LatestSize)));
        Assert.Equal("update failed: Erişim engellendi", Encoding.UTF8.GetString(FieldBytes(state.Status, NativeUpdateState.StatusSize)));
        Assert.Equal(page, Encoding.UTF8.GetString(FieldBytes(state.Page, NativeUpdateState.PageSize)));

        // A line longer than its field is cut after a whole character and still terminated: 127 two-byte letters fit
        // in 255 bytes, half of the next does not.
        var longLine = new string('ş', 200);
        Exports.Write(new UpdateSnapshot(UpdatePhase.Idle, false, UpdateRetry.None, 0, 0, 0, new string('9', 40), longLine, page + new string('x', 256)), &state);

        Assert.Equal(new string('ş', 127), Encoding.UTF8.GetString(FieldBytes(state.Status, NativeUpdateState.StatusSize)));
        Assert.Equal(0, state.Status[254]);
        Assert.Equal(new string('9', 31), Encoding.UTF8.GetString(FieldBytes(state.Latest, NativeUpdateState.LatestSize)));
        // Half an address would open the wrong page: one that does not fit is left out.
        Assert.Equal(0, state.HasPage);
        Assert.Equal(0, state.Page[0]);
        Assert.Equal(0, state.InstalledCopy);

        Exports.Write(default, &state);
        Assert.Equal(0, state.Phase);
        Assert.Equal(0, state.HasPage);
        Assert.Equal(0, state.Status[0]);
    }

    /// <summary>The bytes of a C string field up to its terminator; fails when there is none within the field.</summary>
    private static unsafe ReadOnlySpan<byte> FieldBytes(byte* field, int size)
    {
        var bytes = new ReadOnlySpan<byte>(field, size);
        var end = bytes.IndexOf((byte)0);
        Assert.True(end >= 0, "the field is not terminated");
        return bytes[..end];
    }
}
