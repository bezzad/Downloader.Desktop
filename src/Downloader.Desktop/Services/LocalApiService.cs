using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// The app's loopback HTTP listener (previously BrowserIntegrationService). It serves two clients:
/// the browser extension's legacy endpoints (<c>/add?url=…</c> opens the Add dialog pre-filled,
/// <c>/ping</c> health check — behavior unchanged) and a small JSON API under <c>/api/*</c> for
/// scripts and the CLI: silent add, list, and per-item control (issue #2). Bound to localhost only;
/// gated by the integration toggle in Settings. The <c>/api/*</c> routes deliberately send no CORS
/// headers so web pages cannot read responses (there is no auth token by design).
/// </summary>
public static class LocalApiService
{
    /// <summary>Preferred loopback port. If it's taken, the listener falls back within <see cref="PortRange"/>.</summary>
    public const int PreferredPort = 15151;

    /// <summary>The declared loopback port range the extension's manifest <c>host_permissions</c> cover
    /// (MV3 requires these to be static/install-time — an arbitrary runtime port would be unreachable).
    /// Tried in this order when binding. NOTE: this range must NOT overlap
    /// <see cref="SingleInstanceService"/>'s lock port (15150) — a port held by the single-instance lock
    /// would be permanently unbindable here and silently skipped in the fallback.</summary>
    public static readonly int[] PortRange = { 15151, 15152, 15153, 15154, 15155 };

    /// <summary>Requests bigger than this are rejected (nothing legitimate comes close).</summary>
    public const int MaxBodyBytes = 64 * 1024;

    private static HttpListener _listener;
    private static CancellationTokenSource _cts;

    /// <summary>The port the listener actually bound to this session, or 0 if it hasn't started / failed.</summary>
    public static int EffectivePort { get; private set; }

    /// <summary>Invoked (on the UI thread) with a captured URL — wired by the app to open the Add flow.</summary>
    public static Action<string> OnUrlCaptured { get; set; }

    /// <summary>The download manager the /api routes act on — wired by the app at startup.</summary>
    public static IDownloadManager Manager { get; set; }

    /// <summary>The loaded config (save path + queues for building items) — wired by the app at startup.</summary>
    public static Config Config { get; set; }

    /// <summary>The loaded plugins, so the extension can ask what this install can actually handle — wired
    /// by the app at startup. Null when no plugin system is available, which answers "handled: false".</summary>
    public static PluginManager Plugins { get; set; }

    public static bool IsRunning => _listener is { IsListening: true };

    /// <summary>Ordered bind candidates: the last-known-good persisted port first (if it's in the declared
    /// range), then the whole range in order, without duplicates.</summary>
    public static IEnumerable<int> CandidatePorts()
    {
        var preferred = Config?.Settings?.LocalApiPort ?? 0;
        if (Array.IndexOf(PortRange, preferred) >= 0)
            yield return preferred;
        foreach (var p in PortRange)
            if (p != preferred)
                yield return p;
    }

    /// <summary>Raised (on the UI thread) after the listener state changes: a successful bind — including
    /// a LATE bind from the startup retry — or a stop. Lets the Settings status row and the fallback
    /// notification react even when the API comes up seconds after launch.</summary>
    public static event Action StatusChanged;

    // A transient condition at launch (e.g. the previous app instance still releasing its ports during an
    // update/restart) used to leave the API silently dead until the user toggled it: Start() bound once
    // and never retried. Now a short background retry keeps trying for ~1 minute.
    private static Avalonia.Threading.DispatcherTimer _retry;
    private static int _retriesLeft;
    internal static TimeSpan RetryInterval = TimeSpan.FromSeconds(5); // shortened by tests
    internal const int MaxRetries = 12;

    public static void Start()
    {
        if (IsRunning)
            return;

        if (TryBindOnce())
        {
            StopRetry();
            return;
        }

        // Every candidate was taken / denied right now — fail soft and keep retrying in the background
        // (ports free up when the previous instance finishes exiting or the conflicting app closes).
        AppLog.Info("Local API could not bind any port in the range — retrying in the background.");
        BeginRetry();
    }

