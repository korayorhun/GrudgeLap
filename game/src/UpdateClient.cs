using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace GrudgeLap.Game;

/// <summary>What kind of failure stopped an update step; the updater decides from it whether trying again can help.</summary>
internal enum UpdateFailure
{
    /// <summary>The connection failed or was lost (tried again).</summary>
    Network,
    /// <summary>The server sent nothing for too long (tried again).</summary>
    Timeout,
    /// <summary>GitHub's API has no calls left for this address until <see cref="UpdateException.RetryAt"/>.</summary>
    RateLimited,
    /// <summary>The secure connection could not be made (an untrusted certificate, a TLS-inspecting proxy).</summary>
    Certificate,
    /// <summary>The release or the package is not there.</summary>
    NotFound,
    /// <summary>The disk is full or the download folder cannot be written.</summary>
    Disk,
    /// <summary>The downloaded file does not match its published SHA-256.</summary>
    Checksum,
    /// <summary>The package on the server is not the one the release describes (it was replaced meanwhile).</summary>
    Changed,
    /// <summary>The release metadata could not be read.</summary>
    BadData,
}

/// <summary>An update step that failed, with a reason short and plain enough for the HUD.</summary>
internal sealed class UpdateException(UpdateFailure kind, string reason, Exception? inner = null) : Exception(reason, inner)
{
    public UpdateFailure Kind { get; } = kind;
    /// <summary>When the server asked to be left alone until (Retry-After, X-RateLimit-Reset).</summary>
    public DateTimeOffset? RetryAt { get; init; }
    public bool Transient => Kind is UpdateFailure.Network or UpdateFailure.Timeout;
}

/// <summary>The updater's HTTP: one client for the session, and redirects followed by hand.</summary>
internal static class UpdateHttp
{
    public const string UserAgent = "GrudgeLap-Updater";
    private const int MaxRedirects = 8;
    private static HttpClient? _shared;

    /// <summary>The game's client, made on first use and kept for the session (one connection pool, one proxy lookup).</summary>
    public static HttpClient Shared => LazyInitializer.EnsureInitialized(ref _shared, () => CreateClient());

    /// <summary>
    /// .NET's own HTTP stack instead of Godot's HttpRequest: TLS through Windows (SChannel), which fetches a missing
    /// root certificate the way a browser does, where Godot trusted only the roots already in the store; the system's
    /// proxy with the user's own sign-in, which Godot ignored; reads that can be cancelled from another thread, where
    /// cancelling Godot's request on a dead connection blocked the main thread for good.
    /// </summary>
    /// <param name="useProxy">False for the tests' server on the loopback address, which no proxy should see.</param>
    public static HttpClient CreateClient(string? version = null, bool useProxy = true)
    {
        var handler = new SocketsHttpHandler
        {
            // Followed by SendAsync: every hop is logged, and a resumed download asks github.com again for a fresh
            // signed release-assets URL (they expire within minutes) instead of reusing a stale one.
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            UseProxy = useProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            // New connections now and then look the host up again (a CDN moves).
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        // No overall timeout: a 480 MB package takes minutes on a slow line. Every request is watched for silence instead.
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(version is null ? UserAgent : $"{UserAgent}/{version}");
        return client;
    }

    /// <summary>A GET that follows redirects itself, preparing every hop's request alike (Accept, Range).</summary>
    public static async Task<Reply> SendAsync(HttpClient http, Uri uri, Action<HttpRequestMessage>? prepare, CancellationToken ct)
    {
        var hops = new List<Uri>();
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            prepare?.Invoke(request);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location) return new Reply(response, uri, hops);
            response.Dispose();
            if (hop >= MaxRedirects) throw new UpdateException(UpdateFailure.Network, $"{uri.Host} redirected too many times");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            hops.Add(uri);
        }
    }

    /// <summary>A response and the URLs the redirects went through.</summary>
    public sealed record Reply(HttpResponseMessage Response, Uri Final, IReadOnlyList<Uri> Hops) : IDisposable
    {
        public void Dispose() => Response.Dispose();
    }

    /// <summary>A transport failure as a reason the player can read.</summary>
    public static UpdateException Classify(Exception ex, string host)
    {
        if (ex is UpdateException known) return known;
        if (ex is HttpRequestException http)
        {
            if (http.InnerException is AuthenticationException || http.HttpRequestError == HttpRequestError.SecureConnectionError)
            {
                return new UpdateException(UpdateFailure.Certificate, $"the secure connection to {host} failed (certificate not trusted, or a filtering proxy)", ex);
            }
            return http.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => new UpdateException(UpdateFailure.Network, $"{host} could not be found (no internet connection?)", ex),
                HttpRequestError.ConnectionError => new UpdateException(UpdateFailure.Network, $"no connection to {host}", ex),
                HttpRequestError.ProxyTunnelError => new UpdateException(UpdateFailure.Network, $"the proxy refused the connection to {host}", ex),
                _ => new UpdateException(UpdateFailure.Network, $"the connection to {host} was lost", ex),
            };
        }
        return new UpdateException(UpdateFailure.Network, $"the connection to {host} was lost", ex);
    }

    /// <summary>Scheme, host and path: the signed query of a release-assets URL stays out of the log.</summary>
    public static string Describe(Uri uri) => $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

    public static string Megabytes(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 << 20 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB";
}

