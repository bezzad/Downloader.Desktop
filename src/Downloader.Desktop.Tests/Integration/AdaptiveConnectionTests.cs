using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Localizer = Downloader.Desktop.Services.Localizer;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// What the app does with the connection count it has learned (issue #14): where a download starts, and
/// what the row says while it is running below the number the user configured.
/// <para>
/// These drive the manager without a server. <c>Start</c> builds its configuration synchronously before
/// its first await, so the count an attempt set out with is observable immediately after
/// <c>Add(autoStart: true)</c> — the address is the repo's unreachable one, so nothing is downloaded.
/// </para>
/// </summary>
public class AdaptiveConnectionTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_starts_at_the_count_its_host_is_known_to_accept()
    {
        var (manager, config) = NewManager(chunkCount: 8);
        Remember(config, "10.255.255.1", connections: 4);

        var vm = Start(manager, "https://10.255.255.1/file.zip");

        // No refused attempt, no discarded partial: the cost of learning this was paid once, by an
        // earlier download from the same host.
        Assert.Equal(4, vm.PlannedConnections);
        Assert.True(vm.IsReducingConnections);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_users_ceiling_beats_a_higher_remembered_count()
    {
        var (manager, config) = NewManager(chunkCount: 2); // the user has since chosen two
        Remember(config, "10.255.255.1", connections: 8);

        var vm = Start(manager, "https://10.255.255.1/file.zip");

        Assert.Equal(2, vm.PlannedConnections);
        Assert.False(vm.IsReducingConnections, "two IS the ceiling here; nothing was reduced");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_host_that_never_refused_is_downloaded_exactly_as_before()
    {
        var (manager, _) = NewManager(chunkCount: 8);

        var vm = Start(manager, "https://10.255.255.1/file.zip");

        Assert.Equal(8, vm.PlannedConnections);
        Assert.False(vm.IsReducingConnections);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_stale_limit_is_re_tested_at_the_full_count()
    {
        var (manager, config) = NewManager(chunkCount: 8);
        Remember(config, "10.255.255.1", connections: 2,
            learnedUtc: DateTime.UtcNow - ServerLimits.RetestAfter - TimeSpan.FromDays(1));

        var vm = Start(manager, "https://10.255.255.1/file.zip");

        Assert.Equal(8, vm.PlannedConnections);
    }

    // ── What the user reads ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_row_that_is_stepping_down_reads_as_working_not_failed()
    {
        Localizer.Instance.Load("en");
        var (manager, _) = NewManager(chunkCount: 8);
        var vm = Queue(manager, "https://10.255.255.1/only");
        vm.PlannedConnections = 8;

        Assert.True(manager.RaiseFailedForTest(vm, Forbidden()));

        Assert.NotEqual(global::Downloader.DownloadStatus.Failed, vm.Status);
        Assert.Null(vm.ErrorMessage); // the app is still working on it; no red banner
        // Whether the row is still queued or the pump has already started the reduced attempt, it says the
        // same thing — a download running below the configured count explains itself either way.
        Assert.Contains(Localizer.Instance["State_FewerConnections"], vm.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_host_that_refuses_every_count_names_the_refusal_and_never_the_expired_link()
    {
        Localizer.Instance.Load("en");
        var (manager, _) = NewManager(chunkCount: 8);
        var vm = Queue(manager, "https://10.255.255.1/only"); // one address: nowhere else to go
        vm.PlannedConnections = 8;

        foreach (var count in new[] { 4, 2, 1 })
        {
            Assert.True(manager.RaiseFailedForTest(vm, Forbidden()));
            vm.PlannedConnections = count; // what the next attempt sets out with, as Start would capture it
        }
        Assert.False(manager.RaiseFailedForTest(vm, Forbidden()), "a lone refused request is the answer");

        Assert.Equal(global::Downloader.DownloadStatus.Failed, vm.Status);
        Assert.Equal(Localizer.Instance["Error_ServerRefusedConnections"], vm.ErrorMessage);
        Assert.NotEqual(Localizer.Instance["Error_LinkExpiredRefresh"], vm.ErrorMessage);
        Assert.DoesNotContain("expired", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        // And it no longer tells people to lower a setting the app has just managed on their behalf.
        Assert.DoesNotContain("Settings", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_step_down_is_capped_however_high_the_ceiling_is()
    {
        var (manager, _) = NewManager(chunkCount: 8);
        var vm = Queue(manager, "https://10.255.255.1/only");
        vm.PlannedConnections = 4096; // an absurd ceiling must not buy a dozen discarded partial files

        var steps = 0;
        while (manager.RaiseFailedForTest(vm, Forbidden()) && steps < 50)
        {
            steps++;
            vm.PlannedConnections = vm.AttemptConnections!.Value;
        }

        Assert.Equal(DownloadManager.MaxReducedConnectionAttempts, steps);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static (DownloadManager Manager, Config Config) NewManager(int chunkCount)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.ChunkCount = chunkCount;
        config.Settings.MaxTryAgainOnFailure = 1;
        config.Settings.DefaultSavePath = TempDir();
        manager.Initialize(config);
        return (manager, config);
    }

    private static void Remember(Config config, string host, int connections, DateTime? learnedUtc = null)
        => config.ServerConnectionLimits[host] =
            new ServerConnectionLimit { Connections = connections, LearnedUtc = learnedUtc ?? DateTime.UtcNow };

    private static DownloadItemViewModel Start(DownloadManager manager, string url)
        => manager.Add(NewItem(url), autoStart: true);

    private static DownloadItemViewModel Queue(DownloadManager manager, string url)
        => manager.Add(NewItem(url), autoStart: false);

    private static DownloadItem NewItem(string url) => new()
    {
        Urls = new List<string> { url },
        SaveFolder = TempDir(),
        FileName = "adaptive.bin",
    };

    private static HttpRequestException Forbidden() =>
        new("response status code does not indicate success", null, HttpStatusCode.Forbidden);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-adaptive-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
