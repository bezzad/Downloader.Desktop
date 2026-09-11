using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// One proxy setting, honoured by everything that is not the download engine — the app's own lookups
/// and every plugin, including a third-party one that has never heard of this app's settings.
///
/// <para>The rule that shapes the whole design is that the address is read PER REQUEST. An
/// <see cref="System.Net.Http.HttpClient"/> will not let its proxy be changed after its first request,
/// so a plugin that builds one client in <c>Initialize</c> and keeps it — which is what plugins are
/// told to do — would be stuck with whatever the proxy was at startup. Putting the indirection in the
/// <see cref="System.Net.IWebProxy"/> instead is what makes a change in Settings take effect at once.</para>
/// </summary>
public class AppProxyTests : IDisposable
{
    private readonly Func<string> _original = AppProxy.AddressSource;

    // Process-wide, like every other override seam here — always put it back.
    public void Dispose() => AppProxy.AddressSource = _original;

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void No_proxy_is_configured_by_default()
    {
        AppProxy.AddressSource = () => null;

        Assert.Null(AppProxy.Address);
        Assert.Null(AppProxy.Live.GetProxy(new Uri("https://example.com/file.zip")));
        Assert.True(AppProxy.Live.IsBypassed(new Uri("https://example.com/file.zip")));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_address_means_no_proxy(string? configured)
    {
        AppProxy.AddressSource = () => configured;

        Assert.Null(AppProxy.Address);
        Assert.Null(AppProxy.Parse(configured));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_socks_address_keeps_its_scheme()
    {
        // The reason the scheme has to be typed: SOCKS is what the user actually runs locally, and
        // dropping the scheme would silently turn it into an HTTP proxy that cannot answer.
        var uri = AppProxy.Parse("socks5://127.0.0.1:12000");

        Assert.NotNull(uri);
        Assert.Equal("socks5", uri!.Scheme);
        Assert.Equal(12000, uri.Port);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("socks5")]
    [InlineData("socks4")]
    [InlineData("socks4a")]
    [InlineData("http")]
    public void Every_scheme_the_runtime_supports_survives_parsing(string scheme)
    {
        Assert.Equal(scheme, AppProxy.Parse($"{scheme}://127.0.0.1:12000")!.Scheme);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_address_with_no_scheme_is_read_as_http()
    {
        // Same rule WebProxy applies for the engine, so the one box in Settings means one thing.
        Assert.Equal("http://127.0.0.1:8080/", AppProxy.Parse("127.0.0.1:8080")!.ToString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_address_that_cannot_be_understood_means_no_proxy_rather_than_an_error()
    {
        // A typo in a settings box must never turn every download into an exception.
        Assert.Null(AppProxy.Parse("http://:::::"));
        Assert.Null(AppProxy.Parse("not a url at all"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_source_that_throws_reads_as_no_proxy()
    {
        AppProxy.AddressSource = () => throw new InvalidOperationException("settings not loaded yet");

        Assert.Null(AppProxy.Address);
        Assert.Null(AppProxy.Live.GetProxy(new Uri("https://example.com")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Changing_the_setting_applies_without_rebuilding_anything()
    {
        // THE point of the live IWebProxy: a plugin builds one client at startup and keeps it, so if
        // the proxy were captured then, changing it in Settings would do nothing until a restart.
        var address = "socks5://127.0.0.1:12000";
        AppProxy.AddressSource = () => address;
        var proxy = AppProxy.Live;
        var destination = new Uri("https://example.com/file.zip");

        Assert.Equal(12000, proxy.GetProxy(destination)!.Port);

        address = "socks5://127.0.0.1:9999";
        Assert.Equal(9999, proxy.GetProxy(destination)!.Port);

        address = null!;
        Assert.Null(proxy.GetProxy(destination));
        Assert.True(proxy.IsBypassed(destination));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Starting_the_manager_points_the_proxy_at_the_live_settings()
    {
        // The single wiring point. It takes the CONFIG, not the address, so a later edit in Settings
        // needs nothing to be told about it.
        var config = Config.New();
        config.Settings.ProxyAddress = "socks5://127.0.0.1:12000";
        new DownloadManager().Initialize(config);

        Assert.Equal("socks5://127.0.0.1:12000", AppProxy.Address);

        config.Settings.ProxyAddress = "http://proxy.local:3128";
        Assert.Equal("http://proxy.local:3128", AppProxy.Address);

        config.Settings.ProxyAddress = "";
        Assert.Null(AppProxy.Address);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_client_from_CreateClient_really_sends_its_requests_to_the_proxy()
    {
        // The end-to-end proof, not a property check: an HTTP proxy is asked for a target with a
        // request line in ABSOLUTE form ("GET http://host/path"), which is how this test can tell a
        // proxied request from a direct one.
        using var proxy = new RecordingHttpProxy();
        AppProxy.AddressSource = () => $"http://127.0.0.1:{proxy.Port}";

        using var client = AppProxy.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var response = await client.GetAsync("http://example.invalid-target.test/thing.json",
            TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var line = await proxy.FirstRequestLine;
        Assert.StartsWith("GET http://example.invalid-target.test/thing.json ", line);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task With_no_proxy_configured_the_same_client_goes_direct()
    {
        // The other half: the proxy must be absent when none is set, or an unset setting would break
        // every request instead of simply not proxying it.
        using var server = new RecordingHttpProxy();
        AppProxy.AddressSource = () => null;

        using var client = AppProxy.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/thing.json",
            TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var line = await server.FirstRequestLine;
        Assert.StartsWith("GET /thing.json ", line); // origin form = it was NOT sent through a proxy
    }
}
