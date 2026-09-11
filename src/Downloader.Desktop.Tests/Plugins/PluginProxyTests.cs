using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

using HlsPlugins = Downloader.Desktop.Plugins.Hls;
using OllamaPlugins = Downloader.Desktop.Plugins.Ollama;
using SiteMediaPlugins = Downloader.Desktop.Plugins.SiteMedia;
using GitHubPlugins = Downloader.Desktop.Plugins.GitHub;
using WebsitePlugins = Downloader.Desktop.Plugins.Website;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// A proxy is configured ONCE, in the app, and reaches every plugin — the ones shipped here and any
/// third-party one — without a plugin holding a proxy setting, or any proxy code, of its own.
///
/// <para>The mechanism is a single SDK member: <c>IPluginContext.CreateHttpClient()</c>. So the thing
/// worth pinning is not "does an HttpClient have a proxy" (that is <c>AppProxyTests</c>) but "does
/// every plugin actually ASK for its client instead of newing one up" — a plugin that forgets is
/// silently un-proxied, which is exactly the state all of them were in before.</para>
/// </summary>
public class PluginProxyTests : IDisposable
{
    private readonly Func<string> _original = AppProxy.AddressSource;
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "dldesktop-proxy-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        AppProxy.AddressSource = _original;
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    public static TheoryData<string, IDownloaderPlugin> AllPlugins() => new()
    {
        { "hls", new HlsPlugins.HlsPlugin() },
        { "site-media", new SiteMediaPlugins.SiteMediaPlugin() },
        { "website", new WebsitePlugins.WebsitePlugin() },
        { "ollama", new OllamaPlugins.OllamaPlugin() },
        { "github", new GitHubPlugins.GitHubReleasesPlugin() },
    };

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [MemberData(nameof(AllPlugins))]
    public void Every_plugin_takes_its_http_client_from_the_host(string name, IDownloaderPlugin plugin)
    {
        var context = new CountingContext(_dataDir);

        plugin.Initialize(context);

        Assert.True(context.ClientsCreated > 0,
            $"the {name} plugin built its own HttpClient instead of asking the host for one — " +
            "it will ignore the user's proxy setting");
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_plugin_is_told_the_proxy_address_for_the_tools_it_spawns()
    {
        // yt-dlp is a separate program; no HttpClient of ours can proxy it, so the address has to be
        // readable. This is the only reason ProxyAddress exists beside CreateHttpClient.
        AppProxy.AddressSource = () => "socks5://127.0.0.1:12000";

        Assert.Equal("socks5://127.0.0.1:12000", AppProxy.Address);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_context_that_predates_the_proxy_still_works_and_simply_has_none()
    {
        // The SDK members are default-implemented so an external plugin built against an older host —
        // or its own test double — keeps compiling. Un-proxied is the honest fallback, not a crash.
        IPluginContext legacy = new LegacyContext();

        using var client = legacy.CreateHttpClient();

        Assert.NotNull(client);
        Assert.Null(legacy.ProxyAddress);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_yt_dlp_command_line_carries_the_proxy_when_one_is_set()
    {
        var args = SiteMediaPlugins.YtDlpBinary.BuildArgs(
            "https://example.com/watch", cookieFile: null, denoPath: null, extractorArgs: null,
            proxy: "socks5://127.0.0.1:12000");

        Assert.Contains("--proxy \"socks5://127.0.0.1:12000\"", args);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_yt_dlp_command_line_says_nothing_about_a_proxy_when_none_is_set(string? proxy)
    {
        // An empty --proxy would be worse than none: yt-dlp reads "--proxy ''" as "force direct".
        var args = SiteMediaPlugins.YtDlpBinary.BuildArgs(
            "https://example.com/watch", cookieFile: null, denoPath: null, extractorArgs: null, proxy: proxy);

        Assert.DoesNotContain("--proxy", args);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_transfer_provider_keeps_working_after_its_shared_client_has_been_used()
    {
        // A transfer is built per download but the host's client is shared, and HttpClient refuses a
        // Timeout change once it has sent ANYTHING. So configuring it inside the transfer worked only
        // until the first crawl had made a request — every offline copy after that threw. The used
        // client is the whole point of this test: without the request below it cannot fail.
        using var server = new RecordingHttpProxy();
        AppProxy.AddressSource = () => null;

        // The production sequence: Initialize hands the provider a FRESH client, which the provider
        // configures once; every transfer then shares THAT client.
        var shared = AppProxy.CreateClient();
        var provider = new WebsitePlugins.WebsiteTransferProvider(NullLogger.Instance, shared);
        Assert.NotNull(provider.Create("websitezip:https://example.com", _dataDir));

        // The first crawl sends a request on the shared client. From here HttpClient refuses any
        // change to Timeout, so a transfer that configures the client again throws.
        await shared.GetAsync($"http://127.0.0.1:{server.Port}/warm", TestContext.Current.CancellationToken);

        var second = Record.Exception(() => provider.Create("websitezip:https://example.org", _dataDir));

        Assert.Null(second);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void No_plugin_builds_an_http_client_inside_Initialize()
    {
        // The guard that keeps this true for the NEXT plugin. A `new HttpClient()` in a component's
        // constructor is fine (that is the fallback a test uses); one in Initialize means the plugin
        // declined the host's client, and the user's proxy is silently ignored.
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(PluginSourceRoot(), "*Plugin.cs", SearchOption.AllDirectories))
        {
            var body = InitializeBody(File.ReadAllText(file));
            if (body is not null && body.Contains("new HttpClient", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "these plugins build their own HttpClient in Initialize instead of calling " +
            $"context.CreateHttpClient(): {string.Join(", ", offenders)}");
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_source_scan_can_actually_see_an_offender()
    {
        // A scanner nobody has ever seen fail is a scanner that might be matching nothing at all.
        var source = "public void Initialize(IPluginContext context)\n{\n    var http = new HttpClient();\n}\n";

        Assert.Contains("new HttpClient", InitializeBody(source));
        Assert.Null(InitializeBody("public void Somethingelse() { var http = new HttpClient(); }"));
    }

    /// <summary>The body of an <c>Initialize(IPluginContext …)</c> method, or null when there is none.</summary>
    private static string? InitializeBody(string source)
    {
        var start = Regex.Match(source, @"void\s+Initialize\s*\(\s*IPluginContext[^)]*\)\s*\{");
        if (!start.Success) return null;

        var depth = 0;
        for (var i = start.Index + start.Length - 1; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[(start.Index + start.Length)..i];
        }
        return null;
    }

    private static string PluginSourceRoot()
    {
        // Walk up from the test binary to the repo's src/ folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Downloader.Desktop.Plugins")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Downloader.Desktop.Plugins");
    }

    /// <summary>Records how many times a plugin asked the host for a client.</summary>
    private sealed class CountingContext : IPluginContext
    {
        private readonly List<HttpClient> _issued = new();
        public CountingContext(string dataDirectory) => DataDirectory = dataDirectory;

        public int ClientsCreated => _issued.Count;

        public HttpClient CreateHttpClient()
        {
            var client = AppProxy.CreateClient();
            _issued.Add(client);
            return client;
        }

        public string ProxyAddress => AppProxy.Address;
        public void RegisterResolver(ILinkResolver resolver) { }
        public void RegisterTransferProvider(ITransferProvider provider) { }
        public void RegisterPostProcessor(IPostProcessor processor) { }
        public void RegisterPostDownloadAction(IPostDownloadAction action) { }
        public string DataDirectory { get; }
        public ILogger Logger => NullLogger.Instance;
    }

    /// <summary>Implements only what the interface required before the proxy members were added.</summary>
    private sealed class LegacyContext : IPluginContext
    {
        public void RegisterResolver(ILinkResolver resolver) { }
        public void RegisterTransferProvider(ITransferProvider provider) { }
        public void RegisterPostProcessor(IPostProcessor processor) { }
        public string DataDirectory => ".";
        public ILogger Logger => NullLogger.Instance;
    }
}
