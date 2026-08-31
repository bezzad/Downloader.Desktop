using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// What the app does with a download the browser extension took over (issue #9).
///
/// The two behaviours under test both exist because such a link is NOT like a pasted one: the browser was
/// fetching it a second earlier, and the browser's own copy is still running when the app's attempt fails.
/// </summary>
public class BrowserHandoffTests
{
    private static HttpRequestException Http(HttpStatusCode status) =>
        new("response status code does not indicate success", null, status);

    // ---- The hand-off's links (task 2.2) ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Mirrors_become_fallback_links_after_the_primary_one()
    {
        var clicked = "https://www.softpedia.example/dyn-postdownload.php?p=999";
        var chainEnd = "https://cdn.softpedia.example/blob/6f2c1a";

        // The body the extension sends: the end of the browser's redirect chain leads (it is the address
        // that actually serves the file), the clicked link follows as the fallback. See handOffUrls in
        // common.js — this test is the app half of that contract.
        var req = ApiAddRequest.FromJson(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["url"] = chainEnd,
            ["mirrors"] = new[] { clicked }
        }));

        var item = LocalApiService.BuildItem(req, Config.New());

        // Order is the contract in both directions: the app leads its first attempt with Urls[0] and
        // falls back through the rest (DownloadManager.TryNextUrl), and re-resolves Urls[0] when a link
        // looks expired. Whatever the extension puts first is what the app tries first.
        Assert.Equal(new[] { chainEnd, clicked }, item.Urls.ToArray());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_blank_mirror_is_dropped_rather_than_stored()
    {
        var req = ApiAddRequest.FromJson(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["url"] = "https://e.com/a.zip",
            ["mirrors"] = new[] { "", "   " }
        }));

        Assert.Single(LocalApiService.BuildItem(req, Config.New()).Urls);
    }

    // ---- Marking the download as an extension hand-off (task 2.3) ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_add_can_say_the_browser_had_already_started_the_download()
    {
        var req = ApiAddRequest.FromJson("""{"url":"https://e.com/a.zip","fromBrowser":true}""");
        Assert.True(LocalApiService.BuildItem(req, Config.New()).FromBrowserDownload);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_ordinary_add_is_not_marked_as_a_browser_hand_off()
    {
        var req = ApiAddRequest.FromJson("""{"url":"https://e.com/a.zip"}""");
        Assert.False(LocalApiService.BuildItem(req, Config.New()).FromBrowserDownload);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_query_form_carries_the_flag_too()
    {
        var req = ApiAddRequest.FromQuery(new System.Uri(
            "http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fe.com%2Fa.zip&fromBrowser=1"));
        Assert.True(req.FromBrowser);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_flag_survives_a_restart_and_defaults_to_false_for_older_records()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dl-handoff-{System.Guid.NewGuid():N}.json");
        try
        {
            var config = Config.New();
            config.Downloads.Add(new DownloadItem
            {
                Urls = new List<string> { "https://e.com/a.zip" },
                FromBrowserDownload = true
            });
            config.Downloads.Add(new DownloadItem { Urls = new List<string> { "https://e.com/b.zip" } });

            File.WriteAllText(path, JsonSerializer.Serialize(config));
            var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));

            Assert.True(loaded.Downloads[0].FromBrowserDownload);
            Assert.False(loaded.Downloads[1].FromBrowserDownload);

            // A record written before the field existed reads as false, i.e. exactly today's behaviour.
            var old = JsonSerializer.Deserialize<DownloadItem>("""{"Urls":["https://e.com/c.zip"]}""");
            Assert.False(old.FromBrowserDownload);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- One automatic refresh from zero bytes (task 2.4) ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_browser_hand_off_is_worth_refreshing_before_any_bytes_arrive()
    {
        // The browser was fetching this link moments ago, so an immediate 403 means a spent single-use
        // address far more often than it means a bad link.
        Assert.True(DownloadManager.WorthRefreshingFromZeroBytes(
            new DownloadItem { FromBrowserDownload = true }));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_pasted_link_that_never_delivered_a_byte_is_still_just_a_bad_link()
    {
        Assert.False(DownloadManager.WorthRefreshingFromZeroBytes(new DownloadItem()));
        Assert.False(DownloadManager.WorthRefreshingFromZeroBytes(null));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_zero_byte_exception_only_covers_expired_link_failures()
    {
        // The flag relaxes the "must have bytes" rule; it does NOT make an unrelated failure retryable.
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(Http(HttpStatusCode.InternalServerError)));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(new IOException("disk full")));
    }
}
