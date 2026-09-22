using System;
using System.Net;
using System.Net.Http;

namespace Downloader.Desktop.Services;

/// <summary>
/// The one place that answers "what proxy did the user set?", and the only thing anything outside the
/// download engine needs in order to honour it.
///
/// <para>The address is read from the live settings through <see cref="AddressSource"/> on EVERY
/// request rather than captured once. That is what lets a plugin — including a third-party one the
/// app has never heard of — build a single <see cref="HttpClient"/> in <c>Initialize</c> and still
/// follow a proxy the user changes afterwards: <see cref="HttpClient"/> will not let its handler's
/// proxy be swapped after the first request, but an <see cref="IWebProxy"/> is consulted per
/// request, so the indirection goes there instead.</para>
///
/// <para>Downloads themselves do NOT come through here — the engine gets the same address as a
/// <see cref="WebProxy"/> on its <c>RequestConfiguration</c> (see <c>DownloadSettings</c>).</para>
/// </summary>
public static class AppProxy
{
    /// <summary>Where the address comes from. Wired once to the live config (see
    /// <c>DownloadManager.Initialize</c>); a plain field so tests can point it at their own value.
    /// Never throws — a source that does is treated as "no proxy".</summary>
    internal static Func<string> AddressSource { get; set; } = () => null;

    /// <summary>The configured proxy address, or null when none is set.</summary>
    public static string Address
    {
        get
        {
            try
            {
                var value = AddressSource?.Invoke();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch
            {
                return null; // a proxy lookup must never break the request that asked for it
            }
        }
    }

    /// <summary>An <see cref="IWebProxy"/> that resolves the CURRENT setting on every request.</summary>
    public static IWebProxy Live { get; } = new LiveProxy();

    /// <summary>A client that routes through <see cref="Live"/>. Handed to plugins as
    /// <c>IPluginContext.CreateHttpClient()</c> and used by the app's own lookups.</summary>
    public static HttpClient CreateClient() =>
        new(new SocketsHttpHandler { UseProxy = true, Proxy = Live });

    /// <summary>
    /// The address as a URI, or null when there is none or it cannot be understood. A scheme-less
    /// value is read as HTTP — the same rule <see cref="WebProxy"/> applies, so the box in Settings
    /// means the same thing here as it does for the engine. A SOCKS proxy therefore HAS to be written
    /// with its scheme (<c>socks5://host:port</c>), or it is taken for an HTTP one.
    /// </summary>
    internal static Uri Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var value = address.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// This machine is never reached through a proxy. A proxy is for the outside world, and the app talks
    /// to ITSELF over loopback (its local API, and every loopback server the test suite stands up) — routing
    /// that through a proxy can only break it, which is what curl, browsers and <see cref="WebProxy"/>'s own
    /// <c>BypassOnLocal</c> all assume too. <see cref="Uri.IsLoopback"/> covers 127.0.0.0/8, ::1 and
    /// <c>localhost</c>.
    /// </summary>
    private static bool IsThisMachine(Uri destination) => destination?.IsLoopback == true;

    private sealed class LiveProxy : IWebProxy
    {
        public ICredentials Credentials { get; set; }

        // Null means "no proxy for this destination", which is how a request goes out direct.
        public Uri GetProxy(Uri destination) => IsThisMachine(destination) ? null : Parse(Address);

        public bool IsBypassed(Uri host) => IsThisMachine(host) || Parse(Address) is null;
    }
}
