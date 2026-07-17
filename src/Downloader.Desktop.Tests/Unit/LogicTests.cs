using System;
using System.Text.Json;
using Downloader;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>Pure-logic unit tests (no Avalonia runtime needed).</summary>
public class LogicTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void CountUrls_counts_links_and_ignores_blanks()
    {
        Assert.Equal(0, AddDownloadItemViewModel.CountUrls(""));
        Assert.Equal(0, AddDownloadItemViewModel.CountUrls(null));
        Assert.Equal(1, AddDownloadItemViewModel.CountUrls("https://h/a.zip"));
        Assert.Equal(3, AddDownloadItemViewModel.CountUrls("https://h/a\n\nhttps://h/b\n  \nhttps://h/c\n"));

        // A huge paste is above the bulk threshold → the paste handlers route it to bulk mode.
        var big = string.Join("\n", System.Linq.Enumerable.Range(0, 2000).Select(i => $"https://h/f{i}.mp4"));
        Assert.True(AddDownloadItemViewModel.CountUrls(big) > AddDownloadItemViewModel.BulkPreviewThreshold);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("v1.1.0", true)]   // newer minor
    [InlineData("v2.0.0", true)]   // newer major
    [InlineData("v1.0.1", true)]   // newer patch
    [InlineData("v1.0.0", false)]  // same
    [InlineData("v0.9.9", false)]  // older
    [InlineData("1.1", true)]      // no 'v', missing patch
    [InlineData("garbage", false)] // unparseable
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UpdateService_IsNewer_compares_against_1_0_0(string tag, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(tag, new Version(1, 0, 0)));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void UpdateService_reports_a_real_patch_version_and_ignores_its_own_release()
    {
        // Regression (#update-false-alarm): AssemblyVersion used to be pinned to major.minor.0.0, so the
        // app always reported x.y.0 and treated every patch release (e.g. v1.1.1) as "newer" forever.
        // CurrentVersion must carry the real patch, and a release equal to it must NOT be an update.
        var current = UpdateService.CurrentVersion;
        Assert.False(UpdateService.IsNewer($"v{current.Major}.{current.Minor}.{current.Build}", current),
            $"App version {current} should not see its own release as an update.");
        Assert.True(UpdateService.IsNewer($"v{current.Major}.{current.Minor}.{current.Build + 1}", current),
            "The next patch up should still register as an update.");
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void UpdateService_ExpectedAssetName_matches_release_naming()
    {
        var name = UpdateService.ExpectedAssetName();
        Assert.StartsWith("Downloader-", name);
        Assert.True(name.EndsWith(".zip") || name.EndsWith(".tar.gz"), name);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("movie.mp4", "video")]
    [InlineData("clip.MKV", "video")]
    [InlineData("song.mp3", "audio")]
    [InlineData("photo.jpeg", "image")]
    [InlineData("archive.zip", "archive")]
    [InlineData("report.pdf", "document")]
    [InlineData("setup.exe", "app")]
    [InlineData("ubuntu.iso", "disc")]
    [InlineData("noextension", "file")]
    [InlineData("", "file")]
    public void GetFileKind_classifies_by_extension(string name, string expected)
    {
        Assert.Equal(expected, DownloadItemViewModel.GetFileKind(name));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ToConfiguration_maps_core_options()
    {
        var settings = new DownloadSettings
        {
            ChunkCount = 8,
            ParallelDownload = true,
            MaximumBytesPerSecond = 0, // unlimited
            FileExistPolicy = FileExistPolicy.IgnoreDownload
        };

        var cfg = settings.ToConfiguration();

        Assert.Equal(8, cfg.ChunkCount);
        Assert.True(cfg.ParallelDownload);
        Assert.Equal(FileExistPolicy.IgnoreDownload, cfg.FileExistPolicy);
        // The engine treats <= 0 as unlimited (long.MaxValue).
        Assert.Equal(long.MaxValue, cfg.MaximumBytesPerSecond);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ToConfiguration_clamps_out_of_range_values()
    {
        var settings = new DownloadSettings { ChunkCount = 0, BlockTimeout = 1, HttpClientTimeout = 1 };
        var cfg = settings.ToConfiguration();

        Assert.True(cfg.ChunkCount >= 1);
        Assert.True(cfg.BlockTimeout >= 100);
        Assert.True(cfg.HttpClientTimeout >= 1000);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void DefaultFileExistPolicy_is_IgnoreDownload()
    {
        Assert.Equal(FileExistPolicy.IgnoreDownload, new DownloadSettings().FileExistPolicy);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Config_New_has_settings_and_default_queue()
    {
        var cfg = Config.New();
        Assert.NotNull(cfg.Settings);
        Assert.Single(cfg.Queues);
        Assert.Equal(DownloadQueue.DefaultName, cfg.DefaultQueue.Name);
        Assert.NotNull(cfg.Downloads);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Config_round_trips_through_json()
    {
        var cfg = Config.New();
        cfg.Downloads.Add(new DownloadItem { Url = "https://host/file.zip", SaveFolder = "/tmp", FileName = "file.zip" });

        var json = JsonSerializer.Serialize(cfg);
        var back = JsonSerializer.Deserialize<Config>(json).EnsureValid();

        Assert.NotNull(back.Settings);
        Assert.Single(back.Queues);
        Assert.Single(back.Downloads);
        Assert.Equal("file.zip", back.Downloads[0].FileName);
    }

    // NOTE: Chunk_status_text_tracks_progress moved to AppTests as an [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)] — it reads
    // Localizer strings, which are only loaded under the Avalonia headless runtime (AssetLoader). As a
    // plain [Fact(Timeout = TestTimeouts.DefaultMs)] it was order-dependent and flaky on CI (got the raw "State_Pending" key).

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("http://127.0.0.1:15151/add?url=https%3A%2F%2Fhost%2Ffile.zip", "https://host/file.zip")]
    [InlineData("http://127.0.0.1:15151/add?foo=1&url=https://host/a.bin", "https://host/a.bin")]
    [InlineData("http://127.0.0.1:15151/add?url=", null)]
    [InlineData("http://127.0.0.1:15151/add", null)]
    public void BrowserIntegration_extracts_url_param(string requestUri, string expected)
    {
        Assert.Equal(expected, LocalApiService.ExtractUrl(new System.Uri(requestUri)));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(45, "45s")]
    [InlineData(83, "1m 23s")]
    [InlineData(3900, "1h 5m")]
    [InlineData(-1, "—")]
    public void FormatDuration_is_compact(double seconds, string expected)
    {
        Assert.Equal(expected, DownloadItemViewModel.FormatDuration(seconds));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FormatDuration_handles_non_finite()
    {
        Assert.Equal("—", DownloadItemViewModel.FormatDuration(double.PositiveInfinity));
        Assert.Equal("—", DownloadItemViewModel.FormatDuration(double.NaN));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void LooksAlreadyDownloaded_only_for_ignore_policy_and_existing_file()
    {
        var tmp = System.IO.Path.GetTempFileName(); // a real, existing file
        try
        {
            Assert.True(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, tmp));
            // Other policies don't "ignore", so a cancel there is a real failure, not an existing file.
            Assert.False(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.Delete, tmp));
            // IgnoreDownload but the file isn't there → a genuine interruption, not "already exists".
            Assert.False(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, tmp + ".missing"));
            Assert.False(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, null));
        }
        finally
        {
            System.IO.File.Delete(tmp);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void SingleInstance_FirstUrl_picks_the_first_http_arg()
    {
        Assert.Equal("https://host/a.zip",
            SingleInstanceService.FirstUrl(new[] { "--minimized", "https://host/a.zip", "http://x/y" }));
        Assert.Equal("http://h/f", SingleInstanceService.FirstUrl(new[] { "http://h/f" }));
        Assert.Null(SingleInstanceService.FirstUrl(new[] { "--minimized", "/some/file" }));
        Assert.Null(SingleInstanceService.FirstUrl(System.Array.Empty<string>()));
        Assert.Null(SingleInstanceService.FirstUrl(null));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void About_links_are_wellformed()
    {
        Assert.StartsWith("https://github.com/bezzad", AboutViewModel.RepoUrl);
        Assert.Contains("Donate.md", AboutViewModel.DonateUrl);
        Assert.Equal("https://t.me/bezzad", AboutViewModel.TelegramUrl);
        Assert.Contains("@", AboutViewModel.Email);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Donate_links_include_github_sponsors()
    {
        Assert.Equal("https://github.com/sponsors/bezzad", DonateViewModel.GitHubSponsorsUrl);
        Assert.StartsWith("https://liberapay.com/bezzad", DonateViewModel.LiberapayUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Localizer_lists_all_shipped_languages()
    {
        // The 9 original packs + the 7 added in Round 15.
        Assert.Equal(16, Localizer.Languages.Count);
        foreach (var code in new[] { "it", "pt", "ru", "hi", "zh", "ja", "ko" })
            Assert.Contains(Localizer.Languages, l => l.Code == code);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void DownloadItem_FilePath_combines_folder_and_name()
    {
        var item = new DownloadItem { SaveFolder = "/tmp/dl", FileName = "a.bin" };
        Assert.Equal("/tmp/dl", item.FolderPath);
        Assert.Contains("a.bin", item.FilePath);

        var noName = new DownloadItem { SaveFolder = "/tmp/dl" };
        Assert.Equal("/tmp/dl", noName.FilePath);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(401_000_000L, 2_000_000L, true)]    // resumed 382MB file "completed" at 2MB -> corrupted
    [InlineData(401_000_000L, 400_000_000L, true)]  // resumed but finished smaller than known -> corrupted
    [InlineData(100L, 60L, true)]                   // resumed, finished smaller than known -> corrupted
    [InlineData(401_000_000L, 401_000_000L, false)] // genuine full completion at the known size
    [InlineData(0L, 1_000L, false)]                 // fresh download (no known size) -> never flagged
    [InlineData(401_000_000L, 0L, false)]           // nothing received -> not flagged
    public void Corrupted_after_resume_heuristic(long known, long final, bool expected)
    {
        long? knownSize = known == 0 ? null : known;
        Assert.Equal(expected, DownloadManager.LooksCorruptedAfterResume(knownSize, final));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("<!DOCTYPE html><html><head><title>403</title></head>", 1_200L, true)]   // expired -> HTML error page
    [InlineData("  <html><body>Access denied</body></html>", 800L, true)]                 // anti-bot HTML
    [InlineData("<?xml version=\"1.0\"?><error/>", 500L, true)]                            // XML error body
    [InlineData("PK binary zip content here", 2_000L, false)]                  // genuine small binary
    [InlineData("just a normal small text note", 3_000L, false)]                           // genuine small text (no markup)
    [InlineData("<!DOCTYPE html>...", 5_000_000L, false)]                                  // too big to be an error page
    [InlineData("", 1_000L, false)]                                                        // empty sample
    [InlineData("<html>...", 0L, false)]                                                   // nothing received
    public void Expired_or_invalid_link_heuristic(string head, long totalBytes, bool expected)
    {
        Assert.Equal(expected, DownloadManager.LooksExpiredOrInvalid(head, totalBytes));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://hermes-agent.nousresearch.com/docs/", true)]  // the reported false-failure
    [InlineData("https://host/", true)]
    [InlineData("https://host/blog/post", true)]
    [InlineData("https://host/page.html", true)]
    [InlineData("https://host/index.php?id=3", true)]
    [InlineData("https://cdn/file.zip?token=abc", false)]  // signed file link keeps the protection
    [InlineData("https://cdn/video.mp4", false)]
    [InlineData("ftp://host/docs/", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void Page_like_url_heuristic(string url, bool expected)
    {
        // Page-like URLs are EXPECTED to produce HTML, so the expired-link check must skip them.
        Assert.Equal(expected, DownloadManager.UrlLooksLikePage(url));
    }

}
