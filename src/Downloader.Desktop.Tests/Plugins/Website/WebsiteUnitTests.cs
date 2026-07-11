using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins.Website;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Website;

/// <summary>Pure logic of the Website plugin: reference extraction, URL→local mapping, rewriting,
/// and the page-heuristic / variant trigger — no network, no filesystem.</summary>
public class WebsiteUnitTests
{
    // ---- LinkExtractor: HTML ----------------------------------------------

    [Fact]
    public void Extracts_and_classifies_html_references()
    {
        const string html = """
            <html><head>
            <link rel="stylesheet" href="/css/site.css">
            <link rel="icon" href="/favicon.ico">
            <script src="js/app.js"></script>
            <style>.hero { background: url('/img/hero.jpg'); }</style>
            </head><body>
            <a href="/about.html">About</a>
            <a href="mailto:x@y.z">mail</a>
            <img src="/img/logo.png" srcset="/img/logo.png 1x, /img/logo@2x.png 2x">
            <video poster="/img/poster.jpg" src="/media/clip.mp4"></video>
            <iframe src="/embed/frame.html"></iframe>
            </body></html>
            """;

        var refs = LinkExtractor.ExtractHtmlRefs(html);

        Assert.Equal(RefKind.Stylesheet, refs.Single(r => r.Value == "/css/site.css").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "/favicon.ico").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "js/app.js").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "/img/hero.jpg").Kind);
        Assert.Equal(RefKind.PageLink, refs.Single(r => r.Value == "/about.html").Kind);
        Assert.Equal(RefKind.PageLink, refs.Single(r => r.Value == "/embed/frame.html").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "/media/clip.mp4").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "/img/poster.jpg").Kind);
        // srcset yields one ref per candidate URL (the plain src dedupes against the 1x candidate span)
        Assert.Contains(refs, r => r.Value == "/img/logo@2x.png" && r.Kind == RefKind.Requisite);
        Assert.Contains(refs, r => r.Value == "/img/logo.png");
        // mailto: is extracted raw; normalization is what rejects it
        Assert.False(LinkExtractor.TryNormalize(new Uri("https://s.com/"), "mailto:x@y.z", out _));
    }

    [Fact]
    public void Extracts_css_references()
    {
        const string css = """
            @import "theme.css";
            @font-face { src: url('/fonts/inter.woff2') format("woff2"); }
            .logo { background-image: url(../img/logo.png); }
            """;

        var refs = LinkExtractor.ExtractCssRefs(css);

        Assert.Equal(RefKind.Stylesheet, refs.Single(r => r.Value == "theme.css").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "/fonts/inter.woff2").Kind);
        Assert.Equal(RefKind.Requisite, refs.Single(r => r.Value == "../img/logo.png").Kind);
    }

    [Fact]
    public void Normalization_resolves_relatives_and_rejects_non_web_targets()
    {
        var doc = new Uri("https://site.com/blog/post.html");

        Assert.True(LinkExtractor.TryNormalize(doc, "../img/x.png", out var abs));
        Assert.Equal("https://site.com/img/x.png", abs.AbsoluteUri);

        Assert.True(LinkExtractor.TryNormalize(doc, "/a?b=1#frag", out var noFrag));
        Assert.Equal("https://site.com/a?b=1", noFrag.AbsoluteUri); // fragment stripped

        Assert.True(LinkExtractor.TryNormalize(doc, "https://cdn.other.com/lib.js", out var cross));
        Assert.Equal("cdn.other.com", cross.Host);

        Assert.False(LinkExtractor.TryNormalize(doc, "#section", out _));
        Assert.False(LinkExtractor.TryNormalize(doc, "javascript:void(0)", out _));
        Assert.False(LinkExtractor.TryNormalize(doc, "data:image/png;base64,AAAA", out _));
        Assert.False(LinkExtractor.TryNormalize(doc, "", out _));
    }

    [Fact]
    public void Rewrite_splices_replacements_precisely()
    {
        const string html = """<a href="/x">x</a><img src="/y.png">""";
        var refs = LinkExtractor.ExtractHtmlRefs(html);

        var result = LinkExtractor.Rewrite(html,
            refs.Select(r => (r, r.Value == "/x" ? "x.html" : "img/y.png")).ToList());

        Assert.Equal("""<a href="x.html">x</a><img src="img/y.png">""", result);
    }

    // ---- LocalPathMapper ---------------------------------------------------

    [Theory]
    [InlineData("https://site.com/", true, "site.com/index.html")]
    [InlineData("https://site.com/docs/", true, "site.com/docs/index.html")]
    [InlineData("https://site.com/docs/intro", true, "site.com/docs/intro.html")]
    [InlineData("https://site.com/page.php", true, "site.com/page.php.html")]
    [InlineData("https://site.com/about.html", true, "site.com/about.html")]
    [InlineData("https://cdn.com/img/logo.png", false, "cdn.com/img/logo.png")]
    public void Maps_urls_to_local_paths(string url, bool isPage, string expected) =>
        Assert.Equal(expected, LocalPathMapper.MapToLocalPath(new Uri(url), isPage));

    [Fact]
    public void Query_strings_hash_into_distinct_file_names()
    {
        var a = LocalPathMapper.MapToLocalPath(new Uri("https://s.com/page?id=1"), isPage: true);
        var b = LocalPathMapper.MapToLocalPath(new Uri("https://s.com/page?id=2"), isPage: true);
        Assert.NotEqual(a, b);
        Assert.StartsWith("s.com/page_q", a);
        Assert.EndsWith(".html", a);
    }

    [Theory]
    [InlineData("site.com/index.html", "site.com/css/site.css", "css/site.css")]
    [InlineData("site.com/blog/post.html", "site.com/img/x.png", "../img/x.png")]
    [InlineData("site.com/a/b/c.html", "cdn.com/lib.js", "../../../cdn.com/lib.js")]
    [InlineData("site.com/a.html", "site.com/b.html", "b.html")]
    public void Relative_paths_between_local_files(string from, string to, string expected) =>
        Assert.Equal(expected, LocalPathMapper.RelativePath(from, to));

    // ---- WebsiteResolver ----------------------------------------------------

    [Theory]
    [InlineData("https://site.com/", true)]
    [InlineData("https://site.com/blog/post", true)]
    [InlineData("https://site.com/page.html", true)]
    [InlineData("https://site.com/page.php?id=3", true)]
    [InlineData("https://site.com/file.zip", false)]
    [InlineData("https://site.com/img/logo.png", false)]
    [InlineData("ftp://site.com/", false)]
    [InlineData("not a url", false)]
    public void Page_heuristic(string url, bool expected) =>
        Assert.Equal(expected, WebsiteResolver.LooksLikePage(url));

    [Fact]
    public void Resolver_claims_scheme_urls_and_is_a_fallback()
    {
        var resolver = new WebsiteResolver();
        Assert.True(resolver.IsFallback);
        Assert.True(resolver.CanResolve("websitezip:https://site.com/page"));
        Assert.True(resolver.CanResolve("https://site.com/page"));
        Assert.False(resolver.CanResolve("https://site.com/file.exe"));
    }

    [Fact]
    public async Task Default_resolve_is_a_pass_through()
    {
        var resolver = new WebsiteResolver();
        var plan = await resolver.ResolveAsync("https://site.com/page", CancellationToken.None);
        Assert.Equal("https://site.com/page", Assert.Single(plan.Parts).Url);
        Assert.Equal(Downloader.Desktop.Plugins.PostProcessKind.None, plan.PostProcess.Kind);
    }

    [Fact]
    public async Task Non_page_urls_offer_no_variant_without_any_network_probe()
    {
        var resolver = new WebsiteResolver();
        Assert.Null(await resolver.GetVariantsAsync("https://site.com/file.zip", null, CancellationToken.None));
        Assert.Null(await resolver.GetVariantsAsync("websitezip:https://site.com/x", null, CancellationToken.None));
    }

    [Fact]
    public void Zip_name_comes_from_the_bare_host() =>
        Assert.Equal("site.com.zip", WebsiteTransfer.SuggestedZipName(new Uri("https://www.site.com/deep/page")));
}
