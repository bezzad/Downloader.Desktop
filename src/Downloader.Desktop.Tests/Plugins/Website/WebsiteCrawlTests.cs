using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins.Website;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Website;

/// <summary>
/// End-to-end crawl against a loopback site (multi-page + stylesheet with nested assets + external
/// links): captured files, offline rewriting, zip packaging, pause gate, cancel. No external network.
/// </summary>
public class WebsiteCrawlTests
{
    private sealed class LoopbackSite : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, (string ContentType, byte[] Body)> _routes = new();
        public string Url { get; }
        public Uri Root => new(Url);

        public LoopbackSite()
        {
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = AcceptLoop();
        }

        public void Add(string path, string contentType, string body) =>
            _routes[path] = (contentType, Encoding.UTF8.GetBytes(body));

        public void Add(string path, string contentType, byte[] body) =>
            _routes[path] = (contentType, body);

        private async Task AcceptLoop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                var path = ctx.Request.Url!.AbsolutePath + (ctx.Request.Url.Query ?? "");
                if (_routes.TryGetValue(path, out var route))
                {
                    ctx.Response.ContentType = route.ContentType;
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.OutputStream.WriteAsync(route.Body);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
        }

        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose() => _listener.Close();
    }

    private static LoopbackSite BuildSite()
    {
        var site = new LoopbackSite();
        site.Add("/", "text/html", $"""
            <html><head><link rel="stylesheet" href="/css/site.css"></head>
            <body>
            <a href="/page2.html">Next</a>
            <a href="https://example.org/external">External</a>
            <img src="/img/logo.png">
            <script src="/js/app.js"></script>
            </body></html>
            """);
        site.Add("/page2.html", "text/html", """
            <html><body><a href="/">Home</a><img src="/img/logo.png"></body></html>
            """);
        site.Add("/css/site.css", "text/css",
            "@font-face { src: url('/fonts/inter.woff2'); } .x { background: url(../img/logo.png); }");
        site.Add("/img/logo.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });
        site.Add("/js/app.js", "application/javascript", "console.log('hi');");
        site.Add("/fonts/inter.woff2", "font/woff2", new byte[] { 1, 2, 3, 4, 5 });
        return site;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "website-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static HttpClient Client() => new() { Timeout = Timeout.InfiniteTimeSpan };

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Crawl_captures_pages_and_requisites_and_rewrites_offline()
    {
        using var site = BuildSite();
        var work = TempDir();
        try
        {
            var crawler = new SiteCrawler(Client());
            var captured = await crawler.CrawlAsync(site.Root, work, CancellationToken.None);

            var host = site.Root.Host; // 127.0.0.1
            string Read(string rel) => File.ReadAllText(Path.Combine(work, host, rel));

            // pages + assets all captured, including the CSS-referenced font
            Assert.True(File.Exists(Path.Combine(work, host, "index.html")));
            Assert.True(File.Exists(Path.Combine(work, host, "page2.html")));
            Assert.True(File.Exists(Path.Combine(work, host, "css", "site.css")));
            Assert.True(File.Exists(Path.Combine(work, host, "img", "logo.png")));
            Assert.True(File.Exists(Path.Combine(work, host, "js", "app.js")));
            Assert.True(File.Exists(Path.Combine(work, host, "fonts", "inter.woff2")));
            Assert.Equal(6, captured);

            // index: same-host page + requisites rewritten relative; external link stays absolute
            var index = Read("index.html");
            Assert.Contains("href=\"css/site.css\"", index);
            Assert.Contains("href=\"page2.html\"", index);
            Assert.Contains("src=\"img/logo.png\"", index);
            Assert.Contains("src=\"js/app.js\"", index);
            Assert.Contains("https://example.org/external", index);
            Assert.DoesNotContain(site.Url + "css", index);

            // page2 links back to the captured home page
            Assert.Contains("href=\"index.html\"", Read("page2.html"));

            // stylesheet: nested references rewritten relative to the css file's own folder
            var css = Read(Path.Combine("css", "site.css"));
            Assert.Contains("../fonts/inter.woff2", css);
            Assert.Contains("../img/logo.png", css);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Page_cap_bounds_the_crawl_but_still_succeeds()
    {
        using var site = BuildSite();
        var work = TempDir();
        try
        {
            var crawler = new SiteCrawler(Client(), new CrawlOptions { MaxPages = 1 });
            var captured = await crawler.CrawlAsync(site.Root, work, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(work, site.Root.Host, "index.html")));
            Assert.False(File.Exists(Path.Combine(work, site.Root.Host, "page2.html")));
            Assert.True(captured >= 1);

            // the uncaptured page link falls back to its absolute URL
            var index = File.ReadAllText(Path.Combine(work, site.Root.Host, "index.html"));
            Assert.Contains(site.Url + "page2.html", index);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Transfer_produces_a_zip_with_the_rewritten_site()
    {
        using var site = BuildSite();
        var target = TempDir();
        try
        {
            var transfer = new WebsiteTransfer(WebsiteResolver.Scheme + site.Url, target);
            var progressed = 0;
            transfer.ProgressChanged += (_, p) => { if (p.BytesReceived > 0) progressed++; };

            var zipPath = await transfer.StartAsync(CancellationToken.None);

            Assert.True(File.Exists(zipPath));
            Assert.EndsWith(".zip", zipPath);
            Assert.True(progressed > 0);

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            Assert.Contains($"{site.Root.Host}/index.html", names);
            Assert.Contains($"{site.Root.Host}/css/site.css", names);
            Assert.Contains($"{site.Root.Host}/fonts/inter.woff2", names);

            using var reader = new StreamReader(zip.Entries.First(e => e.FullName.EndsWith("index.html")).Open());
            Assert.Contains("href=\"css/site.css\"", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Pause_suspends_fetching_and_resume_continues()
    {
        using var site = BuildSite();
        var work = TempDir();
        try
        {
            var crawler = new SiteCrawler(Client());
            crawler.Pause(); // gate closed before the first request

            var crawl = crawler.CrawlAsync(site.Root, work, CancellationToken.None);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.False(crawl.IsCompleted);
            Assert.False(File.Exists(Path.Combine(work, site.Root.Host, "index.html")));

            crawler.Resume();
            var captured = await crawl.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(6, captured);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Cancel_aborts_the_crawl_and_the_transfer_cleans_up()
    {
        using var site = BuildSite();
        var target = TempDir();
        try
        {
            var transfer = new WebsiteTransfer(WebsiteResolver.Scheme + site.Url, target);
            transfer.Pause(); // hold the crawl so cancellation is what ends it
            using var cts = new CancellationTokenSource();
            var start = transfer.StartAsync(cts.Token);

            cts.CancelAfter(100);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

            Assert.Empty(Directory.GetFiles(target)); // no zip produced
            // the temp working folder is removed (nothing left behind under %TEMP%/downloader-website-*)
            Assert.DoesNotContain(Directory.GetDirectories(Path.GetTempPath(), "downloader-website-*"),
                d => Directory.GetLastWriteTimeUtc(d) > DateTime.UtcNow.AddMinutes(-1));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    /// <summary>Live-network sanity check (gated like the other DLDESKTOP_NET tests): crawl a real,
    /// tiny, stable site end-to-end through the transfer. Run locally with DLDESKTOP_NET=1.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Live_crawl_of_a_real_site_produces_a_zip()
    {
        if (Environment.GetEnvironmentVariable("DLDESKTOP_NET") != "1")
            return;

        var target = TempDir();
        try
        {
            var transfer = new WebsiteTransfer(WebsiteResolver.Scheme + "https://example.com/", target);
            var zipPath = await transfer.StartAsync(CancellationToken.None);

            using var zip = ZipFile.OpenRead(zipPath);
            var index = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("index.html"));
            Assert.NotNull(index);
            using var reader = new StreamReader(index.Open());
            Assert.Contains("Example", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Unreachable_start_page_fails_with_a_readable_error()
    {
        var target = TempDir();
        try
        {
            var transfer = new WebsiteTransfer(
                WebsiteResolver.Scheme + "http://127.0.0.1:1/", target); // nothing listens on port 1
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => transfer.StartAsync(CancellationToken.None));
            Assert.Contains("Could not fetch the page", ex.Message);
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