    private static bool TryBindOnce()
    {
        foreach (var port in CandidatePorts())
        {
            HttpListener listener = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                _listener = listener;
                EffectivePort = port;
                _cts = new CancellationTokenSource();
                _ = AcceptLoopAsync(_cts.Token);
                // Remember the bound port so the next start prefers it and the CLI can reach us.
                if (Config?.Settings != null)
                    Config.Settings.LocalApiPort = port;
                AppLog.Info($"Local API listening on 127.0.0.1:{port}");
                RaiseStatusChanged();
                return true;
            }
            catch (Exception ex)
            {
                // This port is busy or denied — release the half-built listener and try the next one.
                try { listener?.Close(); } catch { /* best-effort */ }
                AppLog.Error($"Local API could not bind 127.0.0.1:{port}", ex);
            }
        }

        _listener = null;
        EffectivePort = 0;
        return false;
    }

    private static void BeginRetry()
    {
        _retriesLeft = MaxRetries;
        _retry ??= CreateRetryTimer();
        _retry.Interval = RetryInterval;
        _retry.Start();
    }

    private static Avalonia.Threading.DispatcherTimer CreateRetryTimer()
    {
        var timer = new Avalonia.Threading.DispatcherTimer();
        timer.Tick += (_, _) => RetryTick();
        return timer;
    }

    private static void RetryTick()
    {
        if (IsRunning || TryBindOnce())
        {
            StopRetry();
            return;
        }
        if (--_retriesLeft <= 0)
        {
            AppLog.Info("Local API gave up retrying — every port in the range stayed busy.");
            StopRetry();
        }
    }

    /// <summary>Deterministic test seam: runs one background-retry attempt (headless DispatcherTimers
    /// don't fire from RunJobs).</summary>
    internal static void RetryTickForTest() => RetryTick();

    private static void StopRetry() => _retry?.Stop();

    private static void RaiseStatusChanged()
    {
        try { StatusChanged?.Invoke(); } catch { /* observers must not break the listener */ }
    }

    public static void Stop()
    {
        StopRetry();
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _listener = null;
            _cts = null;
            EffectivePort = 0;
            RaiseStatusChanged();
        }
    }

    private static async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break; // listener stopped
            }

            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                // Every route, /ping and the legacy endpoints included: the extension identifies itself on
                // requests it already makes, so this is the one place it needs reading.
                RecordExtensionIdentity(ctx.Request);
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleApiAsync(ctx, path[5..].Trim('/').ToLowerInvariant()).ConfigureAwait(false);
                    continue;
                }

                // ---- Legacy extension endpoints (behavior unchanged) ----
                // Permissive CORS so the extension's fetch is allowed.
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");

                if (path.Contains("ping", StringComparison.OrdinalIgnoreCase))
                {
                    // Health check used by the extension to show "connected".
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                    continue;
                }

                var url = ExtractUrl(ctx.Request.Url);
                ctx.Response.StatusCode = string.IsNullOrWhiteSpace(url) ? 400 : 200;
                ctx.Response.Close();

                if (!string.IsNullOrWhiteSpace(url) && OnUrlCaptured is { } handler)
                    Dispatcher.UIThread.Post(() => handler(url));
            }
            catch
            {
                // ignore a single malformed request and keep listening
            }
        }
    }

    // ---------------- JSON API (/api/*) ----------------

    private static async Task HandleApiAsync(HttpListenerContext ctx, string route)
    {
        var manager = Manager;
        var config = Config;
        if (manager == null || config == null)
        {
            RespondJson(ctx, 503, new Dictionary<string, object> { ["error"] = "app is still starting" });
            return;
        }

        try
        {
            switch (route)
            {
                case "add":
                    await HandleAddAsync(ctx, manager, config).ConfigureAwait(false);
                    break;
                case "settings":
                    HandleSettings(ctx, config);
                    break;
                case "can-handle":
                    HandleCanHandle(ctx);
                    break;
                case "variants":
                    await HandleVariantsAsync(ctx).ConfigureAwait(false);
                    break;
                case "list":
                    await HandleListAsync(ctx, manager).ConfigureAwait(false);
                    break;
                case "pause":
                case "resume":
                case "cancel":
                case "retry":
                case "remove":
                    await HandleControlAsync(ctx, manager, route).ConfigureAwait(false);
                    break;
                default:
                    RespondJson(ctx, 404, new Dictionary<string, object> { ["error"] = $"unknown endpoint '{route}'" });
                    break;
            }
        }
        catch (Exception ex)
        {
            // ROUTE NAME ONLY — never the request URL or its query string. The GET form of /api/add carries
            // cookies and headers in the query (issue #7), and logs are read, shipped and pasted into issues.
            // Widening this to include the URI would put a live session in a log file. Don't.
            AppLog.Error($"Local API request failed ({route})", ex);
            try { RespondJson(ctx, 500, new Dictionary<string, object> { ["error"] = ex.Message }); }
            catch { /* response already gone */ }
        }
    }

    /// <summary>Answers whether THIS install can turn a page URL into a download — i.e. whether an enabled
    /// plugin's resolver claims it. The extension asks before declaring a site unsupported, so a user who
    /// installed the site-media plugin is offered the page instead of being told nothing can be done, and a
    /// user who hasn't is told which plugin would do it (never "sign in", which is not the problem).</summary>
    private static void HandleCanHandle(HttpListenerContext ctx)
    {
        var url = QueryParam(ctx.Request.Url, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            RespondJson(ctx, 400, new Dictionary<string, object> { ["error"] = "missing url" });
            return;
        }

        // CanResolve only — pure and network-free by contract, so this stays a cheap popup-time question.
        var by = Plugins?.FindResolverPluginName(url);
        RespondJson(ctx, 200, new Dictionary<string, object>
        {
            ["url"] = url,
            ["handled"] = by != null,
            ["by"] = by,
        });
    }

    /// <summary>The qualities behind a page URL, so a client can offer the same picker the Add window
    /// does — the browser extension shows them on the page's row (audio-only, 1080p, 720p…). Takes the
    /// caller's cookies like <c>/api/add</c> does: on a site that only answers a signed-in session,
    /// listing the qualities needs the session just as much as downloading them, so an anonymous lookup
    /// would report "no choices" for exactly the pages this exists for. Answers 200 with an empty list
    /// when nothing claims the link or it has no real choice; a resolver FAILURE answers 200 too, with
    /// the reason, so the caller still shows the page as one plain download instead of an error.</summary>
    private static async Task HandleVariantsAsync(HttpListenerContext ctx)
    {
        ApiAddRequest req;
        if (ctx.Request.HttpMethod == "POST")
        {
            var body = await ReadBodyAsync(ctx.Request).ConfigureAwait(false);
            req = body == null
                ? new ApiAddRequest { Error = $"request body too large (max {MaxBodyBytes} bytes)" }
                : ApiAddRequest.FromJson(body);
        }
        else
        {
            req = ApiAddRequest.FromQuery(ctx.Request.Url);
        }

        if (req.Error != null)
        {
            RespondJson(ctx, 400, new Dictionary<string, object> { ["error"] = req.Error });
            return;
        }

        var url = req.Url.Trim();
        string cookieFile = null;
        if (req.Cookies is { Count: > 0 })
        {
            try { cookieFile = CookieFile.WriteTempFile(req.Cookies); }
            catch (Exception ex) { AppLog.Warn($"Couldn't write temp cookie file: {ex.Message}"); }
        }

        var response = new Dictionary<string, object>
        {
            ["url"] = url,
            ["by"] = Plugins?.FindResolverPluginName(url),
        };
        try
        {
            // The extraction behind this can take a few seconds (it runs the site tool); the caller
            // renders first and upgrades when this answers, so there is no deadline here beyond the
            // client's own.
            var variants = Plugins == null
                ? null
                : await Plugins.GetVariantsAsync(url, new global::Downloader.Desktop.Plugins.ResolveOptions { CookieFilePath = cookieFile }, CancellationToken.None)
                    .ConfigureAwait(false);
            response["variants"] = (variants ?? Array.Empty<global::Downloader.Desktop.Plugins.LinkVariant>()).Select(v => new Dictionary<string, object>
            {
                ["id"] = v.Id,
                ["label"] = v.Label,
                ["size"] = v.ExpectedSize,
                ["default"] = v.IsDefault,
                ["url"] = v.SubstituteUrl,
            }).ToArray();
        }
        catch (Exception ex)
        {
            // A lookup that fails is not a failed request: the page can still be handed over as a whole
            // and the app will pick a stream itself. Say why, and let the caller decide what to show.
            AppLog.Warn($"Variant lookup failed for a local-API caller: {ex.Message}");
            response["variants"] = Array.Empty<object>();
            response["error"] = ex.Message;
        }
        finally
        {
            try { if (cookieFile != null) File.Delete(cookieFile); } catch { /* temp file, best effort */ }
        }

        RespondJson(ctx, 200, response);
    }

    /// <summary>What a local client needs to pre-fill its own UI: where downloads go by default, and which
    /// app it is talking to. Read-only, and deliberately just these two fields — the local API ACCEPTS
    /// cookies, headers and credentials, so handing any of the settings object back is how a secret would
    /// eventually leak out of it. The browser extension prefills its download-folder box from this.</summary>
    private static void HandleSettings(HttpListenerContext ctx, Config config)
    {
        RespondJson(ctx, 200, new Dictionary<string, object>
        {
            ["defaultSavePath"] = config.Settings.DefaultSavePath,
            ["version"] = UpdateService.CurrentVersion.ToString(),
        });
    }

    private static async Task HandleAddAsync(HttpListenerContext ctx, IDownloadManager manager, Config config)
    {
        ApiAddRequest req;
        if (ctx.Request.HttpMethod == "POST")
        {
            var body = await ReadBodyAsync(ctx.Request).ConfigureAwait(false);
            req = body == null
                ? new ApiAddRequest { Error = $"request body too large (max {MaxBodyBytes} bytes)" }
                : ApiAddRequest.FromJson(body);
        }
        else
        {
            req = ApiAddRequest.FromQuery(ctx.Request.Url);
        }

        if (req.Error != null)
        {
            RespondJson(ctx, 400, new Dictionary<string, object> { ["error"] = req.Error });
            return;
        }

        var result = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var item = BuildItem(req, config);
            var vm = manager.Add(item, autoStart: req.Start);
            return new Dictionary<string, object>
            {
                ["id"] = item.Id.ToString(),
                ["name"] = vm.DisplayName,
                ["status"] = vm.Status.ToString(),
                // How much request context we actually accepted, so a caller can tell a working hand-off from
                // one we silently dropped (issue #7). COUNTS ONLY — a cookie or header value must never be
                // echoed back; the caller already has them and the response is the wrong place for secrets.
                ["cookies"] = item.Request.Cookies.Count,
                ["headers"] = item.Request.Headers.Count,
                ["referer"] = !string.IsNullOrEmpty(item.Referer),
                ["variantId"] = item.VariantId
            };
        });
        RespondJson(ctx, 201, result);
    }

    private static async Task HandleListAsync(HttpListenerContext ctx, IDownloadManager manager)
    {
        var rows = await Dispatcher.UIThread.InvokeAsync(() =>
            manager.Items.Select(DescribeItem).ToArray());
        RespondJson(ctx, 200, rows);
    }

    private static async Task HandleControlAsync(HttpListenerContext ctx, IDownloadManager manager, string verb)
    {
        string idText;
        if (ctx.Request.HttpMethod == "POST")
        {
            var body = await ReadBodyAsync(ctx.Request).ConfigureAwait(false);
            idText = body == null ? null : ExtractIdFromJson(body);
        }
        else
        {
            idText = QueryParam(ctx.Request.Url, "id");
        }

        if (!Guid.TryParse(idText?.Trim(), out var id))
        {
            RespondJson(ctx, 400, new Dictionary<string, object> { ["error"] = "missing or invalid 'id'" });
            return;
        }

        var vm = await Dispatcher.UIThread.InvokeAsync(() =>
            manager.Items.FirstOrDefault(i => i.GetItem().Id == id));
        if (vm == null)
        {
            RespondJson(ctx, 404, new Dictionary<string, object> { ["error"] = $"no download with id {id}" });
            return;
        }

        // Inapplicable actions (e.g. pausing a completed item) are safe no-ops inside the manager's
        // state guards, so they still return 200 — idempotent and script-friendly.
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            switch (verb)
            {
                case "pause": manager.Pause(vm); break;
                case "resume": manager.Resume(vm); break;
                case "cancel": manager.Cancel(vm); break;
                case "retry": manager.Retry(vm); break;
                case "remove": await manager.Remove(vm); break;
            }
        });
        RespondJson(ctx, 200, new Dictionary<string, object> { ["ok"] = true });
    }

    /// <summary>One /api/list row for a download (runs on the UI thread).</summary>
    public static Dictionary<string, object> DescribeItem(DownloadItemViewModel vm)
    {
        var item = vm.GetItem();
        return new Dictionary<string, object>
        {
            ["id"] = item.Id.ToString(),
            ["name"] = vm.DisplayName,
            ["url"] = item.Url,
            ["status"] = vm.Status.ToString(),
            ["progress"] = Math.Round(vm.Progress, 2),
            ["size"] = item.Size,
            ["downloaded"] = item.Downloaded,
            ["speed"] = vm.Status == DownloadStatus.Running ? Math.Round(vm.Speed) : 0,
            ["folder"] = item.SaveFolder,
            ["filePath"] = item.FilePath,
            ["queue"] = vm.QueueName
        };
    }

    /// <summary>Builds the download descriptor for a programmatic add — mirrors the Add dialog.</summary>
    public static DownloadItem BuildItem(ApiAddRequest req, Config config)
    {
        var urls = new List<string> { req.Url.Trim() };
        urls.AddRange(req.Mirrors.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()));

        // Queue may be given as an id or a (case-insensitive) name; unknown/absent → default queue.
        var queue = string.IsNullOrWhiteSpace(req.Queue)
            ? null
            : config.Queues?.FirstOrDefault(q =>
                string.Equals(q.Id, req.Queue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(q.Name, req.Queue, StringComparison.OrdinalIgnoreCase));

        var item = new DownloadItem
        {
            Urls = urls,
            FileName = string.IsNullOrWhiteSpace(req.Filename) ? null : req.Filename.Trim(),
            SaveFolder = string.IsNullOrWhiteSpace(req.Path) ? config.Settings.DefaultSavePath : req.Path.Trim(),
            QueueId = queue?.Id ?? config.DefaultQueue?.Id,
            FromBrowserDownload = req.FromBrowser,
            VariantId = string.IsNullOrWhiteSpace(req.VariantId) ? null : req.VariantId.Trim(),
            Status = DownloadStatus.Created,
            LastTry = DateTime.Now
        };

        // Whatever context the caller supplied travels with the item (issue #7) so the requests that fetch
        // the BYTES send it too, not just the resolver. Cookies and headers stay in memory for this session;
        // only the referer is persisted (see DownloadItem.Referer).
        if (req.Cookies is { Count: > 0 })
            item.Request.Cookies.AddRange(req.Cookies);
        if (req.Headers is { Count: > 0 })
            foreach (var (key, value) in req.Headers)
                item.Request.Headers[key] = value;
        if (!string.IsNullOrWhiteSpace(req.Referer))
            item.Referer = req.Referer.Trim();

        // If the extension handed over a live session's cookies, write them to a short-lived temp file now;
        // DownloadManager.Start passes it to the plugin resolver and deletes it right after. Best-effort:
        // a failure here must never block adding the download (the URL-only flow still works).
        if (req.Cookies is { Count: > 0 })
        {
            try { item.CookieFilePath = CookieFile.WriteTempFile(req.Cookies); }
            catch (Exception ex) { AppLog.Warn($"Couldn't write temp cookie file: {ex.Message}"); }
        }

        return item;
    }

    // ---------------- Which extension is talking to us ----------------

    /// <summary>An extension that has contacted this app, as it last identified itself.</summary>
    public sealed record ExtensionIdentity(string Version, string Browser, DateTimeOffset At);

    private static readonly object IdentityGate = new();
    private static readonly Dictionary<string, ExtensionIdentity> SeenExtensions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The extensions that have contacted this app since it started, keyed by browser label.
    ///
    /// <para><b>In memory only.</b> It is never written to the config file and never written to the log —
    /// same discipline as the request URL, which is not logged because the GET form of <c>/api/add</c>
    /// carries a live session (issue #7). This exists so the app can say "your Chrome extension is out of
    /// date", which is worth exactly one dictionary and nothing more.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, ExtensionIdentity> LastSeenExtensions
    {
        get { lock (IdentityGate) return new Dictionary<string, ExtensionIdentity>(SeenExtensions, StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>What this browser's extension last reported, or null if it has never called.</summary>
    public static ExtensionIdentity LastSeenExtension(string browser)
    {
        if (string.IsNullOrWhiteSpace(browser))
            return null;
        lock (IdentityGate)
            return SeenExtensions.TryGetValue(browser, out var seen) ? seen : null;
    }

    /// <summary>Test seam: the recorder is process-wide, so a test that asserts on it must start clean.</summary>
    internal static void ClearSeenExtensions()
    {
        lock (IdentityGate) SeenExtensions.Clear();
    }

    private static void RecordExtensionIdentity(HttpListenerRequest request)
    {
        try
        {
            var parsed = ParseExtensionIdentity(
                QueryParam(request?.Url, "extv"),
                QueryParam(request?.Url, "extb"),
                request?.Headers?["X-Downloader-Extension"]);
            if (parsed == null)
                return; // an older extension, the CLI, or another tool — handled exactly as before
            lock (IdentityGate)
                SeenExtensions[parsed.Browser] = parsed;
        }
        catch
        {
            // Never let identity bookkeeping cost a request.
        }
    }

    /// <summary>
    /// Reads the reported identity from the query pair (<c>extv</c>/<c>extb</c>) or, failing that, the
    /// <c>X-Downloader-Extension: &lt;version&gt;; &lt;browser&gt;</c> header. Pure, so the shapes are
    /// tested without a listener. Returns null when nothing usable was reported — which must read as
    /// "an older extension", never as an error.
    /// </summary>
    internal static ExtensionIdentity ParseExtensionIdentity(string queryVersion, string queryBrowser, string header)
    {
        var version = Clean(queryVersion);
        var browser = Clean(queryBrowser);

        if (version == null && !string.IsNullOrWhiteSpace(header))
        {
            var parts = header.Split(';', 2);
            version = Clean(parts[0]);
            browser ??= parts.Length > 1 ? Clean(parts[1]) : null;
        }

        if (version == null)
            return null;
        return new ExtensionIdentity(version, browser ?? "unknown", DateTimeOffset.Now);

        // A reported value is untrusted text: keep it short and single-line so it cannot smuggle
        // anything into whatever renders it.
        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;
            var v = s.Trim();
            if (v.Length > 40)
                v = v[..40];
            return v.Contains('\n') || v.Contains('\r') ? null : v;
        }
    }

    // ---------------- Small pure helpers (unit-testable, no networking) ----------------

    /// <summary>Pulls the <c>url</c> query parameter from a request URI (testable, no networking).</summary>
    public static string ExtractUrl(Uri requestUri) => QueryParam(requestUri, "url");

    /// <summary>Pulls a single query parameter value from a request URI (testable, no networking).</summary>
    public static string QueryParam(Uri requestUri, string name)
    {
        if (requestUri == null)
            return null;

        var query = requestUri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            if (!pair.AsSpan(0, eq).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Parses a browser <c>Cookie</c> header string (<c>name=value; name=value</c>) into cookies for
    /// <paramref name="targetUrl"/>. This is the shape a capture tool actually has to hand on the GET form of
    /// <c>/api/add</c> — the JSON body's per-cookie objects can carry a domain, a header string cannot, so the
    /// domain is taken from the target URL's host (every cookie a browser attached to that URL is by
    /// definition valid for it). Values are kept verbatim because they legitimately contain <c>=</c>.
    /// Pure, and never logs a value.
    /// </summary>
    public static List<CookieDto> ParseCookieHeader(string cookieHeader, string targetUrl)
    {
        var cookies = new List<CookieDto>();
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return cookies;

        // No host ⇒ no domain to scope the cookies to. Drop them rather than invent one; the add itself
        // still succeeds and the response's cookie count tells the caller none were accepted.
        if (!Uri.TryCreate(targetUrl?.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return cookies;

        foreach (var pair in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            var name = pair[..eq].Trim();
            if (name.Length == 0)
                continue;
            cookies.Add(new CookieDto
            {
                Name = name,
                Value = pair[(eq + 1)..].Trim(),
                Domain = uri.Host,
                Path = "/",
                Secure = uri.Scheme == Uri.UriSchemeHttps
            });
        }
        return cookies;
    }

    /// <summary>
    /// Parses a newline-separated <c>Name: value</c> header block (the form a capture tool emits) into a
    /// case-insensitive map. Malformed and empty-named lines are skipped rather than failing the add.
    /// Pure, and never logs a value.
    /// </summary>
    public static Dictionary<string, string> ParseHeaderBlock(string block)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(block))
            return headers;

        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0)
                continue;
            headers[name] = value;
        }
        return headers;
    }

    /// <summary>Pulls the <c>id</c> property out of a JSON body (testable, no networking).</summary>
    public static string ExtractIdFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads a capped request body; null when it exceeds <see cref="MaxBodyBytes"/>.</summary>
    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBodyBytes)
            return null;
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await request.InputStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + read > MaxBodyBytes)
                return null;
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void RespondJson(HttpListenerContext ctx, int status, object payload)
    {
        // NOTE: no Access-Control-Allow-Origin here on purpose — see the class summary.
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }
}

