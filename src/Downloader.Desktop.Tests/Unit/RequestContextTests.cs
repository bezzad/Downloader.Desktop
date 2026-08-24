using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Per-download request context (issue #7): the `/api/add` contract's `headers`/`referer` fields, how they
/// land on the item, and how they're applied to the engine configuration that fetches the bytes. The
/// security property under test is the split: a referer persists, cookies and headers never do.
/// </summary>
public class RequestContextTests
{
    // ---------------- API contract ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FromJson_parses_headers_and_referer()
    {
        var req = ApiAddRequest.FromJson("""
        {"url":"https://cdn.example.com/v/index.m3u8",
         "referer":"https://site.example/watch/42",
         "headers":{"Origin":"https://site.example","X-Token":"abc"}}
        """);

        Assert.Null(req.Error);
        Assert.Equal("https://site.example/watch/42", req.Referer);
        Assert.Equal(2, req.Headers.Count);
        Assert.Equal("https://site.example", req.Headers["Origin"]);
        // Header names are case-insensitive, as they are on the wire.
        Assert.Equal("abc", req.Headers["x-token"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FromJson_drops_malformed_header_entries_without_failing_the_add()
    {
        var req = ApiAddRequest.FromJson("""
        {"url":"https://example.com/f.zip","headers":{"Good":"yes","Bad":42,"":"empty-name"}}
        """);

        Assert.Null(req.Error);
        Assert.Single(req.Headers);
        Assert.Equal("yes", req.Headers["Good"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FromJson_survives_headers_that_are_not_an_object()
    {
        var req = ApiAddRequest.FromJson("""{"url":"https://example.com/f.zip","headers":"nope"}""");

        Assert.Null(req.Error);
        Assert.Empty(req.Headers);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void BuildItem_puts_the_context_on_the_item()
    {
        var req = ApiAddRequest.FromJson("""
        {"url":"https://cdn.example.com/v/index.m3u8","referer":"https://site.example/watch/42",
         "headers":{"Origin":"https://site.example"},
         "cookies":[{"name":"SID","value":"v","domain":".site.example"}]}
        """);

        var item = LocalApiService.BuildItem(req, Config.New());
        try
        {
            Assert.Equal("https://site.example/watch/42", item.Referer);
            Assert.Equal("https://site.example/watch/42", item.Request.Referer);
            Assert.Equal("https://site.example", item.Request.Headers["Origin"]);
            Assert.Single(item.Request.Cookies);
            Assert.Equal("SID", item.Request.Cookies[0].Name);
        }
        finally
        {
            DownloadManager.DeleteCookieFile(item);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_item_without_a_context_is_empty_not_null()
    {
        var item = LocalApiService.BuildItem(ApiAddRequest.FromJson("""{"url":"https://example.com/f.zip"}"""), Config.New());

        Assert.NotNull(item.Request);
        Assert.True(item.Request.IsEmpty);
        Assert.Null(item.Referer);
    }

    // ---------------- Persistence: referer survives, secrets don't ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Saving_the_config_keeps_the_referer_and_drops_cookies_and_headers()
    {
        var item = new DownloadItem { Urls = { "https://cdn.example.com/v/index.m3u8" }, Referer = "https://site.example/watch/42" };
        item.Request.Headers["Authorization"] = "Bearer topsecret";
        item.Request.Cookies.Add(new CookieDto { Name = "SID", Value = "alsosecret", Domain = ".site.example" });

        var config = Config.New();
        config.Downloads = new List<DownloadItem> { item };
        var json = JsonSerializer.Serialize(config);

        Assert.DoesNotContain("topsecret", json);
        Assert.DoesNotContain("alsosecret", json);
        Assert.DoesNotContain("Authorization", json);

        var loaded = JsonSerializer.Deserialize<Config>(json).Downloads[0];
        Assert.Equal("https://site.example/watch/42", loaded.Referer);
        Assert.Equal("https://site.example/watch/42", loaded.Request.Referer);
        Assert.Empty(loaded.Request.Headers);
        Assert.Empty(loaded.Request.Cookies);
    }

    // ---------------- Applying it to the engine ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ApplyRequestContext_sets_cookies_headers_and_referer()
    {
        var cfg = new DownloadConfiguration();
        var ctx = new RequestContext { Referer = "https://site.example/watch/42" };
        ctx.Headers["Origin"] = "https://site.example";
        ctx.Cookies.Add(new CookieDto { Name = "SID", Value = "v", Domain = ".site.example", Path = "/", Secure = true });

        DownloadManager.ApplyRequestContext(cfg, ctx);

        var req = cfg.RequestConfiguration;
        Assert.Equal("https://site.example/watch/42", req.Referer);
        Assert.Equal("https://site.example", req.Headers["Origin"]);
        var jar = req.CookieContainer.GetCookies(new System.Uri("https://www.site.example/"));
        Assert.Equal("SID", jar.Cast<Cookie>().Single().Name);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ApplyRequestContext_routes_the_property_backed_headers()
    {
        var cfg = new DownloadConfiguration();
        var ctx = new RequestContext();
        ctx.Headers["User-Agent"] = "Downloader/1.0";
        ctx.Headers["Accept"] = "video/*";
        ctx.Headers["Content-Type"] = "application/octet-stream";
        ctx.Headers["Referer"] = "https://site.example/watch/42";

        DownloadManager.ApplyRequestContext(cfg, ctx);

        var req = cfg.RequestConfiguration;
        Assert.Equal("Downloader/1.0", req.UserAgent);
        Assert.Equal("video/*", req.Accept);
        Assert.Equal("application/octet-stream", req.ContentType);
        Assert.Equal("https://site.example/watch/42", req.Referer);
        // They must NOT also sit in the raw header collection (the engine would send them twice/badly).
        Assert.Null(req.Headers["User-Agent"]);
        Assert.Null(req.Headers["Accept"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_per_item_value_beats_the_global_setting()
    {
        var settings = new DownloadSettings { Referer = "https://global.example", UserAgent = "GlobalAgent" };
        var cfg = settings.ToConfiguration();
        Assert.Equal("https://global.example", cfg.RequestConfiguration.Referer);

        var ctx = new RequestContext { Referer = "https://site.example/watch/42" };
        ctx.Headers["User-Agent"] = "ItemAgent";
        DownloadManager.ApplyRequestContext(cfg, ctx);

        Assert.Equal("https://site.example/watch/42", cfg.RequestConfiguration.Referer);
        Assert.Equal("ItemAgent", cfg.RequestConfiguration.UserAgent);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_context_changes_nothing()
    {
        var settings = new DownloadSettings { Referer = "https://global.example" };
        var cfg = settings.ToConfiguration();

        DownloadManager.ApplyRequestContext(cfg, new RequestContext());
        DownloadManager.ApplyRequestContext(cfg, null);

        Assert.Equal("https://global.example", cfg.RequestConfiguration.Referer);
        Assert.Equal(0, cfg.RequestConfiguration.CookieContainer?.Count ?? 0);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unusable_cookie_is_skipped_not_fatal()
    {
        var cfg = new DownloadConfiguration();
        var ctx = new RequestContext();
        ctx.Cookies.Add(null);
        ctx.Cookies.Add(new CookieDto { Name = "", Value = "v", Domain = ".site.example" });
        ctx.Cookies.Add(new CookieDto { Name = "NoDomain", Value = "v", Domain = "" });
        ctx.Cookies.Add(new CookieDto { Name = "SID", Value = "v", Domain = ".site.example" });

        DownloadManager.ApplyRequestContext(cfg, ctx);

        var jar = cfg.RequestConfiguration.CookieContainer.GetCookies(new System.Uri("https://www.site.example/"));
        Assert.Equal("SID", jar.Cast<Cookie>().Single().Name);
    }

    // ---------------- Merging with a resolver's per-part headers ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_parts_own_header_wins_over_the_items()
    {
        var cfg = new DownloadConfiguration();
        var ctx = new RequestContext();
        ctx.Headers["Origin"] = "https://site.example";
        ctx.Headers["X-Token"] = "item";

        DownloadManager.ApplyRequestContext(cfg, ctx);
        DownloadManager.ApplyHeaders(cfg, new Dictionary<string, string> { ["X-Token"] = "part" });

        Assert.Equal("https://site.example", cfg.RequestConfiguration.Headers["Origin"]); // untouched
        Assert.Equal("part", cfg.RequestConfiguration.Headers["X-Token"]);                // replaced, not appended
    }

    // ---------------- What a resolver sees ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ResolveHeaders_folds_the_referer_in_and_returns_null_when_empty()
    {
        Assert.Null(DownloadManager.ResolveHeaders(null));
        Assert.Null(DownloadManager.ResolveHeaders(new RequestContext()));

        var ctx = new RequestContext { Referer = "https://site.example/watch/42" };
        ctx.Headers["Origin"] = "https://site.example";
        var headers = DownloadManager.ResolveHeaders(ctx);

        Assert.Equal("https://site.example/watch/42", headers["Referer"]);
        Assert.Equal("https://site.example", headers["Origin"]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_referer_field_wins_over_a_referer_header_everywhere()
    {
        var ctx = new RequestContext { Referer = "https://from-field.example" };
        ctx.Headers["Referer"] = "https://from-header.example";

        // Same precedence on both sides of the boundary: what a resolver sees, and what the engine sends.
        Assert.Equal("https://from-field.example", DownloadManager.ResolveHeaders(ctx)["Referer"]);

        var cfg = new DownloadConfiguration();
        DownloadManager.ApplyRequestContext(cfg, ctx);
        Assert.Equal("https://from-field.example", cfg.RequestConfiguration.Referer);
    }
}
