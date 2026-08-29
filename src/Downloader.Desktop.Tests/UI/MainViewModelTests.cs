using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The shell view model: navigation state, the footer filter pills and the status-bar aggregates.
///
/// The filter buckets are the interesting part. The footer pills double as the list filter and are
/// documented as <b>disjoint</b> — every item belongs to exactly one bucket and the counts must sum to
/// the total. If two buckets overlap (or one drops a status), the pills lie about how much work is
/// outstanding and a filtered list silently hides downloads.
///
/// Persistence is stubbed: <see cref="MainViewModel"/> takes an <see cref="IFileService"/>, so these
/// tests never touch the real config.json.
/// </summary>
public class MainViewModelTests
{
    private sealed class StubFileService : IFileService
    {
        public Config Saved { get; private set; }
        public Task<Config> LoadFromFileAsync() => Task.FromResult(Config.New());
        public Task SaveToFileAsync(Config itemToSave) { Saved = itemToSave; return Task.CompletedTask; }
    }

    private static (MainViewModel main, DownloadManager manager) Build()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(Config.New());
        return (main, manager);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadStatus status)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
        return vm;
    }

    // ---- filter buckets ----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Footer_filter_counts_are_disjoint_and_sum_to_the_total()
    {
        var (main, manager) = Build();

        Add(manager, "running.bin", DownloadStatus.Running);
        Add(manager, "paused.bin", DownloadStatus.Paused);
        Add(manager, "stopped.bin", DownloadStatus.Stopped);
        Add(manager, "queued.bin", DownloadStatus.Created);
        Add(manager, "none.bin", DownloadStatus.None);
        Add(manager, "done.bin", DownloadStatus.Completed);
        Add(manager, "failed.bin", DownloadStatus.Failed);

        Assert.Equal(7, main.AllCount);
        Assert.Equal(1, main.ActiveFilterCount);      // Running
        Assert.Equal(2, main.QueuedFilterCount);      // Created + None
        Assert.Equal(2, main.StoppedFilterCount);     // Paused + Stopped
        Assert.Equal(1, main.CompletedFilterCount);
        Assert.Equal(1, main.FailedFilterCount);

        var bucketed = main.ActiveFilterCount + main.QueuedFilterCount + main.StoppedFilterCount
                       + main.CompletedFilterCount + main.FailedFilterCount;
        Assert.Equal(main.AllCount, bucketed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_list_reports_zero_everywhere()
    {
        var (main, _) = Build();

        Assert.Equal(0, main.AllCount);
        Assert.Equal(0, main.ActiveFilterCount);
        Assert.Equal(0, main.QueuedFilterCount);
        Assert.Equal(0, main.StoppedFilterCount);
        Assert.Equal(0, main.CompletedFilterCount);
        Assert.Equal(0, main.FailedFilterCount);
        Assert.Equal(0, main.ActiveCount);
        Assert.Equal(0, main.QueuedCount);
        Assert.Equal(0, main.CompletedCount);
    }

    // ---- navigation --------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Downloads_is_the_starting_page_with_the_all_filter()
    {
        var (main, _) = Build();

        Assert.True(main.IsDownloadsSelected);
        Assert.True(main.IsAllSelected);
        Assert.False(main.IsQueuesSelected);
        Assert.False(main.IsSchedulerSelected);
        Assert.False(main.IsSettingsSelected);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Navigating_to_a_management_page_selects_exactly_one_section()
    {
        var (main, _) = Build();

        main.ShowQueuesCommand.Execute(null);
        AssertOnlySection(main, queues: true);

        main.ShowSchedulerCommand.Execute(null);
        AssertOnlySection(main, scheduler: true);

        main.ShowSettingViewCommand.Execute(null);
        AssertOnlySection(main, settings: true);

        main.ShowDownloadsCommand.Execute(null);
        AssertOnlySection(main, downloads: true);
    }

    private static void AssertOnlySection(MainViewModel main, bool downloads = false, bool queues = false,
        bool scheduler = false, bool settings = false)
    {
        Assert.Equal(downloads, main.IsDownloadsSelected);
        Assert.Equal(queues, main.IsQueuesSelected);
        Assert.Equal(scheduler, main.IsSchedulerSelected);
        Assert.Equal(settings, main.IsSettingsSelected);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_filter_selects_exactly_one_pill_and_returns_to_the_list()
    {
        var (main, _) = Build();
        main.ShowSettingViewCommand.Execute(null); // start off the downloads page

        main.ShowActiveCommand.Execute(null);
        Assert.True(main.IsDownloadsSelected); // a filter implies the downloads list
        AssertOnlyFilter(main, active: true);

        main.ShowQueuedCommand.Execute(null);
        AssertOnlyFilter(main, queued: true);

        main.ShowStoppedCommand.Execute(null);
        AssertOnlyFilter(main, stopped: true);

        main.ShowCompletedCommand.Execute(null);
        AssertOnlyFilter(main, completed: true);

        main.ShowFailedCommand.Execute(null);
        AssertOnlyFilter(main, failed: true);

        main.ShowAllCommand.Execute(null);
        AssertOnlyFilter(main, all: true);
    }

    private static void AssertOnlyFilter(MainViewModel main, bool all = false, bool active = false,
        bool queued = false, bool stopped = false, bool completed = false, bool failed = false)
    {
        Assert.Equal(all, main.IsAllSelected);
        Assert.Equal(active, main.IsActiveSelected);
        Assert.Equal(queued, main.IsQueuedSelected);
        Assert.Equal(stopped, main.IsStoppedSelected);
        Assert.Equal(completed, main.IsCompletedSelected);
        Assert.Equal(failed, main.IsFailedSelected);
    }

    // ---- sidebar and inputs ------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Collapsing_the_sidebar_narrows_it()
    {
        var (main, _) = Build();
        var expandedWidth = main.SidebarWidth;

        main.ToggleSidebarCommand.Execute(null);
        var collapsed = main.SidebarWidth;

        Assert.NotEqual(expandedWidth, collapsed);
        Assert.True(collapsed < expandedWidth);

        main.ToggleSidebarCommand.Execute(null);
        Assert.Equal(expandedWidth, main.SidebarWidth);
        Assert.True(main.IsSidebarExpanded);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_url_and_search_boxes_round_trip()
    {
        var (main, _) = Build();

        main.DownloadUrl = "https://example.invalid/file.zip";
        Assert.Equal("https://example.invalid/file.zip", main.DownloadUrl);

        main.SearchText = "zip";
        Assert.Equal("zip", main.SearchText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Searching_filters_the_visible_list_without_changing_the_counts()
    {
        var (main, manager) = Build();
        Add(manager, "holiday-photos.zip", DownloadStatus.Completed);
        Add(manager, "invoice.pdf", DownloadStatus.Completed);

        main.SearchText = "invoice";

        // The footer counts describe the whole list, not the search result.
        Assert.Equal(2, main.AllCount);
        Assert.Equal(2, main.CompletedFilterCount);

        Assert.Single(main.Downloads.ItemsView.Cast<DownloadItemViewModel>());
        Assert.Equal("invoice.pdf",
            main.Downloads.ItemsView.Cast<DownloadItemViewModel>().Single().FileName);
    }

    // ---- status bar --------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Total_speed_text_is_rendered_for_the_status_bar()
    {
        var (main, _) = Build();

        Assert.False(string.IsNullOrWhiteSpace(main.TotalSpeedText));
        Assert.False(string.IsNullOrWhiteSpace(main.TotalDownloadedText));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Manager_counts_are_surfaced_for_the_status_bar()
    {
        var (main, manager) = Build();
        Add(manager, "a.bin", DownloadStatus.Running);
        Add(manager, "b.bin", DownloadStatus.Created);
        Add(manager, "c.bin", DownloadStatus.Completed);

        manager.RaiseStatsForTest();

        Assert.Equal(manager.ActiveCount, main.ActiveCount);
        Assert.Equal(manager.QueuedCount, main.QueuedCount);
        Assert.Equal(manager.CompletedCount, main.CompletedCount);
    }

    // ---- pages and commands ------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_page_and_command_is_wired()
    {
        var (main, _) = Build();

        Assert.NotNull(main.Downloads);
        Assert.NotNull(main.CurrentPage);

        Assert.NotNull(main.AddDownloadItemCommand);
        Assert.NotNull(main.StartAllCommand);
        Assert.NotNull(main.StopAllCommand);
        Assert.NotNull(main.ClearAllCommand);
        Assert.NotNull(main.ShowAboutCommand);
        Assert.NotNull(main.DonateCommand);
        Assert.NotNull(main.ApplyUpdateCommand);
        Assert.NotNull(main.ToggleSidebarCommand);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clear_all_removes_only_finished_downloads()
    {
        var (main, manager) = Build();
        Add(manager, "done.bin", DownloadStatus.Completed);
        Add(manager, "running.bin", DownloadStatus.Running);
        Add(manager, "failed.bin", DownloadStatus.Failed);

        main.ClearAllCommand.Execute(null);

        // A completed row is cleared; work in progress and failures the user still needs to see stay.
        Assert.DoesNotContain(manager.Items, i => i.FileName == "done.bin");
        Assert.Contains(manager.Items, i => i.FileName == "running.bin");
        Assert.Contains(manager.Items, i => i.FileName == "failed.bin");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stop_all_stops_running_and_queued_rows_but_leaves_finished_ones()
    {
        var (main, manager) = Build();
        var running = Add(manager, "running.bin", DownloadStatus.Running);
        var queued = Add(manager, "queued.bin", DownloadStatus.Created);
        var done = Add(manager, "done.bin", DownloadStatus.Completed);

        main.StopAllCommand.Execute(null);

        Assert.Equal(DownloadStatus.Stopped, running.Status);
        Assert.Equal(DownloadStatus.Stopped, queued.Status);
        Assert.Equal(DownloadStatus.Completed, done.Status); // never resurrect a finished download
    }

    // ---- finishing everything ---------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Finishing_the_last_download_arms_a_shutdown_only_when_asked()
    {
        var wasEnabled = NotificationService.Enabled;
        try
        {
            NotificationService.Enabled = false; // don't shell out to notify-send
            ShutdownService.Cancel();

            var (_, manager) = Build();
            var row = Add(manager, "a.bin", DownloadStatus.Running);
            row.Status = DownloadStatus.Completed;

            // Shutdown-on-completion is off by default, so draining the list must NOT arm anything.
            manager.RaiseCompletedForTest(row);

            Assert.False(ShutdownService.IsScheduled);
        }
        finally
        {
            ShutdownService.Cancel();
            NotificationService.Enabled = wasEnabled;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_everything_never_arms_a_shutdown()
    {
        var wasEnabled = NotificationService.Enabled;
        try
        {
            NotificationService.Enabled = false;
            ShutdownService.Cancel();

            var (_, manager) = Build();
            var row = Add(manager, "a.bin", DownloadStatus.Running);
            row.Status = DownloadStatus.Stopped;

            // The trigger must only fire when something actually COMPLETED. Otherwise "Stop All"
            // would arm a shutdown whenever a finished item happened to be sitting in the list.
            manager.RaiseStoppedForTest(row);

            Assert.False(ShutdownService.IsScheduled);
        }
        finally
        {
            ShutdownService.Cancel();
            NotificationService.Enabled = wasEnabled;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Update_button_is_hidden_until_an_update_is_staged()
    {
        UpdateFlow.ResetForTests();
        var (main, _) = Build();

        Assert.False(main.IsUpdateReady);
    }
}