/// <summary>
/// A parsed programmatic add request (from the /api/add JSON body, its query string, or the CLI).
/// <see cref="Error"/> is non-null when the input is invalid. Pure and unit-testable.
/// </summary>
public sealed class ApiAddRequest
{
    public string Url { get; set; }
    public string Filename { get; set; }
    public string Path { get; set; }
    public string Queue { get; set; }
    public List<string> Mirrors { get; set; } = new();
    public bool Start { get; set; } = true;

    /// <summary>Optional live-session cookies for this URL, supplied by the browser extension for sites that
    /// need a signed-in session (e.g. YouTube). Never persisted or logged; written to a temp file for yt-dlp.</summary>
    public List<CookieDto> Cookies { get; set; } = new();

    /// <summary>Optional per-download request headers (issue #7), for links a server only serves with the
    /// context they were found in. Applied to this download only and never persisted or logged.</summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional per-download referer, overriding the global setting for this download only.</summary>
    public string Referer { get; set; }

    /// <summary>
    /// Optional stream/quality choice for a link a plugin expands into several (an HLS master's renditions,
    /// a model's tags). The id is the resolving plugin's own — an unknown one is not an error: the resolver
    /// falls back to its default (highest quality), which is what a caller that guessed wrong should get.
    /// This exists so a caller can hand over a MASTER playlist plus the quality it wants, instead of the
    /// rendition URL: a rendition of a master with a separate audio group is video-only, and downloading it
    /// directly produces a file with no sound.
    /// </summary>
    public string VariantId { get; set; }

