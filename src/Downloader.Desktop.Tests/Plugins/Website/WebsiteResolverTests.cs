using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins.Website;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Website;

/// <summary>
/// What the offline-copy plugin will and will not claim.
///
/// This resolver is a FALLBACK: it offers "save this page offline" for anything that looks like a web
/// page. That makes its claim heuristic unusually sensitive — claim too widely and it starts offering
/// an offline copy of an installer or a video, and (before the two-pass lookup was added) polluted
/// the quality list of links a specific plugin owns. Claim too narrowly and the feature silently
/// never appears. So the boundary cases are pinned here rather than left to the live probe.
/// </summary>
public class WebsiteResolverTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://example.com")]              // bare host
    [InlineData("https://example.com/")]             // trailing slash
    [InlineData("https://example.com/docs")]         // extensionless path
    [InlineData("https://example.com/docs/")]
    [InlineData("https://example.com/index.html")]
    [InlineData("https://example.com/index.htm")]
    [InlineData("https://example.com/page.php")]
    [InlineData("http://example.com/page")]          // plain http counts too
    public void A_page_like_link_is_claimed(string url)
    {
        Assert.True(WebsiteResolver.LooksLikePage(url));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://example.com/setup.exe")]
    [InlineData("https://example.com/archive.zip")]
    [InlineData("https://example.com/video.mp4")]
    [InlineData("https://example.com/stream.m3u8")]  // owned by the streaming plugin
    [InlineData("https://example.com/manifest.mpd")]
    [InlineData("https://example.com/photo.jpg")]
    [InlineData("https://example.com/doc.pdf")]
    public void A_link_to_an_actual_file_is_not_claimed(string url)
    {
        // Offering "save an offline copy" of an installer is nonsense, and claiming a media manifest
        // would take it away from the plugin that can actually download it.
        Assert.False(WebsiteResolver.LooksLikePage(url));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file")]           // not http(s)
    [InlineData("file:///etc/passwd")]
    [InlineData("magnet:?xt=urn:btih:abc")]
    [InlineData("gemma3:12b")]                        // an Ollama model reference
    public void Anything_that_is_not_an_http_page_is_rejected(string url)
    {
        Assert.False(WebsiteResolver.LooksLikePage(url));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_null_url_is_rejected_rather_than_throwing()
    {
        Assert.False(WebsiteResolver.LooksLikePage(null));
        Assert.False(WebsiteResolver.IsSchemeUrl(null));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("websitezip:https://example.com/", true)]
    [InlineData("WEBSITEZIP:https://example.com/", true)]   // scheme match is case-insensitive
    [InlineData("https://example.com/", false)]
    [InlineData("website:https://example.com/", false)]
    public void The_offline_copy_scheme_is_recognised(string url, bool expected)
    {
        Assert.Equal(expected, WebsiteResolver.IsSchemeUrl(url));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_resolver_claims_both_its_scheme_and_page_links()
    {
        var resolver = new WebsiteResolver();

        Assert.True(resolver.CanResolve("websitezip:https://example.com/"));
        Assert.True(resolver.CanResolve("https://example.com/docs"));
        Assert.False(resolver.CanResolve("https://example.com/setup.exe"));

        // Being a fallback is what keeps it from shadowing GitHub / streaming / Ollama links.
        Assert.True(resolver.IsFallback);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("text/html", true)]
    [InlineData("TEXT/HTML", true)]
    [InlineData("application/xhtml+xml", true)]
    [InlineData("application/json", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData("video/mp4", false)]
    [InlineData(null, false)]
    public void Only_html_content_confirms_the_offer(string? mediaType, bool expected)
    {
        // The claim heuristic is cheap and syntactic; this is the real confirmation, so a JSON API
        // endpoint at an extensionless URL does not end up offering an "offline copy".
        Assert.Equal(expected, WebsiteResolver.IsHtml(mediaType));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Resolving_strips_the_scheme_so_a_plain_add_behaves_normally()
    {
        var resolver = new WebsiteResolver();

        var plan = await resolver.ResolveAsync("websitezip:https://example.com/page", CancellationToken.None);

        // Picking no variant must behave exactly like a plain add of the underlying link.
        var part = Assert.Single(plan.Parts);
        Assert.Equal("https://example.com/page", part.Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Resolving_a_plain_link_passes_it_through_unchanged()
    {
        var resolver = new WebsiteResolver();

        var plan = await resolver.ResolveAsync("https://example.com/page", CancellationToken.None);

        Assert.Equal("https://example.com/page", Assert.Single(plan.Parts).Url);
    }

    // ---- the live content-type probe --------------------------------------

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task An_html_page_is_offered_as_an_unchecked_offline_copy()
    {
        using var server = new ProbeServer("text/html; charset=utf-8", answerHead: true);
        var resolver = new WebsiteResolver();

        var variants = await resolver.GetVariantsAsync(server.Url + "docs", null, CancellationToken.None);

        var variant = Assert.Single(variants);
        Assert.Equal("offline-zip", variant.Id);
        // Unchecked on purpose: this is a fallback offer, so leaving it alone must still give the
        // user the plain download they asked for.
        Assert.False(variant.IsDefault);
        // The substitute URL switches the item onto the scheme the transfer provider claims.
        Assert.Equal(WebsiteResolver.Scheme + server.Url + "docs", variant.SubstituteUrl);
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_page_like_url_serving_something_else_is_not_offered()
    {
        // An extensionless URL that is really a JSON API — the syntactic claim says "page", the
        // probe says otherwise, and the probe wins.
        using var server = new ProbeServer("application/json", answerHead: true);
        var resolver = new WebsiteResolver();

        Assert.Null(await resolver.GetVariantsAsync(server.Url + "api", null, CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_server_that_rejects_HEAD_is_probed_with_a_ranged_get()
    {
        using var server = new ProbeServer("text/html", answerHead: false);
        var resolver = new WebsiteResolver();

        // Plenty of servers 405 a HEAD; falling back to a one-byte ranged GET is what keeps the
        // offer working for them.
        Assert.NotNull(await resolver.GetVariantsAsync(server.Url + "docs", null, CancellationToken.None));
        Assert.True(server.SawRangedGet);
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task An_unreachable_host_simply_offers_nothing()
    {
        var resolver = new WebsiteResolver();

        // A probe failure must never block or delay adding a download.
        Assert.Null(await resolver.GetVariantsAsync("http://127.0.0.1:1/docs", null, CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_link_that_is_not_page_like_is_never_probed()
    {
        using var server = new ProbeServer("text/html", answerHead: true);
        var resolver = new WebsiteResolver();

        Assert.Null(await resolver.GetVariantsAsync(server.Url + "setup.exe", null, CancellationToken.None));
        Assert.Equal(0, server.Requests);
    }

    /// <summary>Loopback server that answers the content-type probe with a chosen media type.</summary>
    private sealed class ProbeServer : System.IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();
        private readonly string _mediaType;
        private readonly bool _answerHead;
        private int _requests;

        public ProbeServer(string mediaType, bool answerHead)
        {
            _mediaType = mediaType;
            _answerHead = answerHead;

            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();

            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        public string Url { get; }
        public int Requests => Volatile.Read(ref _requests);
        public bool SawRangedGet { get; private set; }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }

                Interlocked.Increment(ref _requests);
                try
                {
                    if (ctx.Request.HttpMethod == "HEAD" && !_answerHead)
                    {
                        ctx.Response.StatusCode = 405;
                        ctx.Response.Close();
                        continue;
                    }

                    if (ctx.Request.HttpMethod == "GET" && !string.IsNullOrEmpty(ctx.Request.Headers["Range"]))
                        SawRangedGet = true;

                    ctx.Response.ContentType = _mediaType;
                    ctx.Response.StatusCode = 200;
                    var body = System.Text.Encoding.UTF8.GetBytes("<html><body>hi</body></html>");
                    ctx.Response.ContentLength64 = body.Length;
                    if (ctx.Request.HttpMethod != "HEAD")
                        ctx.Response.OutputStream.Write(body, 0, body.Length);
                    ctx.Response.Close();
                }
                catch
                {
                    // client went away
                }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }
}
