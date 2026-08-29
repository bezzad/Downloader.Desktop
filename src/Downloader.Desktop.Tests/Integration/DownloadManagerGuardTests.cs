using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The download manager's defensive paths: what it does before a config exists, and what it does
/// with null, blank or nonsensical input.
///
/// These matter more than they look. The manager is constructed by DI and its config arrives
/// asynchronously afterwards, so there is a real window during startup where <c>_config</c> is null
/// and any of these members can be called — by a restored window, by a forwarded CLI add, or by the
/// scheduler tick. Every one of them is written to cope with that, and none of it was exercised: the
/// existing tests all call <c>Initialize</c> first, so the null side of each guard never ran.
/// </summary>
public class DownloadManagerGuardTests
{
    private static DownloadItem Item(string name = "a.bin") => new()
    {
        Url = "https://10.255.255.1/" + name,
        FileName = name,
        SaveFolder = "/tmp",
    };

    // ---- before Initialize has been called --------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_uninitialised_manager_answers_every_query_without_a_config()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();

        // DI builds the manager; the config is loaded asynchronously afterwards. Everything the UI
        // binds to has to survive that window.
        Assert.Empty(manager.Queues);
        Assert.Empty(manager.Items);
        Assert.Equal(0, manager.ActiveCount);
        Assert.Equal(0, manager.QueuedCount);
        Assert.Equal(0, manager.CompletedCount);
        Assert.Equal(0, manager.TotalSpeed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_uninitialised_manager_ignores_every_command()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();

        manager.EvaluateSchedules();
        manager.PumpQueue("no-such-queue");
        manager.PumpQueue(null);
        manager.StartAll();
        manager.StopAll();
        manager.ClearCompleted();
        manager.RaiseStatsForTest();
        manager.Batch(null);          // a null action must not throw
        manager.Batch(() => { });

        Assert.Empty(manager.Items);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Initialising_with_no_config_falls_back_to_defaults()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();

        manager.Initialize(null);

        // A missing or unreadable config.json arrives here as null; the app must come up with
        // defaults rather than an empty shell.
        Assert.NotEmpty(manager.Queues);
        Assert.NotNull(manager.Config);
    }

    // ---- adding nonsense ---------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Adding_an_empty_or_null_batch_does_nothing()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;

        await manager.AddRangeAsync(null, autoStart: false);
        await manager.AddRangeAsync(new List<DownloadItem>(), autoStart: false);

        Assert.Empty(manager.Items);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_item_with_no_usable_url_never_starts()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var vm = manager.Add(new DownloadItem { Urls = new List<string> { "   ", "" }, SaveFolder = "/tmp" },
            autoStart: true);

