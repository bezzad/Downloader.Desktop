using System;
using System.IO;
using System.Linq;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The GET form of `/api/add` carrying a per-download request context (issue #7 follow-up). The reporter
/// drives the app from a capture tool whose "invoke application" template is a GET URL, so what it has to
/// hand is the WIRE shapes — a `Cookie:` header string and a `Name: value` block — not the JSON body's
/// per-cookie objects. Before this, `FromQuery` parsed neither and still answered 201.
/// </summary>
public class QueryRequestContextTests
{
    private const string Target = "https://cdn.example.com/v/index.m3u8";

    // ---------------- ParseCookieHeader ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseCookieHeader_splits_pairs_and_scopes_them_to_the_target_host()
    {
        var cookies = LocalApiService.ParseCookieHeader("SID=abc; pref=1", Target);

        Assert.Equal(2, cookies.Count);
        Assert.Equal("SID", cookies[0].Name);
        Assert.Equal("abc", cookies[0].Value);
        Assert.Equal("cdn.example.com", cookies[0].Domain);
        Assert.Equal("/", cookies[0].Path);
        Assert.True(cookies[0].Secure); // https target
        Assert.Equal("pref", cookies[1].Name);
        Assert.Equal("1", cookies[1].Value);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseCookieHeader_keeps_a_value_containing_equals_verbatim()
    {
        // Base64/JWT cookie values routinely contain '=' padding — only the FIRST '=' separates.
        var cookies = LocalApiService.ParseCookieHeader("token=eyJhbGc=.payload==", Target);

        var only = Assert.Single(cookies);
        Assert.Equal("token", only.Name);
        Assert.Equal("eyJhbGc=.payload==", only.Value);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseCookieHeader_tolerates_whitespace_and_a_trailing_semicolon()
    {
        var cookies = LocalApiService.ParseCookieHeader("  SID = abc ;  pref=1;  ", Target);

        Assert.Equal(2, cookies.Count);
        Assert.Equal("SID", cookies[0].Name);
        Assert.Equal("abc", cookies[0].Value);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage-with-no-pair")]
    [InlineData("=novalue")]      // empty name
    [InlineData(";;;")]
    public void ParseCookieHeader_returns_nothing_for_input_with_no_usable_pair(string? header)
    {
        Assert.Empty(LocalApiService.ParseCookieHeader(header, Target));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseCookieHeader_drops_cookies_when_the_target_url_has_no_host()
    {
        // No host means no domain to scope them to. Inventing one would send a session somewhere unintended.
        Assert.Empty(LocalApiService.ParseCookieHeader("SID=abc", "not-a-url"));
        Assert.Empty(LocalApiService.ParseCookieHeader("SID=abc", null));
    }

    // ---------------- ParseHeaderBlock ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseHeaderBlock_parses_lines_case_insensitively()
    {
        var headers = LocalApiService.ParseHeaderBlock("Origin: https://site.example\nX-Token: abc");

        Assert.Equal(2, headers.Count);
        Assert.Equal("https://site.example", headers["Origin"]);
        Assert.Equal("abc", headers["x-token"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseHeaderBlock_keeps_a_value_containing_a_colon()
    {
        var headers = LocalApiService.ParseHeaderBlock("Referer: https://site.example/watch/42");

        Assert.Equal("https://site.example/watch/42", headers["Referer"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ParseHeaderBlock_skips_blank_and_malformed_lines_without_failing()
    {
        var headers = LocalApiService.ParseHeaderBlock(
            "Origin: https://site.example\n\nnot-a-header\r\n: novalue\nX-Empty:   \nX-Token: abc");

        Assert.Equal(2, headers.Count);
        Assert.Equal("https://site.example", headers["Origin"]);
        Assert.Equal("abc", headers["X-Token"]);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseHeaderBlock_returns_nothing_for_empty_input(string? block)
    {
        Assert.Empty(LocalApiService.ParseHeaderBlock(block));
    }

    // ---------------- FromQuery, end to end ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FromQuery_carries_cookies_headers_and_referer_through_to_the_item()
    {
        var uri = new Uri("http://127.0.0.1:15151/api/add" +
                          "?url=" + Uri.EscapeDataString(Target) +
                          "&referer=" + Uri.EscapeDataString("https://site.example/watch/42") +
                          "&cookies=" + Uri.EscapeDataString("SID=abc; pref=1") +
                          "&headers=" + Uri.EscapeDataString("Origin: https://site.example\nX-Token: abc"));

        var req = ApiAddRequest.FromQuery(uri);
        Assert.Null(req.Error);

        var item = LocalApiService.BuildItem(req, Config.New());
        try
        {
            Assert.Equal(2, item.Request.Cookies.Count);
            Assert.Equal("abc", item.Request.Cookies.First(c => c.Name == "SID").Value);
            Assert.Equal("https://site.example", item.Request.Headers["Origin"]);
            Assert.Equal("abc", item.Request.Headers["X-Token"]);
            Assert.Equal("https://site.example/watch/42", item.Referer);
        }
        finally
        {
            if (item.CookieFilePath is { Length: > 0 } p && File.Exists(p))
                File.Delete(p);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FromQuery_without_a_context_still_adds_the_download()
    {
        var req = ApiAddRequest.FromQuery(
            new Uri("http://127.0.0.1:15151/api/add?url=" + Uri.EscapeDataString(Target)));

        Assert.Null(req.Error);
        Assert.Empty(req.Cookies);
        Assert.Empty(req.Headers);

        var item = LocalApiService.BuildItem(req, Config.New());
        Assert.Empty(item.Request.Cookies);
        Assert.Empty(item.Request.Headers);
        Assert.Null(item.Referer);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FromQuery_with_an_unparseable_context_still_adds_the_download()
    {
        // "A parse problem must never fail the add" — the caller loses its context, not its download.
        var req = ApiAddRequest.FromQuery(new Uri(
            "http://127.0.0.1:15151/api/add?url=" + Uri.EscapeDataString(Target) +
            "&cookies=" + Uri.EscapeDataString("garbage") +
            "&headers=" + Uri.EscapeDataString("garbage")));

        Assert.Null(req.Error);
        Assert.Empty(req.Cookies);
        Assert.Empty(req.Headers);
    }
}
