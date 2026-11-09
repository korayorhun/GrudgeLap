using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrudgeLap.Game;

/// <summary>
/// Where the releases are read from: GitHub itself, or a stand-in on one host (<c>GRUDGELAP_UPDATE_FEED</c>, the tests'
/// in-process server) laid out with the same paths, so that every route below can be exercised without GitHub.
/// </summary>
/// <param name="Web">The repository's page, ending in a slash: release downloads hang under it.</param>
/// <param name="Api">The repository in the REST API, ending in a slash.</param>
internal sealed record UpdateEndpoints(Uri Web, Uri Api)
{
    public static UpdateEndpoints GitHub(string owner, string repo) =>
        new(new Uri($"https://github.com/{owner}/{repo}/"), new Uri($"https://api.github.com/repos/{owner}/{repo}/"));

    /// <summary>A single-host feed: <c>{feed}/{owner}/{repo}/...</c> for the downloads, <c>{feed}/api/repos/{owner}/{repo}/...</c> for the API.</summary>
    public static UpdateEndpoints ForFeed(Uri feed, string owner, string repo)
    {
        var root = feed.AbsoluteUri.EndsWith('/') ? feed : new Uri(feed.AbsoluteUri + "/");
        return new(new Uri(root, $"{owner}/{repo}/"), new Uri(root, $"api/repos/{owner}/{repo}/"));
    }

    /// <summary>
    /// The newest release's manifest. GitHub answers this path itself (a redirect to the release's asset), without
    /// the REST API and its limit of 60 calls an hour for every unauthenticated address, which a home connection
    /// behind a carrier's shared address can find used up by strangers.
    /// </summary>
    public Uri LatestManifest => new(Web, "releases/latest/download/" + UpdateFeed.ManifestName);
    public Uri Download(string tag, string name) => new(Web, $"releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");
    public Uri Page(string tag) => new(Web, $"releases/tag/{Uri.EscapeDataString(tag)}");
    public Uri ApiLatest => new(Api, "releases/latest");
    public Uri ApiList => new(Api, "releases?per_page=30");
}

/// <summary>One file of a release to download.</summary>
/// <param name="Name">The asset's exact name, a plain file name (it names the file on disk too).</param>
/// <param name="Size">Its length in bytes when the release states it.</param>
/// <param name="Sha256">Its SHA-256 as hex when the release states it (update.json, the API's digest); otherwise read from <paramref name="ShaUrl"/>.</param>
internal sealed record UpdatePackage(string Name, Uri Url, long? Size, string? Sha256, Uri? ShaUrl);

/// <summary>A release the feed found, with the package for each kind of copy it carries.</summary>
/// <param name="Source">Which route found it, for the log: update.json, api, release list or redirect.</param>
internal sealed record UpdateRelease(Version Version, string Tag, Uri Page, string Notes, UpdatePackage? Setup, UpdatePackage? Portable, string Source)
{
    public string VersionText => ReleaseVersion.Text(Version);
}

