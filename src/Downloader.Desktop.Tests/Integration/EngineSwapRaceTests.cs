using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The flaky <c>MemoryReleaseTests.A_released_stopped_row_can_be_retried_to_completion</c>
/// ("Value cannot be null. (Parameter 'source')"): a retry builds its engine on a worker thread while the
/// previous attempt's completion is still being handled on the UI thread. If the new engine is attached
/// between that handler's stale check and its release, the handler releases — disposes — the NEW engine,
/// which then fails inside its own start-up (the engine's <c>Clear()</c> nulls its request list).
/// </summary>
public class EngineSwapRaceTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_finished_attempt_never_releases_the_engine_of_the_attempt_that_replaced_it()
    {
        var cfg = Config.New();
        cfg.DefaultQueue.IsRunning = false; // nothing may start on its own
        var manager = new DownloadManager();
        manager.Initialize(cfg);
        var vm = manager.Add(new DownloadItem { Urls = new() { "http://10.255.255.1/file.bin" } }, autoStart: false);

        var previous = new DownloadService();
        var next = new DownloadService();
        manager.AttachForTest(vm, previous);
        vm.Status = DownloadStatus.Stopped; // the user stopped it; its cancellation is what arrives now

        Task attachNext = null;
        DownloadManager.AfterCompletionStaleCheck = row =>
        {
            if (row != vm || attachNext != null)
                return;
            // The retry, on a worker, attaching its engine at the worst possible moment.
            attachNext = Task.Run(() => manager.AttachForTest(vm, next));
            attachNext.Wait(500); // gives it every chance to get in before this handler finishes
        };
        try
        {
            RaiseCompleted(previous, new AsyncCompletedEventArgs(null, cancelled: true, null));
        }
        finally
        {
            DownloadManager.AfterCompletionStaleCheck = null;
        }

        Assert.NotNull(attachNext);
        Assert.True(attachNext.Wait(5000), "the next attempt never got its engine attached");
        Dispatcher.UIThread.RunJobs();

        // The finished attempt released its own engine, and only that one.
        Assert.Same(next, vm.Download);
    }

    private static void RaiseCompleted(DownloadService engine, AsyncCompletedEventArgs e) =>
        typeof(DownloadService).BaseType!
            .GetMethod("OnDownloadFileCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(engine, new object[] { e });
}
