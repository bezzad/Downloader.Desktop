using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.Tests.Plugins.Hls;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// A multi-part plan as the USER sees it: paste a link a plugin claims, and the row has to walk through
/// its parts, assemble them, and end up Completed with the right name, size and progress.
///
/// The runner core is already covered UI-free; this is the half that owns the row, and it is where the
/// user-visible failures live. A failure that forgets to clear the run state leaves the details dialog
/// showing a live segment board for a download that stopped; a completion that skips the terminal
/// bookkeeping means the queue never starts the next item; a stop that only reaches the part in flight
/// leaves the rest of the plan downloading behind a frozen bar.
/// </summary>
public class PlanRowFlowTests
{
    /// <summary>A plugin that claims one scheme and answers with a fixed multi-part plan.</summary>
    private sealed class PlanningPlugin(DownloadPlan plan) : IDownloaderPlugin
    {
        public string Id => "test.planner";
        public string Name => "Planner";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "returns a fixed plan";
        public void Initialize(IPluginContext context) => context.RegisterResolver(new Resolver(plan));

        private sealed class Resolver(DownloadPlan plan) : ILinkResolver
        {
            public bool CanResolve(string url) => url.StartsWith("plan:", StringComparison.Ordinal);
            public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) => Task.FromResult(plan);
        }
    }

    private static void PumpUntil(Func<bool> condition, string what, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {what}");
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static (DownloadManager Manager, Config Config, string Folder) NewManager(DownloadPlan plan)
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new PlanningPlugin(plan));
        var manager = new DownloadManager(pm);
        var config = Config.New();
        var folder = Directory.CreateTempSubdirectory("plan-row-").FullName;
        config.Settings.DefaultSavePath = folder;
        manager.Initialize(config);
        return (manager, config, folder);
    }

    private static DownloadItem Item(string folder) =>
        new() { Urls = { "plan://fixed" }, SaveFolder = folder };

    /// <summary>
    /// The whole flow: three parts fetched, concatenated, and the row left Completed at 100% with the
    /// assembled file's real size, under the name the plan asked for.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_multi_part_plan_runs_to_completion_and_leaves_the_row_finished()
    {
        Localizer.Instance.Load("en");
        using var server = new LoopbackServer();
        var parts = new[] { "AAAA", "BBBB", "CCCC" };
        foreach (var (text, i) in parts.Select((t, i) => (t, i)))
            server.MapBytes($"/part{i}.ts", Encoding.UTF8.GetBytes(text), "video/mp2t");

        var (manager, config, folder) = NewManager(new DownloadPlan
        {
            SuggestedFileName = "joined.bin",
            Parts = parts.Select((_, i) => new DownloadPart { Url = server.Url($"part{i}.ts") }).ToArray()
        });
        var notificationsWereEnabled = NotificationService.Enabled;
        NotificationService.Enabled = false;
        try
        {
            var vm = manager.Add(Item(folder), autoStart: true);

            PumpUntil(() => vm.Status is DownloadStatus.Completed or DownloadStatus.Failed, "the plan to finish");

            Assert.Equal(DownloadStatus.Completed, vm.Status);
            Assert.Null(vm.ErrorMessage);
            Assert.Equal(100, vm.Progress);
            Assert.Null(vm.PlanStage);
            Assert.Null(vm.PlanRun);

            var produced = Path.Combine(folder, vm.FileName);
            Assert.True(File.Exists(produced), $"expected the assembled file at {produced}");
            Assert.Equal("AAAABBBBCCCC", File.ReadAllText(produced));
            Assert.Equal(new FileInfo(produced).Length, vm.Size);
            // The row's name is the plan's, not the pasted link's. (Rewriting a manifest extension is a
            // post-processed plan's concern — a raw concat keeps the name it was given.)
            Assert.Equal("joined.bin", vm.FileName);

            // The scratch folder the parts were downloaded into must not survive a success.
            Assert.Empty(Directory.EnumerateDirectories(folder));
            // The plan is consumed — a retry re-resolves rather than replaying expiring segment URLs.
            Assert.Null(vm.GetItem().PlanJson);
        }
        finally
        {
            NotificationService.Enabled = notificationsWereEnabled;
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// A part the server will not serve fails the whole download — and the row has to say why. Leaving
    /// the run state behind would keep the details dialog showing a live segment board for a download
    /// that stopped.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_part_that_cannot_be_fetched_fails_the_row_with_a_reason()
    {
        Localizer.Instance.Load("en");
        using var server = new LoopbackServer();
        server.MapBytes("/good.ts", Encoding.UTF8.GetBytes("AAAA"), "video/mp2t");
        server.MapStatus("/bad.ts", 404);

        var (manager, config, folder) = NewManager(new DownloadPlan
        {
            SuggestedFileName = "video.mp4",
            Parts = new[]
            {
                new DownloadPart { Url = server.Url("good.ts") },
                new DownloadPart { Url = server.Url("bad.ts") }
            }
        });
        var notificationsWereEnabled = NotificationService.Enabled;
        NotificationService.Enabled = false;
        try
        {
            var vm = manager.Add(Item(folder), autoStart: true);

            PumpUntil(() => vm.Status is DownloadStatus.Completed or DownloadStatus.Failed, "the plan to fail");

            Assert.Equal(DownloadStatus.Failed, vm.Status);
            Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage), "a failed plan must say why");
            Assert.Null(vm.PlanStage);
            Assert.Null(vm.PlanRun);
            Assert.False(File.Exists(Path.Combine(folder, "video.mp4")),
                "a failed plan must not leave a half-assembled file behind");
        }
        finally
        {
            NotificationService.Enabled = notificationsWereEnabled;
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Stopping mid-plan must stop the whole plan, not just the part in flight, and must not report a
    /// failure — the user asked for this. The scratch folder goes with it.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Stopping_a_running_plan_ends_it_without_reporting_a_failure()
    {
        Localizer.Instance.Load("en");
        using var server = new LoopbackServer();
        // Enough parts that the run is still going when the stop arrives.
        var count = 40;
        for (var i = 0; i < count; i++)
            server.MapBytes($"/part{i}.ts", Encoding.UTF8.GetBytes(new string('x', 2048)), "video/mp2t");

        var (manager, config, folder) = NewManager(new DownloadPlan
        {
            SuggestedFileName = "long.mp4",
            Parts = Enumerable.Range(0, count)
                .Select(i => new DownloadPart { Url = server.Url($"part{i}.ts") }).ToArray()
        });
        var notificationsWereEnabled = NotificationService.Enabled;
        NotificationService.Enabled = false;
        try
        {
            var vm = manager.Add(Item(folder), autoStart: true);
            PumpUntil(() => vm.Status == DownloadStatus.Running, "the plan to start");

            manager.Cancel(vm);
            PumpUntil(() => vm.PlanRun == null, "the plan to wind down");

            Assert.Equal(DownloadStatus.Stopped, vm.Status);
            Assert.Null(vm.ErrorMessage);
            Assert.False(File.Exists(Path.Combine(folder, "long.mp4")));
        }
        finally
        {
            NotificationService.Enabled = notificationsWereEnabled;
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }
}
