using System;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// What a programmatic add may say about a download's type: the media type the caller observed, and
/// the category it wants the download filed under. Both optional — an add that mentions neither must
/// behave exactly as it did before they existed.
/// </summary>
public class ApiCategoryTests
{
    private static Config Config()
    {
        var config = Models.Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        return config;
    }

    // ---- the media type ------------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_json_add_can_carry_the_media_type_it_observed()
    {
        var req = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/stream", "mime": "video/mp4" }
        """);

        Assert.Null(req.Error);
        Assert.Equal("video/mp4", req.Mime);

        var item = LocalApiService.BuildItem(req, Config());

        Assert.Equal("video/mp4", item.ContentType);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_query_add_can_carry_it_too()
    {
        var req = ApiAddRequest.FromQuery(
            new Uri("http://127.0.0.1:15151/api/add?url=https%3A%2F%2F10.255.255.1%2Fstream&mime=audio%2Fmpeg"));

        Assert.Null(req.Error);
        Assert.Equal("audio/mpeg", req.Mime);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_add_that_mentions_neither_behaves_exactly_as_before()
    {
        var req = ApiAddRequest.FromJson("""{ "url": "https://10.255.255.1/a.mp4" }""");

        Assert.Null(req.Error);
        Assert.Null(req.Mime);
        Assert.Null(req.Category);

        var item = LocalApiService.BuildItem(req, Config());

        Assert.Null(item.ContentType);
        Assert.Null(item.CategoryId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_media_type_files_a_name_less_link_under_a_type()
    {
        var config = Config();
        var req = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/d/8f3a2b", "mime": "video/mp4" }
        """);

        var item = LocalApiService.BuildItem(req, config);
        var service = new CategoryService();
        service.Initialize(config);

        // No extension anywhere in the name — without the media type this would be Other.
        Assert.Equal("video", service.Resolve(item).Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_extension_still_beats_the_media_type()
    {
        var config = Config();
        var req = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/song.mp3", "filename": "song.mp3", "mime": "application/octet-stream" }
        """);

        var item = LocalApiService.BuildItem(req, config);
        var service = new CategoryService();
        service.Initialize(config);

        Assert.Equal("audio", service.Resolve(item).Id);
    }

    // ---- the category --------------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_caller_can_name_the_category_by_id_or_by_name()
    {
        var config = Config();

        Assert.Equal("image", LocalApiService.ResolveCategoryId("image", config));
        Assert.Equal("image", LocalApiService.ResolveCategoryId("Cat_Image", config));
        Assert.Equal("image", LocalApiService.ResolveCategoryId("  IMAGE  ", config));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_category_the_caller_names_is_applied_as_an_explicit_choice()
    {
        var config = Config();
        var req = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/clip.mp4", "filename": "clip.mp4", "category": "image" }
        """);

        var item = LocalApiService.BuildItem(req, config);
        var service = new CategoryService();
        service.Initialize(config);

        // An explicit choice, so it beats what the .mp4 extension would say.
        Assert.Equal("image", item.CategoryId);
        Assert.Equal("image", service.Resolve(item).Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_category_that_does_not_exist_is_ignored_rather_than_refused()
    {
        var config = Config();
        var req = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/clip.mp4", "filename": "clip.mp4", "category": "cat_nope" }
        """);

        // The download is still what the caller asked for — a stale category list must not cost them
        // the download; detection files it where the app would have anyway.
        Assert.Null(req.Error);
        var item = LocalApiService.BuildItem(req, config);

        Assert.Null(item.CategoryId);
        var service = new CategoryService();
        service.Initialize(config);
        Assert.Equal("video", service.Resolve(item).Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_forwarded_cli_add_carries_both_and_never_a_secret()
    {
        var req = ApiAddRequest.FromJson("""
        {
          "url": "https://10.255.255.1/x", "mime": "video/mp4", "category": "video",
          "referer": "https://example.test/page",
          "cookies": [ { "name": "sid", "value": "s3cr3t", "domain": "example.test" } ],
          "headers": { "X-Token": "s3cr3t" }
        }
        """);

        var json = req.ToJson();

        Assert.Contains("\"mime\":\"video/mp4\"", json);
        Assert.Contains("\"category\":\"video\"", json);
        // The referer travels (it is not a credential); cookies and headers never do.
        Assert.Contains("example.test/page", json);
        Assert.DoesNotContain("s3cr3t", json);
    }
}
