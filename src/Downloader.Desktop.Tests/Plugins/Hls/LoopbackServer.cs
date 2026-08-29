using System.Net;
using System.Text;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// A tiny in-process HTTP server for tests: register path -> (bytes, content-type) and it serves them on
/// 127.0.0.1. No external network. Mirrors the host's IntegrationTests loopback pattern.
/// </summary>
internal sealed class LoopbackServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, (byte[] body, string type)> _routes = new();
    private readonly Dictionary<string, int> _status = new();
    private readonly CancellationTokenSource _cts = new();

    public string BaseUrl { get; }

    public LoopbackServer()
    {
        int port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public LoopbackServer MapText(string path, string content, string type = "application/vnd.apple.mpegurl")
    {
        _routes[Normalize(path)] = (Encoding.UTF8.GetBytes(content), type);
        return this;
    }

    public LoopbackServer MapBytes(string path, byte[] content, string type = "video/mp2t")
    {
        _routes[Normalize(path)] = (content, type);
        return this;
    }

    /// <summary>Answers this path with a bare status code — for the "server is up but unhappy" cases
    /// (a 500, a 403) that an unmapped path's 404 cannot stand in for.</summary>
    public LoopbackServer MapStatus(string path, int statusCode)
    {
        _status[Normalize(path)] = statusCode;
        return this;
    }

    public string Url(string path) => BaseUrl + path.TrimStart('/');

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }

            var key = Normalize(ctx.Request.Url!.AbsolutePath);
            try
            {
                if (_status.TryGetValue(key, out var code))
                {
                    ctx.Response.StatusCode = code;
                }
                else if (ctx.Request.HttpMethod == "HEAD" && _routes.TryGetValue(key, out var h))
                {
                    ctx.Response.ContentType = h.type;
                    ctx.Response.StatusCode = 200;
                }
                else if (_routes.TryGetValue(key, out var r))
                {
                    ctx.Response.ContentType = r.type;
                    ctx.Response.StatusCode = 200;
                    if (ctx.Request.HttpMethod != "HEAD")
                        await ctx.Response.OutputStream.WriteAsync(r.body).ConfigureAwait(false);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
            }
            catch { /* ignore */ }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    private static string Normalize(string path) => "/" + path.Trim('/');

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _listener.Close();
        _cts.Dispose();
    }
}
