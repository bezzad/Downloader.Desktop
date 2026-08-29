using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The local API's request parsing, at the edges.
///
/// Everything here runs on input from outside the app — a browser extension, a capture tool, a shell
/// script — so "malformed" is the normal case, not the exception. Two rules shape all of it: a bad
/// request must never take the app down, and a partially-understood request must never answer 201
/// while silently dropping what it did not understand. The second one is a real past bug: the query
/// form accepted cookies and headers, dropped them, and still reported success, so a capture tool
/// believed it had handed over a session it never delivered.
///
/// Cookies and headers are session secrets, so the counts are asserted, never the values — the same
/// reason the API's own response reports counts only.
/// </summary>
public class LocalApiParsingTests
{
    // ---- the JSON body -----------------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_json_start_flag_decides_whether_the_download_begins(string value, bool expected)
    {
        var req = ApiAddRequest.FromJson($$"""{"url":"https://host/f.zip","start":{{value}}}""");

        Assert.Null(req.Error);
        Assert.Equal(expected, req.Start);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_json_body_without_a_start_flag_begins_the_download()
    {
        // Adding something you do not want to start is the unusual case, so the default is to go.
        Assert.True(ApiAddRequest.FromJson("""{"url":"https://host/f.zip"}""").Start);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Cookies_and_headers_in_a_json_body_are_carried_across()
    {
        var req = ApiAddRequest.FromJson("""
            {
              "url": "https://host/f.zip",
              "referer": "https://host/page",
              "cookies": [
                { "name": "session", "value": "abc", "domain": "host", "path": "/" },
                { "name": "other",   "value": "def", "domain": "host" }
              ],
              "headers": { "X-Token": "t", "User-Agent": "ua" }
            }
            """);

        Assert.Null(req.Error);
        Assert.Equal(2, req.Cookies.Count);
        Assert.Equal(2, req.Headers.Count);
        Assert.Equal("https://host/page", req.Referer);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_malformed_cookie_is_dropped_without_failing_the_add()
    {
        var req = ApiAddRequest.FromJson("""
            {
              "url": "https://host/f.zip",
              "cookies": [
                "not an object",
                { "value": "no name" },
                { "name": "no domain" },
                { "name": "good", "value": "v", "domain": "host" }
              ]
            }
            """);

        // Losing a cookie costs the caller its session; failing the request costs them the download.
        Assert.Null(req.Error);
        Assert.Single(req.Cookies);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""{"url":"https://host/f.zip","cookies":"not an array"}""")]
    [InlineData("""{"url":"https://host/f.zip","headers":"not an object"}""")]
    [InlineData("""{"url":"https://host/f.zip","cookies":[]}""")]
    [InlineData("""{"url":"https://host/f.zip","headers":{}}""")]
    public void Wrongly_typed_context_is_ignored_rather_than_fatal(string json)
    {
        var req = ApiAddRequest.FromJson(json);

        Assert.Null(req.Error);
        Assert.Empty(req.Cookies);
        Assert.Empty(req.Headers);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""{"url":"ftp://host/f.zip"}""")]
    [InlineData("""{"url":"   "}""")]
    [InlineData("""{"url":null}""")]
    [InlineData("""{}""")]
    public void A_request_without_a_usable_http_url_is_rejected(string json)
    {
        // The URL is the only thing the request cannot do without.
        Assert.NotNull(ApiAddRequest.FromJson(json).Error);
    }

    // ---- the query form ----------------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("start=false", false)]
    [InlineData("start=FALSE", false)]
    [InlineData("start=0", false)]
    [InlineData("start=true", true)]
    [InlineData("start=1", true)]
    [InlineData("start=anything-else", true)]
    public void The_query_start_flag_accepts_the_shapes_callers_actually_send(string query, bool expected)
    {
        var req = ApiAddRequest.FromQuery(new Uri($"http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fhost%2Ff.zip&{query}"));

        Assert.Equal(expected, req.Start);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_query_form_carries_cookies_and_headers_in_their_wire_shapes()
    {
        var uri = new Uri("http://127.0.0.1:15151/api/add"
                          + "?url=" + Uri.EscapeDataString("https://host/f.zip")
                          + "&cookies=" + Uri.EscapeDataString("session=abc; other=def")
                          + "&headers=" + Uri.EscapeDataString("X-Token: t\nUser-Agent: ua")
                          + "&referer=" + Uri.EscapeDataString("https://host/page"));

        var req = ApiAddRequest.FromQuery(uri);

        // A capture tool driving us from a GET template used to get 201 back with all of this
        // silently discarded.
        Assert.Equal(2, req.Cookies.Count);
        Assert.Equal(2, req.Headers.Count);
        Assert.Equal("https://host/page", req.Referer);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_query_with_no_context_simply_has_none()
    {
        var req = ApiAddRequest.FromQuery(new Uri("http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fhost%2Ff.zip"));

        Assert.Null(req.Error);
        Assert.Empty(req.Cookies);
        Assert.Empty(req.Headers);
        Assert.Null(req.Referer);
        Assert.True(req.Start);
    }

    // ---- the wire-shape parsers --------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_cookie_header_splits_on_the_first_equals_so_base64_values_survive()
    {
        var cookies = LocalApiService.ParseCookieHeader("a=b; token=eyJhbGciOiJIUzI1NiJ9==; c=d", "https://host/f");

        Assert.Equal(3, cookies.Count);
        // Splitting on every '=' would truncate a base64 or JWT value into nonsense.
        Assert.Equal("eyJhbGciOiJIUzI1NiJ9==", cookies.Single(c => c.Name == "token").Value);
        Assert.All(cookies, c => Assert.Equal("host", c.Domain));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_cookie_is_marked_secure_only_for_an_https_target()
    {
        Assert.True(LocalApiService.ParseCookieHeader("a=b", "https://host/f").Single().Secure);
        Assert.False(LocalApiService.ParseCookieHeader("a=b", "http://host/f").Single().Secure);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-equals-sign")]
    [InlineData("=novalue")]
    [InlineData(";;;")]
    public void A_junk_cookie_header_yields_nothing_rather_than_throwing(string? header)
    {
        Assert.Empty(LocalApiService.ParseCookieHeader(header, "https://host/f"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Cookies_cannot_be_parsed_without_a_target_to_scope_them_to()
    {
        // The domain comes from the target URL; with no usable target there is nothing to attach them to.
        Assert.Empty(LocalApiService.ParseCookieHeader("a=b", null));
        Assert.Empty(LocalApiService.ParseCookieHeader("a=b", "not a url"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_header_block_reads_one_name_value_pair_per_line()
    {
        var headers = LocalApiService.ParseHeaderBlock("X-One: 1\nX-Two:2\r\n  X-Three : 3  ");

        Assert.Equal(3, headers.Count);
        Assert.Equal("1", headers["X-One"]);
        Assert.Equal("2", headers["X-Two"]);
        Assert.Equal("3", headers["X-Three"]);
        // Header names are case-insensitive on the wire.
        Assert.Equal("1", headers["x-one"]);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no colon here")]
    [InlineData(": novalue")]
    [InlineData("noname:")]
    public void A_junk_header_block_yields_nothing_rather_than_throwing(string? block)
    {
        Assert.Empty(LocalApiService.ParseHeaderBlock(block));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""{"id":"abc"}""", "abc")]
    [InlineData("""{ "id" : "abc" }""", "abc")]
    [InlineData("""{"other":"x","id":"abc"}""", "abc")]
    public void An_id_is_read_out_of_a_control_request(string json, string expected)
    {
        Assert.Equal(expected, LocalApiService.ExtractIdFromJson(json));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"id":42}""")]
    [InlineData("[]")]
    public void A_control_request_with_no_readable_id_yields_null(string json)
    {
        Assert.Null(LocalApiService.ExtractIdFromJson(json));
    }

    // ---- turning a request into a download --------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_built_download_carries_the_request_context_but_only_the_referer_persists()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var req = ApiAddRequest.FromJson("""
            {
              "url": "https://host/f.zip",
              "referer": "https://host/page",
              "cookies": [ { "name": "s", "value": "v", "domain": "host" } ],
              "headers": { "X-Token": "t" }
            }
            """);

        var item = LocalApiService.BuildItem(req, config);

        Assert.Equal("https://host/f.zip", item.Url);
        Assert.Equal("https://host/page", item.Referer);
        Assert.Single(item.Request.Cookies);
        Assert.Single(item.Request.Headers);
        // A cookie file is written for the resolver to use, and deleted again when the download ends.
        Assert.False(string.IsNullOrWhiteSpace(item.CookieFilePath));

        try { if (System.IO.File.Exists(item.CookieFilePath)) System.IO.File.Delete(item.CookieFilePath); }
        catch { /* best-effort */ }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_built_download_with_no_context_writes_no_cookie_file()
    {
        Localizer.Instance.Load("en");
        var req = ApiAddRequest.FromJson("""{"url":"https://host/f.zip"}""");

        var item = LocalApiService.BuildItem(req, Config.New());

        Assert.Null(item.CookieFilePath);
        Assert.Empty(item.Request.Cookies);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_is_described_for_the_list_endpoint()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;
        var vm = manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/f.zip",
            FileName = "f.zip",
            SaveFolder = "/tmp",
            Size = 2048,
        }, autoStart: false);

        var described = LocalApiService.DescribeItem(vm);

        // The id is what every control verb addresses, so it has to be there and be a real guid.
        Assert.True(described.ContainsKey("id"));
        Assert.True(Guid.TryParse(described["id"]?.ToString(), out _));
        Assert.Equal("f.zip", described["name"]);
        Assert.Equal("https://10.255.255.1/f.zip", described["url"]);
    }

    // ---- port selection ----------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_last_known_good_port_is_tried_first()
    {
        var previous = LocalApiService.Config;
        try
        {
            var config = Config.New();
            config.Settings.LocalApiPort = LocalApiService.PortRange[3];
            LocalApiService.Config = config;

            var candidates = LocalApiService.CandidatePorts().ToList();

            // Sticking to the port the extension last saw avoids a needless re-discovery round.
            Assert.Equal(LocalApiService.PortRange[3], candidates[0]);
            Assert.Equal(LocalApiService.PortRange.Length, candidates.Distinct().Count());
        }
        finally
        {
            LocalApiService.Config = previous;
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void With_no_remembered_port_the_declared_range_is_tried_in_order()
    {
        var previous = LocalApiService.Config;
        try
        {
            LocalApiService.Config = null;

            // The extension's host_permissions are static, so only these ports can ever be reached.
            Assert.Equal(LocalApiService.PortRange, LocalApiService.CandidatePorts().ToArray());
        }
        finally
        {
            LocalApiService.Config = previous;
        }
    }
}