/// <summary>How <see cref="UpdateClient"/> waits and gives up; the defaults are the game's, the tests shorten them.</summary>
internal sealed record UpdateClientOptions
{
    /// <summary>
    /// No byte for this long and the connection counts as dead. A half-open connection (a router that dropped it
    /// without telling either end) never ends by itself; the stream keeps flowing on a slow but live one.
    /// </summary>
    public TimeSpan Inactivity { get; init; } = TimeSpan.FromSeconds(45);
    /// <summary>Attempts in a row that add nothing before the download is given up.</summary>
    public int AttemptsWithoutProgress { get; init; } = 6;
    /// <summary>All the attempts of one download: a line that drops every few megabytes still gets there, but not for ever.</summary>
    public int MaxAttempts { get; init; } = 40;
    /// <summary>The waits between attempts that added nothing, the last one repeated.</summary>
    public IReadOnlyList<TimeSpan> Backoff { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];
    /// <summary>The longest a server's Retry-After is honoured.</summary>
    public TimeSpan MaxServerWait { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Free space wanted on the download's drive beyond the download itself (the portable copy unpacks there too).</summary>
    public long Reserve { get; init; } = 64L << 20;
    public int BufferSize { get; init; } = 1 << 20;
}

/// <summary>Where a download stands, for the HUD.</summary>
internal enum DownloadPhase { Idle, Connecting, Downloading, Waiting, Verifying, Done }

/// <summary>
/// Downloads one package to a folder and returns its path once its size and SHA-256 hold. Runs on the thread pool
/// (the game polls <see cref="BytesDone"/>, <see cref="BytesTotal"/>, <see cref="Phase"/> and <see cref="Note"/> from its
/// main thread), never touches the engine, and can be cancelled at any moment without blocking.
/// </summary>
/// <remarks>
/// The bytes go to <c>name.part</c> and keep it between attempts and sessions: every attempt asks the package's own
/// URL again for the rest (an HTTP range), so a dropped connection costs the seconds since the drop and not the whole
/// package, which at home took 1.5-3 minutes against a release every 25-40 minutes. Only a checked file is renamed to
/// its real name, so a file under the real name is always a whole, verified package and is reused without a
/// download (the game was closed before it could apply it).
/// </remarks>
internal sealed class UpdateClient(HttpClient http, UpdateClientOptions? options = null, Action<string>? log = null, Func<string, long>? freeSpace = null)
{
    private readonly UpdateClientOptions _options = options ?? new UpdateClientOptions();
    private readonly Func<string, long> _freeSpace = freeSpace ?? FreeSpaceOf;
    private long _done;
    private long _total;
    private int _phase;
    private int _attempt;
    private string _note = "";
    private int _loggedTenth;

    public long BytesDone => Interlocked.Read(ref _done);
    public long BytesTotal => Interlocked.Read(ref _total);
    public DownloadPhase Phase => (DownloadPhase)Volatile.Read(ref _phase);
    public int Attempt => Volatile.Read(ref _attempt);
    /// <summary>What the download is waiting for, when it waits (a retry); empty otherwise.</summary>
    public string Note => Volatile.Read(ref _note);

    /// <summary>Downloads <paramref name="package"/> into <paramref name="folder"/> (or reuses it there) and returns the verified file's path.</summary>
    /// <exception cref="UpdateException">The download failed for good; its message is the reason.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled (the game is quitting).</exception>
    public async Task<string> DownloadAsync(UpdatePackage package, string folder, CancellationToken ct)
    {
        if (!UpdateFeed.IsPlainFileName(package.Name)) throw new UpdateException(UpdateFailure.BadData, $"unexpected package name \"{package.Name}\"");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateFailure.Disk, $"the download folder cannot be made ({ex.Message})", ex);
        }
        var final = Path.Combine(folder, package.Name);
        var part = final + ".part";
        var expected = package.Sha256 is { } sha && UpdateFeed.IsSha256(sha) ? sha.ToLowerInvariant() : await FetchShaAsync(package, ct).ConfigureAwait(false);

