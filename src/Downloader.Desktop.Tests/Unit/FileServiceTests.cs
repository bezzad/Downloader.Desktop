using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Reading and writing the app's config.json.
///
/// This is where everything the user has ever set up lives — their download list, queues, schedules
/// and settings — so the failure that matters is not "the file is missing" but "the file was there
/// and came back wrong, or got truncated". The write is deliberately atomic (temp file, then move)
/// and the read deliberately swallows everything, because a config the app cannot parse must degrade
/// to defaults rather than block startup.
///
/// None of this was previously covered: the path is resolved from the real %AppData%/~/.config, so a
/// test would have overwritten the developer's own config. It now goes through
/// <c>FileService.ConfigFileOverride</c>, a temp file per test.
/// </summary>
public class FileServiceTests : IDisposable
{
    private readonly string _dir;

    public FileServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dldesktop-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        FileService.ConfigFileOverride = Path.Combine(_dir, "config.json");
    }

    public void Dispose()
    {
        FileService.ConfigFileOverride = null;   // never leave the real path redirected
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_missing_config_loads_as_defaults_rather_than_failing()
    {
        // First launch on a new machine: there is no file at all, and the app must still come up.
        var config = await new FileService().LoadFromFileAsync();

        Assert.NotNull(config);
        Assert.NotNull(config.Settings);
        Assert.NotEmpty(config.Queues);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Everything_the_user_set_up_survives_a_save_and_load()
    {
        var service = new FileService();
        var config = Config.New();
        config.Settings.ChunkCount = 7;
        config.Settings.MaxConcurrentDownloads = 3;
        config.Settings.DefaultSavePath = "/tmp/downloads";
        config.Settings.Language = "fr";
        config.IsThemeDarkMode = true;
        config.Downloads.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/movie.mkv",
            FileName = "movie.mkv",
            SaveFolder = "/tmp",
            Status = DownloadStatus.Paused,
            Downloaded = 4096,
            Size = 8192,
        });
        config.Queues.Add(new DownloadQueue { Id = "q2", Name = "videos", MaxConcurrent = 2 });
        config.Schedules.Add(new DownloadSchedule
        {
            Enabled = true,
            TargetQueueId = "q2",
            StartTime = new TimeSpan(1, 30, 0),
        });

        await service.SaveToFileAsync(config);
        var loaded = await service.LoadFromFileAsync();

        // Losing any of this silently is the worst outcome here: the user's queue setup and
        // half-finished downloads simply vanish on next launch.
        Assert.Equal(7, loaded.Settings.ChunkCount);
        Assert.Equal(3, loaded.Settings.MaxConcurrentDownloads);
        Assert.Equal("/tmp/downloads", loaded.Settings.DefaultSavePath);
        Assert.Equal("fr", loaded.Settings.Language);
        Assert.True(loaded.IsThemeDarkMode);

        var item = Assert.Single(loaded.Downloads);
        Assert.Equal("movie.mkv", item.FileName);
        Assert.Equal("https://10.255.255.1/movie.mkv", item.Url);
        Assert.Equal(4096, item.Downloaded);
        Assert.Equal(8192, item.Size);

        Assert.Contains(loaded.Queues, q => q.Id == "q2" && q.Name == "videos" && q.MaxConcurrent == 2);
        Assert.Equal("q2", Assert.Single(loaded.Schedules).TargetQueueId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_corrupt_config_degrades_to_defaults_instead_of_blocking_startup()
    {
        await File.WriteAllTextAsync(FileService.ConfigFileOverride, "{ this is not valid json", TestContext.Current.CancellationToken);

        var config = await new FileService().LoadFromFileAsync();

        // Throwing here would make the app unlaunchable with no way for the user to recover short of
        // finding and deleting the file themselves.
        Assert.NotNull(config);
        Assert.NotNull(config.Settings);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{}")]
    public async Task Any_unexpected_shape_still_yields_a_usable_config(string content)
    {
        await File.WriteAllTextAsync(FileService.ConfigFileOverride, content, TestContext.Current.CancellationToken);

        var config = await new FileService().LoadFromFileAsync();

        Assert.NotNull(config);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Saving_creates_the_folder_when_it_is_not_there_yet()
    {
        var nested = Path.Combine(_dir, "deeper", "still", "config.json");
        FileService.ConfigFileOverride = nested;

        await new FileService().SaveToFileAsync(Config.New());

        // On a first run the whole Downloader folder is absent.
        Assert.True(File.Exists(nested));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_save_replaces_the_previous_file_without_leaving_a_temp_behind()
    {
        var service = new FileService();
        var first = Config.New();
        first.Settings.ChunkCount = 4;
        await service.SaveToFileAsync(first);

        var second = Config.New();
        second.Settings.ChunkCount = 9;
        await service.SaveToFileAsync(second);

        Assert.Equal(9, (await service.LoadFromFileAsync()).Settings.ChunkCount);

        // The write goes via a temp file and a move so a crash mid-write cannot truncate the real
        // one; the temp must not survive a successful save.
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Concurrent_saves_do_not_corrupt_the_file()
    {
        var service = new FileService();

        // Autosave, a settings change and shutdown can all fire at once; the write gate is what stops
        // them interleaving into a half-written file.
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
        {
            var config = Config.New();
            config.Settings.ChunkCount = i + 1;
            return service.SaveToFileAsync(config);
        }));

        var loaded = await service.LoadFromFileAsync();

        Assert.NotNull(loaded.Settings);
        Assert.InRange(loaded.Settings.ChunkCount, 1, 12);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Secrets_are_never_written_to_disk()
    {
        var service = new FileService();
        var config = Config.New();
        var item = new DownloadItem { Url = "https://10.255.255.1/f.zip", Referer = "https://10.255.255.1/" };
        item.Request.Cookies.Add(new CookieDto { Name = "session", Value = "super-secret-value", Domain = "host", Path = "/" });
        item.Request.Headers["Authorization"] = "Bearer super-secret-token";
        config.Downloads.Add(item);

        await service.SaveToFileAsync(config);
        var text = await File.ReadAllTextAsync(FileService.ConfigFileOverride, TestContext.Current.CancellationToken);

        // Cookies and headers are a live session; a referer is not. That split is the whole reason
        // RequestContext is [JsonIgnore] and Referer is a persisted proxy onto it.
        Assert.DoesNotContain("super-secret-value", text);
        Assert.DoesNotContain("super-secret-token", text);
        Assert.Contains("10.255.255.1", text);

        var loaded = await service.LoadFromFileAsync();
        var reloaded = Assert.Single(loaded.Downloads);
        Assert.Equal("https://10.255.255.1/", reloaded.Referer);
        Assert.Empty(reloaded.Request.Cookies);
    }
}
