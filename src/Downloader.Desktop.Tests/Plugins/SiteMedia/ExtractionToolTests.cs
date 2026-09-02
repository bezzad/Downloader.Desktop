using System.Net;
using System.Security.Cryptography;
using System.Text;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.SiteMedia;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.SiteMedia;

/// <summary>
/// How the extraction tool is obtained and, above all, what happens when what arrives is not what the
/// publisher published. The plugin fetches a third-party binary at runtime, so "verified before it is ever
/// executed" is the security property this file guards — a mismatch must leave nothing on disk that a
/// later run could mistake for an installed tool.
/// </summary>
public class ExtractionToolTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    // yt-dlp's SHA2-256SUMS lists every asset; the right line must be picked.
    [InlineData("aaaa  yt-dlp.exe\nbbbb  yt-dlp_linux\n", "yt-dlp_linux", "bbbb")]
    [InlineData("bbbb  ./yt-dlp_linux\n", "yt-dlp_linux", "bbbb")]
    [InlineData("bbbb *yt-dlp_linux\n", "yt-dlp_linux", "bbbb")]
    // Not listed ⇒ no digest, which the caller treats as "refuse to run it".
    [InlineData("aaaa  yt-dlp.exe\n", "yt-dlp_linux", null)]
    [InlineData("", "yt-dlp_linux", null)]
    [InlineData("not a sums file at all", "yt-dlp_linux", null)]
    public void The_published_digest_for_an_asset_is_found_or_honestly_missing(
        string sums, string asset, string? expectedTail)
    {
        // The theory's short markers stand in for real digests; expand them to the 64 hex chars a real
        // sums file carries, since anything else is (correctly) ignored as not-a-digest.
        var text = sums.Replace("aaaa", new string('a', 64)).Replace("bbbb", new string('b', 64));

        var found = ToolChecksum.ParseSums(text, asset);

        Assert.Equal(expectedTail is null ? null : new string(expectedTail[0], 64), found);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_single_entry_sums_file_is_accepted_whatever_it_names()
    {
        // Deno publishes one <asset>.sha256sum per asset, fetched by that asset's own URL, and its name
        // column has carried a path prefix in the past. Requiring an exact name match would reject it.
        var text = $"{new string('c', 64)}  deno-x86_64-unknown-linux-gnu.zip\n";

        Assert.Equal(new string('c', 64), ToolChecksum.ParseSums(text, "deno-aarch64-apple-darwin.zip", allowSingleEntry: true));
        // ...but a listing that could name several assets never guesses.
        Assert.Null(ToolChecksum.ParseSums(text, "deno-aarch64-apple-darwin.zip"));
    }

    /// <summary>
    /// Deno publishes its WINDOWS digests as PowerShell <c>Get-FileHash</c> output, not the coreutils
    /// shape every other asset uses. Reading only coreutils meant every Windows install of this plugin
    /// threw away a perfectly good Deno archive and told the user the published checksum could not be
    /// read (issue #11) — with no way for them to fix it. This is the publisher's file verbatim.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_windows_digest_deno_actually_publishes_is_read()
    {
        var text = "\r\nAlgorithm : SHA256\r\n"
                   + "Hash      : 15E5300B0BA3C3695A7621D90160A746EC9E710228CEE639AFA9D580F6E3CD11\r\n"
                   + "Path      : C:\\a\\deno\\deno\\target\\release\\deno-x86_64-pc-windows-msvc.zip\r\n";

        // Matched BY NAME out of the build machine's path, and lowercased so it compares to a computed sum.
        Assert.Equal("15e5300b0ba3c3695a7621d90160a746ec9e710228cee639afa9d580f6e3cd11",
            ToolChecksum.ParseSums(text, "deno-x86_64-pc-windows-msvc.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_get_file_hash_block_for_a_different_asset_is_not_accepted_as_this_one()
    {
        // The new shape must not become a way to accept the wrong asset's digest: matching stays by name.
        var text = "Algorithm : SHA256\r\n"
                   + "Hash      : " + new string('D', 64) + "\r\n"
                   + "Path      : C:\\build\\deno-aarch64-pc-windows-msvc.zip\r\n";

        Assert.Null(ToolChecksum.ParseSums(text, "deno-x86_64-pc-windows-msvc.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_get_file_hash_block_with_no_usable_digest_is_refused()
    {
        // Truncated/garbled output must read as "no digest", never as a pass.
        var text = "Algorithm : SHA256\r\nHash      : nothex\r\nPath      : C:\\x\\deno.zip\r\n";

        Assert.Null(ToolChecksum.ParseSums(text, "deno.zip", allowSingleEntry: true));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_file_that_matches_its_digest_is_kept()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "tool");
        await File.WriteAllTextAsync(path, "the real tool", TestContext.Current.CancellationToken);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("the real tool"))).ToLowerInvariant();

        await ToolChecksum.VerifyOrDiscardAsync(path, digest, "tool", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_file_that_does_not_match_is_deleted_and_never_becomes_runnable()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "tool");
        await File.WriteAllTextAsync(path, "something else entirely", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolChecksum.VerifyOrDiscardAsync(path, new string('d', 64), "yt-dlp",
                TestContext.Current.CancellationToken));

        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never run", ex.Message);
        // Deleting is the point: a rejected download left on disk would pass a later "is it installed?"
        // check and get executed by the next run.
        Assert.False(File.Exists(path));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_downloaded_tool_whose_checksum_does_not_match_is_discarded_before_it_is_run()
    {
        var dir = TempDir();
        // Serve a "binary" plus a sums file that names a different digest — i.e. a tampered or corrupted
        // download. Nothing may survive, and nothing may be executed.
        using var http = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsoluteUri.EndsWith("SHA2-256SUMS", StringComparison.Ordinal)
                ? $"{new string('e', 64)}  {YtDlpAssetNameForThisOs()}\n"
                : "MZ this is not the published binary"));

        var tool = new YtDlpBinary(dir, http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.EnsureYtDlpAsync(TestContext.Current.CancellationToken));

        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(dir, "yt-dlp-bin")));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_download_with_no_published_checksum_is_refused_rather_than_trusted()
    {
        var dir = TempDir();
        using var http = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsoluteUri.EndsWith("SHA2-256SUMS", StringComparison.Ordinal)
                ? null // the publisher's sums file is unreachable
                : "MZ a plausible binary"));

        var tool = new YtDlpBinary(dir, http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.EnsureYtDlpAsync(TestContext.Current.CancellationToken));

        Assert.Contains("unverified", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(dir, "yt-dlp-bin")));
    }

    // ── Never read the user's browser (issue #4) ─────────────────────────────────────────────────────

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("/tmp/session-cookies.txt")]
    public void The_tool_is_only_ever_given_a_cookie_FILE_never_a_browser(string? cookieFile)
    {
        var args = YtDlpBinary.BuildArgs("https://www.youtube.com/watch?v=abc", cookieFile, denoPath: null);

        Assert.DoesNotContain("cookies-from-browser", args);
        if (cookieFile is null)
            Assert.DoesNotContain("--cookies", args);
        else
            Assert.Contains($"--cookies \"{cookieFile}\"", args);
        Assert.Contains("-J", args); // metadata only; the tool never downloads
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_cookie_file_from_the_extension_reaches_the_extraction()
    {
        var yt = new SiteMediaResolverTests.StubYtDlp("""
        { "title": "Signed in", "formats": [ { "format_id": "p", "url": "https://cdn/v.mp4", "ext": "mp4", "protocol": "https", "vcodec": "h264", "acodec": "aac", "height": 720 } ] }
        """);
        var resolver = SiteMediaResolverTests.NewResolver(yt);

        await resolver.ResolveAsync("https://www.youtube.com/watch?v=abc",
            new ResolveOptions { CookieFilePath = "/tmp/session.txt" }, TestContext.Current.CancellationToken);

        Assert.Equal("/tmp/session.txt", yt.LastCookieFile);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_in_the_plugin_reads_a_browser_profile_or_cookie_store()
    {
        // NoShellSpawnTests scans src/Downloader.Desktop.Plugins recursively, so this plugin is already
        // covered — assert that it really is, rather than trusting the glob to keep including it.
        var pluginDir = FindPluginDir();
        var sources = Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(sources);

        string[] forbidden =
        {
            "cookies-from-browser", "Cookies.sqlite", "Login Data", "Local State",
            "Chrome/User Data", "Mozilla/Firefox/Profiles",
        };
        foreach (var file in sources)
        {
            var text = Downloader.Desktop.Tests.Unit.NoShellSpawnTests.StripComments(File.ReadAllText(file));
            foreach (var needle in forbidden)
                Assert.DoesNotContain(needle, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindPluginDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Downloader.Desktop.Plugins",
                "Downloader.Desktop.Plugins.SiteMedia");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("the SiteMedia plugin source folder was not found");
    }

    private static string YtDlpAssetNameForThisOs() =>
        OperatingSystem.IsWindows() ? "yt-dlp.exe"
        : OperatingSystem.IsMacOS() ? "yt-dlp_macos"
        : "yt-dlp_linux";

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-sitemedia-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Answers each request with the body the mapper returns, or 404 when it returns null.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, string?> map) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = map(request);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
