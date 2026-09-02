using System;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// `/api/add` carrying a stream/quality choice. The browser extension's HLS picker used to send the
/// chosen RENDITION's URL, and a rendition of a master whose audio lives in a separate #EXT-X-MEDIA
/// group is video-only — the download then had no sound (reported on x.com). It now hands over the
/// MASTER plus the id of the quality the user picked, which is what this field carries.
/// </summary>
public class ApiVariantChoiceTests
{
    private const string Master = "https://video.twimg.com/amplify_video/123/pl/master.m3u8";

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_json_body_carries_the_variant_choice()
    {
        var req = ApiAddRequest.FromJson($"{{\"url\":\"{Master}\",\"variantId\":\"4800000\"}}");

        Assert.Null(req.Error);
        Assert.Equal("4800000", req.VariantId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_query_form_carries_the_variant_choice_too()
    {
        // The extension keeps using the GET form when it has no cookies/headers to hand over, so the
        // quality has to survive that shape as well.
        var req = ApiAddRequest.FromQuery(
            new Uri($"http://127.0.0.1:15151/api/add?url={Uri.EscapeDataString(Master)}&variantId=2400000"));

        Assert.Null(req.Error);
        Assert.Equal("2400000", req.VariantId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_choice_reaches_the_download_item_so_the_resolver_sees_it()
    {
        var config = Config.New();
        var req = ApiAddRequest.FromJson($"{{\"url\":\"{Master}\",\"variantId\":\" 4800000 \"}}");

        var item = LocalApiService.BuildItem(req, config);

        Assert.Equal("4800000", item.VariantId);
        Assert.Equal(Master, item.Urls[0]);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_add_without_a_choice_leaves_the_item_on_the_resolvers_default()
    {
        var config = Config.New();
        var req = ApiAddRequest.FromJson($"{{\"url\":\"{Master}\"}}");

        var item = LocalApiService.BuildItem(req, config);

        Assert.Null(item.VariantId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_blank_choice_is_treated_as_no_choice()
    {
        var config = Config.New();
        var req = ApiAddRequest.FromJson($"{{\"url\":\"{Master}\",\"variantId\":\"   \"}}");

        Assert.Null(LocalApiService.BuildItem(req, config).VariantId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_forwarded_cli_add_keeps_the_choice()
    {
        // The forward path re-serializes the request; a quality is not a credential, so unlike cookies
        // and headers it travels.
        var original = ApiAddRequest.FromJson($"{{\"url\":\"{Master}\",\"variantId\":\"4800000\"}}");

        var round = ApiAddRequest.FromJson(original.ToJson());

        Assert.Equal("4800000", round.VariantId);
    }
}
