using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// A download that stops responding must end, one way or another.
/// <para>
/// The engine normally reports every outcome, but against a server that refuses every request it can
/// finish without raising a completion at all: the awaited task returns and the row stays Running for
/// ever — no error, no file, nothing to retry, and no way for the user to tell it apart from a slow
/// download. (The root cause is fixed in the engine too; this is the app noticing on its own, because it
/// ships against whatever engine version is released.)
/// </para>
/// </summary>
public class StalledDownloadTests
{
    // ── The decision, in isolation ───────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_running_download_that_has_gone_quiet_is_stalled()
    {
        var now = DateTime.UtcNow;
        var silent = now - DownloadManager.StallTimeout - TimeSpan.FromSeconds(1);

        Assert.True(DownloadManager.IsStalled(global::Downloader.DownloadStatus.Running,
            hasLiveEngine: true, planStage: null, lastProgressUtc: silent, nowUtc: now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_that_is_merely_slow_is_left_alone()
    {
        var now = DateTime.UtcNow;

        // Just inside the budget: a trickling download is still a download.
        Assert.False(DownloadManager.IsStalled(global::Downloader.DownloadStatus.Running,
            hasLiveEngine: true, planStage: null,
            lastProgressUtc: now - DownloadManager.StallTimeout + TimeSpan.FromSeconds(1), nowUtc: now));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(global::Downloader.DownloadStatus.Paused)]
    [InlineData(global::Downloader.DownloadStatus.Stopped)]
    [InlineData(global::Downloader.DownloadStatus.Created)]
    [InlineData(global::Downloader.DownloadStatus.Completed)]
    [InlineData(global::Downloader.DownloadStatus.Failed)]
    public void Only_a_running_download_can_stall(global::Downloader.DownloadStatus status)
    {
        // A paused download is silent BY DESIGN, and a queued or finished one has no attempt to end.
        // Failing any of these would be the watchdog causing the problem it exists to catch.
        var now = DateTime.UtcNow;
        Assert.False(DownloadManager.IsStalled(status, hasLiveEngine: true, planStage: null,
            lastProgressUtc: now - DownloadManager.StallTimeout - TimeSpan.FromMinutes(5), nowUtc: now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_that_is_being_assembled_is_left_alone()
    {
        // Joining segments or running ffmpeg moves no bytes for minutes at a time, and that is normal.
        var now = DateTime.UtcNow;
        Assert.False(DownloadManager.IsStalled(global::Downloader.DownloadStatus.Running,
            hasLiveEngine: true, planStage: "Assembling…",
            lastProgressUtc: now - DownloadManager.StallTimeout - TimeSpan.FromMinutes(5), nowUtc: now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_row_with_no_engine_is_left_alone()
    {
        // Between attempts the row can read Running with nothing attached; there is no attempt to fail.
        var now = DateTime.UtcNow;
        Assert.False(DownloadManager.IsStalled(global::Downloader.DownloadStatus.Running,
            hasLiveEngine: false, planStage: null,
            lastProgressUtc: now - DownloadManager.StallTimeout - TimeSpan.FromMinutes(5), nowUtc: now));
    }

    // ── End to end, through the real pump ────────────────────────────────────────────────────────────

    /// <summary>The whole point: a download the engine never reports on is ended by the app, with a
    /// message that says what happened. Driven through the real UI pump, with the timeout shortened.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_download_the_engine_never_reports_on_is_ended_by_the_app()
    {
        Localizer.Instance.Load("en");
        var original = DownloadManager.StallTimeout;
        DownloadManager.StallTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            var manager = new DownloadManager();
            manager.Initialize(Config.New());
            // An address that accepts nothing and answers nothing: the attempt starts and goes quiet.
            manager.Add(new DownloadItem
            {
                Url = "https://10.255.255.1/never-answers.bin",
                SaveFolder = Path.GetTempPath(),
                FileName = "never-answers.bin",
            }, autoStart: true);
            var vm = manager.Items[0];

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (vm.Status is not (global::Downloader.DownloadStatus.Failed
                                     or global::Downloader.DownloadStatus.Completed)
                   && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25);
            }
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(global::Downloader.DownloadStatus.Failed, vm.Status);
            Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        }
        finally
        {
            DownloadManager.StallTimeout = original; // process-wide: never leak it into another test
        }
    }

    /// <summary>A stall is worth trying the download's other address, and says so rather than blaming
    /// the link.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_stall_is_reported_honestly_and_earns_another_address()
    {
        Localizer.Instance.Load("en");
        var stalled = new DownloadStalledException("stopped responding");

        Assert.True(DownloadManager.CanRetryWithAnotherUrl(stalled));
        // It is not an expired link and must not be described as one.
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(stalled));
        Assert.NotEqual("Error_DownloadStalled", Localizer.Instance["Error_DownloadStalled"]);
    }
}
