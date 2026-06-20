using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Downloader.Desktop.Services;

/// <summary>
/// A tiny loopback HTTP listener so a browser extension can send a link to the app: the extension
/// does e.g. <c>fetch("http://127.0.0.1:15151/add?url=…")</c> and the app opens the Add dialog with
/// that URL pre-filled. Bound to localhost only and opt-in (off by default) since it opens a socket.
/// This is the app side only; the browser extension itself ships separately.
/// </summary>
public static class BrowserIntegrationService
{
    /// <summary>Fixed loopback port the companion extension targets.</summary>
    public const int Port = 15151;

    private static HttpListener _listener;
    private static CancellationTokenSource _cts;

    /// <summary>Invoked (on the UI thread) with a captured URL — wired by the app to open the Add flow.</summary>
    public static Action<string> OnUrlCaptured { get; set; }

    public static bool IsRunning => _listener is { IsListening: true };

    public static void Start()
    {
        if (IsRunning)
            return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            AppLog.Info($"Browser integration listening on 127.0.0.1:{Port}");
        }
        catch (Exception ex)
        {
            // Port busy or no permission — fail soft, the rest of the app is unaffected.
            AppLog.Error("Browser integration could not start", ex);
            _listener = null;
        }
    }

    public static void Stop()
    {
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
                var url = ExtractUrl(ctx.Request.Url);
                // Permissive CORS so the extension's fetch from a web page is allowed.
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
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

    /// <summary>Pulls the <c>url</c> query parameter from a request URI (testable, no networking).</summary>
    public static string ExtractUrl(Uri requestUri)
    {
        if (requestUri == null)
            return null;

        var query = requestUri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            if (!pair.AsSpan(0, eq).Equals("url", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        return null;
    }
}