/// <summary>Release tags and the package names derived from them.</summary>
internal static partial class ReleaseVersion
{
    /// <summary>
    /// True for a plain version tag, "v0.77.0" or "0.77.0" (two to four numbers), with the version padded to four
    /// parts so that 0.77.0 and 0.77.0.0 compare equal. Anything else is not a game release: the car model releases
    /// ("models-2026-11"), a suffixed tag ("v0.77.0-rc1").
    /// </summary>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (tag is null || !TagPattern().IsMatch(tag)) return false;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return false;
        version = Normalise(parsed);
        return true;
    }

    public static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>The version as the game shows it: three parts, four only when the fourth is set.</summary>
    public static string Text(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);

    /// <summary>The version part of a tag as it appears in the asset names ("v0.77.0" gives "0.77.0").</summary>
    public static string NameText(string tag) => tag.Length > 0 && tag[0] is 'v' or 'V' ? tag[1..] : tag;

    public static string SetupName(string tag) => $"GrudgeLap-Setup-{NameText(tag)}.exe";
    public static string PortableName(string tag) => $"GrudgeLap-Portable-{NameText(tag)}.zip";

    [GeneratedRegex(@"^[vV]?\d{1,9}(\.\d{1,9}){1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}

/// <summary>When a check is due and when a failed one is tried again within the session.</summary>
internal static class UpdateSchedule
{
    /// <summary>
    /// Starts within <paramref name="interval"/> of the last check share it, so that a crash loop does not hammer
    /// GitHub. A last check in the future (the clock was wrong when it was written, or has been put back since) counts
    /// as due: until 0.76 such a stamp stopped the automatic checks until the clock caught up with it.
    /// </summary>
    public static bool IsDue(long lastCheck, long now, TimeSpan interval) => lastCheck > now || now - lastCheck >= interval.TotalSeconds;

    /// <summary>The wait before the n-th in-session retry of a failed automatic update (n from 1); null when it is not tried again.</summary>
    public static TimeSpan? RetryDelay(int retry) => retry switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(3),
        _ => null,
    };
}

/// <summary>
/// Finds the newest game release. Three routes, cheapest and most certain first: the release's own update.json
/// (written by tools/release.ps1 with the packages' exact names, sizes and SHA-256; read through github.com, outside
/// the REST API's limit), the API's latest release (sizes and the digests GitHub computes), and the API's release list
/// (when the latest release is not a game version, e.g. a car model release made latest by mistake). When the API is
/// out of calls, the latest tag that github.com's own redirect gave away still names the packages exactly.
/// </summary>
internal sealed partial class UpdateFeed(HttpClient http, UpdateEndpoints endpoints, Action<string>? log = null)
{
    public const string ManifestName = "update.json";
    private const int MaxJsonBytes = 4 << 20;

    /// <summary>The longest a metadata request may take, all of it: these answers are a few kilobytes.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<UpdateRelease> FindLatestAsync(CancellationToken ct)
    {
        UpdateException? failure = null;
        string? latestTag = null;

        // 1. update.json through github.com's "latest" redirect.
        try
        {
            var (status, body, hops, _) = await GetAsync(endpoints.LatestManifest, "application/json", ct).ConfigureAwait(false);
            latestTag = TagFromManifestRedirect(hops);
            if (status == HttpStatusCode.OK)
            {
                var release = ParseManifest(body, endpoints);
                Log($"read the update.json of {release.Tag}");
                return release;
            }
            Log(status == HttpStatusCode.NotFound
                ? $"the latest release{(latestTag is null ? "" : $" ({latestTag})")} has no update.json"
                : $"update.json: HTTP {(int)status}");
        }
        catch (UpdateException ex)
        {
            Log($"update.json: {ex.Message}");
            failure = Remember(failure, ex);
        }

        // 2. The API's latest release.
        try
        {
            var (status, body, _, headers) = await GetAsync(endpoints.ApiLatest, "application/vnd.github+json", ct).ConfigureAwait(false);
            LogRateLimit(headers);
            if (status != HttpStatusCode.OK) throw ApiFailure(status, headers, body);
            using var doc = ParseJson(body, "the API's answer");
            if (FromApiRelease(doc.RootElement, endpoints, "api") is { } release)
            {
                Log($"API latest release: {release.Tag}");
                return release;
            }
            var tag = doc.RootElement.ValueKind == JsonValueKind.Object ? Str(doc.RootElement, "tag_name") : null;
            Log($"the API's latest release ({tag}) is not a game version with packages: reading the release list");
        }
        catch (UpdateException ex)
        {
            Log($"API latest release: {ex.Message}");
            failure = Remember(failure, ex);
        }

        // 2b. The API is out of reach or out of calls, but github.com named the latest tag on the way to update.json:
        // the packages' names follow from it, and their .sha256 files give the checksums.
        if (failure is not null && ReleaseVersion.TryParse(latestTag, out var tagged))
        {
            Log($"using the latest tag github.com redirected to ({latestTag}) without the API");
            return new UpdateRelease(tagged, latestTag!, endpoints.Page(latestTag!), "",
                Derived(endpoints, latestTag!, ReleaseVersion.SetupName(latestTag!)),
                Derived(endpoints, latestTag!, ReleaseVersion.PortableName(latestTag!)), "redirect");
        }

        // 3. The API's release list: the newest published game version among them.
        try
        {
            var (status, body, _, headers) = await GetAsync(endpoints.ApiList, "application/vnd.github+json", ct).ConfigureAwait(false);
            LogRateLimit(headers);
            if (status != HttpStatusCode.OK) throw ApiFailure(status, headers, body);
            using var doc = ParseJson(body, "the release list");
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new UpdateException(UpdateFailure.BadData, "the release list is not a list");
            UpdateRelease? best = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (FromApiRelease(item, endpoints, "release list") is { } candidate && (best is null || candidate.Version > best.Version)) best = candidate;
            }
            if (best is null) throw new UpdateException(UpdateFailure.NotFound, "no game release with packages was found on GitHub");
            Log($"release list: newest game release {best.Tag}");
            return best;
        }
        catch (UpdateException ex)
        {
            Log($"release list: {ex.Message}");
            throw Remember(failure, ex);
        }
    }

    /// <summary>
    /// The reason to show when every route failed. A rate limit wins (the player can only wait, and is told until
    /// when); otherwise the first failure, which is usually the telling one (no connection, an untrusted certificate).
    /// </summary>
    private static UpdateException Remember(UpdateException? earlier, UpdateException latest) =>
        earlier is null || latest.Kind == UpdateFailure.RateLimited && earlier.Kind != UpdateFailure.RateLimited ? latest : earlier;

    private static UpdatePackage Derived(UpdateEndpoints endpoints, string tag, string name) =>
        new(name, endpoints.Download(tag, name), null, null, endpoints.Download(tag, name + ".sha256"));

    /// <summary>"…/releases/download/v0.77.0/update.json" from github.com's first redirect gives the latest tag away.</summary>
    internal static string? TagFromManifestRedirect(IReadOnlyList<Uri> hops)
    {
        foreach (var hop in hops)
        {
            var match = ManifestPath().Match(hop.AbsolutePath);
            if (match.Success) return Uri.UnescapeDataString(match.Groups["tag"].Value);
        }
        return null;
    }

    [GeneratedRegex(@"/releases/download/(?<tag>[^/]+)/update\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex ManifestPath();

    /// <summary>
    /// update.json as tools/release.ps1 writes it:
    /// <c>{"version":"0.77.0","tag":"v0.77.0","published":…,"setup":{"name":…,"size":…,"sha256":…},"portable":{…}}</c>.
    /// </summary>
    internal static UpdateRelease ParseManifest(byte[] body, UpdateEndpoints endpoints)
    {
        using var doc = ParseJson(body, "update.json");
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new UpdateException(UpdateFailure.BadData, "update.json is not an object");
        var versionText = Str(root, "version");
        if (!ReleaseVersion.TryParse(versionText, out var version)) throw new UpdateException(UpdateFailure.BadData, $"update.json names no version ({versionText})");
        var tag = Str(root, "tag") is { Length: > 0 } t ? t : "v" + versionText;
        UpdatePackage? Package(string property, string extension)
        {
            if (!root.TryGetProperty(property, out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
            var name = Str(entry, "name");
            var sha = Str(entry, "sha256");
            if (name is null || !IsPlainFileName(name) || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || !IsSha256(sha)) return null;
            return new UpdatePackage(name, endpoints.Download(tag, name), Size(entry), sha!.ToLowerInvariant(), null);
        }
        var setup = Package("setup", ".exe");
        var portable = Package("portable", ".zip");
        if (setup is null && portable is null) throw new UpdateException(UpdateFailure.BadData, "update.json names no package");
        return new UpdateRelease(version, tag, endpoints.Page(tag), Str(root, "notes") ?? "", setup, portable, "update.json");
    }

    /// <summary>A published game release from the API (a release object of /releases/latest or of the list); null when it is not one.</summary>
    internal static UpdateRelease? FromApiRelease(JsonElement release, UpdateEndpoints endpoints, string source)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;
        if (IsTrue(release, "draft") || IsTrue(release, "prerelease")) return null;
        var tag = Str(release, "tag_name");
        if (!ReleaseVersion.TryParse(tag, out var version)) return null;
        var assets = new Dictionary<string, (Uri Url, long? Size, string? Sha)>(StringComparer.Ordinal);
        if (release.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var name = Str(asset, "name");
                var url = Str(asset, "browser_download_url");
                // An asset still uploading ("starter") has no content yet.
                if (name is null || url is null || Str(asset, "state") is { } state && state != "uploaded") continue;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
                var size = Size(asset);
                // GitHub computes "sha256:<hex>" for every asset uploaded since mid-2025.
                var digest = Str(asset, "digest");
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && IsSha256(digest[7..]) ? digest[7..].ToLowerInvariant() : null;
                assets[name] = (uri, size, sha);
            }
        }
        UpdatePackage? Package(string name)
        {
            // Exact names only: the Unreal preview ("GrudgeLapUE-Preview-…zip") or any other extra asset is never taken.
            if (!assets.TryGetValue(name, out var a)) return null;
            var shaUrl = assets.TryGetValue(name + ".sha256", out var s) ? s.Url : null;
            return a.Sha is null && shaUrl is null ? null : new UpdatePackage(name, a.Url, a.Size, a.Sha, shaUrl);
        }
        var setup = Package(ReleaseVersion.SetupName(tag!));
        var portable = Package(ReleaseVersion.PortableName(tag!));
        if (setup is null && portable is null) return null;
        var page = Str(release, "html_url") is { } html && Uri.TryCreate(html, UriKind.Absolute, out var p) ? p : endpoints.Page(tag!);
        return new UpdateRelease(version, tag!, page, Str(release, "body") ?? "", setup, portable, source);
    }

    private UpdateException ApiFailure(HttpStatusCode status, ResponseHeaders headers, byte[] body)
    {
        var remaining = headers.Get("X-RateLimit-Remaining");
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests && (remaining == "0" || status == HttpStatusCode.TooManyRequests))
        {
            DateTimeOffset? reset = long.TryParse(headers.Get("X-RateLimit-Reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;
            var until = reset is { } r ? $" (until {r.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)})" : "";
            return new UpdateException(UpdateFailure.RateLimited, $"GitHub's limit of update checks for this connection is used up{until}") { RetryAt = reset };
        }
        var message = "";
        try
        {
            using var doc = ParseJson(body, "the error");
            message = doc.RootElement.ValueKind == JsonValueKind.Object ? Str(doc.RootElement, "message") ?? "" : "";
        }
        catch (Exception)
        {
            // Not JSON (a proxy's page): the status says enough.
        }
        return new UpdateException(status == HttpStatusCode.NotFound ? UpdateFailure.NotFound : UpdateFailure.Network,
            $"GitHub answered HTTP {(int)status}{(message.Length > 0 ? $" ({Shorten(message, 80)})" : "")}");
    }

    private void LogRateLimit(ResponseHeaders headers)
    {
        if (headers.Get("X-RateLimit-Remaining") is not { } remaining) return;
        var reset = long.TryParse(headers.Get("X-RateLimit-Reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "?";
        Log($"API calls left this hour: {remaining} of {headers.Get("X-RateLimit-Limit") ?? "?"} (resets at {reset})");
    }

    /// <summary>A small GET through the redirects, read whole: (status, body, the hops after the first URL, headers).</summary>
    private async Task<(HttpStatusCode Status, byte[] Body, IReadOnlyList<Uri> Hops, ResponseHeaders Headers)> GetAsync(Uri uri, string accept, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var reply = await UpdateHttp.SendAsync(http, uri, request =>
            {
                request.Headers.Accept.ParseAdd(accept);
                if (accept.Contains("github", StringComparison.Ordinal)) request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            }, timeout.Token).ConfigureAwait(false);
            var response = reply.Response;
            var headers = new ResponseHeaders(response);
            if (response.Content.Headers.ContentLength > MaxJsonBytes) throw new UpdateException(UpdateFailure.BadData, $"{uri.Host} sent {response.Content.Headers.ContentLength} bytes for a small file");
            var body = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            return (response.StatusCode, body, reply.Hops, headers);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException(UpdateFailure.Timeout, $"{uri.Host} did not answer within {RequestTimeout.TotalSeconds:0} s");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw UpdateHttp.Classify(ex, uri.Host);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            copy.Write(buffer, 0, read);
            if (copy.Length > MaxJsonBytes) throw new UpdateException(UpdateFailure.BadData, "the answer is too large for release metadata");
        }
        return copy.ToArray();
    }

    /// <summary>
    /// Parses UTF-8 JSON, a byte-order mark allowed (Windows PowerShell writes one). Anything else with a 200 (a
    /// hotspot's sign-in page, a filtering proxy's notice) is a readable failure, not an exception from the parser.
    /// </summary>
    private static JsonDocument ParseJson(byte[] body, string what)
    {
        var span = body.AsMemory();
        if (span.Span.StartsWith(Encoding.UTF8.Preamble)) span = span[3..];
        try
        {
            return JsonDocument.Parse(span);
        }
        catch (JsonException ex)
        {
            throw new UpdateException(UpdateFailure.BadData, $"{what} is not JSON (a sign-in page of a hotspot or a proxy?)", ex);
        }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool IsTrue(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static long? Size(JsonElement e) => e.TryGetProperty("size", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n > 0 ? n : null;

    internal static bool IsSha256(string? hex) => hex is { Length: 64 } && hex.All(Uri.IsHexDigit);

    /// <summary>A name that stays inside the download folder: no directory part, no reserved characters.</summary>
    internal static bool IsPlainFileName(string name) =>
        name.Length is > 0 and < 200 && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.Trim('.').Length > 0;

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    private void Log(string line) => log?.Invoke(line);

    /// <summary>The few response headers the feed reads, by name, case-insensitively.</summary>
    internal sealed class ResponseHeaders(HttpResponseMessage response)
    {
        private readonly Dictionary<string, string> _values = Collect(response);

        public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

        private static Dictionary<string, string> Collect(HttpResponseMessage response)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, list) in response.Headers) values[key] = string.Join(",", list);
            foreach (var (key, list) in response.Content.Headers) values[key] = string.Join(",", list);
            return values;
        }
    }
}
