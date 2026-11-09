using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrudgeLap.Game;

namespace GrudgeLap.Update.Tests;

/// <summary>
/// An in-process stand-in for GitHub on the loopback address, laid out as <see cref="UpdateEndpoints.ForFeed"/> expects:
/// <c>/korayorhun/GrudgeLap/releases/latest/download/…</c> and <c>…/releases/download/{tag}/{name}</c> redirect, as
/// github.com does, to a signed <c>/blob/…?sig=</c> URL that works once (release-assets.githubusercontent.com's expire
/// within minutes), and <c>/api/repos/korayorhun/GrudgeLap/releases[/latest]</c> answers as the REST API. Each asset can
/// drop its connection or fall silent part-way, ignore ranges, trickle, or fail with a status.
/// </summary>
internal sealed class FakeGitHub : IAsyncDisposable
{
    private const string RepoPath = "/korayorhun/GrudgeLap/";
    private const string ApiPath = "/api/repos/korayorhun/GrudgeLap/";
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly ConcurrentDictionary<string, (FakeAsset Asset, bool Expired)> _signatures = new();
    private readonly AsyncLocal<Hit?> _current = new();
    private int _signatureSerial;

    public FakeGitHub()
    {
        // A free port from the system, then http.sys on it (a loopback prefix needs no administrator rights).
        for (var attempt = 0; ; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                _listener.Start();
                Root = new Uri($"http://127.0.0.1:{port}/");
                break;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                _listener.Close();
            }
        }
        _loop = Task.Run(LoopAsync);
    }

    public Uri Root { get; }
    public UpdateEndpoints Endpoints => UpdateEndpoints.ForFeed(Root, "korayorhun", "GrudgeLap");
    public List<FakeRelease> Releases { get; } = [];
    /// <summary>The tag /releases/latest points at; null: no release.</summary>
    public string? LatestTag { get; set; }
    /// <summary>The API answers 403 with X-RateLimit-Remaining: 0, as for an address that used its 60 calls.</summary>
    public bool ApiRateLimited { get; set; }
    /// <summary>The repository's web paths (github.com) answer 503.</summary>
    public bool WebDown { get; set; }
    /// <summary>Every request gets a hotspot's sign-in page with a 200, as on a hotel's or a train's Wi-Fi.</summary>
    public bool SignInPage { get; set; }
    public ConcurrentQueue<Hit> Hits { get; } = new();
    public int ApiCalls => Hits.Count(h => h.Path.StartsWith(ApiPath, StringComparison.Ordinal));

    /// <summary>A request, recorded as it arrives (an answer that stalls ends only with the test); its status once decided.</summary>
    public sealed class Hit(string path, string query, string? range)
    {
        public string Path { get; } = path;
        public string Query { get; } = query;
        public string? Range { get; } = range;
        public int Status { get; set; }
    }

    public FakeRelease AddRelease(string tag, bool latest = true)
    {
        var release = new FakeRelease(tag);
        Releases.Add(release);
        if (latest) LatestTag = tag;
        return release;
    }

    public Uri Download(FakeRelease release, string name) => Endpoints.Download(release.Tag, name);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                continue;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var path = Uri.UnescapeDataString(request.Url!.AbsolutePath);
        var hit = new Hit(path, request.Url!.Query, request.Headers["Range"]);
        Hits.Enqueue(hit);
        _current.Value = hit;
        try
        {
            hit.Status = await RouteAsync(context, path).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The client went away mid-answer (the tests' drops and stalls do that on purpose).
            try
            {
                context.Response.Abort();
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }

    private async Task<int> RouteAsync(HttpListenerContext context, string path)
    {
        if (SignInPage) return Answer(context, 200, "<html><body>Sign in to use the Wi-Fi</body></html>", "text/html");
        if (path.StartsWith(ApiPath, StringComparison.Ordinal)) return Api(context, path[ApiPath.Length..]);
        if (path.StartsWith("/blob/", StringComparison.Ordinal)) return await BlobAsync(context).ConfigureAwait(false);
        if (!path.StartsWith(RepoPath, StringComparison.Ordinal)) return Answer(context, 404, "not found");
        if (WebDown) return Answer(context, 503, "unavailable");
        var rest = path[RepoPath.Length..];
        const string latest = "releases/latest/download/";
        const string download = "releases/download/";
        if (rest.StartsWith(latest, StringComparison.Ordinal))
        {
            if (LatestTag is null) return Answer(context, 404, "not found");
            return Redirect(context, Endpoints.Download(LatestTag, rest[latest.Length..]));
        }
        if (rest.StartsWith(download, StringComparison.Ordinal))
        {
            var parts = rest[download.Length..].Split('/', 2);
            var asset = Releases.FirstOrDefault(r => r.Tag == parts[0])?.Assets.GetValueOrDefault(parts[1]);
            if (asset is null) return Answer(context, 404, "not found");
            if (asset.FailWith is { } failure) return Answer(context, failure, "failing on purpose");
            // As github.com: a signed URL on another path that works for one request (an expired one gets 403).
            var signature = $"s{Interlocked.Increment(ref _signatureSerial)}";
            _signatures[signature] = (asset, asset.ExpireFirstSignature && Interlocked.Exchange(ref asset.FirstSignatureIssued, 1) == 0);
            return Redirect(context, new Uri(Root, $"blob/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}?sig={signature}"));
        }
        return Answer(context, 404, "not found");
    }

    private async Task<int> BlobAsync(HttpListenerContext context)
    {
        var signature = context.Request.QueryString["sig"] ?? "";
        if (!_signatures.TryRemove(signature, out var entry) || entry.Expired)
        {
            // Release-assets' answer to a signature used twice or too late.
            return Answer(context, 403, "<Error><Code>AuthenticationFailed</Code><Message>Request has expired</Message></Error>");
        }
        var asset = entry.Asset;
        Interlocked.Increment(ref asset.Requests);
        var response = context.Response;
        var content = asset.Content;
        long start = 0;
        var partial = false;
        var range = context.Request.Headers["Range"];
        if (!asset.IgnoreRange && range is not null && range.StartsWith("bytes=", StringComparison.Ordinal) && range.EndsWith('-')
            && long.TryParse(range["bytes=".Length..^1], out var from))
        {
            if (from >= content.Length)
            {
                response.AddHeader("Content-Range", $"bytes */{content.Length}");
                return Answer(context, 416, "");
            }
            start = from;
            partial = true;
        }
        response.StatusCode = partial ? 206 : 200;
        if (_current.Value is { } hit) hit.Status = response.StatusCode;
        if (partial) response.AddHeader("Content-Range", $"bytes {start}-{content.Length - 1}/{content.Length}");
        response.AddHeader("Accept-Ranges", "bytes");
        response.ContentType = "application/octet-stream";
        response.ContentLength64 = content.Length - start;
        var output = response.OutputStream;

        // A fault happens once per asset, on the first request that reaches its point.
        long? cut = null;
        var stall = false;
        if (asset.DropAt is { } drop && (long)(content.Length * drop) > start && Interlocked.Exchange(ref asset.FaultDone, 1) == 0) cut = (long)(content.Length * drop);
        else if (asset.StallAt is { } still && (long)(content.Length * still) > start && Interlocked.Exchange(ref asset.FaultDone, 1) == 0)
        {
            cut = (long)(content.Length * still);
            stall = true;
        }
        var slice = asset.BytesPerSecond > 0 ? Math.Max(1024, asset.BytesPerSecond / 10) : 64 * 1024;
        var position = start;
        var end = cut ?? content.Length;
        while (position < end)
        {
            var count = (int)Math.Min(slice, end - position);
            await output.WriteAsync(content.AsMemory((int)position, count), _stop.Token).ConfigureAwait(false);
            position += count;
            if (asset.BytesPerSecond > 0) await Task.Delay(TimeSpan.FromSeconds((double)count / asset.BytesPerSecond), _stop.Token).ConfigureAwait(false);
        }
        if (cut is not null)
        {
            await output.FlushAsync(_stop.Token).ConfigureAwait(false);
            // Long enough for the bytes so far to reach the client before the connection goes.
            await Task.Delay(200, _stop.Token).ConfigureAwait(false);
            if (stall)
            {
                // Half-open: nothing more and no end, until the test is over.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The server is closing.
                }
            }
            response.Abort();
            return partial ? 206 : 200;
        }
        response.Close();
        return partial ? 206 : 200;
    }

    private int Api(HttpListenerContext context, string rest)
    {
        if (ApiRateLimited)
        {
            context.Response.AddHeader("X-RateLimit-Limit", "60");
            context.Response.AddHeader("X-RateLimit-Remaining", "0");
            context.Response.AddHeader("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(37).ToUnixTimeSeconds().ToString());
            return Answer(context, 403, """{"message":"API rate limit exceeded for 127.0.0.1. (But here's the good news: Authenticated requests get a higher rate limit.)"}""", "application/json");
        }
        context.Response.AddHeader("X-RateLimit-Limit", "60");
        context.Response.AddHeader("X-RateLimit-Remaining", "59");
        context.Response.AddHeader("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds().ToString());
        if (rest == "releases/latest")
        {
            var latest = Releases.FirstOrDefault(r => r.Tag == LatestTag);
            return latest is null ? Answer(context, 404, """{"message":"Not Found"}""", "application/json") : Answer(context, 200, JsonSerializer.Serialize(ReleaseJson(latest)), "application/json");
        }
        if (rest == "releases") return Answer(context, 200, JsonSerializer.Serialize(Releases.AsEnumerable().Reverse().Select(ReleaseJson).ToList()), "application/json");
        return Answer(context, 404, """{"message":"Not Found"}""", "application/json");
    }

    private object ReleaseJson(FakeRelease release) => new Dictionary<string, object?>
    {
        ["tag_name"] = release.Tag,
        ["name"] = "GrudgeLap " + release.Tag,
        ["draft"] = release.Draft,
        ["prerelease"] = release.Prerelease,
        ["html_url"] = new Uri(Root, $"korayorhun/GrudgeLap/releases/tag/{release.Tag}").AbsoluteUri,
        ["body"] = "notes of " + release.Tag,
        ["assets"] = release.Assets.Select(a => new Dictionary<string, object?>
        {
            ["name"] = a.Key,
            ["size"] = a.Value.Content.Length,
            ["state"] = "uploaded",
            ["browser_download_url"] = Download(release, a.Key).AbsoluteUri,
            ["digest"] = a.Value.PublishDigest ? "sha256:" + Sha(a.Value.Content) : null,
        }).ToList(),
    };

    private int Redirect(HttpListenerContext context, Uri location)
    {
        if (_current.Value is { } hit) hit.Status = 302;
        context.Response.StatusCode = 302;
        context.Response.RedirectLocation = location.AbsoluteUri;
        context.Response.Close();
        return 302;
    }

    private int Answer(HttpListenerContext context, int status, string body, string type = "text/plain")
    {
        if (_current.Value is { } hit) hit.Status = status;
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = type;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
        return status;
    }

    public static string Sha(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        _listener.Close();
        try
        {
            await _loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The loop ends with the listener.
        }
        _stop.Dispose();
    }
}

/// <summary>A release on the fake server: a tag and its assets by name.</summary>
internal sealed class FakeRelease(string tag)
{
    public string Tag { get; } = tag;
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public Dictionary<string, FakeAsset> Assets { get; } = new(StringComparer.Ordinal);

    /// <summary>Adds a package with its .sha256 beside it ("hash  name", as tools/release.ps1 writes it).</summary>
    public FakeAsset Add(string name, byte[] content, bool withShaFile = true)
    {
        var asset = new FakeAsset(content);
        Assets[name] = asset;
        if (withShaFile) Assets[name + ".sha256"] = new FakeAsset(Encoding.ASCII.GetBytes($"{FakeGitHub.Sha(content).ToUpperInvariant()}  {name}"));
        return asset;
    }

    /// <summary>The release's update.json for its setup and portable packages, as tools/release.ps1 writes it.</summary>
    public FakeAsset AddManifest(string? setupSha = null, bool bom = false)
    {
        var version = Tag.TrimStart('v');
        var setupName = $"GrudgeLap-Setup-{version}.exe";
        var portableName = $"GrudgeLap-Portable-{version}.zip";
        object? Entry(string name, string? sha) => Assets.TryGetValue(name, out var a) ? new { name, size = a.Content.Length, sha256 = sha ?? FakeGitHub.Sha(a.Content) } : null;
        var json = JsonSerializer.Serialize(new { version, tag = Tag, published = "2026-11-09T02:00:00Z", setup = Entry(setupName, setupSha), portable = Entry(portableName, null), notes = "notes" });
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bom) bytes = [.. Encoding.UTF8.Preamble, .. bytes];
        var asset = new FakeAsset(bytes);
        Assets["update.json"] = asset;
        return asset;
    }
}

/// <summary>An asset's bytes and how the fake server misbehaves when serving them.</summary>
internal sealed class FakeAsset(byte[] content)
{
    public byte[] Content { get; } = content;
    /// <summary>Share of the file after which the first request that gets there loses its connection.</summary>
    public double? DropAt { get; set; }
    /// <summary>Share of the file after which the first request that gets there falls silent with its connection open.</summary>
    public double? StallAt { get; set; }
    /// <summary>Answers 200 with the whole file even to a range request.</summary>
    public bool IgnoreRange { get; set; }
    /// <summary>Trickles at this rate; 0 as fast as it goes.</summary>
    public int BytesPerSecond { get; set; }
    /// <summary>Answers this status on its github.com path instead of redirecting.</summary>
    public int? FailWith { get; set; }
    /// <summary>The first signed URL handed out has already expired when it is used.</summary>
    public bool ExpireFirstSignature { get; set; }
    public bool PublishDigest { get; set; } = true;
    public int Requests;
    public int FaultDone;
    public int FirstSignatureIssued;
}