        if (File.Exists(final))
        {
            if (package.Size is not { } size || new FileInfo(final).Length == size)
            {
                if (await VerifyAsync(final, expected, ct).ConfigureAwait(false))
                {
                    Log($"reusing the package downloaded earlier: {final}");
                    SetPhase(DownloadPhase.Done);
                    return final;
                }
            }
            Log("the package downloaded earlier does not verify: deleted");
            Delete(final);
        }

        // A resumed download is only as good as every attempt's piece: two pieces from different uploads of the same
        // name would join into a file that fails the checksum. Such a file is fetched once more from the start.
        for (var round = 1; ; round++)
        {
            await FetchAsync(package, part, folder, ct).ConfigureAwait(false);
            if (await VerifyAsync(part, expected, ct).ConfigureAwait(false))
            {
                await MoveIntoPlaceAsync(part, final, ct).ConfigureAwait(false);
                SetPhase(DownloadPhase.Done);
                return final;
            }
            Delete(part);
            if (round == 2) throw new UpdateException(UpdateFailure.Checksum, "the download does not match its published SHA-256 checksum");
            Log("downloading the package again from the start");
        }
    }

    /// <summary>Brings <paramref name="part"/> to the package's full length, attempt after attempt.</summary>
    private async Task FetchAsync(UpdatePackage package, string part, string folder, CancellationToken ct)
    {
        var withoutProgress = 0;
        for (var attempt = 1; ; attempt++)
        {
            Volatile.Write(ref _attempt, attempt);
            var have = File.Exists(part) ? new FileInfo(part).Length : 0L;
            if (package.Size is { } size)
            {
                if (have == size) return;
                if (have > size)
                {
                    Delete(part);
                    have = 0;
                }
                CheckFreeSpace(folder, size - have);
            }
            try
            {
                await AttemptAsync(package, part, folder, have, ct).ConfigureAwait(false);
                return;
            }
            catch (UpdateException ex) when (ex.Transient && !ct.IsCancellationRequested)
            {
                var now = File.Exists(part) ? new FileInfo(part).Length : 0L;
                withoutProgress = now > have ? 0 : withoutProgress + 1;
                Log($"attempt {attempt} stopped at {UpdateHttp.Megabytes(now)}: {ex.Message}");
                if (withoutProgress >= _options.AttemptsWithoutProgress || attempt >= _options.MaxAttempts) throw;
                var wait = _options.Backoff[Math.Clamp(withoutProgress - 1, 0, _options.Backoff.Count - 1)];
                if (ex.RetryAt is { } at && at - DateTimeOffset.UtcNow is var asked && asked > wait) wait = asked < _options.MaxServerWait ? asked : _options.MaxServerWait;
                var total = BytesTotal;
                var where = total > 0 ? $"{now * 100 / total}%" : UpdateHttp.Megabytes(now);
                Volatile.Write(ref _note, $"{(ex.Kind == UpdateFailure.Timeout ? "stalled" : "connection lost")} at {where} - trying again in {Seconds(wait)}");
                SetPhase(DownloadPhase.Waiting);
                await Task.Delay(wait, ct).ConfigureAwait(false);
                Volatile.Write(ref _note, "");
            }
        }
    }

    /// <summary>One request for the rest of the package: returns once the file is complete, throws otherwise.</summary>
    private async Task AttemptAsync(UpdatePackage package, string part, string folder, long have, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(_options.Inactivity);
        SetPhase(DownloadPhase.Connecting);
        var host = package.Url.Host;
        try
        {
            using var reply = await UpdateHttp.SendAsync(http, package.Url, request =>
            {
                if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
            }, idle.Token).ConfigureAwait(false);
            var response = reply.Response;
            host = reply.Final.Host;
            long start;
            long? total;
            switch (response.StatusCode)
            {
                case HttpStatusCode.PartialContent:
                    if (response.Content.Headers.ContentRange is not { From: { } from } range || from != have)
                    {
                        // Not the rest that was asked for: the piece cannot be joined to the file. Start it again.
                        Delete(part);
                        throw new UpdateException(UpdateFailure.Network, $"{host} resumed at the wrong place");
                    }
                    start = have;
                    total = range.Length;
                    break;
                case HttpStatusCode.OK:
                    if (have > 0) Log($"{host} sent the whole package instead of the rest: writing it from the beginning");
                    start = 0;
                    total = response.Content.Headers.ContentLength;
                    break;
                case HttpStatusCode.RequestedRangeNotSatisfiable when package.Size is null && response.Content.Headers.ContentRange?.Length == have:
                    // Without a size from the release the file's own length says it is complete.
                    return;
                case HttpStatusCode.RequestedRangeNotSatisfiable:
                    Delete(part);
                    throw new UpdateException(UpdateFailure.Network, $"{host} could not resume the download");
                default:
                    throw StatusFailure(response, host);
            }
            if (package.Size is { } size && total is { } served && served != size)
            {
                throw new UpdateException(UpdateFailure.Changed, $"the package on the server ({UpdateHttp.Megabytes(served)}) is not the release's ({UpdateHttp.Megabytes(size)})");
            }
            if (package.Size is null && total is { } unknownSize) CheckFreeSpace(folder, unknownSize - start);
            Interlocked.Exchange(ref _total, total ?? package.Size ?? 0);
            Interlocked.Exchange(ref _done, start);
            SetPhase(DownloadPhase.Downloading);
            if (start == 0) _loggedTenth = 0;
            Log($"{(start > 0 ? "resuming" : "downloading")} {package.Name}{(start > 0 ? $" at {UpdateHttp.Megabytes(start)}" : "")} from {UpdateHttp.Describe(reply.Final)} (HTTP {(int)response.StatusCode}, {(total is { } t ? UpdateHttp.Megabytes(t) : "size unknown")})");

            await using var body = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            await using var file = OpenPart(part, start);
            var buffer = ArrayPool<byte>.Shared.Rent(_options.BufferSize);
            try
            {
                while (true)
                {
                    var read = await body.ReadAsync(buffer.AsMemory(0, _options.BufferSize), idle.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    idle.CancelAfter(_options.Inactivity);
                    await WriteAsync(file, buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    // A slow disk is not a silent server: the clock starts again once the bytes are written.
                    idle.CancelAfter(_options.Inactivity);
                    LogProgress(Interlocked.Add(ref _done, read), total);
                }
                await FlushAsync(file, ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            // The stream's end is only the package's end when the length agrees: a server without a length that
            // closes early is caught by the release's size, or failing that by the checksum.
            var length = file.Length;
            if ((total ?? package.Size) is { } expected && length != expected)
            {
                throw new UpdateException(UpdateFailure.Network, $"the connection closed at {UpdateHttp.Megabytes(length)} of {UpdateHttp.Megabytes(expected)}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException(UpdateFailure.Timeout, $"{host} sent nothing for {Seconds(_options.Inactivity)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            // A read cancelled by the silence timer can surface as an I/O error instead of a cancellation.
            if (idle.IsCancellationRequested) throw new UpdateException(UpdateFailure.Timeout, $"{host} sent nothing for {Seconds(_options.Inactivity)}", ex);
            throw UpdateHttp.Classify(ex, host);
        }
    }

    private static UpdateException StatusFailure(HttpResponseMessage response, string host)
    {
        var code = (int)response.StatusCode;
        DateTimeOffset? retryAt = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => DateTimeOffset.UtcNow + delta,
            { Date: { } date } => date,
            _ => null,
        };
        return code switch
        {
            404 or 410 => new UpdateException(UpdateFailure.NotFound, $"the package is not on {host} (HTTP {code})"),
            // 403 also answers a signed release-assets URL that expired; the next attempt asks github.com for a new one.
            403 or 408 or 425 or 429 or >= 500 => new UpdateException(UpdateFailure.Network, $"{host} answered HTTP {code}") { RetryAt = retryAt },
            _ => new UpdateException(UpdateFailure.BadData, $"{host} answered HTTP {code}"),
        };
    }

    /// <summary>The checksum published beside the package (its .sha256 asset: the hash, two spaces, the name).</summary>
    private async Task<string> FetchShaAsync(UpdatePackage package, CancellationToken ct)
    {
        if (package.ShaUrl is not { } url) throw new UpdateException(UpdateFailure.BadData, "the release publishes no checksum for the package");
        for (var attempt = 1; ; attempt++)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(_options.Inactivity);
            try
            {
                using var reply = await UpdateHttp.SendAsync(http, url, null, idle.Token).ConfigureAwait(false);
                if (reply.Response.StatusCode != HttpStatusCode.OK) throw StatusFailure(reply.Response, reply.Final.Host);
                var text = await reply.Response.Content.ReadAsStringAsync(idle.Token).ConfigureAwait(false);
                var hash = text.Trim();
                hash = hash.Length >= 64 ? hash[..64] : hash;
                if (!UpdateFeed.IsSha256(hash)) throw new UpdateException(UpdateFailure.BadData, "the published checksum is not a SHA-256");
                return hash.ToLowerInvariant();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 3 && ex is OperationCanceledException or HttpRequestException or IOException or UpdateException { Transient: true })
            {
                Log($"checksum attempt {attempt}: {ex.Message}");
                await Task.Delay(_options.Backoff[Math.Min(attempt - 1, _options.Backoff.Count - 1)], ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new UpdateException(UpdateFailure.Timeout, $"{url.Host} sent no checksum for {Seconds(_options.Inactivity)}");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                throw UpdateHttp.Classify(ex, url.Host);
            }
        }
    }

    /// <summary>The file's SHA-256 against <paramref name="expected"/>, read on this worker thread (485 MB took the main thread seconds on a slow disk).</summary>
    private async Task<bool> VerifyAsync(string path, string expected, CancellationToken ct)
    {
        SetPhase(DownloadPhase.Verifying);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(_options.BufferSize);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            Interlocked.Exchange(ref _total, stream.Length);
            Interlocked.Exchange(ref _done, 0);
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, _options.BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                Interlocked.Add(ref _done, read);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An antivirus that holds the fresh file can make it unreadable for a while; the next check tries again.
            throw new UpdateException(UpdateFailure.Disk, $"the download could not be read back ({ex.Message})", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        var actual = System.Convert.ToHexString(hash.GetHashAndReset());
        var ok = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        Log(ok ? $"SHA-256 verified: {Path.GetFileName(path)}" : $"SHA-256 mismatch on {Path.GetFileName(path)}: expected {expected}, got {actual.ToLowerInvariant()}");
        return ok;
    }

    /// <summary>The checked .part under its real name; an antivirus still scanning the new file can hold it for a moment.</summary>
    private async Task MoveIntoPlaceAsync(string part, string final, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(part, final, overwrite: true);
                Log($"verified package ready: {final}");
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 10) throw new UpdateException(UpdateFailure.Disk, $"the verified download could not be renamed ({ex.Message})", ex);
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }
    }

    private void CheckFreeSpace(string folder, long remaining)
    {
        var needed = Math.Max(remaining, 0) + _options.Reserve;
        var free = _freeSpace(folder);
        if (free >= needed) return;
        var drive = Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder;
        throw new UpdateException(UpdateFailure.Disk, $"not enough free space on {drive.TrimEnd('\\')} ({UpdateHttp.Megabytes(free)} free, the update needs {UpdateHttp.Megabytes(needed)})");
    }

    /// <summary>The free space of the folder's drive; unknown counts as plenty (a network share), the writes will tell.</summary>
    public static long FreeSpaceOf(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return long.MaxValue;
        }
    }

    private static FileStream OpenPart(string part, long start)
    {
        try
        {
            var file = new FileStream(part, start > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.Read, 1, FileOptions.Asynchronous);
            if (start > 0)
            {
                file.SetLength(start);
                file.Seek(start, SeekOrigin.Begin);
            }
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateFailure.Disk, $"the download cannot be written ({ex.Message})", ex);
        }
    }

    private static async Task WriteAsync(FileStream file, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        try
        {
            await file.WriteAsync(data, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateFailure.Disk, $"the download cannot be written ({ex.Message})", ex);
        }
    }

    private static async Task FlushAsync(FileStream file, CancellationToken ct)
    {
        try
        {
            await file.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateFailure.Disk, $"the download cannot be written ({ex.Message})", ex);
        }
    }

    private void LogProgress(long done, long? total)
    {
        if (total is not > 0) return;
        var tenth = (int)(done * 10 / total.Value);
        if (tenth <= _loggedTenth || tenth >= 10) return;
        _loggedTenth = tenth;
        Log($"{tenth * 10}% ({UpdateHttp.Megabytes(done)} of {UpdateHttp.Megabytes(total.Value)})");
    }

    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"could not delete {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private void SetPhase(DownloadPhase phase) => Volatile.Write(ref _phase, (int)phase);

    private static string Seconds(TimeSpan span) => span.TotalSeconds >= 90
        ? $"{span.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)} min"
        : $"{Math.Max(1, span.TotalSeconds).ToString("0", CultureInfo.InvariantCulture)} s";

    private void Log(string line) => log?.Invoke(line);
}
