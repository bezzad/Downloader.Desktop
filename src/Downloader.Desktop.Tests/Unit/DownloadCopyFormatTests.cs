using System.Collections.Generic;
using System.Text.Json;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>The row menu's Copy formats: link, JSON and cURL — and that none of them leak secrets.</summary>
public class DownloadCopyFormatTests
{
    private static DownloadItem Item(string url, string name = "file.zip", string referer = null)
    {
        var item = new DownloadItem
        {
            Urls = new List<string> { url, "https://mirror.example/file.zip" },
            FileName = name,
            SaveFolder = "/home/me/Downloads",
            Size = 1234,
        };
        item.Referer = referer;
        item.Request.Cookies.Add(new CookieDto { Name = "session", Value = "SECRET" });
        item.Request.Headers["Authorization"] = "Bearer SECRET";
        item.CookieFilePath = "/tmp/SECRET-cookies.txt";
        item.PlanJson = "{\"plan\":\"SECRET\"}";
        return item;
    }

    [Fact]
    public void Links_are_one_per_line()
    {
        var text = DownloadCopyFormat.Links(new[] { Item("https://a/1"), Item("https://a/2"), Item("https://a/3") });
        Assert.Equal("https://a/1\nhttps://a/2\nhttps://a/3", text);
    }

    [Fact]
    public void Curl_with_a_referer_saves_to_the_file_name()
    {
        var text = DownloadCopyFormat.Curl(new[] { Item("https://a/f.zip", referer: "https://page/") });
        Assert.Equal("curl -L -o 'file.zip' -e 'https://page/' 'https://a/f.zip'", text);
    }

    [Fact]
    public void Curl_without_a_referer_has_no_referer_flag()
    {
        var text = DownloadCopyFormat.Curl(new[] { Item("https://a/f.zip") });
        Assert.Equal("curl -L -o 'file.zip' 'https://a/f.zip'", text);
    }

    [Theory]
    [InlineData("it's.zip", "'it'\\''s.zip'")]
    [InlineData("my file.zip", "'my file.zip'")]
    [InlineData("فیلم.mp4", "'فیلم.mp4'")]
    public void Posix_quoting_survives_quotes_spaces_and_unicode(string value, string quoted) =>
        Assert.Equal(quoted, DownloadCopyFormat.Quote(value));

    [Fact]
    public void Curl_gives_one_line_per_download()
    {
        var text = DownloadCopyFormat.Curl(new[] { Item("https://a/1"), Item("https://a/2") });
        Assert.Equal(2, text.Split('\n').Length);
    }

    [Fact]
    public void Json_has_url_mirrors_name_folder_and_size()
    {
        var text = DownloadCopyFormat.Json(new[] { Item("https://a/f.zip") });
        var row = JsonDocument.Parse(text).RootElement[0];

        Assert.Equal("https://a/f.zip", row.GetProperty("url").GetString());
        Assert.Equal("https://mirror.example/file.zip", row.GetProperty("mirrors")[0].GetString());
        Assert.Equal("file.zip", row.GetProperty("fileName").GetString());
        Assert.Equal("/home/me/Downloads", row.GetProperty("folder").GetString());
        Assert.Equal(1234, row.GetProperty("size").GetInt64());
    }

    [Fact]
    public void Secrets_are_never_copied()
    {
        var items = new[] { Item("https://a/f.zip", referer: "https://page/") };
        Assert.DoesNotContain("SECRET", DownloadCopyFormat.Json(items));
        Assert.DoesNotContain("SECRET", DownloadCopyFormat.Curl(items));
    }
}
