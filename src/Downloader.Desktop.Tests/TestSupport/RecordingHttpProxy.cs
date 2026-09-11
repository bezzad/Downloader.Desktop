using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Downloader.Desktop.Tests;

/// <summary>
/// A loopback listener that records the FIRST request line it is given and answers 200.
///
/// <para>It is deliberately a raw <see cref="TcpListener"/> rather than an <see cref="HttpListener"/>:
/// the whole point is to see the request line verbatim, because that line is the only honest evidence
/// of whether a request was proxied. A client talking to an HTTP proxy sends the target in ABSOLUTE
/// form (<c>GET http://host/path HTTP/1.1</c>); a direct request sends origin form (<c>GET /path</c>).
/// <see cref="HttpListener"/> normalises that difference away.</para>
///
/// <para>It also stands in for an ordinary origin server, so one fixture covers both sides of
/// "proxied" and "not proxied".</para>
/// </summary>
internal sealed class RecordingHttpProxy : IDisposable
{
    private readonly TcpListener _listener;
    private readonly TaskCompletionSource<string> _firstRequestLine =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RecordingHttpProxy()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }

    public int Port { get; }

    /// <summary>The first request line received, e.g. "GET http://host/path HTTP/1.1".</summary>
    public Task<string> FirstRequestLine => _firstRequestLine.Task;

    private async Task AcceptAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

            var line = await reader.ReadLineAsync().ConfigureAwait(false) ?? "";
            _firstRequestLine.TrySetResult(line);

            // Drain the headers so the client sees a well-formed exchange, then answer.
            string header;
            while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync().ConfigureAwait(false) ?? "")) { }

            var body = Encoding.ASCII.GetBytes("{}");
            var head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Disposal races the accept loop; a test that never got its line fails on its own timeout.
            _firstRequestLine.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { /* already stopped */ }
    }
}