        // Nothing downloadable, so it must not be left claiming to be Running.
        Assert.NotEqual(DownloadStatus.Running, vm.Status);
    }

    // ---- queue plumbing with missing pieces -------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Pumping_an_unknown_or_paused_queue_starts_nothing()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;

        var vm = manager.Add(Item(), autoStart: false);
        vm.Status = DownloadStatus.Created;

        manager.PumpQueue("no-such-queue-id");
        manager.PumpQueue(null);
        manager.PumpQueue(config.DefaultQueue.Id); // the queue itself is paused

        // A paused queue must swallow the pump, or completion's auto-advance would restart work the
        // user just stopped.
        Assert.Equal(DownloadStatus.Created, vm.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_last_queue_cannot_be_removed()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        manager.RemoveQueue(config.DefaultQueue);
        manager.RemoveQueue(null);

        // Removing the only queue would leave every download orphaned with nowhere to run.
        Assert.NotEmpty(config.Queues);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_a_queue_moves_its_downloads_to_the_one_that_remains()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;

        var extra = manager.AddQueue("videos");
        extra.IsRunning = false;
        var vm = manager.Add(Item(), autoStart: false);
        manager.MoveToQueue(vm, extra.Id);

        manager.RemoveQueue(extra);

        // Orphaning them would make them invisible on the Queues page and unstartable.
        Assert.Equal(config.DefaultQueue.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_queue_added_with_no_name_still_gets_one()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var queue = manager.AddQueue(null);

        Assert.NotNull(queue);
        Assert.False(string.IsNullOrWhiteSpace(queue.Name));
        Assert.False(string.IsNullOrWhiteSpace(queue.Id));
    }

    // ---- post-download actions --------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_post_download_action_is_offered_only_for_a_completed_row()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;

        var vm = manager.Add(Item(), autoStart: false);

        Assert.Null(manager.PostDownloadActionLabel(null));
        Assert.Null(manager.PostDownloadActionLabel(vm));   // not finished yet

        vm.Status = DownloadStatus.Completed;
        // No plugin offers one for this link, so still nothing — and running it must be a no-op
        // rather than a null dereference.
        Assert.Null(manager.PostDownloadActionLabel(vm));
        await manager.RunPostDownloadAction(vm);
        await manager.RunPostDownloadAction(null);
    }

    // ---- failure descriptions ----------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_failure_always_produces_a_message_a_user_can_act_on()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;

        foreach (var error in new Exception[]
                 {
                     new TimeoutException("timed out"),
                     new OperationCanceledException(),
                     new System.Net.Http.HttpRequestException("no such host"),
                     new System.IO.IOException("disk full"),
                     new UnauthorizedAccessException("denied"),
                     new AggregateException(new InvalidOperationException("inner boom")),
                     new InvalidOperationException("something else"),
                 })
        {
            var vm = manager.Add(Item(Guid.NewGuid().ToString("N") + ".bin"), autoStart: false);
            vm.Status = DownloadStatus.Running;

            manager.RaiseFailedForTest(vm, error);

            Assert.Equal(DownloadStatus.Failed, vm.Status);
            // A bare "Operation cancelled" or an empty message tells the user nothing about what to
            // do next, which is the complaint these descriptions exist to answer.
            Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_failure_with_no_exception_still_gets_a_message()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;

        var vm = manager.Add(Item(), autoStart: false);
        vm.Status = DownloadStatus.Running;

        manager.RaiseFailedForTest(vm, null);

        Assert.Equal(DownloadStatus.Failed, vm.Status);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }

    // ---- pausing and cancelling things that are not running ----------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Acting_on_a_row_with_no_live_engine_is_safe()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;

        var vm = manager.Add(Item(), autoStart: false);
        vm.Status = DownloadStatus.Running; // marked running, but nothing was ever started

        // Every one of these dereferences an engine handle that does not exist yet — the null-
        // conditional guards are what keep a fast Pause-after-Add from crashing.
        manager.Pause(vm);
        manager.Resume(vm);
        manager.Cancel(vm);
        manager.Retry(vm);

        Assert.NotNull(vm);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Removing_a_row_that_never_started_is_safe()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;

        var vm = manager.Add(Item(), autoStart: false);

        await manager.Remove(vm);

        Assert.Empty(manager.Items);
    }

    // ---- the page heuristic behind the expired-link sniff ------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://host/page", true)]
    [InlineData("https://host/", true)]
    [InlineData("https://host/index.html", true)]
    [InlineData("https://host/a.php", true)]
    [InlineData("https://host/a.aspx", true)]
    [InlineData("https://host/file.zip", false)]
    [InlineData("https://host/movie.mp4", false)]
    [InlineData("https://host/installer.exe", false)]
    public void A_page_url_is_exempt_from_the_expired_link_sniff(string url, bool expected)
    {
        // HTML is the expected content of a page URL, so sniffing it as "expired link" would fail
        // every plugin-resolved page download. The sniff still applies to signed file links.
        Assert.Equal(expected, DownloadManager.UrlLooksLikePage(url));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_blank_url_is_not_treated_as_a_page()
    {
        Assert.False(DownloadManager.UrlLooksLikePage(null));
        Assert.False(DownloadManager.UrlLooksLikePage(""));
        Assert.False(DownloadManager.UrlLooksLikePage("   "));
    }
}
