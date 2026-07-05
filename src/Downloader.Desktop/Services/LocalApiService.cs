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
            AppLog.Error($"Local API request failed ({route})", ex);
            try { RespondJson(ctx, 500, new Dictionary<string, object> { ["error"] = ex.Message }); }
            catch { /* response already gone */ }
        }
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
                ["status"] = vm.Status.ToString()
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

        return new DownloadItem
        {
            Urls = urls,
            FileName = string.IsNullOrWhiteSpace(req.Filename) ? null : req.Filename.Trim(),
            SaveFolder = string.IsNullOrWhiteSpace(req.Path) ? config.Settings.DefaultSavePath : req.Path.Trim(),
            QueueId = queue?.Id ?? config.DefaultQueue?.Id,
            Status = DownloadStatus.Created,
            LastTry = DateTime.Now
        };
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
                Queue = GetString(root, "queue")
            };
            if (root.TryGetProperty("start", out var start) &&
                start.ValueKind is JsonValueKind.False or JsonValueKind.True)
                req.Start = start.GetBoolean();
            if (root.TryGetProperty("mirrors", out var mirrors) && mirrors.ValueKind == JsonValueKind.Array)
                foreach (var m in mirrors.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.String)
                        req.Mirrors.Add(m.GetString());
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
            Queue = LocalApiService.QueryParam(requestUri, "queue")
        };
        if (LocalApiService.QueryParam(requestUri, "start") is { } start)
            req.Start = !start.Equals("false", StringComparison.OrdinalIgnoreCase) && start != "0";
        return req.Validate();
    }

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["url"] = Url,
        ["filename"] = Filename,
        ["path"] = Path,
        ["queue"] = Queue,
        ["mirrors"] = Mirrors,
        ["start"] = Start
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
}
