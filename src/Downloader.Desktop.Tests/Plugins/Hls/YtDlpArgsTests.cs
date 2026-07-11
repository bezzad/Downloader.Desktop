using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// yt-dlp argument construction for the cookie hand-off (fix-hls-youtube-resolver §3). The runtime
/// short-circuit ordering (a working supplied cookie skips the browser loop; an expired one falls through)
/// is exercised by the gated live test, since it shells out to the real binary — here we lock the argument
/// shape: a supplied cookie FILE uses `--cookies` and is never combined with `--cookies-from-browser`.
/// </summary>
public class YtDlpArgsTests
{
    [Fact]
    public void Supplied_cookie_file_uses_cookies_flag_not_browser_store()
    {
        var args = YtDlpBinary.BuildArgs("https://youtu.be/x", cookieFile: "/tmp/c.txt", cookieBrowser: null, denoPath: null);
        Assert.Contains("--cookies \"/tmp/c.txt\"", args);
        Assert.DoesNotContain("--cookies-from-browser", args);
    }

    [Fact]
    public void Browser_store_path_uses_cookies_from_browser()
    {
        var args = YtDlpBinary.BuildArgs("https://youtu.be/x", cookieFile: null, cookieBrowser: "chrome", denoPath: null);
        Assert.Contains("--cookies-from-browser chrome", args);
        Assert.DoesNotContain("--cookies \"", args);
    }

    [Fact]
    public void Anonymous_attempt_has_neither_cookie_source()
    {
        var args = YtDlpBinary.BuildArgs("https://youtu.be/x", cookieFile: null, cookieBrowser: null, denoPath: null);
        Assert.DoesNotContain("--cookies", args);
        Assert.Contains("-J", args);
        Assert.Contains("\"https://youtu.be/x\"", args);
    }

    [Fact]
    public void Missing_formats_is_detected_from_ytdlp_stderr()
    {
        // The signature of an unsolved YouTube "n challenge" (no JS runtime): cookies pass the sign-in
        // wall but only storyboards exist, so -J's default selection fails with this exact error.
        Assert.True(YtDlpBinary.MissingFormats(
            "ERROR: [youtube] abc: Requested format is not available. Use --list-formats ..."));
        Assert.False(YtDlpBinary.MissingFormats("ERROR: [youtube] abc: Sign in to confirm you're not a bot."));
        Assert.False(YtDlpBinary.MissingFormats(""));
    }
}
