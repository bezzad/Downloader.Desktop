using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The host's plugin-transfer path: when an enabled plugin's ITransferProvider claims an item URL, the
/// manager must run the plugin's ITransfer end-to-end — progress staged, pause/resume/cancel routed,
/// queue cap obeyed, terminal states owned. Driven by an in-process fake provider (no network).
/// </summary>
public class TransferPathTests
{
    private sealed class FakeTransfer : ITransfer
    {
        public readonly TaskCompletionSource<string> Result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PauseCalls, ResumeCalls;

        public event EventHandler<TransferProgress> ProgressChanged;

        public async Task<string> StartAsync(CancellationToken cancellationToken) =>
            await Result.Task.WaitAsync(cancellationToken);

        public void Pause() => PauseCalls++;
        public void Resume() => ResumeCalls++;

        public void RaiseProgress(double pct, long bytes) => ProgressChanged?.Invoke(this,
            new TransferProgress { Percentage = pct, BytesReceived = bytes, TotalBytes = bytes, BytesPerSecond = 1000 });
    }

    private sealed class FakeTransferProvider : ITransferProvider
    {
        public readonly System.Collections.Generic.List<FakeTransfer> Created = new();
        public bool CanHandle(string url) => url.StartsWith("faketransfer:");
        public ITransfer Create(string url, string targetFolder)
        {
            var t = new FakeTransfer();
            Created.Add(t);
            return t;
        }
    }

    private sealed class FakeTransferPlugin(FakeTransferProvider provider) : IDownloaderPlugin
    {
        public string Id => "test.transfer";
        public string Name => "Transfer Test Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "fake transfer plugin";
        public void Initialize(IPluginContext context) => context.RegisterTransferProvider(provider);
    }

    private static (DownloadManager Manager, FakeTransferProvider Provider) NewManager(int cap = 4)
    {
        var pm = new PluginManager();
        var provider = new FakeTransferProvider();
        pm.RegisterPlugin(new FakeTransferPlugin(provider));
        var manager = new DownloadManager(pm);
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = cap;
        manager.Initialize(config);
        return (manager, provider);
    }

    /// <summary>Start() hops threads (Task.Run + UI posts) — pump the dispatcher until the condition holds.</summary>
    private static void PumpUntil(Func<bool> condition, string what, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"timed out waiting for: {what}");
        }
    }

    private static DownloadItemViewModel AddAndStart(DownloadManager manager, string url = "faketransfer:site")
    {
        var vm = manager.Add(new DownloadItem { Url = url, SaveFolder = Path.GetTempPath() }, autoStart: true);
        PumpUntil(() => vm.ActiveTransfer != null, "the transfer to become active");
        return vm;
    }

    [AvaloniaFact]
    public void Transfer_backed_item_completes_with_the_produced_file()
    {
        var (manager, provider) = NewManager();
        var final = Path.Combine(Path.GetTempPath(), $"transfer-out-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(final, new byte[1234]);
        try
        {
            var vm = AddAndStart(manager);
            Assert.Equal(DownloadStatus.Running, vm.Status);

            // live progress flows through the normal staging pipeline
            provider.Created.Single().RaiseProgress(40, 500);
            PumpUntil(() => { Dispatcher.UIThread.RunJobs(); return vm.FlushProgress() || vm.Progress > 0; },
                "staged progress to flush");

            provider.Created.Single().Result.SetResult(final);
            PumpUntil(() => vm.Status == DownloadStatus.Completed, "completion");

            Assert.Equal(Path.GetFileName(final), vm.FileName);
            Assert.Equal(1234, vm.Size);
            Assert.Equal(100, vm.Progress);
            Assert.Null(vm.ActiveTransfer);
        }
        finally
        {
            File.Delete(final);
        }
    }

    [AvaloniaFact]
    public void Transfer_failure_marks_the_item_failed_with_the_message()
    {
        var (manager, provider) = NewManager();
        var vm = AddAndStart(manager);

        provider.Created.Single().Result.SetException(new InvalidOperationException("crawl exploded"));
        PumpUntil(() => vm.Status == DownloadStatus.Failed, "failure");

        Assert.Contains("crawl exploded", vm.ErrorMessage);
        Assert.Null(vm.ActiveTransfer);
    }

    [AvaloniaFact]
    public void Pause_and_resume_route_to_the_transfer()
    {
        var (manager, provider) = NewManager();
        var vm = AddAndStart(manager);
        var transfer = provider.Created.Single();

        manager.Pause(vm);
        Assert.Equal(DownloadStatus.Paused, vm.Status);
        Assert.Equal(1, transfer.PauseCalls);

        manager.Resume(vm);
        Assert.Equal(DownloadStatus.Running, vm.Status);
        Assert.Equal(1, transfer.ResumeCalls);

        // still the SAME transfer — a paused transfer resumes in place, it is never restarted
        Assert.Single(provider.Created);
    }

    [AvaloniaFact]
    public void Cancel_stops_the_transfer_and_keeps_the_row_stopped()
    {
        var (manager, provider) = NewManager();
        var vm = AddAndStart(manager);

        manager.Cancel(vm);
        Assert.Equal(DownloadStatus.Stopped, vm.Status);

        // the cancellation reaches StartAsync (its token trips) and the row STAYS Stopped — the
        // OperationCanceledException must not be reported as a failure
        PumpUntil(() => vm.ActiveTransfer == null, "transfer teardown");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DownloadStatus.Stopped, vm.Status);
        Assert.Null(vm.ErrorMessage);
    }

    [AvaloniaFact]
    public void Transfers_obey_the_queue_concurrency_cap()
    {
        var (manager, provider) = NewManager(cap: 1);
        manager.Add(new DownloadItem { Url = "faketransfer:a", SaveFolder = "/tmp" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "faketransfer:b", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();
        PumpUntil(() => provider.Created.Count == 1 && manager.Items.Any(i => i.ActiveTransfer != null),
            "the first transfer to start");

        Assert.Equal(1, manager.ActiveCount);
        Assert.Equal(1, manager.QueuedCount);

        // finishing the first frees the slot and the pump starts the queued one
        var final = Path.Combine(Path.GetTempPath(), $"transfer-cap-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(final, new byte[10]);
        try
        {
            provider.Created[0].Result.SetResult(final);
            PumpUntil(() => provider.Created.Count == 2, "the queued transfer to start");
            Assert.Equal(1, manager.ActiveCount);
        }
        finally
        {
            File.Delete(final);
        }
    }
}