    /// <summary>True when the browser extension took this download over from the browser itself. Such a link
    /// was demonstrably fetchable a second ago, which changes how a first-request failure is read — see
    /// <see cref="DownloadItem.FromBrowserDownload"/>.</summary>
    public bool FromBrowser { get; set; }

    /// <summary>Human-readable validation error, or null when the request is usable.</summary>
    public string Error { get; set; }

    public static ApiAddRequest FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var req = new ApiAddRequest
            {
                Url = GetString(root, "url"),
                Filename = GetString(root, "filename"),
                Path = GetString(root, "path"),
                Queue = GetString(root, "queue"),
                Referer = GetString(root, "referer"),
                VariantId = GetString(root, "variantId")
            };
            if (root.TryGetProperty("fromBrowser", out var fromBrowser) &&
                fromBrowser.ValueKind is JsonValueKind.False or JsonValueKind.True)
                req.FromBrowser = fromBrowser.GetBoolean();
            if (root.TryGetProperty("start", out var start) &&
                start.ValueKind is JsonValueKind.False or JsonValueKind.True)
                req.Start = start.GetBoolean();
            if (root.TryGetProperty("mirrors", out var mirrors) && mirrors.ValueKind == JsonValueKind.Array)
                foreach (var m in mirrors.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.String)
                        req.Mirrors.Add(m.GetString());
            if (root.TryGetProperty("cookies", out var cookies) && cookies.ValueKind == JsonValueKind.Array)
                foreach (var c in cookies.EnumerateArray())
                    if (ParseCookie(c) is { } dto)
                        req.Cookies.Add(dto);
            // Headers: a plain {"Name":"value"} object. A malformed entry (or a malformed `headers`) is
            // skipped rather than failing the add — a header problem must never cost the user the download.
            if (root.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
                foreach (var h in headers.EnumerateObject())
                    if (h.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(h.Name))
                        req.Headers[h.Name] = h.Value.GetString();
            return req.Validate();
        }
        catch (JsonException)
        {
            return new ApiAddRequest { Error = "invalid JSON body" };
        }
    }

    public static ApiAddRequest FromQuery(Uri requestUri)
    {
        var req = new ApiAddRequest
        {
            Url = LocalApiService.QueryParam(requestUri, "url"),
            Filename = LocalApiService.QueryParam(requestUri, "filename"),
            Path = LocalApiService.QueryParam(requestUri, "path"),
            Queue = LocalApiService.QueryParam(requestUri, "queue"),
            Referer = LocalApiService.QueryParam(requestUri, "referer"),
            VariantId = LocalApiService.QueryParam(requestUri, "variantId")
        };
        if (LocalApiService.QueryParam(requestUri, "fromBrowser") is { } fromBrowser)
            req.FromBrowser = !fromBrowser.Equals("false", StringComparison.OrdinalIgnoreCase) && fromBrowser != "0";
        if (LocalApiService.QueryParam(requestUri, "start") is { } start)
            req.Start = !start.Equals("false", StringComparison.OrdinalIgnoreCase) && start != "0";

        // The query form carries the same per-download context the JSON body does, in the WIRE shapes a
        // browser/capture tool already has: a Cookie-header string and a `Name: value` block. Without this a
        // caller's session hand-off was silently dropped while the add still answered 201 (issue #7).
        // Parsing never fails the add — a bad context costs the caller its context, not its download.
        req.Cookies = LocalApiService.ParseCookieHeader(LocalApiService.QueryParam(requestUri, "cookies"), req.Url);
        req.Headers = LocalApiService.ParseHeaderBlock(LocalApiService.QueryParam(requestUri, "headers"));
        return req.Validate();
    }

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["url"] = Url,
        ["filename"] = Filename,
        ["path"] = Path,
        ["queue"] = Queue,
        ["mirrors"] = Mirrors,
        ["start"] = Start,
        // Referer travels with a forwarded CLI add (it is not a credential); cookies and headers never do.
        ["referer"] = Referer,
        ["variantId"] = VariantId
    });

    private ApiAddRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Url) ||
            !Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            Error = "missing or invalid 'url' (must be an absolute http/https link)";
        else if (!string.IsNullOrWhiteSpace(Path) && !System.IO.Path.IsPathRooted(Path.Trim()))
            Error = "'path' must be an absolute folder path";
        return this;
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Parse one cookie object (the <c>chrome.cookies.getAll</c> shape) into a <see cref="CookieDto"/>,
    /// or null if it lacks the minimum (name + domain). Never logs values.</summary>
    private static CookieDto ParseCookie(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return null;
        var name = GetString(e, "name");
        var domain = GetString(e, "domain");
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(domain))
            return null;
        var dto = new CookieDto
        {
            Name = name,
            Value = GetString(e, "value") ?? string.Empty,
            Domain = domain,
            Path = GetString(e, "path") ?? "/",
            Secure = e.TryGetProperty("secure", out var s) && s.ValueKind == JsonValueKind.True,
        };
        if (e.TryGetProperty("expires", out var exp) && exp.ValueKind == JsonValueKind.Number &&
            exp.TryGetInt64(out var epoch))
            dto.Expires = epoch;
        return dto;
    }
}
