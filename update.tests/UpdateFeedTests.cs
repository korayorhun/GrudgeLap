using System.Text;
using GrudgeLap.Game;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Update.Tests;

/// <summary>
/// Finding the newest release: update.json first (no API call), then the API, then its release list; exact package
/// names only; a readable reason when nothing answers. The fake server stands in for github.com and api.github.com.
/// </summary>
public sealed class UpdateFeedTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<string> _log = [];
    private readonly HttpClient _http = UpdateHttp.CreateClient("test", useProxy: false);

    public void Dispose()
    {
        lock (_log) foreach (var line in _log) output.WriteLine(line);
        _http.Dispose();
    }

    private UpdateFeed Feed(FakeGitHub server) => new(_http, server.Endpoints, line => { lock (_log) _log.Add(line); }) { RequestTimeout = TimeSpan.FromSeconds(10) };

    private static CancellationToken Within(int seconds) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    /// <summary>A game release as tools/release.ps1 publishes it: both packages with their .sha256 files, and update.json unless told otherwise.</summary>
    private static FakeRelease GameRelease(FakeGitHub server, string tag, bool manifest = true, bool latest = true)
    {
        var release = server.AddRelease(tag, latest);
        var version = tag.TrimStart('v');
        release.Add($"GrudgeLap-Setup-{version}.exe", Encoding.ASCII.GetBytes("setup " + tag));
        release.Add($"GrudgeLap-Portable-{version}.zip", Encoding.ASCII.GetBytes("portable zip of " + tag));
        if (manifest) release.AddManifest();
        return release;
    }

    [Fact]
    public async Task Reads_update_json_first_without_calling_the_API()
    {
        await using var server = new FakeGitHub();
        var release = GameRelease(server, "v0.77.0");

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Equal("update.json", found.Source);
        Assert.Equal(new Version(0, 77, 0, 0), found.Version);
        Assert.Equal("0.77.0", found.VersionText);
        Assert.Equal("v0.77.0", found.Tag);
        var setup = release.Assets["GrudgeLap-Setup-0.77.0.exe"];
        Assert.Equal("GrudgeLap-Setup-0.77.0.exe", found.Setup!.Name);
        Assert.Equal(setup.Content.Length, found.Setup.Size);
        Assert.Equal(FakeGitHub.Sha(setup.Content), found.Setup.Sha256);
        Assert.Equal(server.Download(release, "GrudgeLap-Setup-0.77.0.exe"), found.Setup.Url);
        Assert.Equal("GrudgeLap-Portable-0.77.0.zip", found.Portable!.Name);
        Assert.Equal(0, server.ApiCalls);
    }

    [Fact]
    public async Task Reads_an_update_json_written_with_a_byte_order_mark()
    {
        await using var server = new FakeGitHub();
        var release = server.AddRelease("v0.77.0");
        release.Add("GrudgeLap-Setup-0.77.0.exe", [1, 2, 3]);
        release.AddManifest(bom: true);

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Equal("update.json", found.Source);
        Assert.NotNull(found.Setup);
        Assert.Null(found.Portable);
    }

    [Fact]
    public async Task Falls_back_to_the_API_for_a_release_without_update_json()
    {
        await using var server = new FakeGitHub();
        var release = GameRelease(server, "v0.76.0", manifest: false);

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Equal("api", found.Source);
        Assert.Equal("v0.76.0", found.Tag);
        // GitHub's own digest of the asset: no .sha256 download needed.
        Assert.Equal(FakeGitHub.Sha(release.Assets["GrudgeLap-Setup-0.76.0.exe"].Content), found.Setup!.Sha256);
        Assert.Equal(release.Assets["GrudgeLap-Portable-0.76.0.zip"].Content.Length, found.Portable!.Size);
        Assert.Contains(_log, l => l.Contains("has no update.json", StringComparison.Ordinal));
        Assert.Contains(_log, l => l.StartsWith("API calls left this hour: 59 of 60", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Takes_only_the_exact_package_names()
    {
        await using var server = new FakeGitHub();
        var release = server.AddRelease("v0.77.0");
        // What a release carries besides the packages, and names one prefix match would have taken.
        release.Add("GrudgeLapUE-Preview-0.77.0.zip", Encoding.ASCII.GetBytes("unreal preview"));
        release.Add("GrudgeLap-Setup-0.77.0-debug.exe", Encoding.ASCII.GetBytes("debug setup"));
        release.Add("GrudgeLap-Portable-0.77.0-symbols.zip", Encoding.ASCII.GetBytes("symbols"));
        release.Add("GrudgeLap-Setup-0.77.0.exe", Encoding.ASCII.GetBytes("the setup"));
        release.Add("GrudgeLap-Portable-0.77.0.zip", Encoding.ASCII.GetBytes("the portable zip"));

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Equal("api", found.Source);
        Assert.Equal("GrudgeLap-Setup-0.77.0.exe", found.Setup!.Name);
        Assert.Equal("GrudgeLap-Portable-0.77.0.zip", found.Portable!.Name);
        Assert.Equal(FakeGitHub.Sha(Encoding.ASCII.GetBytes("the setup")), found.Setup.Sha256);
    }

    [Fact]
    public async Task A_release_without_a_digest_points_to_its_checksum_file()
    {
        await using var server = new FakeGitHub();
        var release = GameRelease(server, "v0.77.0", manifest: false);
        release.Assets["GrudgeLap-Setup-0.77.0.exe"].PublishDigest = false;

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Null(found.Setup!.Sha256);
        Assert.Equal(server.Download(release, "GrudgeLap-Setup-0.77.0.exe.sha256"), found.Setup.ShaUrl);
    }

    [Fact]
    public async Task Skips_a_latest_release_that_is_not_a_game_version()
    {
        await using var server = new FakeGitHub();
        GameRelease(server, "v0.76.0", latest: false);
        GameRelease(server, "v0.77.0", latest: false);
        GameRelease(server, "v0.78.0", latest: false).Draft = true;
        GameRelease(server, "v0.79.0", latest: false).Prerelease = true;
        GameRelease(server, "v0.80.0-rc1", latest: false);
        // A car model release made latest by mistake (they are created with --latest=false).
        server.AddRelease("models-2026-11").Add("toyota-86-ethanchew.glb", [1, 2, 3, 4]);

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Equal("release list", found.Source);
        Assert.Equal("v0.77.0", found.Tag);
        Assert.Contains(_log, l => l.Contains("(models-2026-11) is not a game version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uses_the_tag_github_redirected_to_when_the_API_is_out_of_calls()
    {
        await using var server = new FakeGitHub();
        var release = GameRelease(server, "v0.77.0", manifest: false);
        server.ApiRateLimited = true;

        var found = await Feed(server).FindLatestAsync(Within(20));

        Assert.Equal("redirect", found.Source);
        Assert.Equal("v0.77.0", found.Tag);
        Assert.Equal(server.Download(release, "GrudgeLap-Setup-0.77.0.exe"), found.Setup!.Url);
        Assert.Equal(server.Download(release, "GrudgeLap-Setup-0.77.0.exe.sha256"), found.Setup.ShaUrl);
        Assert.Null(found.Setup.Sha256);
        Assert.Null(found.Setup.Size);

        // And the package found that way downloads and verifies through its .sha256 file.
        var folder = Directory.CreateTempSubdirectory("grudgelap-update-tests-").FullName;
        try
        {
            var client = new UpdateClient(_http, new UpdateClientOptions { Reserve = 0 });
            var path = await client.DownloadAsync(found.Setup, folder, Within(20));
            Assert.Equal(release.Assets["GrudgeLap-Setup-0.77.0.exe"].Content, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Says_until_when_the_rate_limit_is_used_up()
    {
        await using var server = new FakeGitHub();
        GameRelease(server, "v0.77.0", manifest: false);
        server.ApiRateLimited = true;
        server.WebDown = true;

        var error = await Assert.ThrowsAsync<UpdateException>(() => Feed(server).FindLatestAsync(Within(20)));

        Assert.Equal(UpdateFailure.RateLimited, error.Kind);
        Assert.Matches(@"^GitHub's limit of update checks for this connection is used up \(until \d\d:\d\d\)$", error.Message);
        Assert.NotNull(error.RetryAt);
    }

    [Fact]
    public async Task Names_the_failed_connection_when_nothing_answers()
    {
        // A port nothing listens on: every route fails to connect.
        var endpoints = UpdateEndpoints.ForFeed(new Uri("http://127.0.0.1:1/"), "korayorhun", "GrudgeLap");
        var feed = new UpdateFeed(_http, endpoints) { RequestTimeout = TimeSpan.FromSeconds(10) };

        var error = await Assert.ThrowsAsync<UpdateException>(() => feed.FindLatestAsync(Within(30)));

        Assert.Equal(UpdateFailure.Network, error.Kind);
        Assert.Equal("no connection to 127.0.0.1", error.Message);
    }

    [Fact]
    public async Task Says_when_a_sign_in_page_answers_instead_of_github()
    {
        await using var server = new FakeGitHub();
        GameRelease(server, "v0.77.0");
        server.SignInPage = true;

        var error = await Assert.ThrowsAsync<UpdateException>(() => Feed(server).FindLatestAsync(Within(20)));

        Assert.Equal(UpdateFailure.BadData, error.Kind);
        Assert.Equal("update.json is not JSON (a sign-in page of a hotspot or a proxy?)", error.Message);
    }
    [Fact]
    public async Task Says_when_no_release_has_packages()
    {
        await using var server = new FakeGitHub();
        server.AddRelease("models-2026-11").Add("model.glb", [1]);

        var error = await Assert.ThrowsAsync<UpdateException>(() => Feed(server).FindLatestAsync(Within(20)));

        Assert.Equal(UpdateFailure.NotFound, error.Kind);
    }

    [Theory]
    [InlineData("v0.77.0", "0.77.0.0")]
    [InlineData("0.77.0", "0.77.0.0")]
    [InlineData("V1.2", "1.2.0.0")]
    [InlineData("v0.77.0.1", "0.77.0.1")]
    public void Reads_version_tags(string tag, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("models-2026-11")]
    [InlineData("v0.77.0-rc1")]
    [InlineData("v0.77.0.1.2")]
    [InlineData("v")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("v 0.77.0")]
    public void Refuses_tags_that_are_not_game_versions(string? tag) => Assert.False(ReleaseVersion.TryParse(tag, out _));

    [Fact]
    public void Compares_three_and_four_part_versions_alike()
    {
        ReleaseVersion.TryParse("v0.77.0", out var three);
        ReleaseVersion.TryParse("v0.77.0.0", out var four);
        ReleaseVersion.TryParse("v0.77.0.1", out var patch);
        Assert.Equal(three, four);
        Assert.True(patch > three);
        Assert.Equal("0.77.0", ReleaseVersion.Text(three));
        Assert.Equal("0.77.0.1", ReleaseVersion.Text(patch));
        Assert.Equal("GrudgeLap-Setup-0.77.0.exe", ReleaseVersion.SetupName("v0.77.0"));
        Assert.Equal("GrudgeLap-Portable-0.77.0.zip", ReleaseVersion.PortableName("v0.77.0"));
    }

    [Fact]
    public void A_last_check_in_the_future_counts_as_due()
    {
        var interval = TimeSpan.FromMinutes(2);
        const long now = 1_800_000_000;
        Assert.True(UpdateSchedule.IsDue(now + 86_400, now, interval));
        Assert.False(UpdateSchedule.IsDue(now - 60, now, interval));
        Assert.True(UpdateSchedule.IsDue(now - 120, now, interval));
        Assert.True(UpdateSchedule.IsDue(0, now, interval));
        Assert.Equal(TimeSpan.FromSeconds(30), UpdateSchedule.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(3), UpdateSchedule.RetryDelay(2));
        Assert.Null(UpdateSchedule.RetryDelay(3));
    }

    [Fact]
    public void Parses_the_update_json_that_release_ps1_writes()
    {
        // tools/release.ps1's ConvertTo-Json output for a release, as it came out of the script (8 November 2026).
        const string json = """
            {
              "version": "0.77.0",
              "tag": "v0.77.0",
              "published": "2026-11-08T23:56:51Z",
              "setup": {
                "name": "GrudgeLap-Setup-0.77.0.exe",
                "size": 478575344,
                "sha256": "13f38ba96b4ff1c4001b165ab740423d34ce3eb4dddc32c8826803bd394d6785"
              },
              "portable": {
                "name": "GrudgeLap-Portable-0.77.0.zip",
                "size": 535192626,
                "sha256": "a901ec09c3bc89ef7e739589297174fdffc02c2e4ff83b8108dfa388602e5471"
              }
            }
            """;
        var endpoints = UpdateEndpoints.GitHub("korayorhun", "GrudgeLap");

        var release = UpdateFeed.ParseManifest(Encoding.UTF8.GetBytes(json), endpoints);

        Assert.Equal("0.77.0", release.VersionText);
        Assert.Equal("https://github.com/korayorhun/GrudgeLap/releases/download/v0.77.0/GrudgeLap-Setup-0.77.0.exe", release.Setup!.Url.AbsoluteUri);
        Assert.Equal(478575344, release.Setup.Size);
        Assert.Equal("a901ec09c3bc89ef7e739589297174fdffc02c2e4ff83b8108dfa388602e5471", release.Portable!.Sha256);
        Assert.Equal("https://github.com/korayorhun/GrudgeLap/releases/tag/v0.77.0", release.Page.AbsoluteUri);
    }

    [Fact]
    public void Refuses_an_update_json_that_names_unsafe_files()
    {
        var endpoints = UpdateEndpoints.GitHub("korayorhun", "GrudgeLap");
        var json = """{"version":"0.77.0","tag":"v0.77.0","setup":{"name":"..\\GrudgeLap.exe","size":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}""";

        var error = Assert.Throws<UpdateException>(() => UpdateFeed.ParseManifest(Encoding.UTF8.GetBytes(json), endpoints));

        Assert.Equal(UpdateFailure.BadData, error.Kind);
    }

    [Fact]
    public void Builds_the_github_urls()
    {
        var endpoints = UpdateEndpoints.GitHub("korayorhun", "GrudgeLap");
        Assert.Equal("https://github.com/korayorhun/GrudgeLap/releases/latest/download/update.json", endpoints.LatestManifest.AbsoluteUri);
        Assert.Equal("https://github.com/korayorhun/GrudgeLap/releases/download/v0.77.0/GrudgeLap-Setup-0.77.0.exe", endpoints.Download("v0.77.0", "GrudgeLap-Setup-0.77.0.exe").AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/korayorhun/GrudgeLap/releases/latest", endpoints.ApiLatest.AbsoluteUri);
        Assert.Equal("v0.76.0", UpdateFeed.TagFromManifestRedirect([new Uri("https://github.com/korayorhun/GrudgeLap/releases/download/v0.76.0/update.json")]));
    }
}
