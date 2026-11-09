using System.Diagnostics;
using GrudgeLap.Game;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Update.Tests;

/// <summary>
/// The download client against the fake GitHub's faults: what happened at home (a drop part-way, a connection that
/// went silent, an expired signed redirect) must cost a retry from where it stopped, never the whole package or a
/// frozen game, and what cannot work (a bad checksum, a full disk) must stop with a reason a player can read.
/// </summary>
public sealed class UpdateClientTests(ITestOutputHelper output) : IDisposable
{
    private const string SetupName = "GrudgeLap-Setup-0.77.0.exe";
    private readonly string _folder = Directory.CreateTempSubdirectory("grudgelap-update-tests-").FullName;
    private readonly List<string> _log = [];
    private readonly HttpClient _http = UpdateHttp.CreateClient("test", useProxy: false);

    /// <summary>The game's settings with the waits shortened: silence after 1.5 s, retries after 50-100 ms.</summary>
    private static readonly UpdateClientOptions Fast = new()
    {
        Inactivity = TimeSpan.FromSeconds(1.5),
        Backoff = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)],
        AttemptsWithoutProgress = 3,
        BufferSize = 64 * 1024,
        Reserve = 0,
    };

    public void Dispose()
    {
        // The client's own log, as update.log would have it: what a failing run did.
        lock (_log) foreach (var line in _log) output.WriteLine(line);
        _http.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A handle the OS has not let go of yet; the temp folder is the system's to clear.
        }
    }

    private static byte[] Payload(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private UpdateClient Client(UpdateClientOptions? options = null, Func<string, long>? freeSpace = null) =>
        new(_http, options ?? Fast, line => { lock (_log) _log.Add(line); }, freeSpace);

    private static UpdatePackage Package(FakeGitHub server, FakeRelease release, string name, byte[] content, bool sha = true, bool size = true) =>
        new(name, server.Download(release, name), size ? content.Length : null, sha ? FakeGitHub.Sha(content) : null, server.Download(release, name + ".sha256"));

    private static CancellationToken Within(int seconds) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    private static List<FakeGitHub.Hit> BlobHits(FakeGitHub server) => server.Hits.Where(h => h.Path.StartsWith("/blob/", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Downloads_and_verifies_a_package()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_500_000, 1);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);
        var client = Client();

        var path = await client.DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(Path.Combine(_folder, SetupName), path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal(DownloadPhase.Done, client.Phase);
        Assert.Single(BlobHits(server));
    }

    [Fact]
    public async Task Resumes_after_the_connection_drops_at_40_percent()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(2_000_000, 2);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).DropAt = 0.4;

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var blobs = BlobHits(server);
        Assert.Equal(2, blobs.Count);
        Assert.Null(blobs[0].Range);
        // The second request asks for the rest, not the whole package again.
        Assert.NotNull(blobs[1].Range);
        var resumedAt = long.Parse(blobs[1].Range!["bytes=".Length..].TrimEnd('-'));
        Assert.InRange(resumedAt, 1, bytes.Length - 1);
        Assert.Contains(_log, l => l.StartsWith($"resuming {SetupName} at", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recovers_from_a_half_open_connection_that_falls_silent()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_200_000, 3);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).StallAt = 0.3;
        var watch = Stopwatch.StartNew();

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        // It waited out the silence (1.5 s here, 45 s in the game) and no longer: the server held the line for 60 s.
        Assert.InRange(watch.Elapsed.TotalSeconds, 1.4, 20);
        var blobs = BlobHits(server);
        Assert.Equal(2, blobs.Count);
        Assert.NotNull(blobs[1].Range);
        Assert.Contains(_log, l => l.Contains("sent nothing for", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Starts_again_from_the_beginning_when_the_server_ignores_the_range()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_000_000, 4);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).IgnoreRange = true;
        // Half a package from an earlier session, and not even the right bytes: a 200 must replace it, not extend it.
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName + ".part"), Payload(500_000, 99));

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var blobs = BlobHits(server);
        Assert.Single(blobs);
        Assert.Equal("bytes=500000-", blobs[0].Range);
        Assert.Equal(200, blobs[0].Status);
        Assert.Contains(_log, l => l.Contains("sent the whole package instead of the rest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Asks_github_again_for_a_fresh_signed_url_on_every_attempt()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_500_000, 5);
        var release = server.AddRelease("v0.77.0");
        var asset = release.Add(SetupName, bytes);
        asset.DropAt = 0.5;

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        // Each signed URL works once on this server: a client that kept the first one would have met a 403.
        Assert.DoesNotContain(server.Hits, h => h.Status == 403);
        Assert.Equal(2, server.Hits.Count(h => h.Path.EndsWith("/releases/download/v0.77.0/" + SetupName, StringComparison.Ordinal)));
        Assert.Equal(2, BlobHits(server).Select(h => h.Query).Distinct().Count());
    }

    [Fact]
    public async Task Gets_past_a_signed_url_that_had_already_expired()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(600_000, 6);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).ExpireFirstSignature = true;

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Single(server.Hits, h => h.Status == 403);
    }

    [Fact]
    public async Task Rejects_a_package_that_does_not_match_its_checksum()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(800_000, 7);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);
        var wrong = Package(server, release, SetupName, bytes) with { Sha256 = FakeGitHub.Sha(Payload(10, 8)) };

        var error = await Assert.ThrowsAsync<UpdateException>(() => Client().DownloadAsync(wrong, _folder, Within(30)));

        Assert.Equal(UpdateFailure.Checksum, error.Kind);
        Assert.Contains("SHA-256", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
        // Fetched once more from the start before giving up: two resumed pieces of different uploads fail the same way.
        Assert.Equal(2, BlobHits(server).Count);
    }

    [Fact]
    public async Task Stops_before_downloading_when_the_disk_is_nearly_full()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_500_000, 9);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);

        var error = await Assert.ThrowsAsync<UpdateException>(() =>
            Client(Fast with { Reserve = 64L << 20 }, freeSpace: _ => 40L << 20).DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30)));

        Assert.Equal(UpdateFailure.Disk, error.Kind);
        Assert.StartsWith("not enough free space", error.Message);
        Assert.Contains("40 MB free", error.Message);
        Assert.Empty(server.Hits);
    }

    [Fact]
    public async Task Keeps_going_on_a_slow_server_that_still_sends()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(300_000, 10);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).BytesPerSecond = 100_000;
        var client = Client();
        var watch = Stopwatch.StartNew();

        var download = client.DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));
        var seen = new SortedSet<long>();
        while (!download.IsCompleted)
        {
            if (client.Phase == DownloadPhase.Downloading) seen.Add(client.BytesDone);
            await Task.Delay(50);
        }
        var path = await download;

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        // Three seconds in all, twice the silence limit: the limit is on silence, not on the whole download.
        Assert.True(watch.Elapsed.TotalSeconds > 2.5, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Single(BlobHits(server));
        // The HUD's counters moved along the way.
        Assert.True(seen.Count(b => b > 0 && b < bytes.Length) >= 5, $"progress seen: {string.Join(", ", seen)}");
    }

    [Fact]
    public async Task Reuses_a_verified_package_without_downloading()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(700_000, 11);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName), bytes);

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(Path.Combine(_folder, SetupName), path);
        Assert.Empty(server.Hits);
        Assert.Contains(_log, l => l.StartsWith("reusing the package", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replaces_a_package_that_does_not_verify()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(700_000, 12);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName), Payload(700_000, 13));

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Single(BlobHits(server));
    }

    [Fact]
    public async Task Resumes_a_part_left_by_an_earlier_session()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_000_000, 14);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName + ".part"), bytes[..600_000]);

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var blob = Assert.Single(BlobHits(server));
        Assert.Equal("bytes=600000-", blob.Range);
        Assert.Equal(206, blob.Status);
    }

    [Fact]
    public async Task Cancelling_during_a_stall_returns_at_once()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1_000_000, 15);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).StallAt = 0.2;
        // The game's own silence limit: quitting must not wait for it.
        var client = Client(Fast with { Inactivity = TimeSpan.FromSeconds(45) });
        using var quit = new CancellationTokenSource();

        var download = client.DownloadAsync(Package(server, release, SetupName, bytes), _folder, quit.Token);
        var deadline = Stopwatch.StartNew();
        while (client.BytesDone < 200_000 && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        await Task.Delay(300);
        var watch = Stopwatch.StartNew();
        quit.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.True(watch.Elapsed.TotalSeconds < 3, $"cancel took {watch.Elapsed.TotalSeconds:0.0} s");
        // What came stays for the next session to resume.
        Assert.True(File.Exists(Path.Combine(_folder, SetupName + ".part")));
    }

    [Fact]
    public async Task Gives_up_after_attempts_that_bring_nothing()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(100_000, 16);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes).FailWith = 503;

        var error = await Assert.ThrowsAsync<UpdateException>(() => Client().DownloadAsync(Package(server, release, SetupName, bytes), _folder, Within(30)));

        Assert.Equal(UpdateFailure.Network, error.Kind);
        Assert.Contains("HTTP 503", error.Message);
        Assert.Equal(Fast.AttemptsWithoutProgress, server.Hits.Count(h => h.Status == 503));
    }

    [Fact]
    public async Task Reads_the_checksum_file_when_the_release_states_none()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(400_000, 17);
        var release = server.AddRelease("v0.77.0");
        release.Add(SetupName, bytes);

        var path = await Client().DownloadAsync(Package(server, release, SetupName, bytes, sha: false, size: false), _folder, Within(30));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Contains(server.Hits, h => h.Path.EndsWith(SetupName + ".sha256", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_package_missing_from_the_release_is_not_tried_again()
    {
        await using var server = new FakeGitHub();
        var bytes = Payload(1000, 18);
        var release = server.AddRelease("v0.77.0");
        var missing = new UpdatePackage(SetupName, server.Download(release, SetupName), bytes.Length, FakeGitHub.Sha(bytes), null);

        var error = await Assert.ThrowsAsync<UpdateException>(() => Client().DownloadAsync(missing, _folder, Within(30)));

        Assert.Equal(UpdateFailure.NotFound, error.Kind);
        Assert.Single(server.Hits);
    }

    [Fact]
    public async Task Refuses_a_package_name_that_leaves_the_folder()
    {
        await using var server = new FakeGitHub();
        var package = new UpdatePackage(@"..\GrudgeLap.exe", server.Root, 10, new string('a', 64), null);

        var error = await Assert.ThrowsAsync<UpdateException>(() => Client().DownloadAsync(package, _folder, Within(5)));

        Assert.Equal(UpdateFailure.BadData, error.Kind);
        Assert.Empty(server.Hits);
    }
}
