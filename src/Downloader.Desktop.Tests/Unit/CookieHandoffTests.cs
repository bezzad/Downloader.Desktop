using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Cookie hand-off plumbing (fix-hls-youtube-resolver §2): the Netscape-format writer, the /api/add JSON
/// contract's optional `cookies` field, and the temp-file lifecycle. Cookies are secrets — these assert the
/// on-disk format, that values never round-trip back out of the request (no accidental echo/log), and that
/// the temp file is deleted after use.
/// </summary>
public class CookieHandoffTests
{
    private static List<CookieDto> Sample() => new()
    {
        new CookieDto { Name = "SID", Value = "abc123", Domain = ".youtube.com", Path = "/", Secure = true, Expires = 1893456000 },
        new CookieDto { Name = "PREF", Value = "hl=en", Domain = "youtube.com", Path = "/", Secure = false }, // session cookie
    };

    [Fact]
    public void ToNetscape_writes_the_expected_tab_separated_format()
    {
        var text = CookieFile.ToNetscape(Sample());
        var lines = text.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("# Netscape HTTP Cookie File", lines[0]);
        // .youtube.com → include-subdomains TRUE, secure TRUE, real expiry, value present.
        var sid = lines.First(l => l.Contains("SID"));
        Assert.Equal(".youtube.com\tTRUE\t/\tTRUE\t1893456000\tSID\tabc123", sid);
        // youtube.com (no leading dot) → include-subdomains FALSE; session cookie → expiry 0.
        var pref = lines.First(l => l.Contains("PREF"));
        Assert.Equal("youtube.com\tFALSE\t/\tFALSE\t0\tPREF\thl=en", pref);
    }

    [Fact]
    public void WriteTempFile_creates_a_readable_file_then_can_be_deleted()
    {
        var path = CookieFile.WriteTempFile(Sample());
        try
        {
            Assert.True(File.Exists(path));
            Assert.Contains("SID\tabc123", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ApiAddRequest_parses_cookies_from_json()
    {
        var json = """
        {"url":"https://youtu.be/x","cookies":[
          {"name":"SID","value":"abc123","domain":".youtube.com","path":"/","secure":true,"expires":1893456000},
          {"name":"bad_no_domain","value":"v"}
        ]}
        """;
        var req = ApiAddRequest.FromJson(json);

        Assert.Null(req.Error);
        Assert.Single(req.Cookies); // the domain-less cookie is dropped
        Assert.Equal("SID", req.Cookies[0].Name);
        Assert.Equal(".youtube.com", req.Cookies[0].Domain);
        Assert.True(req.Cookies[0].Secure);
        Assert.Equal(1893456000, req.Cookies[0].Expires);
    }

    [Fact]
    public void ApiAddRequest_ToJson_never_echoes_cookie_values()
    {
        var req = ApiAddRequest.FromJson("""{"url":"https://youtu.be/x","cookies":[{"name":"SID","value":"topsecret","domain":".youtube.com"}]}""");
        var round = req.ToJson();
        Assert.DoesNotContain("topsecret", round);
        Assert.DoesNotContain("cookies", round);
    }

    [Fact]
    public void BuildItem_writes_a_temp_cookie_file_when_cookies_are_supplied()
    {
        var config = Config.New();
        var req = ApiAddRequest.FromJson("""{"url":"https://youtu.be/x","cookies":[{"name":"SID","value":"v","domain":".youtube.com"}]}""");
        var item = LocalApiService.BuildItem(req, config);
        try
        {
            Assert.False(string.IsNullOrEmpty(item.CookieFilePath));
            Assert.True(File.Exists(item.CookieFilePath));
        }
        finally
        {
            if (item.CookieFilePath != null && File.Exists(item.CookieFilePath)) File.Delete(item.CookieFilePath);
        }
    }

    [Fact]
    public void BuildItem_leaves_cookie_path_null_when_none_supplied()
    {
        var item = LocalApiService.BuildItem(ApiAddRequest.FromJson("""{"url":"https://youtu.be/x"}"""), Config.New());
        Assert.Null(item.CookieFilePath);
    }

    [Fact]
    public void DeleteCookieFile_removes_the_file_and_clears_the_path()
    {
        var path = CookieFile.WriteTempFile(Sample());
        var item = new DownloadItem { CookieFilePath = path };

        DownloadManager.DeleteCookieFile(item);

        Assert.False(File.Exists(path));
        Assert.Null(item.CookieFilePath);
        // Idempotent / safe when already cleared.
        DownloadManager.DeleteCookieFile(item);
    }
}
