using System;
using System.Linq;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Issue #19: a service hands out links with unescaped spaces in the path
/// (<c>https://example.com/my file.zip</c>), and every way of adding one split it into two downloads
/// (<c>https://example.com/my</c> and <c>file.zip</c>). Splitting on spaces is kept on purpose — a batch
/// pasted into a box that collapsed its new lines, or links mixed into a line of text, still split — so
/// the rule is narrower: a line that starts with an http(s) link and whose other parts are not links is
/// ONE link, with its spaces percent-encoded. And the GUI, the CLI and the local API must agree on it.
/// </summary>
public class LinkTextTests
{
    private const string Spaced = "https://example.com/my file.zip";
    private const string Escaped = "https://example.com/my%20file.zip";

    // ---- the splitting rule ---------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_link_with_spaces_in_its_path_stays_one_link_with_the_spaces_escaped()
    {
        Assert.Equal(new[] { Escaped }, LinkText.Split(Spaced));
        Assert.Equal(new[] { "https://example.com/a%20b%20%20c%20(1).zip" },
            LinkText.Split("  https://example.com/a b  c (1).zip  "));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Each_line_with_spaces_is_its_own_link()
    {
        Assert.Equal(new[] { Escaped, "https://example.com/other%20one.mp4", "https://example.com/plain.iso" },
            LinkText.Split(Spaced + "\r\nhttps://example.com/other one.mp4\n\nhttps://example.com/plain.iso\n"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_comma_or_semicolon_inside_a_single_link_is_kept_and_a_trailing_one_dropped()
    {
        Assert.Equal(new[] { "https://example.com/a,b%20c.zip" }, LinkText.Split("https://example.com/a,b c.zip;"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Several_links_on_one_line_still_split_on_spaces_commas_and_semicolons()
    {
        Assert.Equal(new[] { "https://h/a.zip", "https://h/b.zip", "https://h/c.zip", "https://h/d.zip" },
            LinkText.Split("https://h/a.zip https://h/b.zip, https://h/c.zip;https://h/d.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_line_of_text_that_mentions_links_still_yields_the_links()
    {
        var parts = LinkText.Split("Get it here: https://h/a.zip or https://h/b.zip");

        Assert.Equal(new[] { "https://h/a.zip", "https://h/b.zip" }, parts.Where(LinkText.IsHttpLink).ToArray());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_line_mixing_a_link_with_a_plugin_tag_keeps_splitting()
    {
        Assert.Equal(new[] { "https://ollama.com/library/gemma3", "llama3:8b" },
            LinkText.Split("https://ollama.com/library/gemma3 llama3:8b"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Blank_input_gives_no_links()
    {
        Assert.Empty(LinkText.Split(null));
        Assert.Empty(LinkText.Split(" \n\t ,; \r\n"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Escaping_touches_only_whitespace()
    {
        Assert.Equal("https://h/already%20escaped.zip?x=1&y=%2F",
            LinkText.EscapeWhitespace("https://h/already%20escaped.zip?x=1&y=%2F"));
        Assert.Equal("https://h/a%20b%09c.zip", LinkText.EscapeWhitespace(" https://h/a b\tc.zip "));
        Assert.Null(LinkText.EscapeWhitespace(null));
    }

    // ---- every add path agrees --------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_paste_box_counts_a_spaced_link_as_one()
    {
        Assert.Equal(1, AddDownloadItemViewModel.CountUrls(Spaced));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_api_json_add_stores_the_same_escaped_link_as_the_dialog()
    {
        var req = ApiAddRequest.FromJson($$"""{ "url": "{{Spaced}}", "mirrors": ["https://mirror.example/my file.zip"] }""");

        Assert.Null(req.Error);
        var item = LocalApiService.BuildItem(req, Config.New());

        Assert.Equal(new[] { Escaped, "https://mirror.example/my%20file.zip" }, item.Urls.ToArray());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_api_query_add_stores_the_same_escaped_link()
    {
        // A caller that encoded the space as '+' or %20 in the query hands us a decoded space.
        var req = ApiAddRequest.FromQuery(
            new Uri("http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fexample.com%2Fmy%20file.zip"));

        Assert.Null(req.Error);
        Assert.Equal(Escaped, LocalApiService.BuildItem(req, Config.New()).Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_cli_add_stores_the_same_escaped_link()
    {
        Assert.True(CliParser.TryParse(new[] { "add", "--url", Spaced }, out var cmd));
        Assert.Null(cmd.Error);

        // The CLI forwards its request as JSON and the running app builds it with BuildItem.
        var forwarded = ApiAddRequest.FromJson(cmd.Add.ToJson());
        Assert.Equal(Escaped, LocalApiService.BuildItem(forwarded, Config.New()).Url);
    }
}
